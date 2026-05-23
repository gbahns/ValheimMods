using BepInEx;
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

        private void Awake()
        {
            Instance = this;
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
            ArmoryUI.Tick();
        }

        private void OnGUI()
        {
            ArmoryUI.OnGUI();
        }

        private void OnDestroy()
        {
            _harmony.UnpatchSelf();
        }
    }

    // ── Harmony: block player movement/action while the Armory UI is open ──────────

    [HarmonyPatch(typeof(Player), nameof(Player.TakeInput))]
    internal static class Player_TakeInput_Patch
    {
        static bool Prefix() => !ArmoryUI.IsOpen;
    }
}
