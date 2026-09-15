using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using ServerSync;

namespace TheGreatestPortal
{
    /// <summary>
    /// The Greatest Portal: choose where each portal goes.
    ///
    ///  * Press Use on a portal to name it and pick its destination from a list of every portal
    ///    in the world, or pick the destination by clicking a portal on the map.
    ///  * Mark a portal as the default and every portal you build afterwards connects to it.
    ///  * Leave a portal open (no destination) and stepping in shows the map: click any portal,
    ///    on the map or in the list on the left, and you are there. Favorites sit at the top.
    ///
    /// The server holds the truth: names, destinations and the vanilla portal connections that
    /// carry the actual teleport. Install on the server and on every client.
    /// </summary>
    // No [BepInProcess] filter on purpose: the mod must load in the client, the Windows
    // dedicated server (valheim_server.exe) and the Linux one (valheim_server.x86_64), and
    // BepInEx matches that attribute against the bare process name.
    [BepInPlugin(ModGuid, ModName, ModVersion)]
    public class TheGreatestPortalMod : BaseUnityPlugin
    {
        public const string ModGuid    = "DeathMonger.TheGreatestPortal";
        public const string ModName    = "The Greatest Portal";
        public const string ModVersion = "0.3.2";

        // Oldest version whose network messages this build still speaks. ServerSync refuses
        // peers below this, so bump it only when a message format changes.
        public const string MinCompatibleVersion = "0.1.0";

        internal static TheGreatestPortalMod Instance { get; private set; }
        internal static ManualLogSource Log { get; private set; }

        // Master toggle, bound first and deliberately not server-synced, so a player who runs
        // into a problem can switch the mod off in BepInEx/config/DeathMonger.TheGreatestPortal.cfg
        // without removing the DLL.
        internal static ConfigEntry<bool> ModEnabled;

        private readonly Harmony _harmony = new Harmony(ModGuid);
        private static ConfigSync _configSync;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            ModEnabled = Config.Bind("General", "Mod Enabled", true,
                "Master toggle for the entire mod. Set to false to disable every patch, the portal panel, the " +
                "map picker and the console commands without removing the DLL. Portals then behave as vanilla " +
                "(tag pairing) again. Not server-synced. Requires a game restart to take effect.");
            if (!ModEnabled.Value)
            {
                Log.LogInfo("[TheGreatestPortal] Mod Enabled = false in config; skipping patches and commands.");
                return;
            }

            _configSync = new ConfigSync(ModGuid)
            {
                DisplayName            = ModName,
                CurrentVersion         = ModVersion,
                MinimumRequiredVersion = MinCompatibleVersion,
                ModRequired            = true,
            };

            TgpConfig.Bind(this);
            Commands.Register();
            _harmony.PatchAll();
            Log.LogInfo($"[TheGreatestPortal] {ModVersion} loaded.");
        }

        private void Update()
        {
            if (!ModEnabled.Value) return;
            PortalNetwork.Update();
            if (Player.m_localPlayer == null) return;
            PortalPanel.Update();
            MapPicker.Update();
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

        /// <summary>Binds a server-synced integer entry with an allowed range.</summary>
        internal ConfigEntry<int> BindSyncedRange(string section, string key, int defaultValue, int min, int max, string description)
        {
            var entry = Config.Bind(section, key, defaultValue,
                new ConfigDescription(description + " [Synced with Server]", new AcceptableValueRange<int>(min, max)));
            _configSync.AddConfigEntry(entry).SynchronizedConfig = true;
            return entry;
        }

        /// <summary>Binds a client-only config entry.</summary>
        internal ConfigEntry<T> BindLocal<T>(string section, string key, T defaultValue, string description)
        {
            return Config.Bind(section, key, defaultValue, new ConfigDescription(description));
        }

        /// <summary>Binds a client-only float entry with an allowed range (shown as a slider by config managers).</summary>
        internal ConfigEntry<float> BindLocalRange(string section, string key, float defaultValue, float min, float max, string description)
        {
            return Config.Bind(section, key, defaultValue, new ConfigDescription(description, new AcceptableValueRange<float>(min, max)));
        }

        /// <summary>Binds a client-only integer entry with an allowed range.</summary>
        internal ConfigEntry<int> BindLocalRangeInt(string section, string key, int defaultValue, int min, int max, string description)
        {
            return Config.Bind(section, key, defaultValue, new ConfigDescription(description, new AcceptableValueRange<int>(min, max)));
        }

        /// <summary>Small top-left HUD message. Respects the Show Messages toggle unless <paramref name="always"/>.</summary>
        internal static void Message(string text, bool always = false)
        {
            if (!always && (TgpConfig.ShowMessages == null || !TgpConfig.ShowMessages.Value)) return;
            if (MessageHud.instance == null) return;
            MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, text);
        }
    }
}
