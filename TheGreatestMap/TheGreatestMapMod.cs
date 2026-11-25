using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using ServerSync;

namespace TheGreatestMap
{
    /// <summary>
    /// The Greatest Map: a shared, living map for a server.
    ///
    ///  * Map markers are shared instantly between players through a server-side store that is
    ///    the single source of truth (so a deleted pin stays deleted).
    ///  * The cartography table only carries exploration (fog); it syncs automatically when you
    ///    are in range.
    ///  * A "pocket" map with no inventory slot: a keybind takes it out (both hands), and only
    ///    while it is out can you write on the map.
    ///  * While the map is out, things you have actually found (looked at or interacted with)
    ///    within a few meters are recorded automatically. Never proximity radar.
    ///
    /// Install on the server and on every client.
    /// </summary>
    // No [BepInProcess] filter on purpose: the mod must load in the client, the Windows
    // dedicated server (valheim_server.exe) and the Linux one (valheim_server.x86_64), and
    // BepInEx matches that attribute against the bare process name.
    [BepInPlugin(ModGuid, ModName, ModVersion)]
    public class TheGreatestMapMod : BaseUnityPlugin
    {
        public const string ModGuid    = "DeathMonger.TheGreatestMap";
        public const string ModName    = "The Greatest Map";
        public const string ModVersion = "4.15.0";

        // Oldest version whose shared-marker wire format this build still speaks. ServerSync
        // refuses peers below this, so bump it only when the format or an RPC changes, not on
        // every fix; otherwise every patch would force the server and all players to update
        // at the same moment. 0.2.0: personal maps, merge-based sync, tombstones, new RPCs.
        //
        // 3.0.0: the saved format became 5, and a build before that refuses a format it does not
        // know rather than reading around it. Left at 0.2.0, an older client joined happily and
        // then quietly received nothing -- its own markers still worked and still reached
        // everyone else, so neither side saw anything wrong while that player's map went stale.
        // Turning them away at the door says why, which a map that silently stops filling cannot.
        //
        // 4.0.0: markers lost the GUID they used to be identified by and are now known by their
        // owner, position and source instead -- format 6. Every bump before this one could be
        // read by an older build, because the framing lets a reader step over a field it does
        // not know; it cannot step over one that is no longer written. A 3.x client reading a
        // 4.x map would take the owner for a name and come apart from there, so this is the
        // first bump that genuinely has to turn people away.
        //
        // Deliberately the floor and not ModVersion: a patch must not lock out the players who
        // installed the release before it. This rises only when the format does.
        // 4.2.0: format 7. Icon, Kind, Type and Auto left the record, because all four
        // follow from Source and a second copy of an answer is one that can disagree -- which
        // it did, on forty-two markers, for weeks. A 4.1.x client reads a 4.2 record's source
        // where it expects a pin type and comes apart from there.
        // 4.3.0: format 8, the columnar layout. 4.2.0 wrote format 7 a marker at a time; for a
        // few hours it also wrote the columnar layout under that same number 7, which is the
        // mistake this bump exists to make impossible. A 4.2 client handed a columnar map reads
        // the string table as marker records. Since both shapes went out calling themselves
        // 4.2.0, the floor has to rise to 4.3.0 -- a version number that cannot be told apart
        // from another with different bytes is no check at all.
        // 4.4.0: nothing in the format changed, and that is the point worth recording. A sync
        // now sends a watermark and gets back only what has changed since, and the whole thing
        // was built to need no bump at all -- both numbers are written after the map, so a
        // reader that predates them stops where it always did, answers in full, and works.
        //
        // The floor rises anyway, because that path was reasoned and never run. The choice is
        // between a mixed-version group taking an untested road and a group that updates
        // together taking a tested one, and nobody is owed the first. The code that tolerates a
        // missing watermark stays exactly as it is: read every shape, enforce none, and let the
        // version check decide who is allowed to speak rather than what we are able to hear.
        // Raised from 4.4.0 because 4.8.4 and 4.8.5 damaged the shared map just by logging in:
        // a pass in those versions wrote "Manual" into every marker with a blank source, which
        // rekeyed it and sent the new identity to everyone. A floor is the only thing that keeps
        // a client like that out, and tgm_unmanual can only repair a map nothing is still harming.
        //
        // A literal rather than ModVersion, which would make every release breaking. The judgment
        // is per release and it is not a close-run thing: leave the floor where it is only when
        // the release is known not to need it, and raise it to the new version whenever forcing
        // the update helps or there is any doubt. Nobody here is inconvenienced much by being
        // asked to update -- they take each version as it appears -- so doubt costs less than
        // a client doing damage nobody can see.
        //
        // Raising it cuts both ways, which is sometimes the point: a client AHEAD of the server
        // is refused too, so a server running a build nobody else has keeps the rest of the crew
        // off it. The same rule means that between publishing a raised floor and deploying the
        // server, nobody can join at all.
        public const string MinCompatibleVersion = "4.12.0";

