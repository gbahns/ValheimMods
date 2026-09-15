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

        /// <summary>A change that came from elsewhere: needs saving only.</summary>
        internal static void MarkDirty() => Dirty = true;

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
                Store = MapStore.FromBytes(Utils.Decompress(Convert.FromBase64String(data)));
                TheGreatestMapMod.Log.LogInfo($"[TheGreatestMap] Personal map loaded: {Store.Pins.Count} markers, {Store.Tombstones.Count} erased.");
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
