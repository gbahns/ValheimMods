using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace SapExtractorProgressionFix
{
    /// <summary>
    /// Sap Extractor Progression Fix: a sap extractor is meant to fill up for the time nobody was
    /// near it, the way a beehive does, and in vanilla it mostly does not. See
    /// <see cref="SapCollectorPatch"/> for what goes wrong and what is done about it.
    ///
    /// The extractor's tick runs on whichever machine owns it, which is normally the player who
    /// walked up to it. A dedicated server only builds live objects around the world center,
    /// where no ancient root can be, so it would never run this code: client-side only. It adds
    /// no prefab and no RPC, so a player without it breaks nothing; the extractors they load
    /// just behave as vanilla.
    /// </summary>
    [BepInPlugin(ModGuid, ModName, ModVersion)]
    [BepInProcess("valheim.exe")]
    public class SapExtractorProgressionFixMod : BaseUnityPlugin
    {
        public const string ModGuid    = "DeathMonger.SapExtractorProgressionFix";
        public const string ModName    = "Sap Extractor Progression Fix";
        public const string ModVersion = "0.1.0";

        internal static ManualLogSource Log { get; private set; }

        internal static ConfigEntry<bool> ModEnabled;
        internal static ConfigEntry<bool> LogCatchUp;

        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;

            ModEnabled = Config.Bind("General", "Mod Enabled", true,
                "Master switch. Off leaves sap extractors exactly as the game runs them. " +
                "Takes effect immediately.");
            LogCatchUp = Config.Bind("General", "Log Catch Up", true,
                "Write a line to the BepInEx log each time you return to a sap extractor: how long " +
                "you were away, what the unmodified game would have done with that time, and what " +
                "was credited instead.");

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