        internal static TheGreatestMapMod Instance { get; private set; }
        internal static ManualLogSource Log { get; private set; }

        // Master toggle, bound first and deliberately not server-synced, so a player who runs
        // into a problem can switch the mod off in BepInEx/config/DeathMonger.TheGreatestMap.cfg
        // without removing the DLL.
        internal static ConfigEntry<bool> ModEnabled;

        private readonly Harmony _harmony = new Harmony(ModGuid);
        private static ConfigSync _configSync;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            // Everything this mod logs can be repeated in the dev console, so that watching a
            // sync or a repair does not mean alt-tabbing to a file. Hooked at the source rather
            // than at the hundreds of places that log, which also means a line added later is
            // carried without anybody remembering to.
            Log.LogEvent += EchoToConsole;

            ModEnabled = Config.Bind("General", "Mod Enabled", true,
                "Master toggle for the entire mod. Set to false to disable every patch, the pocket map, " +
                "pin sharing, auto-recording and the console commands without removing the DLL. " +
                "Not server-synced. Requires a game restart to take effect.");
            if (!ModEnabled.Value)
            {
                Log.LogInfo("[TheGreatestMap] Mod Enabled = false in config; skipping patches and commands.");
                return;
            }

            _configSync = new ConfigSync(ModGuid)
            {
                DisplayName            = ModName,
                CurrentVersion         = ModVersion,
                MinimumRequiredVersion = MinCompatibleVersion,
            };

            // Before the settings, because binding them runs the one-time passes and those ask
            // this what has already been done.
            Progress.Load(Config);
            // Before the first Bind: a config entry's type is fixed the moment it is bound, and
            // BepInEx cannot bind a type it has no converter for.
            Colors.Register();
            TgmConfig.Bind(this);
            WatchConfigFile();
            Commands.Register();
            _harmony.PatchAll();
            Log.LogInfo($"[TheGreatestMap] {ModVersion} loaded.");
        }

        /// <summary>
        /// Read the config file again when it changes on disk, so an edit made in a text editor
        /// while the game is running takes effect. BepInEx reads the file once at startup and
        /// writes it from memory afterwards, so without this an edit is not merely ignored -- it
        /// is overwritten the next time anything saves a setting, which is how an afternoon's
        /// careful catalog edits disappear.
        ///
        /// Settings carry their own change hooks, so a reload rebuilds the catalog, restyles the
        /// markers and repositions the map by itself. Server-synced settings are safe: ServerSync
        /// patches the re-read so a value from the file lands in the local copy rather than over
        /// what the server said.
        /// </summary>
        private FileSystemWatcher _configWatcher;

        private void WatchConfigFile()
        {
            try
            {
                _configWatcher = new FileSystemWatcher(Paths.ConfigPath, Path.GetFileName(Config.ConfigFilePath));
                _configWatcher.Changed += OnConfigFileChanged;
                _configWatcher.Created += OnConfigFileChanged;
                _configWatcher.Renamed += OnConfigFileChanged;
                _configWatcher.SynchronizingObject = ThreadingHelper.SynchronizingObject; // back on the main thread
                _configWatcher.EnableRaisingEvents = true;
            }
            catch (Exception ex)
            {
                Log.LogWarning($"[TheGreatestMap] Cannot watch the config file for changes: {ex.Message}");
            }
        }

