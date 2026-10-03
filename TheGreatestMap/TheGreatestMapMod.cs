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
        public const string ModVersion = "1.7.5";

        // Oldest version whose shared-marker wire format this build still speaks. ServerSync
        // refuses peers below this, so bump it only when the format or an RPC changes, not on
        // every fix; otherwise every patch would force the server and all players to update
        // at the same moment. 0.2.0: personal maps, merge-based sync, tombstones, new RPCs.
        public const string MinCompatibleVersion = "0.2.0";

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
            PinStore.Update();
            ServerSave.Update();
            Portals.Update(); // also runs on the dedicated server, which is the source of the list
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
