using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Managers;

namespace Armory
{
    [BepInPlugin(ModGuid, "Armory", "1.0.0")]
    [BepInProcess("valheim.exe")]
    [BepInDependency("com.jotunn.jotunn")]
    public class ArmoryMod : BaseUnityPlugin
    {
        public const string ModGuid = "GBahns.Armory";

        private readonly Harmony _harmony = new Harmony(ModGuid);
        public static ArmoryMod Instance;

        // Master toggle.  Bound first so users can edit BepInEx/config/GBahns.Armory.cfg to
        // disable the mod without removing the DLL.  When false we skip Harmony patching,
        // piece registration, and the UI tick — effectively a no-op load.
        public static ConfigEntry<bool> ModEnabled;
        private bool _initialized;

        // Public so ArmoryUI.Open/Close can read the current value.
        public static ConfigEntry<bool> PauseGameWhileOpen;
        public static ConfigEntry<bool> ShowSummaryText;
        public static ConfigEntry<bool> ShowIcons;

        // Persisted main-panel position so it survives across game sessions (logins).
        // Auto-updated when the player closes the rack; defaults place the panel to the right
        // of the vanilla inventory.  Float pair instead of Vector2 since BepInEx's TomlTypeConverter
        // for UnityEngine.Vector2 isn't guaranteed to be registered in this runtime.
        public static ConfigEntry<float> PanelPosX;
        public static ConfigEntry<float> PanelPosY;

        private void Awake()
        {
            Instance = this;

            // Bind ALL config entries up-front (even when disabled) so users see every option
            // in the .cfg file and can re-enable without first toggling other settings.
            ModEnabled = Config.Bind(
                section:     "General",
                key:         "Mod Enabled",
                defaultValue: true,
                description: "Master toggle for the entire mod. Set to false to disable the Armory " +
                             "Rack piece, the UI, and all Harmony patches without removing the DLL. " +
                             "Useful for quickly isolating mod conflicts.  Requires a game restart " +
                             "to take effect.");

            PauseGameWhileOpen = Config.Bind(
                section:     "General",
                key:         "Pause Game While Armory Open",
                defaultValue: false,
                description: "Pause the game (Time.timeScale = 0) while the Armory Rack panel is open. " +
                             "Intended for singleplayer — in multiplayer this only pauses your local clock, " +
                             "which can desync you from the server.");

            ShowSummaryText = Config.Bind(
                section:     "UI",
                key:         "Show Summary Text",
                defaultValue: true,
                description: "Show the textual list of items below each loadout's name (Helm:..., Chest:..., etc).");

            ShowIcons = Config.Bind(
                section:     "UI",
                key:         "Show Icons",
                defaultValue: true,
                description: "Show the row of item icons at the bottom of each loadout row.");

            PanelPosX = Config.Bind(
                section:     "Window",
                key:         "Panel Position X",
                defaultValue: 380f,
                description: "Saved horizontal panel position (auto-updated when you close the rack).");

            PanelPosY = Config.Bind(
                section:     "Window",
                key:         "Panel Position Y",
                defaultValue: 0f,
                description: "Saved vertical panel position (auto-updated when you close the rack).");

            if (!ModEnabled.Value)
            {
                Jotunn.Logger.LogInfo("[Armory] Mod Enabled = false in config — skipping piece registration and Harmony patches.");
                return;
            }
            _initialized = true;

            _harmony.PatchAll();

            // Register English localization tokens via Jotunn.
            var loc = LocalizationManager.Instance.GetLocalization();
            loc.AddTranslation("English", new System.Collections.Generic.Dictionary<string, string>
            {
                ["armory_rack_name"] = "Armory Rack",
                ["armory_rack_desc"] = "A masterwork rack for storing and instantly recalling named equipment sets.",
            });

            PrefabManager.OnVanillaPrefabsAvailable += ArmoryPieces.CreatePiece;
            PieceManager.OnPiecesRegistered         += ArmoryPieces.ConfigureAndRegister;
        }

        private void Update()
        {
            if (!_initialized) return;
            ArmoryUI.Tick();
        }

        private void OnDestroy()
        {
            if (!_initialized) return;
            _harmony.UnpatchSelf();
        }
    }

    // ── Harmony: block player movement/action while the Armory UI is open ──────────

    [HarmonyPatch(typeof(Player), nameof(Player.TakeInput))]
    internal static class Player_TakeInput_Patch
    {
        static bool Prefix() => !ArmoryUI.IsOpen;
    }

    // TakeInput blocks movement/action input, but the camera reads mouse-look via
    // Player.SetMouseLook which bypasses TakeInput.  Skip it while the UI is open
    // so the cursor can be used to click buttons.
    [HarmonyPatch(typeof(Player), nameof(Player.SetMouseLook))]
    internal static class Player_SetMouseLook_Patch
    {
        static bool Prefix() => !ArmoryUI.IsOpen;
    }

    // While the player is typing into our rename InputField, claim chat focus so Valheim's
    // input loop treats the keystrokes as text-entry and doesn't dispatch them to gameplay
    // shortcuts (inventory toggle, map, hotbar, etc).
    [HarmonyPatch(typeof(Chat), nameof(Chat.HasFocus))]
    internal static class Chat_HasFocus_Patch
    {
        static void Postfix(ref bool __result)
        {
            if (ArmoryUI.IsEditing) __result = true;
        }
    }
}