        private void OnConfigFileChanged(object sender, FileSystemEventArgs e)
        {
            if (!File.Exists(Config.ConfigFilePath)) return;
            try
            {
                Config.Reload();
                Log.LogInfo("[TheGreatestMap] Config file changed on disk; settings reloaded.");
            }
            catch (Exception ex)
            {
                Log.LogError($"[TheGreatestMap] Config file changed but could not be read: {ex.Message}");
            }
        }

        private void Update()
        {
            if (!ModEnabled.Value) return;
            Watchdog.Update();
            StrayWatch.Update();
            PinStore.Update();
            ServerSave.Update();
            Portals.Update(); // also runs on the dedicated server, which is the source of the list
            Transports.Update(); // likewise: the sweep for ships and carts is the server's
            Builds.Update();     // and the sweep for what players built, which also paints the map
            Portals.Paint();
            MapPause.Refresh(); // also lets go of the pause when the player is gone
            PauseButton.Update();
            if (Player.m_localPlayer == null) return;
            if (Keys.IsDown(TgmConfig.LegendKey.Value) && Keys.CanTakeInput()) LegendPanel.Toggle();
            LegendPanel.Update();
            PicturePanel.Update();
            MarkerMenu.Update();
            MapFrame.Update();
            MarkerToggle.Update();
            OwnMarks.Update();
            ManualPlace.Update();
            ClientPins.AdoptStrays();
            PlayerSpotlight.Update();
            Locate.Paint();
            // After the ping, which is what the survey preview lives inside: when the wave ends,
            // the would-be markers come off the map.
            SurveyPreview.Update();
            PlayerArrow.Update();
            MarkerTooltip.Update();
            Reveals.Update();
            JustCrossedOff.Paint();
            PocketMap.Update();
            MapOutStatus.Update();
            DiscoveryLedger.Update();
            Recorder.Update();
            Tilled.Update();
            Migrations.Update();
            TableSync.Update();
            SyncEngine.Update();
        }

        /// <summary>
        /// Clipping the minimap happens here, after every Update in the frame has run. The game
        /// lays its markers out from Minimap.Update, and other mods add theirs from a patch on that
        /// same pass -- so anything we hid from our own Update was turned back on by whoever ran
        /// after us. That only showed while moving, because the layout pass runs when the map moves
        /// and not while you stand still. LateUpdate is after all of them, whatever the order.
        /// </summary>
        private void LateUpdate()
        {
            if (!ModEnabled.Value) return;
            MinimapClip.Update();
            // After Minimap.Update has moved the map, so the sharp layer is aimed at where the
            // map is this frame rather than where it was last one.
            MapPaint.FollowView();
        }

        private void OnDestroy()
        {
            _harmony.UnpatchSelf();
        }

        /// <summary>Binds a config entry and registers it with ServerSync so the server's value overrides clients.</summary>
        internal ConfigEntry<T> BindSynced<T>(string section, string key, T defaultValue, string description)
        {
            var entry = Config.Bind(section, key, defaultValue, new ConfigDescription(description + " [Synced with Server]"));
            _configSync.AddConfigEntry(entry).SynchronizedConfig = true;
            return entry;
        }

        /// <summary>
        /// Registers the entry that decides whether synced settings are the server's to set. Until
        /// one is registered ServerSync's IsLocked is permanently false, which switches off both
        /// halves of its enforcement: a client broadcasts its own edits to everyone, and the server
        /// skips the admin check on a config package it receives. Synced settings are then merely
        /// shared, and the last player to touch one wins.
        /// </summary>
        internal void LockConfig<T>(ConfigEntry<T> entry) where T : System.IConvertible
        {
            _configSync.AddLockingConfigEntry(entry);
        }

