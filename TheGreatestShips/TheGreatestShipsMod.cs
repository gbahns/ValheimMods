using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Managers;
using ServerSync;

namespace TheGreatestShips
{
    // Deliberately no [BepInProcess("valheim.exe")] gate: the ships are new prefabs, and a
    // dedicated server that cannot resolve a prefab hash deletes the ZDO in
    // ZNetScene.CreateObjectsSorted ("Destroyed invalid prefab ZDO") -- a moored ship near the
    // world center would be gone for good.  The server also owns the synced config.
    [BepInPlugin(ModGuid, ModName, ModVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    public class TheGreatestShipsMod : BaseUnityPlugin
    {
        public const string ModGuid    = "DeathMonger.TheGreatestShips";
        public const string ModName    = "The Greatest Ships";
        public const string ModVersion = "0.9.4";

        // Oldest version this one can share a server with.  Raise it only for a release that
        // changes what the two sides must agree on (prefab names, synced config, storage size).
        // 0.9.1: the Fast Longship's hold grew from 3x3 to 4x3, and a client that still thinks
        // it is 3x3 would mishandle the items in the new slots.
        public const string MinCompatibleVersion = "0.9.2";

        internal static TheGreatestShipsMod Instance { get; private set; }

        private readonly Harmony _harmony = new Harmony(ModGuid);

        private static ConfigSync _configSync;

        private void Awake()
        {
            Instance = this;

            _configSync = new ConfigSync(ModGuid)
            {
                DisplayName            = ModName,
                CurrentVersion         = ModVersion,
                MinimumRequiredVersion = MinCompatibleVersion,
            };

            ShipConfig.Bind(this);
            _harmony.PatchAll();

            // Clone the vanilla hull once vanilla prefabs can be resolved (main menu).  The rest
            // -- recipe, Hammer table, ZNetScene -- runs from the ObjectDB/ZNetScene postfixes in
            // Patches.cs on every world load.  Jotunn's PieceManager is not used: see the note in
            // ForsakenShrinesMod.Awake for why it is unsafe on Valheim 1.0.
            PrefabManager.OnVanillaPrefabsAvailable += ShipPrefabs.CreateClones;
        }

        private void OnDestroy()
        {
            _harmony.UnpatchSelf();
        }

        /// <summary>
        /// Binds a BepInEx config entry and registers it with ServerSync so the server's value
        /// overrides clients.
        /// </summary>
        internal ConfigEntry<T> BindSynced<T>(string section, string key, T defaultValue, string description)
        {
            var entry = Config.Bind(section, key, defaultValue,
                new ConfigDescription(description + " [Synced with Server]"));
            _configSync.AddConfigEntry(entry).SynchronizedConfig = true;
            return entry;
        }

        /// <summary>
        /// The entry that locks the synced settings: while the server has it on, only admins
        /// (adminlist.txt) can change a synced setting from their client, and other players see
        /// them read-only.  Without one, ServerSync accepts a change from anyone.
        /// </summary>
        internal ConfigEntry<bool> BindLocking(string section, string key, bool defaultValue, string description)
        {
            var entry = Config.Bind(section, key, defaultValue,
                new ConfigDescription(description + " [Synced with Server]"));
            _configSync.AddLockingConfigEntry(entry);
            return entry;
        }
    }
}
