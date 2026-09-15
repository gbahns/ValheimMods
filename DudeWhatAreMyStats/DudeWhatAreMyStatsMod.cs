using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace DudeWhatAreMyStats
{
    /// <summary>
    /// Dude, What Are My Stats? — your Valheim stats on a key press, and a scoreboard of everyone
    /// else online.
    ///
    ///  * One key (default I) opens the panel wherever you are. No inventory, no menus.
    ///  * The game pauses while you read, exactly as far as the ESC menu would: alone in a solo
    ///    or hosted game it freezes; on a busy server it does not.
    ///  * The Scoreboard tab lists every player online who also runs this mod, with kills, deaths,
    ///    K/D, bosses, time played and their best skill. Click a column to sort by it.
    ///  * The Details tab breaks one player's stats out by section, with their skills and the
    ///    creatures they have killed most.
    ///
    /// The server is optional. Players who are online answer for themselves over a routed RPC,
    /// which an unmodded server forwards without needing the mod, so the client alone is enough to
    /// compare everyone currently playing. Install it on the server as well and it also remembers
    /// each character's last known stats, which is what puts players who are not online on the
    /// board. Without it they simply do not appear.
    /// </summary>
    [BepInPlugin(ModGuid, ModName, ModVersion)]
    public class DudeWhatAreMyStatsMod : BaseUnityPlugin
    {
        public const string ModGuid    = "DeathMonger.DudeWhatAreMyStats";
        public const string ModName    = "Dude What Are My Stats";
        public const string ModVersion = "0.2.0";

        internal static DudeWhatAreMyStatsMod Instance { get; private set; }
        internal static ManualLogSource Log { get; private set; }

        // Master toggle, bound first, so a player who runs into a problem can switch the mod off in
        // BepInEx/config/DeathMonger.DudeWhatAreMyStats.cfg without removing the DLL.
        internal static ConfigEntry<bool> ModEnabled;

        private readonly Harmony _harmony = new Harmony(ModGuid);

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            ModEnabled = Config.Bind("General", "Mod Enabled", true,
                "Master toggle for the entire mod. Set to false to disable every patch, the stats panel and the " +
                "console command without removing the DLL. Requires a game restart to take effect.");
            if (!ModEnabled.Value)
            {
                Log.LogInfo("[DudeWhatAreMyStats] Mod Enabled = false in config; skipping patches and commands.");
                return;
            }

            DwamsConfig.Bind(this);
            Commands.Register();
            _harmony.PatchAll();
            Log.LogInfo($"[DudeWhatAreMyStats] {ModVersion} loaded.");
        }

        private void Update()
        {
            if (!ModEnabled.Value) return;
            StatsNetwork.Update();
            StatsStore.Update();
            if (Player.m_localPlayer == null)
            {
                // Logged out with the panel up: drop the pause and the roster with the world.
                StatsPanel.Close();
                return;
            }
            StatsPanel.Update();
        }

        private void OnDestroy()
        {
            StatsPause.Release();
            _harmony.UnpatchSelf();
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

        /// <summary>Small top-left HUD message.</summary>
        internal static void Message(string text)
        {
            if (MessageHud.instance == null) return;
            MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, text);
        }
    }
}
