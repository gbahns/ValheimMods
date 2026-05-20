using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Managers;
using ServerSync;

namespace ForesakenShrines
{
    [BepInPlugin(ModGuid, ModName, ModVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [BepInProcess("valheim.exe")]
    public class ForesakenShrinesMod : BaseUnityPlugin
    {
        public const string ModGuid    = "DeathMonger.ForesakenShrines";
        public const string ModName    = "Forsaken Shrines";
        public const string ModVersion = "0.8.0";

        internal static ForesakenShrinesMod Instance { get; private set; }

        private readonly Harmony _harmony = new Harmony(ModGuid);

        // ServerSync keeps config values consistent between server and clients.
        // v1 has no synced entries yet; the infrastructure is wired so adding one later is
        // a one-liner call to BindSynced<T>().
        private static readonly ConfigSync _configSync = new ConfigSync(ModGuid)
        {
            DisplayName          = ModName,
            CurrentVersion       = ModVersion,
            MinimumRequiredVersion = ModVersion,
        };

        // ── Future synced config entries go here ────────────────────────────────────
        // Example (ward restriction, v2):
        //   internal static ConfigEntry<bool> WardRestriction;
        //   WardRestriction = Instance.BindSynced("Restrictions", "WardRestriction", false,
        //       "Only one shrine may be built per ward.");
        // ────────────────────────────────────────────────────────────────────────────

        private void Awake()
        {
            Instance = this;
            ShrineConfig.Bind(this);
            ShrineConsoleCommands.Register();
            _harmony.PatchAll();
            // Phase 1: OnVanillaPrefabsAvailable fires inside ZNetScene.Awake, BEFORE the world
            // file is deserialized. Clones must exist in ZNetScene.m_namedPrefabs at that moment
            // so placed shrines survive a full game restart. ObjectDB is NOT ready here.
            PrefabManager.OnVanillaPrefabsAvailable += ShrinePieces.CreateClones;
            // Per-world-load refresh (same event, fires every world load including first).
            PrefabManager.OnVanillaPrefabsAvailable += ShrinePieces.OnWorldLoad;
            // Phase 2: OnPiecesRegistered fires from ObjectDB.Awake, AFTER Phase 1.
            // Populates requirements, icon, and Jotunn's piece registry.
            PieceManager.OnPiecesRegistered += ShrinePieces.ConfigureAndRegister;
        }

        private void OnDestroy()
        {
            _harmony.UnpatchSelf();
        }

        /// <summary>
        /// Binds a BepInEx config entry and registers it with ServerSync so the server's value
        /// overrides clients.  Call from Awake to add future synced config options.
        /// </summary>
        internal ConfigEntry<T> BindSynced<T>(string section, string key, T defaultValue, string description)
        {
            var entry = Config.Bind(section, key, defaultValue,
                new ConfigDescription(description + " [Synced with Server]"));
            _configSync.AddConfigEntry(entry).SynchronizedConfig = true;
            return entry;
        }
    }
}
