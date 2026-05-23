using System.Linq;
using Jotunn.Managers;

namespace Armory
{
    /// <summary>
    /// Reads the player's current equipment into a LoadoutSlot and applies a saved
    /// LoadoutSlot back to the player. Items must already be in the player's inventory —
    /// nothing is created for free.
    /// </summary>
    internal static class LoadoutManager
    {
        // ── Capture ────────────────────────────────────────────────────────────────

        public static LoadoutSlot CaptureCurrentEquipment(Player player)
        {
            return new LoadoutSlot
            {
                Name      = "New Loadout",
                Helmet    = Serialize(player.m_helmetItem),
                Chest     = Serialize(player.m_chestItem),
                Legs      = Serialize(player.m_legItem),
                Shoulder  = Serialize(player.m_shoulderItem),
                Utility   = Serialize(player.m_utilityItem),
                RightHand = Serialize(player.m_rightItem),
                LeftHand  = Serialize(player.m_leftItem),
            };
        }

        private static SavedItem Serialize(ItemDrop.ItemData item)
        {
            if (item == null) return null;
            return new SavedItem
            {
                SharedName = item.m_shared.m_name,
                Quality    = item.m_quality,
                Variant    = item.m_variant,
            };
        }

        // ── Apply ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// Unequips all current gear then equips each item found in inventory.
        /// Returns (equippedCount, missingCount).
        /// </summary>
        public static (int found, int missing) ApplyLoadout(Player player, LoadoutSlot slot)
        {
            var inventory = player.GetInventory();

            // Unequip everything first so slots are available.
            if (player.m_helmetItem   != null) player.UnequipItem(player.m_helmetItem,   false);
            if (player.m_chestItem    != null) player.UnequipItem(player.m_chestItem,    false);
            if (player.m_legItem      != null) player.UnequipItem(player.m_legItem,      false);
            if (player.m_shoulderItem != null) player.UnequipItem(player.m_shoulderItem, false);
            if (player.m_utilityItem  != null) player.UnequipItem(player.m_utilityItem,  false);
            if (player.m_rightItem    != null) player.UnequipItem(player.m_rightItem,    false);
            if (player.m_leftItem     != null) player.UnequipItem(player.m_leftItem,     false);

            int found = 0, missing = 0;
            TryEquip(player, inventory, slot.Helmet,    ref found, ref missing);
            TryEquip(player, inventory, slot.Chest,     ref found, ref missing);
            TryEquip(player, inventory, slot.Legs,      ref found, ref missing);
            TryEquip(player, inventory, slot.Shoulder,  ref found, ref missing);
            TryEquip(player, inventory, slot.Utility,   ref found, ref missing);
            TryEquip(player, inventory, slot.RightHand, ref found, ref missing);
            TryEquip(player, inventory, slot.LeftHand,  ref found, ref missing);

            return (found, missing);
        }

        private static void TryEquip(Player player, Inventory inventory, SavedItem saved, ref int found, ref int missing)
        {
            if (saved == null) return;
            var item = FindInInventory(inventory, saved);
            if (item != null)
            {
                player.EquipItem(item, false);
                found++;
            }
            else
            {
                missing++;
            }
        }

        // ── Inventory lookup ───────────────────────────────────────────────────────

        /// <summary>
        /// Finds a matching item in inventory. Prefers exact quality; falls back to
        /// the highest available quality of the same item type.
        /// </summary>
        public static ItemDrop.ItemData FindInInventory(Inventory inventory, SavedItem saved)
        {
            if (saved == null) return null;
            var all = inventory.GetAllItems();
            var exact = all.FirstOrDefault(i =>
                i.m_shared.m_name == saved.SharedName && i.m_quality == saved.Quality);
            if (exact != null) return exact;
            return all
                .Where(i => i.m_shared.m_name == saved.SharedName)
                .OrderByDescending(i => i.m_quality)
                .FirstOrDefault();
        }

        public static bool IsInInventory(Inventory inventory, SavedItem saved) =>
            saved == null || FindInInventory(inventory, saved) != null;

        // ── Display helpers ────────────────────────────────────────────────────────

        public static string GetItemDisplayName(SavedItem saved)
        {
            if (saved == null) return string.Empty;
            var localized = LocalizationManager.Instance?.TryTranslate(saved.SharedName);
            return !string.IsNullOrEmpty(localized) ? localized : saved.SharedName;
        }

        /// <summary>
        /// Ensures the ArmoryData has at least <paramref name="count"/> named slots.
        /// </summary>
        public static void EnsureSlots(ArmoryData data, int count)
        {
            while (data.Slots.Count < count)
                data.Slots.Add(new LoadoutSlot { Name = $"Slot {data.Slots.Count + 1}" });
        }
    }
}