        /// <summary>Binds a client-only config entry.</summary>
        internal ConfigEntry<T> BindLocal<T>(string section, string key, T defaultValue, string description)
        {
            return Config.Bind(section, key, defaultValue, new ConfigDescription(description));
        }

        /// <summary>
        /// A setting that used to live in a different section. BepInEx binds by section and key
        /// together, so moving one leaves the player's line behind as an orphan and the new entry
        /// starts at its default: a tidier config file would quietly switch somebody's settings
        /// back. The old value is carried across once, and only when the new entry is still at its
        /// default, so a choice already made in the new place wins.
        /// </summary>
        /// <summary>
        /// Bind a setting whose default has changed, and carry over anybody who never chose for
        /// themselves.
        ///
        /// <para>A config file is written once and kept forever, so changing a default in the
        /// code reaches nobody who has already played: their file still holds the old value, and
        /// the mod goes on doing the thing that was wrong. A player holding exactly the old
        /// default never chose it -- the file chose it for them -- so they are moved to the new
        /// one and told so. Anybody who picked something of their own keeps it.</para>
        ///
        /// <para>Somebody who deliberately set the old default is overridden once, which is the
        /// price of the other hundred people getting the fix. The log line says what changed and
        /// the setting is still theirs to set back.</para>
        /// </summary>
        internal ConfigEntry<T> BindRetuned<T>(string section, string key, T oldDefault, T defaultValue,
                                               string description, bool synced = false)
        {
            var entry = synced
                ? BindSynced(section, key, defaultValue, description)
                : BindLocal(section, key, defaultValue, description);
            return Retune(entry, oldDefault, defaultValue);
        }

        /// <summary>
        /// The retune on its own, for a setting that has also moved.
        ///
        /// <para>BindMoved and BindRetuned each do one thing to a setting and a setting can need
        /// both, so the part that can be shared is. Order matters and is the caller's to get
        /// right: carry the old value across first, then decide whether it was a choice or a
        /// leftover default, because doing it the other way round retunes a value that has not
        /// arrived yet.</para>
        /// </summary>
        internal ConfigEntry<T> Retune<T>(ConfigEntry<T> entry, T oldDefault, T defaultValue)
        {
            if (!Equals(entry.Value, oldDefault) || Equals(oldDefault, defaultValue)) return entry;
            entry.Value = defaultValue;
            Log.LogInfo($"[TheGreatestMap] \"{entry.Definition.Key}\" has a better default than it used to, so " +
                        $"yours is now {defaultValue} instead of {oldDefault}. Set it yourself if you preferred " +
                        "the old one.");
            return entry;
        }

        internal ConfigEntry<T> BindMoved<T>(string oldSection, string section, string key, T defaultValue, string description,
                                             bool synced = false)
        {
            var entry = synced
                ? BindSynced(section, key, defaultValue, description)
                : BindLocal(section, key, defaultValue, description);
            try
            {
                // ConfigFile.OrphanedEntries is internal; AccessTools so runtime enforcement
                // does not throw, and a BepInEx that renames it costs a log line, not a crash.
                var orphans = HarmonyLib.AccessTools.Property(typeof(ConfigFile), "OrphanedEntries")
                    ?.GetValue(Config) as System.Collections.IDictionary;
                var was = new ConfigDefinition(oldSection, key);
                string raw = orphans != null && orphans.Contains(was) ? orphans[was] as string : null;
                // And take the old one out, or BepInEx writes it back beside the new one and the
                // file ends up with the same setting twice -- the dead copy in the section it
                // used to live in, where somebody will eventually change it and wonder why
                // nothing happens.
                if (orphans != null && orphans.Contains(was)) orphans.Remove(was);
                if (!string.IsNullOrEmpty(raw) && Equals(entry.Value, defaultValue))
                {
                    var carried = (T)TomlTypeConverter.ConvertToValue(raw, typeof(T));
                    if (!Equals(carried, entry.Value))
                    {
                        entry.Value = carried;
                        Log.LogInfo($"[TheGreatestMap] Moved \"{key}\" from [{oldSection}] to [{section}], keeping your {carried}.");
                    }
                }
            }
            catch (Exception e)
            {
                Log.LogWarning($"[TheGreatestMap] Could not carry \"{key}\" over from [{oldSection}]: {e.Message}");
            }
            return entry;
        }

