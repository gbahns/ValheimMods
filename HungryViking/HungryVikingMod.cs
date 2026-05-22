using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace HungryViking
{
    [BepInPlugin(ModGuid, "Hungry Viking", "1.1.0")]
    [BepInProcess("valheim.exe")]
    public class HungryVikingMod : BaseUnityPlugin
    {
        public const string ModGuid = "DeathMonger.HungryViking";
        private readonly Harmony harmony = new Harmony(ModGuid);

        public static HungryVikingMod Instance { get; private set; }
        public static BepInEx.Logging.ManualLogSource Log => Instance.Logger;

        public ConfigEntry<bool>  Enabled;
        public ConfigEntry<bool>  ShowStatusIcon;
        public ConfigEntry<float> HungerThreshold;
        public ConfigEntry<float> VignetteIntensity;
        public ConfigEntry<float> VignetteExtent;
        public ConfigEntry<float> SmokedVignetteIntensity;
        public ConfigEntry<float> SmokedVignetteExtent;
        public ConfigEntry<float> PoisonedVignetteIntensity;
        public ConfigEntry<float> PoisonedVignetteExtent;

        private FoodMonitor        _foodMonitor;
        private VignetteOverlay    _vignette;
        private SmokedOverlay      _smokedOverlay;
        private SmokedOverlay      _poisonedOverlay;
        private HungerStatusEffect _statusEffect;
        private StatusEffect       _activeStatusEffect;
        private Player             _statusEffectPlayer;

        // Hashes confirmed via hv_selist against a live game instance.
        private const int SmokedHash   = -1612278721; // "$se_smoked_name"
        private const int PoisonedHash = 0;           // TODO: run hv_selist while poisoned to confirm

        private bool  _smokedTestActive;
        private bool  _poisonedTestActive;
        private bool  _hungerTestActive;
        private float _hungerPreviewTimer;
        private float _smokedPreviewTimer;
        private float _poisonedPreviewTimer;

        public string HungerMessage { get; private set; } = "";

        private void Awake()
        {
            Instance = this;
            InitConfig();
            harmony.PatchAll();

            _vignette        = gameObject.AddComponent<VignetteOverlay>();
            _smokedOverlay   = gameObject.AddComponent<SmokedOverlay>();
            _poisonedOverlay = gameObject.AddComponent<SmokedOverlay>();
            _poisonedOverlay.SetLabelBaseColor(new Color(0.2f, 0.75f, 0.2f, 1f));
            _foodMonitor  = new FoodMonitor(this);
            _statusEffect = HungerStatusEffect.Create();

            _foodMonitor.OnWarning  += () => Logger.LogInfo("HungryViking: entered hunger window");
            _foodMonitor.OnStarving += () => Logger.LogInfo("HungryViking: slot expired");

            VignetteIntensity.SettingChanged        += (_, __) => _hungerPreviewTimer   = 2f;
            VignetteExtent.SettingChanged           += (_, __) => _hungerPreviewTimer   = 2f;
            SmokedVignetteIntensity.SettingChanged  += (_, __) => _smokedPreviewTimer   = 2f;
            SmokedVignetteExtent.SettingChanged     += (_, __) => _smokedPreviewTimer   = 2f;
            PoisonedVignetteIntensity.SettingChanged += (_, __) => _poisonedPreviewTimer = 2f;
            PoisonedVignetteExtent.SettingChanged   += (_, __) => _poisonedPreviewTimer = 2f;

            InitCommands();

            Logger.LogInfo($"Hungry Viking loaded. HungerThreshold={HungerThreshold.Value}s Intensity={VignetteIntensity.Value}");
        }

        private void InitCommands()
        {
            new Terminal.ConsoleCommand("hv_clearfood",
                "[Hungry Viking] Instantly removes all food buffs.",
                _ =>
                {
                    var player = Player.m_localPlayer;
                    if (player == null) return;
                    FoodMonitor.ClearFoods(player);
                    Log.LogInfo("hv_clearfood: all food buffs removed.");
                }, isCheat: true);

            new Terminal.ConsoleCommand("hv_drainfood",
                "[Hungry Viking] hv_drainfood <slot 1-3> [seconds=60] — subtracts seconds from one food slot.",
                args =>
                {
                    var player = Player.m_localPlayer;
                    if (player == null) return;
                    if (args.Length < 2 || !TryParseSlot(args[1], out int idx))
                    {
                        Log.LogInfo("Usage: hv_drainfood <1|2|3> [seconds]");
                        return;
                    }
                    float drain = args.Length >= 3 && float.TryParse(args[2], out float d) ? d : 60f;
                    Log.LogInfo(FoodMonitor.DrainFood(player, idx, drain));
                }, isCheat: true);

            new Terminal.ConsoleCommand("hv_drainfoods",
                "[Hungry Viking] hv_drainfoods [seconds=60] — subtracts seconds from all food slots.",
                args =>
                {
                    var player = Player.m_localPlayer;
                    if (player == null) return;
                    float drain = args.Length >= 2 && float.TryParse(args[1], out float d) ? d : 60f;
                    Log.LogInfo(FoodMonitor.DrainAllFoods(player, drain));
                }, isCheat: true);

            new Terminal.ConsoleCommand("hv_setfoodtime",
                "[Hungry Viking] hv_setfoodtime <slot 1-3> <seconds> — sets a slot to exactly X seconds remaining.",
                args =>
                {
                    var player = Player.m_localPlayer;
                    if (player == null) return;
                    if (args.Length < 3 || !TryParseSlot(args[1], out int idx) || !float.TryParse(args[2], out float secs))
                    {
                        Log.LogInfo("Usage: hv_setfoodtime <1|2|3> <seconds>");
                        return;
                    }
                    Log.LogInfo(FoodMonitor.SetFoodTime(player, idx, secs));
                }, isCheat: true);

            new Terminal.ConsoleCommand("hv_foodstatus",
                "[Hungry Viking] Prints the name and remaining time of each food slot.",
                _ =>
                {
                    var player = Player.m_localPlayer;
                    if (player == null) return;
                    Log.LogInfo(FoodMonitor.GetFoodStatus(player));
                }, isCheat: true);

            new Terminal.ConsoleCommand("hv_selist",
                "[Hungry Viking] Lists all active status effects on the local player with their name hashes.",
                _ =>
                {
                    var player = Player.m_localPlayer;
                    if (player == null) return;
                    var ses = player.GetSEMan().GetStatusEffects();
                    if (ses.Count == 0) { Log.LogInfo("No active status effects."); return; }
                    var sb = new System.Text.StringBuilder();
                    foreach (var se in ses)
                        sb.AppendLine($"  \"{se.m_name}\"  hash={se.NameHash()}");
                    sb.AppendLine($"  HaveStatusEffect(SmokedHash={SmokedHash}) = {player.GetSEMan().HaveStatusEffect(SmokedHash)}");
                    Log.LogInfo(sb.ToString().TrimEnd());
                }, isCheat: true);

            new Terminal.ConsoleCommand("hv_testhunger",
                "[Hungry Viking] Toggles the hunger vignette on/off for visual testing. Also triggers automatically for 2 seconds when Intensity or Extent config values change.",
                _ =>
                {
                    _hungerTestActive = !_hungerTestActive;
                    Log.LogInfo($"hv_testhunger: hunger overlay {(_hungerTestActive ? "ON" : "OFF")}");
                });

            new Terminal.ConsoleCommand("hv_testsmoked",
                "[Hungry Viking] Toggles the smoked vignette overlay on/off for visual testing.",
                _ =>
                {
                    _smokedTestActive = !_smokedTestActive;
                    Log.LogInfo($"hv_testsmoked: smoked overlay {(_smokedTestActive ? "ON" : "OFF")}");
                });

            new Terminal.ConsoleCommand("hv_testpoisoned",
                "[Hungry Viking] Toggles the poisoned vignette overlay on/off for visual testing.",
                _ =>
                {
                    _poisonedTestActive = !_poisonedTestActive;
                    Log.LogInfo($"hv_testpoisoned: poisoned overlay {(_poisonedTestActive ? "ON" : "OFF")}");
                });
        }

        private static bool TryParseSlot(string s, out int zeroBasedIndex)
        {
            if (int.TryParse(s, out int slot) && slot >= 1 && slot <= 3)
            {
                zeroBasedIndex = slot - 1;
                return true;
            }
            zeroBasedIndex = -1;
            return false;
        }

        private void InitConfig()
        {
            Enabled = Config.Bind("General", "Enabled", true,
                "Enable or disable all Hungry Viking effects.");

            ShowStatusIcon = Config.Bind("Hunger", "Show Status Icon",
                MigrateBool("General", "Show Status Icon", true),
                "Show the Hunger status icon in the HUD alongside Rested, Shelter, etc.");

            HungerThreshold = Config.Bind("Hunger", "Hunger Threshold (seconds)",
                MigrateFloat("General", "Hunger Threshold (seconds)", 90f),
                "Seconds remaining on a food buff when the warning begins. Vignette and label fade in from here.");

            VignetteIntensity = Config.Bind("Hunger", "Vignette Intensity",
                MigrateFloat("General", "Vignette Intensity", 0.20f),
                new ConfigDescription(
                    "Maximum vignette edge opacity. 0 = off, 1 = fully opaque.",
                    new AcceptableValueRange<float>(0f, 1f)));

            VignetteExtent = Config.Bind("Hunger", "Vignette Extent",
                MigrateFloat("General", "Vignette Extent", 0.50f),
                new ConfigDescription(
                    "How far the vignette extends from the screen edge toward the center. 0 = not visible, 1 = covers the full screen.",
                    new AcceptableValueRange<float>(0f, 1f)));

            SmokedVignetteIntensity = Config.Bind("Smoked", "Vignette Intensity", 0.25f,
                new ConfigDescription(
                    "Maximum opacity of the center smoke vignette. 0 = off, 1 = fully opaque.",
                    new AcceptableValueRange<float>(0f, 1f)));

            SmokedVignetteExtent = Config.Bind("Smoked", "Vignette Extent", 1.00f,
                new ConfigDescription(
                    "How far from the screen center the smoke vignette reaches. 0 = invisible, 1 = covers the full screen.",
                    new AcceptableValueRange<float>(0f, 1f)));

            PoisonedVignetteIntensity = Config.Bind("Poisoned", "Vignette Intensity", 0.25f,
                new ConfigDescription(
                    "Maximum opacity of the center poison vignette. 0 = off, 1 = fully opaque.",
                    new AcceptableValueRange<float>(0f, 1f)));

            PoisonedVignetteExtent = Config.Bind("Poisoned", "Vignette Extent", 0.55f,
                new ConfigDescription(
                    "How far from the screen center the poison vignette reaches. 0 = invisible, 1 = covers the full screen.",
                    new AcceptableValueRange<float>(0f, 1f)));

            Config.Save();
        }

        // Reads key=value pairs from a section of the BepInEx INI config file on disk.
        // Used once at startup to migrate values from renamed sections.
        private System.Collections.Generic.Dictionary<string, string> _migrationCache;

        private string MigrateRaw(string section, string key)
        {
            if (_migrationCache == null)
            {
                _migrationCache = new System.Collections.Generic.Dictionary<string, string>(
                    System.StringComparer.OrdinalIgnoreCase);

                if (System.IO.File.Exists(Config.ConfigFilePath))
                {
                    string currentSection = null;
                    foreach (var line in System.IO.File.ReadAllLines(Config.ConfigFilePath))
                    {
                        var t = line.Trim();
                        if (t.StartsWith("[") && t.EndsWith("]"))
                        {
                            currentSection = t.Substring(1, t.Length - 2).Trim();
                            continue;
                        }
                        if (currentSection == null || t.StartsWith("#") || t.StartsWith("//")) continue;
                        int eq = t.IndexOf('=');
                        if (eq < 0) continue;
                        _migrationCache[$"{currentSection}/{t.Substring(0, eq).Trim()}"] = t.Substring(eq + 1).Trim();
                    }
                }
            }
            return _migrationCache.TryGetValue($"{section}/{key}", out string val) ? val : null;
        }

        private float MigrateFloat(string section, string key, float fallback)
        {
            var raw = MigrateRaw(section, key);
            return raw != null && float.TryParse(raw, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : fallback;
        }

        private bool MigrateBool(string section, string key, bool fallback)
        {
            var raw = MigrateRaw(section, key);
            return raw != null && bool.TryParse(raw, out bool v) ? v : fallback;
        }

        private void Update()
        {
            if (!Enabled.Value || Player.m_localPlayer == null)
            {
                if (_activeStatusEffect != null && Player.m_localPlayer != null)
                    Player.m_localPlayer.GetSEMan().RemoveStatusEffect(_activeStatusEffect.NameHash(), false);
                _activeStatusEffect = null;
                _vignette.SetBase(0f, Color.black);
                _vignette.SetHungerLabel(null);
                _smokedOverlay.SetBase(0f, Color.white);
                _smokedOverlay.SetLabel(null);
                _poisonedOverlay.SetBase(0f, Color.white);
                _poisonedOverlay.SetLabel(null);
                return;
            }

            var player = Player.m_localPlayer;

            if (player != _statusEffectPlayer)
            {
                _activeStatusEffect = null;
                _statusEffectPlayer = player;
            }

            _vignette.SetInnerBoundary(1.0f - VignetteExtent.Value);
            _foodMonitor.Tick(Time.deltaTime);

            bool  anyEmpty    = _foodMonitor.CurrentFoodCount < 3;
            float realUrgency = anyEmpty ? 1f : _foodMonitor.WorstSlotUrgency;

            if (_hungerPreviewTimer > 0f) _hungerPreviewTimer -= Time.deltaTime;
            bool  hungerTest     = _hungerTestActive || _hungerPreviewTimer > 0f;
            float displayUrgency = hungerTest ? 1f : realUrgency;

            if (displayUrgency > 0f)
                _vignette.SetBase(VignetteIntensity.Value * displayUrgency, new Color(1f, 0.8f, 0.15f));
            else
                _vignette.SetBase(0f, Color.black);

            string label = null;
            if (hungerTest || anyEmpty)
                label = "You are hungry.";
            else if (realUrgency >= 0.5f)
                label = "You are getting hungry.";
            else if (realUrgency > 0f)
                label = "You are starting to feel hungry.";

            _vignette.SetHungerLabel(label, displayUrgency);

            UpdateSmokedOverlay(player);
            UpdatePoisonedOverlay(player);

            // Status effect driven by real hunger state only, not test mode.
            HungerMessage   = anyEmpty ? "Hungry" : "Getting Hungry";
            bool showEffect = (realUrgency > 0f || anyEmpty) && ShowStatusIcon.Value;

            if (showEffect && _activeStatusEffect == null)
                _activeStatusEffect = player.GetSEMan().AddStatusEffect(_statusEffect, false);
            else if (!showEffect && _activeStatusEffect != null)
            {
                player.GetSEMan().RemoveStatusEffect(_activeStatusEffect.NameHash(), false);
                _activeStatusEffect = null;
            }
        }

        private void UpdateSmokedOverlay(Player player)
        {
            if (_smokedPreviewTimer > 0f) _smokedPreviewTimer -= Time.deltaTime;
            bool isSmoked = _smokedTestActive || _smokedPreviewTimer > 0f
                         || player.GetSEMan().HaveStatusEffect(SmokedHash);

            _smokedOverlay.SetOuterBoundary(SmokedVignetteExtent.Value);

            if (isSmoked)
            {
                _smokedOverlay.SetBase(SmokedVignetteIntensity.Value, new Color(0.85f, 0.85f, 0.85f));
                _smokedOverlay.SetLabel("You can't breathe in the smoke!", 1f);
            }
            else
            {
                _smokedOverlay.SetBase(0f, Color.white);
                _smokedOverlay.SetLabel(null);
            }
        }

        private void UpdatePoisonedOverlay(Player player)
        {
            if (_poisonedPreviewTimer > 0f) _poisonedPreviewTimer -= Time.deltaTime;
            bool isPoisoned = _poisonedTestActive || _poisonedPreviewTimer > 0f
                           || (PoisonedHash != 0 && player.GetSEMan().HaveStatusEffect(PoisonedHash));

            _poisonedOverlay.SetOuterBoundary(PoisonedVignetteExtent.Value);

            if (isPoisoned)
            {
                _poisonedOverlay.SetBase(PoisonedVignetteIntensity.Value, new Color(0.2f, 0.8f, 0.2f));
                _poisonedOverlay.SetLabel("You are poisoned.", 1f);
            }
            else
            {
                _poisonedOverlay.SetBase(0f, Color.white);
                _poisonedOverlay.SetLabel(null);
            }
        }

        private void OnDestroy()
        {
            harmony.UnpatchSelf();
        }
    }
}
