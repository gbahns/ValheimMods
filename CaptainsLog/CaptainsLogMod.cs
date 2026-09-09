using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace CaptainsLog
{
    [BepInPlugin(ModGuid, "Captain's Log", "1.0.0")]
    [BepInProcess("valheim.exe")]
    [BepInDependency(Jotunn.Main.ModGuid)]
    public class CaptainsLogMod : BaseUnityPlugin
    {
        public const string ModGuid = "DeathMonger.CaptainsLog";
        public static CaptainsLogMod Instance;
        internal static ManualLogSource Log;

        public ConfigEntry<bool> LoggingEnabled;
        public ConfigEntry<float> SampleIntervalSeconds;
        public ConfigEntry<bool> ShowHud;
        public ConfigEntry<float> HudOffsetX;
        public ConfigEntry<float> HudOffsetY;

        private readonly Harmony harmony = new Harmony(ModGuid);

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            LoggingEnabled = Config.Bind("Logging", "Enabled", true,
                new ConfigDescription("Log ship telemetry (speed, wind, heading) to a CSV file while sailing."));
            SampleIntervalSeconds = Config.Bind("Logging", "Sample Interval (s)", 0.5f,
                new ConfigDescription("Minimum time between logged samples.", new AcceptableValueRange<float>(0.1f, 5f)));

            ShowHud = Config.Bind("HUD", "Enabled", true,
                new ConfigDescription("Show a small HUD widget with live ship speed and wind data while sailing."));
            HudOffsetX = Config.Bind("HUD", "Offset X (px)", 10f,
                new ConfigDescription("Horizontal offset from the upper-left corner of the screen."));
            HudOffsetY = Config.Bind("HUD", "Offset Y (px)", 40f,
                new ConfigDescription("Vertical offset from the top of the screen."));

            harmony.PatchAll();
            Log.LogInfo("Captain's Log armed.");
        }

        private void Update()
        {
            ShipHud.Tick();
        }

        private void OnDestroy()
        {
            ShipTelemetry.Shutdown();
        }
    }
}
