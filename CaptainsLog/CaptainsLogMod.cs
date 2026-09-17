using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace CaptainsLog
{
    // No [BepInProcess] filter on purpose: on a dedicated server (valheim_server.exe or
    // valheim_server.x86_64) the mod logs every player's sailing into one CSV, and BepInEx
    // matches that attribute against the bare process name.
    [BepInPlugin(ModGuid, "Captain's Log", "1.1.0")]
    [BepInDependency(Jotunn.Main.ModGuid)]
    public class CaptainsLogMod : BaseUnityPlugin
    {
        public const string ModGuid = "DeathMonger.CaptainsLog";
        public static CaptainsLogMod Instance;
        internal static ManualLogSource Log;

        public ConfigEntry<bool> LoggingEnabled;
        public ConfigEntry<float> SampleIntervalSeconds;
        public ConfigEntry<bool> ShowHud;
        public ConfigEntry<HudPosition> HudPosition;
        public ConfigEntry<float> HudNudgeX;
        public ConfigEntry<float> HudNudgeY;
        public ConfigEntry<float> HudBackgroundOpacity;
        public ConfigEntry<SpeedUnits> SpeedUnits;
        public ConfigEntry<bool> ShowWindAngle;
        public ConfigEntry<bool> ShowShipName;

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
            HudPosition = Config.Bind("HUD", "Position", CaptainsLog.HudPosition.BelowHotbar,
                new ConfigDescription("BelowHotbar: under the lowest hotbar row, including a second row added by a mod such as AzuExtendedPlayerInventory. " +
                                      "Centered: beside the ship's rudder icon, following the ship, like Azumatt's Ship Stats. " +
                                      "TopLeft: a fixed spot in the upper-left corner."));
            HudNudgeX = Config.Bind("HUD", "Nudge X (px)", 0f,
                new ConfigDescription("Moves the widget from its Position. Positive is right."));
            HudNudgeY = Config.Bind("HUD", "Nudge Y (px)", 0f,
                new ConfigDescription("Moves the widget from its Position. Positive is down."));
            HudBackgroundOpacity = Config.Bind("HUD", "Background Opacity", 0.5f,
                new ConfigDescription("Opacity of the dark panel behind the text. 0 hides it.", new AcceptableValueRange<float>(0f, 1f)));
            SpeedUnits = Config.Bind("HUD", "Speed Units", CaptainsLog.SpeedUnits.Knots,
                new ConfigDescription("Units for ship speed: Knots, MetersPerSecond, or KnotsWithMetersPerSecond (knots, then m/s in parentheses). The CSV always has both."));
            ShowWindAngle = Config.Bind("HUD", "Show Wind Angle", true,
                new ConfigDescription("Show where the wind comes from relative to the ship (astern, port quarter, starboard beam, port bow, ahead...). Not a compass direction."));
            ShowShipName = Config.Bind("HUD", "Show Ship Name", true,
                new ConfigDescription("Show the kind of ship you're aboard (Karve, Longship...) as the first line."));

            harmony.PatchAll();
            Log.LogInfo("Captain's Log armed.");
        }

        private void Update()
        {
            if (ShipTelemetry.IsDedicatedServer) return;
            ShipHud.Tick();
        }

        private void OnDestroy()
        {
            ShipTelemetry.Shutdown();
        }
    }
}
