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
        public const string ModVersion = "1.2.0";

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
            Commands.Register();
            _harmony.PatchAll();
            Log.LogInfo($"[TheGreatestMap] {ModVersion} loaded.");
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
            MarkerToggle.Update();
            MarkerTooltip.Update();
            Reveals.Update();
            PocketMap.Update();
            MapOutStatus.Update();
            DiscoveryLedger.Update();
            Recorder.Update();
            TableSync.Update();
            SyncEngine.Update();
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
