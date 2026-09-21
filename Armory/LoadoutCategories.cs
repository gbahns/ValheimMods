using System.Collections.Generic;
using System.Linq;

namespace Armory
{
    /// <summary>
    /// How the panel groups a loadout's cells, and the per-cell inclusion behind each one.
    /// Worn gear is categorized by the slot it is worn in; everything else — hotbar and
    /// quick-slot items — by what the item is, so a spare helmet parked on the hotbar still
    /// counts as Armor and a stack of arrows as Ammo.
    ///
    /// Nothing here knows about any inventory mod.  It is item data and vanilla slots only,
    /// which is what makes the categories work with or without one.
    /// </summary>
    internal static class LoadoutCategories
    {
        /// <summary>Display order, worn categories first.</summary>
        public static readonly LoadoutCategory[] Order =
        {
            LoadoutCategory.Armor, LoadoutCategory.Cape, LoadoutCategory.Accessories, LoadoutCategory.Weapons,
            LoadoutCategory.Ammo, LoadoutCategory.Food, LoadoutCategory.Meads,
        };

        public static string Label(LoadoutCategory category)
        {
            switch (category)
            {
                case LoadoutCategory.Armor:       return "Armor";
                case LoadoutCategory.Cape:        return "Cape";
                case LoadoutCategory.Accessories: return "Accessories";
                case LoadoutCategory.Weapons:     return "Weapons";
                case LoadoutCategory.Ammo:        return "Ammo";
                case LoadoutCategory.Food:        return "Food";
                case LoadoutCategory.Meads:       return "Meads";
                default:                          return category.ToString();
            }
        }

        public static LoadoutCategory Categorize(ItemDrop.ItemData.SharedData shared)
        {
            if (shared == null) return LoadoutCategory.Weapons;
            switch (shared.m_itemType)
            {
                case ItemDrop.ItemData.ItemType.Helmet:
                case ItemDrop.ItemData.ItemType.Chest:
                case ItemDrop.ItemData.ItemType.Legs:
                    return LoadoutCategory.Armor;
                case ItemDrop.ItemData.ItemType.Shoulder:
                    return LoadoutCategory.Cape;
                case ItemDrop.ItemData.ItemType.Utility:
                case ItemDrop.ItemData.ItemType.Trinket:
                    return LoadoutCategory.Accessories;
                case ItemDrop.ItemData.ItemType.Ammo:
                case ItemDrop.ItemData.ItemType.AmmoNonEquipable:
                    return LoadoutCategory.Ammo;
                case ItemDrop.ItemData.ItemType.Consumable:
                    // Both are "Consumable" to the game.  Food nourishes; a mead only carries a
                    // status effect.
                    return (shared.m_food > 0f || shared.m_foodStamina > 0f || shared.m_foodEitr > 0f)
                        ? LoadoutCategory.Food
                        : LoadoutCategory.Meads;
                default:
                    // Weapons, shields, bows, torches, tools — and whatever else someone keeps on
                    // the hotbar.  A stack of wood there rides with the weapons rather than
                    // silently belonging to no category and never being restored.
                    return LoadoutCategory.Weapons;
            }
        }

        public static LoadoutCategory CategoryOf(SavedItem saved) => Categorize(LoadoutManager.GetSharedData(saved));

        /// <summary>One cell of a loadout as the panel sees it, with the key to its inclusion.</summary>
        public struct Entry
        {
            public LoadoutCategory Category;
            public string    Label;        // worn slot name, or the hotbar key number, or "" for a quick slot
            public SavedItem Item;         // null for an empty worn slot
            public bool      Included;
            public bool      Worn;         // a fixed worn slot (shown even when empty)
            public WornSlot  Slot;         // worn entries
            public int       HotbarIndex;  // -1 unless a hotbar cell
            public bool      IsExtended;   // a quick-slot / equipment cell, keyed by Cell
            public Vector2i  Cell;

