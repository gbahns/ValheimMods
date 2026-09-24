using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace DiagnoseServerLag
{
    /// <summary>
    /// Diagnose Server Lag â€” says which of the six unrelated things called "lag" is actually
    /// happening, and shows the numbers it decided on.
    ///
    /// A starved server, a saturated link, a lossy connection, an overloaded base, a world still
    /// loading and a computer that cannot keep up all feel identical while playing: the game goes
    /// sticky. The fixes are unrelated, so guessing wrong costs an evening. This mod measures both
    /// ends once a second and names the cause.
    ///
    /// Install on the server and on every client. The server half is the point: a client watching
    /// its own frame times can tell that the game feels bad, and only the server's own tick times
    /// can tell it whether the server was keeping up at the time â€” which is the one question that
    /// decides whether anything on your machine is worth changing. A server without the mod simply
    /// never answers, and clients say so and fall back to diagnosing their own end alone.
    /// </summary>
    // No [BepInProcess] filter on purpose: the mod must load in the client, the Windows dedicated
    // server (valheim_server.exe) and the Linux one (valheim_server.x86_64), and BepInEx matches
    // that attribute against the bare process name.
    [BepInPlugin(ModGuid, ModName, ModVersion)]
    public class DiagnoseServerLagMod : BaseUnityPlugin
    {
        public const string ModGuid    = "DeathMonger.DiagnoseServerLag";
        public const string ModName    = "Diagnose Server Lag";
        public const string ModVersion = "0.7.1";

        internal static DiagnoseServerLagMod Instance { get; private set; }
        internal static ManualLogSource Log { get; private set; }

        // Master toggle, bound first, so a player who runs into a problem can switch the mod off in
        // BepInEx/config/DeathMonger.DiagnoseServerLag.cfg without removing the DLL.
        internal static ConfigEntry<bool> ModEnabled;

        private readonly Harmony _harmony = new Harmony(ModGuid);

        /// <summary>Whether a world was live last frame, so leaving one can be noticed and cleared.</summary>
        private bool _wasInWorld;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            ModEnabled = Config.Bind("General", "Mod Enabled", true,
                "Master toggle for the entire mod. Set to false to disable the measurements, the report and " +
                "the console commands without removing the DLL. Requires a game restart to take effect.");
            if (!ModEnabled.Value)
            {
                Log.LogInfo("[DiagnoseServerLag] Mod Enabled = false in config; measuring nothing.");
                return;
            }

            DslConfig.Bind(this);
            // The ring is built at type initialisation, before any of that existed; size it now.
            Sampler.ApplyCapacity(DslConfig.HistoryMinutes.Value * 60);
            Commands.Register();
            _harmony.PatchAll();
            Log.LogInfo($"[DiagnoseServerLag] {ModVersion} loaded.");
        }

        private void Update()
        {
            if (!ModEnabled.Value) return;

            bool inWorld = ZNet.instance != null;
            if (_wasInWorld && !inWorld)
            {
                // The measurements describe a world and a connection, both of which have just gone.
                // Keeping them would let the next session open onto the last one's verdict.
                Sampler.Reset();
                LagNetwork.Reset();
                LagPanel.Close();
                LagPause.Release();
            }
            _wasInWorld = inWorld;

            Sampler.Tick();
            LagNetwork.Update();

            // A dedicated server has no player, no canvas and no keyboard; everything below is the
            // client half and stops here on the server without needing a process check.
            if (Player.m_localPlayer == null) { LagPanel.Close(); LagPause.Release(); return; }
            LagPanel.Update();
            LagHud.Update();
            // Every frame, not just on open and close: the re-assert that keeps another mod's
            // Game.Unpause() from silently dropping our pause lives in here. See LagPause.Refresh.
            LagPause.Refresh();
        }

        private void OnDestroy()
        {
            LagPause.Release();
            _harmony.UnpatchSelf();
        }

        /// <summary>Binds a config entry.</summary>
        internal ConfigEntry<T> Bind<T>(string section, string key, T defaultValue, string description)
        {
            return Config.Bind(section, key, defaultValue, new ConfigDescription(description));
        }

        /// <summary>
        /// Binds a keybind, and replaces a stored value that is still an old release's default with
        /// the current one.
        ///
        /// BepInEx writes every default into the .cfg the first time a mod runs, so once a player
        /// has launched the game the old default is a value in their config file and a new default
        /// in the code reaches nobody who already installed the mod - which is everyone who has it.
        /// That is the whole reason 0.1.0's clashing F10 needed this rather than a one-line edit.
        /// A key the player actually chose is never touched.
        ///
        /// KeyboardShortcut implements Equals but defines no == operator, so the comparison has to
        /// be the method.
        /// </summary>
        internal ConfigEntry<KeyboardShortcut> BindKey(string section, string key, KeyboardShortcut defaultValue,
                                                       string description, params KeyboardShortcut[] supersededDefaults)
        {
            var entry = Config.Bind(section, key, defaultValue, new ConfigDescription(description));
            foreach (var superseded in supersededDefaults)
            {
                if (!entry.Value.Equals(superseded)) continue;
                entry.Value = defaultValue;
                Log.LogInfo($"[DiagnoseServerLag] Config '{key}' still held the old default {superseded}; moved it to {defaultValue}.");
                break;
            }
            return entry;
        }

        /// <summary>Binds a float entry with an allowed range (shown as a slider by config managers).</summary>
        internal ConfigEntry<float> BindRange(string section, string key, float defaultValue, float min, float max, string description)
        {
            return Config.Bind(section, key, defaultValue, new ConfigDescription(description, new AcceptableValueRange<float>(min, max)));
        }

        /// <summary>Binds an integer entry with an allowed range.</summary>
        internal ConfigEntry<int> BindRangeInt(string section, string key, int defaultValue, int min, int max, string description)
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