        /// <summary>
        /// Bind a setting that used to go by another name in the same section.
        ///
        /// <para><see cref="BindMoved"/> is the near neighbor and does not fit: it carries the old
        /// value across, which is right for a setting that only changed address. A rename usually
        /// comes with a change of meaning or of default, and here it comes with both -- so the old
        /// value is deliberately dropped and everybody starts at the new default.</para>
        ///
        /// <para>What still has to happen is the cleanup. BepInEx keeps a line it does not
        /// recognize and writes it back on the next save, so without this the file ends up holding
        /// the setting twice: the live one under the new name and a dead one under the old, which
        /// somebody will eventually find and change and then wonder why nothing happens.</para>
        /// </summary>
        internal ConfigEntry<T> BindRenamed<T>(string section, string oldKey, string key, T defaultValue,
                                               string description, bool synced = false)
        {
            var entry = synced
                ? BindSynced(section, key, defaultValue, description)
                : BindLocal(section, key, defaultValue, description);
            try
            {
                var orphans = HarmonyLib.AccessTools.Property(typeof(ConfigFile), "OrphanedEntries")
                    ?.GetValue(Config) as System.Collections.IDictionary;
                var was = new ConfigDefinition(section, oldKey);
                if (orphans != null && orphans.Contains(was)) orphans.Remove(was);
            }
            catch (Exception e)
            {
                Log.LogWarning($"[TheGreatestMap] Could not tidy away the old \"{oldKey}\" line: {e.Message}");
            }
            return entry;
        }

        /// <summary>Binds a client-only integer entry with an allowed range (shown as a slider by config managers).</summary>
        internal ConfigEntry<int> BindLocalRange(string section, string key, int defaultValue, int min, int max, string description)
        {
            return Config.Bind(section, key, defaultValue, new ConfigDescription(description, new AcceptableValueRange<int>(min, max)));
        }

        /// <summary>
        /// Small top-left HUD message, respecting the ShowMessages toggle.
        ///
        /// <para><paramref name="console"/> is for the notes that could not be answering anything
        /// a player typed -- the ones the mod says to itself while you play. Those belong on the
        /// HUD and nowhere else: a nag that repeats while you fight pushes the answer you were
        /// reading off the top of a console that holds a limited scrollback.</para>
        /// </summary>
        internal static void Message(string text, bool console = true)
        {
            // The console first, and not only when messages are switched on. Anything worth
            // telling a player is worth finishing a command they typed: a console command whose
            // answer arrives over the network can only print "working on it..." when it runs,
            // and if the answer then goes to the HUD alone the command appears to hang. Worse,
            // the HUD does not draw while the game is paused -- which is exactly when somebody
            // has the console open.
            if (console) Say(text);
            if (TgmConfig.ShowMessages == null || !TgmConfig.ShowMessages.Value) return;
            if (MessageHud.instance == null) return;
            MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, text);
        }

        /// <summary>
        /// Repeat one of our log lines in the dev console, when asked to.
        ///
        /// Off unless switched on: the console holds a limited scrollback and this mod is
        /// talkative during a conversion. Guarded rather than trusted, because a log line can
        /// arrive from whatever thread did the logging and the console is Unity's.
        /// </summary>
        private static void EchoToConsole(object sender, LogEventArgs e)
        {
            if (TgmConfig.EchoLogToConsole == null || !TgmConfig.EchoLogToConsole.Value) return;
            if (e == null || e.Data == null) return;
            try { Console.instance?.AddString(e.Data.ToString()); } catch (Exception) { /* never worth a crash */ }
        }

        /// <summary>Write a line into the dev console, if one is open to write to.</summary>
        internal static void Say(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            var console = Console.instance;
            if (console == null) return;
            try { console.AddString("[TheGreatestMap] " + text); } catch (Exception) { /* never worth a crash */ }
        }
    }
}
