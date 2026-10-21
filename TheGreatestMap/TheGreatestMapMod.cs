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
        public const string ModVersion = "3.2.1";

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
        // Deliberately the floor and not ModVersion: a patch must not lock out the players who
        // installed the release before it. This rises only when the format does.
        public const string MinCompatibleVersion = "3.0.0";

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
            PinStore.Update();
            ServerSave.Update();
            Portals.Update(); // also runs on the dedicated server, which is the source of the list
            Transports.Update(); // likewise: the sweep for ships and carts is the server's
            Portals.Paint();
            MapPause.Refresh(); // also lets go of the pause when the player is gone
            PauseButton.Update();
            if (Player.m_localPlayer == null) return;
            if (Keys.IsDown(TgmConfig.LegendKey.Value) && Keys.CanTakeInput()) LegendPanel.Toggle();
            LegendPanel.Update();
            MarkerMenu.Update();
            MapFrame.Update();
            MarkerToggle.Update();
            PlayerSpotlight.Update();
            Locate.Paint();
            PlayerArrow.Update();
            MarkerTooltip.Update();
            Reveals.Update();
            PocketMap.Update();
            MapOutStatus.Update();
            DiscoveryLedger.Update();
            Recorder.Update();
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
        internal ConfigEntry<T> BindMoved<T>(string oldSection, string section, string key, T defaultValue, string description)
        {
            var entry = BindLocal(section, key, defaultValue, description);
            try
            {
                // ConfigFile.OrphanedEntries is internal; AccessTools so runtime enforcement
                // does not throw, and a BepInEx that renames it costs a log line, not a crash.
                var orphans = HarmonyLib.AccessTools.Property(typeof(ConfigFile), "OrphanedEntries")
                    ?.GetValue(Config) as System.Collections.IDictionary;
                var was = new ConfigDefinition(oldSection, key);
                string raw = orphans != null && orphans.Contains(was) ? orphans[was] as string : null;
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

        /// <summary>Binds a client-only integer entry with an allowed range (shown as a slider by config managers).</summary>
        internal ConfigEntry<int> BindLocalRange(string section, string key, int defaultValue, int min, int max, string description)
        {
            return Config.Bind(section, key, defaultValue, new ConfigDescription(description, new AcceptableValueRange<int>(min, max)));
        }

        /// <summary>Small top-left HUD message, respecting the ShowMessages toggle.</summary>
        internal static void Message(string text)
        {
            if (TgmConfig.ShowMessages == null || !TgmConfig.ShowMessages.Value) return;
            if (MessageHud.instance == null) return;
            MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, text);
        }
    }
}
