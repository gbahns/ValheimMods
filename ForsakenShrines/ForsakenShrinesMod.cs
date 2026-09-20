using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Managers;
using ServerSync;
using UnityEngine;

namespace ForsakenShrines
{
    // Deliberately no [BepInProcess("valheim.exe")] gate: this mod must load on a dedicated
    // server as well as on clients, so do not add one back.
    //
    // The shrines are new prefabs ("shrine_eikthyr" and friends) that exist only because this
    // mod clones them; nothing in vanilla knows those names.  A dedicated server that does not
    // have the mod cannot resolve their prefab hashes, and ZNetScene.CreateObjectsSorted deletes
    // what it cannot instantiate -- "Destroyed invalid prefab ZDO" -- so a placed shrine is gone
    // for good.  The server only instantiates around its own reference position, which never
    // leaves the world center on a dedicated server, so in practice this claims the shrines
    // built near spawn: silently, permanently, and exactly where people build.
    //
    // ServerSync is the second reason: without the plugin on the server there is no
    // DeathMonger.ForsakenShrines.cfg for an admin to edit and no authoritative values to push,
    // so every client would quietly run its own placement rules and recipes.
    //
    // Running headless is safe.  The Player patches (placement, ghost height, OnSpawned) simply
    // never fire without a local player, and the two hooks that matter -- ObjectDB.Awake and
    // ZNetScene.Awake -- are exactly the ones that register the clones into ZNetScene so the
    // server can spawn them.
    [BepInPlugin(ModGuid, ModName, ModVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    public class ForsakenShrinesMod : BaseUnityPlugin
    {
        public const string ModGuid    = "DeathMonger.ForsakenShrines";
        public const string ModName    = "Forsaken Shrines";
        public const string ModVersion = "0.8.5";
        // The oldest version that can share a server with this one.  Kept apart from ModVersion
        // so a release that changes no network contract does not lock older clients out.
        public const string MinCompatibleVersion = "0.8.3";

        internal static ForsakenShrinesMod Instance { get; private set; }

        // Master toggle.  Bound first and deliberately NOT server-synced, so a player who runs
        // into a problem in-game can switch the mod off in
        // BepInEx/config/DeathMonger.ForsakenShrines.cfg without removing the DLL.  When false,
        // Awake binds this one entry and returns: no Harmony patches, no prefab clones, no piece
        // registration, no console commands, and no ServerSync registration.  The remaining
        // entries stay in the .cfg untouched (BepInEx preserves entries a plugin hasn't bound).
        //
        // Ignored on a dedicated server -- see IsDedicatedServer.
        internal static ConfigEntry<bool> ModEnabled;

        // A dedicated server that skips piece registration cannot resolve the shrine prefabs,
        // and ZNetScene.CreateObjectsSorted deletes the ZDOs it cannot instantiate rather than
        // leaving them alone the way a client does -- so honoring "Mod Enabled = false" here
        // would quietly destroy every placed shrine the server reaches.  That is the same world
        // damage the old [BepInProcess("valheim.exe")] gate caused, reachable through a config
        // line, and the toggle's purpose (a player isolating a problem in their own game) is a
        // client concern to begin with.  So the server ignores it and says so in the log; an
        // admin who really wants the mod gone removes the DLL, which loses no shrines because
        // the prefabs are simply never asked for.
        //
        // Two signals, either of which is enough: the process name BepInProcess itself compares
        // against ("valheim_server" on Windows, and on Linux too once the .x86_64 suffix is
        // stripped), and Unity's batch mode, which no client runs in.
        private static bool IsDedicatedServer =>
            (BepInEx.Paths.ProcessName ?? string.Empty)
                .StartsWith("valheim_server", System.StringComparison.OrdinalIgnoreCase)
            || Application.isBatchMode;

        private readonly Harmony _harmony = new Harmony(ModGuid);

        // ServerSync keeps config values consistent between server and clients.  Created only
        // when the mod is enabled: ConfigSync's constructor registers the instance with
        // ServerSync's ZNet patches, and a disabled mod should not take part in that.
        private static ConfigSync _configSync;

        private void Awake()
        {
            Instance = this;

            ModEnabled = Config.Bind("General", "Mod Enabled", true,
                "Master toggle for the entire mod. Set to false to disable the shrine build pieces, " +
                "placement rules, console commands, and all Harmony patches without removing the DLL. " +
                "Useful for isolating a problem in your own game. Not server-synced, and ignored on a " +
                "dedicated server, where switching the mod off would delete the shrines players have " +
                "already built. Requires a game restart to take effect; on a client, shrines already " +
                "placed in the world are hidden until re-enabled.");
            if (!ModEnabled.Value)
            {
                if (IsDedicatedServer)
                {
                    Jotunn.Logger.LogError(
                        "[ForsakenShrines] Mod Enabled = false, but this is a dedicated server — ignoring it and " +
                        "loading anyway. A server without the shrine prefabs deletes every placed shrine it comes " +
                        "across, permanently. To take the mod off this server, remove the DLL instead: that leaves " +
                        "the shrines in the world untouched.");
                }
                else
                {
                    Jotunn.Logger.LogInfo("[ForsakenShrines] Mod Enabled = false in config — skipping patches, piece registration, and console commands.");
                    return;
                }
            }

            _configSync = new ConfigSync(ModGuid)
            {
                DisplayName            = ModName,
                CurrentVersion         = ModVersion,
                MinimumRequiredVersion = MinCompatibleVersion,
            };

            ShrineConfig.Bind(this);
            ShrineConsoleCommands.Register();
            _harmony.PatchAll();

            // Phase 1: Jotunn raises OnVanillaPrefabsAvailable from its ObjectDB.CopyOtherDB
            // prefix at the main menu, once vanilla prefabs (including Valheim 1.0's
            // soft-referenced BossStones) can be resolved.  Clones are created once per game
            // session and parented under Jotunn's inactive prefab container, so they survive
            // scene changes.
            PrefabManager.OnVanillaPrefabsAvailable += ShrinePieces.CreateClones;

            // Phase 2 (configure pieces, insert into the Hammer table and ZNetScene, refresh
            // unlock state) runs from this mod's own ObjectDB.Awake / ZNetScene.Awake postfixes
            // in ShrineLifecyclePatches — on every world load, not just the first.
            //
            // Jotunn's PieceManager is intentionally not used anywhere in this mod.  Merely
            // touching it (an event subscription is enough) runs its static constructor, which
            // Harmony-patches PieceTable.UpdateAvailable.  Jotunn 2.29.2's prefix reads
            // PieceTable.m_availablePieces with its pre-1.0 type; Valheim 1.0 changed that field,
            // so the prefix throws MissingFieldException, UpdateAvailable never populates the
            // per-category lists, and anyone who logs in with a Hammer equipped is thrown into
            // a respawn loop (spinning camera, character frozen in the wake-up pose).
            // Jotunn 2.30.0 fixes that field access but still has no custom-category support
            // on 1.0; staying off PieceManager keeps this mod independent of either.
        }

        private void OnDestroy()
        {
            _harmony.UnpatchSelf();
        }

        /// <summary>
        /// Binds a BepInEx config entry and registers it with ServerSync so the server's value
        /// overrides clients.  Only valid once Awake has created the ConfigSync (mod enabled).
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