            public string Describe()
            {
                if (Item == null) return $"{Label}: nothing";
                var name = LoadoutManager.GetItemDisplayName(Item);
                return HotbarIndex >= 0 ? $"{name}  (hotbar {Label})" : name;
            }
        }

        /// <summary>
        /// Every cell a loadout has, tagged by category.  Worn slots always appear, empty or
        /// not; hotbar and quick-slot items appear only when saved.
        /// </summary>
        public static List<Entry> Entries(LoadoutSlot slot)
        {
            var list = new List<Entry>(24);
            if (slot == null) return list;
            void AddWorn(LoadoutCategory cat, string label, SavedItem item, WornSlot key) =>
                list.Add(new Entry
                {
                    Category = cat, Label = label, Item = item, Worn = true, Slot = key,
                    Included = slot.IncludesWorn(key), HotbarIndex = -1,
                });
            AddWorn(LoadoutCategory.Armor,       "Helm",    slot.Helmet,   WornSlot.Helmet);
            AddWorn(LoadoutCategory.Armor,       "Chest",   slot.Chest,    WornSlot.Chest);
            AddWorn(LoadoutCategory.Armor,       "Legs",    slot.Legs,     WornSlot.Legs);
            AddWorn(LoadoutCategory.Cape,        "Cape",    slot.Shoulder, WornSlot.Cape);
            AddWorn(LoadoutCategory.Accessories, "Belt",    slot.Utility,  WornSlot.Belt);
            AddWorn(LoadoutCategory.Accessories, "Trinket", slot.Trinket,  WornSlot.Trinket);

            if (slot.Hotbar != null)
                for (int i = 0; i < slot.Hotbar.Length; i++)
                {
                    var h = slot.Hotbar[i];
                    if (h == null || string.IsNullOrEmpty(h.SharedName)) continue;
                    list.Add(new Entry
                    {
                        Category = CategoryOf(h), Label = (i + 1).ToString(), Item = h,
                        Included = slot.IncludesHotbar(i), HotbarIndex = i,
                    });
                }
            if (slot.Extended != null)
                foreach (var ext in slot.Extended)
                {
                    if (ext == null || string.IsNullOrEmpty(ext.SharedName)) continue;
                    list.Add(new Entry
                    {
                        Category = CategoryOf(ext), Label = "", Item = ext,
                        Included = slot.IncludesExtended(ext.GridX, ext.GridY),
                        HotbarIndex = -1, IsExtended = true, Cell = new Vector2i(ext.GridX, ext.GridY),
                    });
                }
            return list;
        }

        public static void SetIncluded(LoadoutSlot slot, Entry e, bool on)
        {
            if (slot == null) return;
            if (e.Worn)                slot.SetWorn(e.Slot, on);
            else if (e.HotbarIndex >= 0) slot.SetHotbar(e.HotbarIndex, on);
            else if (e.IsExtended)     slot.SetExtended(e.Cell.x, e.Cell.y, on);
        }

        public static void Toggle(LoadoutSlot slot, Entry e) => SetIncluded(slot, e, !e.Included);

        /// <summary>How many of a category's cells there are, and how many are in the loadout.</summary>
        public static (int total, int included) CategoryState(LoadoutSlot slot, LoadoutCategory cat)
        {
            int total = 0, included = 0;
            foreach (var e in Entries(slot))
            {
                if (e.Category != cat) continue;
                total++;
                if (e.Included) included++;
            }
            return (total, included);
        }

        public static void SetCategory(LoadoutSlot slot, LoadoutCategory cat, bool on)
        {
            foreach (var e in Entries(slot))
                if (e.Category == cat) SetIncluded(slot, e, on);
        }

        /// <summary>
        /// True when loading this slot would do anything: a worn slot is included (empty or
        /// not — empty means "wear nothing there"), or an included cell has an item.
        /// </summary>
        public static bool HasIncludedContent(LoadoutSlot slot)
        {
            if (slot == null) return false;
            return Entries(slot).Any(e => e.Included && (e.Worn || e.Item != null));
        }
    }
}
