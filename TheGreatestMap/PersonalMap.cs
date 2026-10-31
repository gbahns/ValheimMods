using System;
using HarmonyLib;

namespace TheGreatestMap
{
    /// <summary>
    /// The character's own map: every marker they recorded, placed or received, plus tombstones
    /// for what they erased. Saved with the character, one map per world, so it travels like the
    /// vanilla explored area does. Merged into the shared map at cartography tables or with
    /// another player's map when both are out.
    /// </summary>
    internal static class PersonalMap
    {
        private const string KeyPrefix = "TheGreatestMap.Map:";

        internal static MapStore Store { get; private set; } = new MapStore();

        /// <summary>
        /// Put a different map in place of this one, for tgm_restore. Marked dirty so it is
        /// written to the character, and so the next merge sends it like any other change.
        /// </summary>
        internal static void Replace(MapStore store)
        {
            if (store == null) return;
            Store = store;
            Loaded = true;
            IsNew = false;
            MarkDirty();
        }
        internal static bool Loaded { get; private set; }
        internal static bool Dirty { get; private set; }
        /// <summary>True when this character has never had a map for this world: it receives the shared map once on spawn.</summary>
        internal static bool IsNew { get; private set; }

        internal static void Reset()
        {
            Store = new MapStore();
            Loaded = false;
            Dirty = false;
            IsNew = false;
        }

        private static string Key
        {
            get
            {
                var world = ZNet.World;
                string worldKey = world != null ? world.m_name + "_" + world.m_seed : "world";
                return KeyPrefix + worldKey;
            }
        }

        /// <summary>A local edit: needs saving, and in Instant mode a sync soon.</summary>
        internal static void Touch()
        {
            Dirty = true;
            SyncEngine.OnLocalChange();
        }

        /// <summary>
        /// A change that came from elsewhere. Still sets the flag, because a map merged from
        /// another player holds markers the server has not seen and they have to go up.
        ///
        /// The comment here used to say "needs saving only", which was not true of the flag it
        /// sets: saving happens on the player's own save hook and never reads this. It only ever
        /// meant "we have something the server may not", which is why a merge from the server
        /// has to put it back afterwards -- see SyncEngine.OnSharedMap.
        /// </summary>
        internal static void MarkDirty() => Dirty = true;

        /// <summary>The map as it stands has gone to the server, so it need not go again unchanged.</summary>
        internal static void Sent() => Dirty = false;

        /// <summary>Which character on which world, for naming a backup of this map.</summary>
        /// <summary>What this map is called in a file name: the character and the world.</summary>
        internal static string Label => Name(Player.m_localPlayer);

        private static string Name(Player player)
        {
            string who = player != null ? player.GetPlayerName() : "character";
            var world = ZNet.World;
            return (string.IsNullOrEmpty(who) ? "character" : who) + "_" + (world != null ? world.m_name : "world");
        }

        internal static void LoadFrom(Player player)
        {
            Store = new MapStore();
            Loaded = true;
            Dirty = false;
            IsNew = false;
            if (player == null || player.m_customData == null) return;
            if (!player.m_customData.TryGetValue(Key, out var data) || string.IsNullOrEmpty(data)) { IsNew = true; return; }
            try
            {
                var stored = Utils.Decompress(Convert.FromBase64String(data));
                Store = MapStore.FromBytes(stored);
                int crops = Store.ForgetCropErasures();
                if (crops > 0)
                {
                    MarkDirty();
                    TheGreatestMapMod.Log.LogInfo(
                        $"[TheGreatestMap] Forgot {crops} crop erasure(s) from your own map.");
                }
                // On every load; see PinStore for why it is no longer tied to the conversion.
                int ghosts = Store.ForgetGhosts();
                if (ghosts > 0)
                {
                    MarkDirty();
                    TheGreatestMapMod.Log.LogInfo(
                        $"[TheGreatestMap] Dropped {ghosts} erasure(s) from your own map that were only the former " +
                        "name of a marker still standing in the same place.");
                }
                if (Store.ReadVersion < MapStore.FormatVersion)
                {
                    // Before anything saves over it. A personal map cannot be copied as a file,
                    // because it lives inside the character; this writes out the bytes that were
                    // in there, which tgm_restore reads back.
                    MapBackup.Keep(Name(player), Store.ReadVersion, stored);
                    TheGreatestMapMod.Log.LogInfo(
                        $"[TheGreatestMap] Your own map was in format {Store.ReadVersion} and has been brought up to " +
                        $"{MapStore.FormatVersion}: {Store.Pins.Count} markers, {Store.LegacyTombstones.Count} erasure(s) carried over." +
                        (Store.Collapsed > 0
                            ? $" {Store.Collapsed} marker(s) were a second record of a spot already marked and are now one marker each."
                            : ""));
                    MarkDirty();
                }
                TheGreatestMapMod.Log.LogInfo(
                    $"[TheGreatestMap] Personal map loaded: {Store.Pins.Count} markers, {Store.Tombstones.Count} erased" +
                    (Store.LegacyTombstones.Count > 0 ? $" (and {Store.LegacyTombstones.Count} erased before the rekey)" : "") + ".");
            }
            catch (Exception e)
            {
                TheGreatestMapMod.Log.LogWarning($"[TheGreatestMap] Could not read the personal map from the character; starting empty: {e.Message}");
                Store = new MapStore();
            }
        }

        internal static void SaveTo(Player player)
        {
            if (!Loaded || player == null || player.m_customData == null) return;
            try
            {
                player.m_customData[Key] = Convert.ToBase64String(Utils.Compress(Store.ToBytes()));
                Dirty = false;
            }
            catch (Exception e)
            {
                TheGreatestMapMod.Log.LogWarning($"[TheGreatestMap] Could not save the personal map: {e.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.Save))]
    internal static class Player_Save_PersonalMap_Patch
    {
        private static void Prefix(Player __instance)
        {
            if (__instance == Player.m_localPlayer) PersonalMap.SaveTo(__instance);
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.Load))]
    internal static class Player_Load_PersonalMap_Patch
    {
        private static void Postfix(Player __instance)
        {
            PersonalMap.LoadFrom(__instance);
            ClientPins.RebuildPins();
            if (TgmConfig.RepairDungeonIcons.Value && PersonalMap.Store.Pins.Count > 0)
            {
                int fixedUp = ClientPins.RepairDungeonIcons();
                if (fixedUp > 0) TheGreatestMapMod.Log.LogInfo($"[TheGreatestMap] Corrected the icon on {fixedUp} dungeon marker(s) on the personal map that had the crypt key.");
            }
            if (TgmConfig.ApplyLabelRulesOnSync.Value && PersonalMap.Store.Pins.Count > 0)
            {
                int stripped = ClientPins.ApplyLabelRules(null);
                if (stripped > 0) TheGreatestMapMod.Log.LogInfo($"[TheGreatestMap] Removed labels from {stripped} markers on the personal map to match the label rules.");
            }
        }
    }
}
