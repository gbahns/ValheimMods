using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace WhatsMyWorkbenchMissing
{
    /// <summary>
    /// What's My Workbench Missing: hover the level number of an open crafting station and see which of its
    /// upgrades you have not built yet but could, each with its materials, colored by whether
    /// you are carrying enough. The upgrades you have not discovered are only counted, never
    /// named.
    ///
    /// Everything here is read from the local player's own knowledge and inventory and from the
    /// extension pieces around the station, so nothing is sent anywhere and the server needs
    /// nothing. Client-side only.
    /// </summary>
    [BepInPlugin(ModGuid, ModName, ModVersion)]
    [BepInProcess("valheim.exe")]
    public class WhatsMyWorkbenchMissingMod : BaseUnityPlugin
    {
        public const string ModGuid    = "DeathMonger.WhatsMyWorkbenchMissing";
        public const string ModName    = "What's My Workbench Missing";
        public const string ModVersion = "0.1.0";

        internal static ManualLogSource Log { get; private set; }

        internal static ConfigEntry<bool> ModEnabled;
        internal static ConfigEntry<bool> ShowMaterials;
        internal static ConfigEntry<bool> ShowUndiscovered;
        internal static ConfigEntry<bool> ShowBuilt;

        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;

            ModEnabled = Config.Bind("General", "Mod Enabled", true,
                "Master switch. Off means the level number shows no tooltip. Takes effect immediately.");
            ShowMaterials = Config.Bind("General", "Show Materials", true,
                "List each upgrade's materials after its name. Amounts you are short of are red and " +
                "show how many you carry, as in 4/10 Flint.");
            ShowUndiscovered = Config.Bind("General", "Show Undiscovered Count", true,
                "Add a line counting the upgrades you have not discovered yet, without naming them. " +
                "Off leaves the undiscovered ones out entirely.");
            ShowBuilt = Config.Bind("General", "Show Built", false,
                "Also list the upgrades already attached to the station, dimmed, so the tooltip " +
                "shows the whole picture.");

            _harmony = new Harmony(ModGuid);
            _harmony.PatchAll();
            Log.LogInfo($"{ModName} {ModVersion} loaded.");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }
}
