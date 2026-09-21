using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Managers;

namespace Armory
{
    // Deliberately no [BepInProcess("valheim.exe")] gate: this mod must load on a dedicated
    // server as well as on clients, so do not add one back.
    //
    // The rack is a new prefab ("armory_rack") that exists only because this mod clones it.
    // A server without the mod cannot resolve that hash, and ZNetScene.CreateObjectsSorted does
    // not merely skip what it cannot instantiate — on the server it takes ownership and calls
    // ZDOMan.DestroyZDO, logging "Destroyed invalid prefab ZDO".  The server instantiates around
    // its own reference position, which never leaves the world center without a local player, so
    // this claims exactly the racks built near spawn: silently, permanently, and with the stored
    // gear and every saved loadout inside them.
    //
    // The second reason is ownership.  A rack near spawn belongs to the server, and a rack is
    // unowned for a moment after a restart; with the mod there, the server can instantiate it,
    // answer Container's open request and run the rack's own ArmoryRPC_SetData.  Without it,
    // neither the container nor the loadouts have anyone to answer for them.
    //
    // Running headless is safe.  The Player and Chat patches never fire without a local player,
    // ArmoryUI is only ever reached from InventoryGui.Show, and ArmoryPause returns immediately
    // while Player.m_localPlayer is null.  What does run is the part the server needs: the clone,
    // its registration into ZNetScene, and the ArmoryRack component on the spawned object.
    [BepInPlugin(ModGuid, "Armory", "2.0.0")]
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
        public static ConfigEntry<bool> ShowRowButtons;

        // Persisted main-panel position so it survives across game sessions (logins).
        // Auto-updated when the player closes the rack; defaults place the panel to the right
        // of the vanilla inventory.  Float pair instead of Vector2 since BepInEx's TomlTypeConverter
        // for UnityEngine.Vector2 isn't guaranteed to be registered in this runtime.
        public static ConfigEntry<float> PanelPosX;
        public static ConfigEntry<float> PanelPosY;
        public static ConfigEntry<float> PanelWidth;
        public static ConfigEntry<float> PanelHeight;

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
                description: "Pause the game while the Armory Rack panel is open, the way the ESC menu does. " +
                             "This works by itself when playing solo or hosting alone; on a dedicated server " +
                             "it takes the Pause My Server mod, and without it the request is simply refused " +
                             "rather than stopping your own clock while the server runs on.");
            PauseGameWhileOpen.SettingChanged += (_, __) => ArmoryPause.Refresh();

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

            ShowRowButtons = Config.Bind(
                section:     "UI",
                key:         "Show Row Buttons",
                defaultValue: false,
                description: "Show Save, Load, +, Cmp and x on every loadout row. Off puts them in a menu " +
                             "behind one button per row, which takes less room and lets the panel be narrower.");

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

            PanelWidth = Config.Bind(
                section:     "Window",
                key:         "Panel Width",
                defaultValue: 760f,
                description: "Saved panel width (auto-updated when you drag the grip in the panel's bottom-right " +
                             "corner). A loadout's icon groups sit on one line when they fit the width, and wrap " +
                             "onto more lines only when they don't.");

            PanelHeight = Config.Bind(
                section:     "Window",
                key:         "Panel Height",
                defaultValue: 0f,
                description: "Saved panel height (auto-updated when you drag the grip). 0 means fit the panel to " +
                             "its loadouts, up to most of the screen; the list scrolls past that.");

            if (!ModEnabled.Value)
            {
                Jotunn.Logger.LogInfo("[Armory] Mod Enabled = false in config — skipping piece registration and Harmony patches.");
                return;
            }
            _initialized = true;

            _harmony.PatchAll();

            // Register English localization tokens via Jotunn.  Guarded because this is the one
            // Jotunn manager used here with no headless precedent in this repo — ForsakenShrines
            // proves PrefabManager and PieceManager are fine on a dedicated server, but nothing
            // has exercised LocalizationManager there.  The tokens are display text and so are a
            // client's concern anyway; a headless surprise must not be allowed to stop the clone
            // and its ZNetScene registration below, which is the whole reason the server has this.
            try
            {
                var loc = LocalizationManager.Instance.GetLocalization();
                loc.AddTranslation("English", new System.Collections.Generic.Dictionary<string, string>
                {
                    ["armory_rack_name"] = "Armory Rack",
                    ["armory_rack_desc"] = "A masterwork rack for storing and instantly recalling named equipment sets.",
                    ["armory_rename_topic"] = "Armory Name",
                });
            }
            catch (System.Exception e)
            {
                Jotunn.Logger.LogWarning($"[Armory] Localization registration skipped: {e.Message}");
            }

            PrefabManager.OnVanillaPrefabsAvailable += ArmoryPieces.CreatePiece;
            PieceManager.OnPiecesRegistered         += ArmoryPieces.ConfigureAndRegister;
        }

        private void Update()
        {
            if (!_initialized) return;
            ArmoryUI.Tick();
            // After Tick, so a panel it just closed is already reflected. Runs whether or not the
            // panel is open — that is how the pause gets let go of when the player dies or logs out.
            ArmoryPause.Refresh();
        }

        private void OnDestroy()
        {
            if (!_initialized) return;
            ArmoryPause.Release();
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
