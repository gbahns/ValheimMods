using System.Collections.Generic;
using HarmonyLib;
using Jotunn.Managers;
using UnityEngine;

namespace TheGreatestShips
{
    /// <summary>
    /// The vanilla Karve's hold, sized by the Karve Hold Slots setting (issue #57: the jump
    /// from the Karve's 4 slots to the longship's 18 is bigger than the hulls are).  The size
    /// goes onto the Karve prefab, so every Karve built or loaded from then on has it, and onto
    /// the Karves already in the world, whose inventories were made at the old size.  Synced,
    /// so everyone sees the same grid.
    /// </summary>
    internal static class KarveHold
    {
        /// <summary>The slot counts a hold setting offers: each has a grid in <see cref="Layout"/>.</summary>
        internal static readonly int[] Sizes = { 4, 6, 8, 9, 10, 12, 15, 16, 18 };

        private static readonly AccessTools.FieldRef<Inventory, int> _width  = AccessTools.FieldRefAccess<Inventory, int>("m_width");
        private static readonly AccessTools.FieldRef<Inventory, int> _height = AccessTools.FieldRefAccess<Inventory, int>("m_height");

        /// <summary>The grid for a slot count the setting allows: the widest rectangle up to the
        /// longship's six columns, so it reads as a hold and not a column.</summary>
        internal static Vector2Int Layout(int slots)
        {
            switch (slots)
            {
                case 6:  return new Vector2Int(3, 2);
                case 8:  return new Vector2Int(4, 2);
                case 9:  return new Vector2Int(3, 3);
                case 10: return new Vector2Int(5, 2);
                case 12: return new Vector2Int(6, 2);
                case 15: return new Vector2Int(5, 3);
                case 16: return new Vector2Int(4, 4);
                case 18: return new Vector2Int(6, 3);
                default: return new Vector2Int(2, 2);   // the game's own
            }
        }

        internal static void Apply()
        {
            var prefab = PrefabManager.Instance?.GetPrefab("Karve");
            if (prefab == null) return;
            var grid = Layout(ShipConfig.KarveHoldSlots.Value);
            int? updated = Resize(prefab, "Karve", grid);
            if (updated.HasValue)
                Jotunn.Logger.LogInfo($"[TheGreatestShips] Karve hold {grid.x}x{grid.y} ({grid.x * grid.y} slots); {updated} Karve(s) in the world updated.");
        }

        /// <summary>Sizes the hold of a ship prefab and of every ship of it afloat (this mod's
        /// configurable holds use it too).</summary>
        /// <returns>How many ships afloat were updated, or null when the prefab already had that grid.</returns>
        internal static int? Resize(GameObject prefab, string prefabName, Vector2Int grid)
        {
            var container = prefab.GetComponentInChildren<Container>(true);
            if (container == null) return null;
            if (container.m_width == grid.x && container.m_height == grid.y) return null;
            container.m_width  = grid.x;
            container.m_height = grid.y;

            int updated = 0;
            foreach (var updater in Ship.Instances)
            {
                if (!(updater is Ship ship) || Utils.GetPrefabName(ship.gameObject) != prefabName) continue;
                var hold = ship.GetComponentInChildren<Container>(true);
                if (hold == null) continue;
                hold.m_width  = grid.x;
                hold.m_height = grid.y;
                var inventory = hold.GetInventory();
                if (inventory == null) continue;   // not yet awake in the world
                _width(inventory)  = grid.x;
                _height(inventory) = grid.y;
                if (hold.IsOwner())
                    HoldGrid.Fit(hold);            // cargo outside the new grid finds a slot, or keeps its row
                else
                    HoldGrid.KeepRows(hold);       // the owner moves the cargo; until then, show it where it is
                updated++;
            }
            return updated;
        }
    }

    /// <summary>
    /// A hold whose grid got smaller than the cargo in it: a ship whose hold the server now
    /// says is smaller, a Karve shrunk by Karve Hold Slots.  The game only ever grows a grid
    /// (Container.UpdateRows adds rows under cargo that sits below the bottom), so cargo left
    /// beside the grid would be unreachable.
    /// </summary>
    internal static class HoldGrid
    {
        // Private in the game (the publicized reference only makes it compile): marks the
        // inventory dirty so the owner saves it.
        private static readonly System.Reflection.MethodInfo _changed = AccessTools.Method(typeof(Inventory), "Changed");

        /// <summary>Owner only: moves every stack outside the container's grid into a free slot
        /// of it, and once nothing sits below the grid, takes the grown rows away.  A stack with
        /// no slot to go to stays where the grown rows show it.</summary>
        internal static void Fit(Container container)
        {
            var inventory = container.GetInventory();
            if (inventory == null) return;
            int width = container.m_width, height = container.m_height;   // the intended grid, not the grown one
            var items = inventory.GetAllItems();
            var taken = new HashSet<Vector2i>();
            foreach (var item in items) taken.Add(item.m_gridPos);

            int moved = 0;
            bool stranded = false;
            foreach (var item in new List<ItemDrop.ItemData>(items))
            {
                if (item.m_gridPos.x < width && item.m_gridPos.y < height) continue;
                var slot = new Vector2i(-1, -1);
                for (int y = 0; y < height && slot.x < 0; y++)
                    for (int x = 0; x < width; x++)
                        if (!taken.Contains(new Vector2i(x, y))) { slot = new Vector2i(x, y); break; }
                if (slot.x < 0) { stranded = true; break; }   // no room: leave it where the grown rows show it
                taken.Remove(item.m_gridPos);
                taken.Add(slot);
                item.m_gridPos = slot;
                moved++;
            }
            if (stranded)
                KeepRows(container);
            else if (inventory.GetHeight() != height)
                inventory.SetHeight(height);   // undo UpdateRows now that nothing sits below the grid
            if (moved > 0)
            {
                _changed?.Invoke(inventory, null);
                Jotunn.Logger.LogInfo($"[TheGreatestShips] {container.m_name}: moved {moved} stack(s) that sat outside the {width}x{height} hold into free slots.");
            }
        }

        /// <summary>Grows the grid's rows to reach the lowest stack, as the game's UpdateRows
        /// does, so nothing aboard is hidden.</summary>
        internal static void KeepRows(Container container)
        {
            var inventory = container.GetInventory();
            if (inventory == null) return;
            int height = container.m_height;
            foreach (var item in inventory.GetAllItems())
                height = Mathf.Max(height, item.m_gridPos.y + 1);
            if (inventory.GetHeight() != height) inventory.SetHeight(height);
        }
    }
}
