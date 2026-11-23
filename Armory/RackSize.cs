using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Armory
{
    /// <summary>
    /// The rack's storage grid, sized by the synced Rack Width and Rack Height settings
    /// (issue #73).  The size goes onto the rack prefab, so every rack built or loaded from
    /// then on has it, and onto the racks already in the world, whose inventories were made at
    /// the old size.  Synced and locked, so every player opens the same grid: a client with a
    /// smaller grid than the rack's owner would open rows it cannot see.
    ///
    /// The storage started life at 8×5 (May 2026) and came down to 8×4 before the first
    /// release only because the vanilla container panel clips past four rows.  The fit patch at
    /// the bottom of this file is what lifts that limit, so 8×5 is the default again.
    /// </summary>
    internal static class RackSize
    {
        public const int DefaultWidth  = 12;   // 72 slots: the rack is 2.5 times the vanilla wardrobe's volume
        public const int DefaultHeight = 6;    // (50 slots) and still the least dense container in the game
        public const int MaxWidth  = 12;
        public const int MaxHeight = 10;

        // Private in the game; the publicized reference only makes them compile.
        private static readonly AccessTools.FieldRef<Inventory, int> _invWidth  = AccessTools.FieldRefAccess<Inventory, int>("m_width");
        private static readonly AccessTools.FieldRef<Inventory, int> _invHeight = AccessTools.FieldRefAccess<Inventory, int>("m_height");

        public static int Width  => Mathf.Clamp(ArmoryMod.RackWidth?.Value  ?? DefaultWidth,  1, MaxWidth);
        public static int Height => Mathf.Clamp(ArmoryMod.RackHeight?.Value ?? DefaultHeight, 1, MaxHeight);

        // The Small Armory Rack (#84) holds the 8×5 the standard rack had before 2.2.1, and
        // follows the two settings at that proportion rather than with settings of its own.
        public const float SmallWidthRatio  = 8f / 12f;
        public const float SmallHeightRatio = 5f / 6f;
        public static int SmallWidth  => Mathf.Max(1, Mathf.RoundToInt(Width  * SmallWidthRatio));
        public static int SmallHeight => Mathf.Max(1, Mathf.RoundToInt(Height * SmallHeightRatio));

        public static int WidthFor(bool small)  => small ? SmallWidth  : Width;
        public static int HeightFor(bool small) => small ? SmallHeight : Height;

        /// <summary>Put the configured size on both prefabs and on every rack in the world.</summary>
        internal static void Apply()
        {
            foreach (var prefab in ArmoryPieces.Prefabs)
            {
                var prefabContainer = prefab.GetComponent<Container>();
                var kind            = prefab.GetComponent<ArmoryRack>();
                if (prefabContainer == null || kind == null) continue;
                prefabContainer.m_width  = WidthFor(kind.Small);
                prefabContainer.m_height = HeightFor(kind.Small);
            }

            int updated = 0;
            foreach (var rack in ArmoryRack.Instances)
            {
                if (rack == null) continue;
                var storage = rack.GetStorage();
                if (storage == null) continue;
                int w = WidthFor(rack.Small), h = HeightFor(rack.Small);
                if (storage.m_width == w && storage.m_height == h) continue;
                storage.m_width  = w;
                storage.m_height = h;
                var inventory = storage.GetInventory();
                if (inventory == null) continue;   // not yet awake in the world
                _invWidth(inventory)  = w;
                _invHeight(inventory) = h;
                // A smaller grid can leave items beside it.  The owner moves them into free
                // cells (and saves); anyone else leaves that to the owner — the open path
                // migrates again once ownership has passed to whoever opens the rack.
                if (storage.IsOwner())
                    LoadoutManager.MigrateOutOfBoundsItems(inventory);
                updated++;
            }
            Jotunn.Logger.LogInfo($"[Armory] Rack storage {Width}x{Height} ({Width * Height} slots), small {SmallWidth}x{SmallHeight} ({SmallWidth * SmallHeight}); {updated} rack(s) in the world resized.");
        }
    }

    /// <summary>
    /// The vanilla container panel draws cells at a fixed size, so a grid taller than four
    /// rows either scrolls or spills.  When a rack's grid is bigger than the panel, draw its
    /// cells scaled down to fit instead, and size the grid root to the drawn grid so nothing is
    /// left to scroll.  Lifted from TheGreatestShips, which does the same for its holds.
    /// </summary>
    [HarmonyPatch(typeof(InventoryGrid), "UpdateGui")]
    internal static class RackGridFitPatch
    {
        private static readonly AccessTools.FieldRef<InventoryGui, InventoryGrid> _containerGrid =
            AccessTools.FieldRefAccess<InventoryGui, InventoryGrid>("m_containerGrid");
        private static readonly AccessTools.FieldRef<InventoryGrid, Inventory> _inventory =
            AccessTools.FieldRefAccess<InventoryGrid, Inventory>("m_inventory");
        private static readonly AccessTools.FieldRef<InventoryGrid, List<InventoryElement>> _elements =
            AccessTools.FieldRefAccess<InventoryGrid, List<InventoryElement>>("m_elements");
        private static readonly AccessTools.FieldRef<InventoryGrid, int>   _width  = AccessTools.FieldRefAccess<InventoryGrid, int>("m_width");
        private static readonly AccessTools.FieldRef<InventoryGrid, int>   _height = AccessTools.FieldRefAccess<InventoryGrid, int>("m_height");
        private static readonly AccessTools.FieldRef<InventoryGrid, float> _space  = AccessTools.FieldRefAccess<InventoryGrid, float>("m_elementSpace");

        private static bool _loggedScale;

        [HarmonyPostfix]
        private static void Postfix(InventoryGrid __instance)
        {
            var gui = InventoryGui.instance;
            if (gui == null || __instance != _containerGrid(gui)) return;
            // Judged by the inventory the grid is drawing, not by the opened container: a
            // preview (PeekAChest) hands a rack's inventory to this grid with nothing opened,
            // and gated on the opened container it drew the full grid with a scroll bar.
            if (!ArmoryRack.IsRackInventory(_inventory(__instance))) return;

            var elements = _elements(__instance);
            if (elements == null || elements.Count == 0) return;
            int width  = _width(__instance);
            int height = _height(__instance);
            if (width <= 0 || height <= 0) return;

            var panel = __instance.transform as RectTransform;
            float space      = _space(__instance);
            float gridWidth  = width * space;
            float gridHeight = height * space;
            if (panel == null || panel.rect.width <= 0f || panel.rect.height <= 0f) return;

            float scale = Mathf.Min(1f, panel.rect.width / gridWidth, panel.rect.height / gridHeight);
            if (scale >= 1f) return;

            // UpdateGui just sized the grid root to the full grid; size it to the drawn one so
            // nothing is left to scroll.
            if (__instance.m_gridRoot != null)
                __instance.m_gridRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, gridHeight * scale);

            var first = elements[0].transform as RectTransform;
            if (first == null || Mathf.Approximately(first.localScale.x, scale)) return;   // already fitted

            float start = panel.rect.width / 2f - gridWidth * scale / 2f;
            for (int i = 0; i < elements.Count; i++)
            {
                var rect = elements[i].transform as RectTransform;
                if (rect == null) continue;
                int x = i % width, y = i / width;
                rect.localScale       = new Vector3(scale, scale, 1f);
                rect.anchoredPosition = new Vector2(start + x * space * scale, -y * space * scale);
            }
            if (!_loggedScale)
            {
                _loggedScale = true;
                Jotunn.Logger.LogInfo($"[Armory] Rack grid {width}x{height} is bigger than the container panel ({panel.rect.width:0}x{panel.rect.height:0}); cells drawn at {scale:0.00} scale to fit.");
            }
        }
    }
}
