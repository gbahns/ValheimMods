using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace TheGreatestShips
{
    /// <summary>
    /// World-load hooks: on every world load put the ships in the Hammer's piece table and in
    /// ZNetScene's prefab registry, and resolve their recipes.  Both callees catch their own
    /// exceptions, since one escaping an Awake postfix would abort the game's initialization.
    /// </summary>
    [HarmonyPatch(typeof(ObjectDB), "Awake")]
    internal static class ObjectDBAwakePatch
    {
        [HarmonyPostfix, HarmonyPriority(Priority.Last)]
        static void Postfix() => ShipPrefabs.OnObjectDBAwake();
    }

    /// <summary>The camera's boat zoom limit, from the config, once the camera exists.</summary>
    [HarmonyPatch(typeof(GameCamera), "Awake")]
    internal static class GameCameraAwakePatch
    {
        [HarmonyPostfix]
        static void Postfix(GameCamera __instance) => SailLook.ApplyCamera(__instance);
    }

    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    internal static class ZNetSceneAwakePatch
    {
        [HarmonyPostfix, HarmonyPriority(Priority.Last)]
        static void Postfix() => ShipPrefabs.OnZNetSceneAwake();
    }

    /// <summary>
    /// The build HUD shows a piece's requirements in a fixed row of slots and puts the crafting
    /// station in the slot after the last ingredient, so a recipe with as many ingredients as
    /// there are slots throws IndexOutOfRange (Hud.SetupPieceInfo) the moment it's selected.
    /// Several ships here have six or seven ingredients.  Grow the row by cloning its last slot
    /// as needed; the clones inherit the slot's layout and are left in place for later pieces.
    /// </summary>
    [HarmonyPatch(typeof(Hud), "SetupPieceInfo")]
    internal static class HudSetupPieceInfoPatch
    {
        static bool _logged;

        [HarmonyPrefix]
        static void Prefix(Hud __instance, Piece piece)
        {
            var slots = __instance.m_requirementItems;
            if (piece == null || piece.m_resources == null || slots == null || slots.Length == 0) return;

            int needed = piece.m_resources.Length + 1; // + the crafting station
            if (needed <= slots.Length) return;

            // The slots are placed by hand, not by a layout group, so a clone lands exactly on
            // top of the slot it was copied from: step each one along by the spacing between the
            // last two existing slots.
            var grown = new List<GameObject>(slots);
            Vector2 step = Vector2.zero;
            if (slots.Length >= 2)
            {
                var a = slots[slots.Length - 2].GetComponent<RectTransform>();
                var b = slots[slots.Length - 1].GetComponent<RectTransform>();
                if (a != null && b != null) step = b.anchoredPosition - a.anchoredPosition;
            }
            while (grown.Count < needed)
            {
                var last  = grown[grown.Count - 1];
                var clone = Object.Instantiate(last, last.transform.parent);
                clone.name = last.name;
                clone.transform.SetSiblingIndex(last.transform.GetSiblingIndex() + 1);
                var rect = clone.GetComponent<RectTransform>();
                if (rect != null) rect.anchoredPosition += step;
                grown.Add(clone);
            }
            __instance.m_requirementItems = grown.ToArray();

            if (!_logged)
            {
                _logged = true;
                Jotunn.Logger.LogInfo($"[TheGreatestShips] Build HUD had {slots.Length} requirement slots; grew it to {grown.Count} for {piece.m_name}.");
            }
        }
    }

    /// <summary>
    /// Items are saved by grid position, and the grid's size is not saved: it comes from the
    /// prefab on every load.  So a hold that changed shape leaves some items outside the new
    /// grid.  Vanilla half-covers this: Container.UpdateRows, run after every load, grows the
    /// inventory's height to the lowest row with an item in it (an 8 x 8 hold that became 11 x 6
    /// came back as 11 x 8), but nothing covers a lost column.  After a hold loads, anything
    /// outside the intended grid (the Container's own width and height) moves into a free slot
    /// inside it, and the height is set back.  Only the owner's copy is touched, and Changed()
    /// makes the owner save it.
    /// </summary>
    [HarmonyPatch(typeof(Container), "Load")]
    internal static class ContainerLoadFitPatch
    {
        static readonly HashSet<string> _ourShips =
            new HashSet<string>(System.Linq.Enumerable.Select(ShipDefinitions.All, d => d.PrefabName));

        [HarmonyPostfix]
        static void Postfix(Container __instance, bool __result)
        {
            if (!__result || __instance == null || !__instance.IsOwner()) return;
            if (!_ourShips.Contains(Utils.GetPrefabName(__instance.transform.root.gameObject))) return;

            var inventory = __instance.GetInventory();
            if (inventory == null) return;
            int width = __instance.m_width, height = __instance.m_height;   // the intended grid, not the grown one
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
                if (slot.x < 0) { stranded = true; break; }   // no room: leave it where vanilla's grown rows show it
                taken.Remove(item.m_gridPos);
                taken.Add(slot);
                item.m_gridPos = slot;
                moved++;
            }
            if (!stranded && inventory.GetHeight() != height)
                inventory.SetHeight(height);   // undo UpdateRows now that nothing sits below the grid
            if (moved > 0 || inventory.GetHeight() == height)
            {
                if (moved > 0) inventory.Changed();
                if (moved > 0)
                    Jotunn.Logger.LogInfo($"[TheGreatestShips] {__instance.m_name}: moved {moved} stack(s) that sat outside the {width}x{height} hold into free slots.");
            }
        }
    }

    /// <summary>
    /// A hold bigger than the inventory panel -- wider than its eight columns, or taller than the
    /// rows it shows, as the Big Busse's 8 x 8 is -- either overflows it (InventoryGrid.UpdateGui
    /// centers the grid, so half a slot falls off each side) or scrolls.  The slots are drawn
    /// smaller instead, square, at whatever scale fits both ways, re-spaced about the same
    /// center; the grid root, which UpdateGui sizes to the full grid every call (that is what
    /// makes it scroll), is sized to the drawn grid.  A hold that fits is left alone.
    ///
    /// Only the container grid, and only while one of this mod's ships is open: the player's
    /// grid is another mod's to extend (AzuExtendedPlayerInventory lays its extra rows out
    /// itself, and re-spacing them scrambled its equipment slots), and other containers are
    /// their own mods' business.
    /// </summary>
    [HarmonyPatch(typeof(InventoryGrid), "UpdateGui")]
    internal static class InventoryGridFitPatch
    {
        static readonly HashSet<string> _ourShips =
            new HashSet<string>(System.Linq.Enumerable.Select(ShipDefinitions.All, d => d.PrefabName));

        // Private in the game; the publicized reference assembly only makes them compile, and
        // Mono refuses them at runtime (FieldAccessException, every frame the inventory is open).
        static readonly AccessTools.FieldRef<InventoryGui, InventoryGrid> _containerGrid =
            AccessTools.FieldRefAccess<InventoryGui, InventoryGrid>("m_containerGrid");
        static readonly AccessTools.FieldRef<InventoryGui, Container> _currentContainer =
            AccessTools.FieldRefAccess<InventoryGui, Container>("m_currentContainer");
        static readonly AccessTools.FieldRef<InventoryGrid, List<InventoryElement>> _elements =
            AccessTools.FieldRefAccess<InventoryGrid, List<InventoryElement>>("m_elements");
        static readonly AccessTools.FieldRef<InventoryGrid, int> _width  = AccessTools.FieldRefAccess<InventoryGrid, int>("m_width");
        static readonly AccessTools.FieldRef<InventoryGrid, int> _height = AccessTools.FieldRefAccess<InventoryGrid, int>("m_height");
        static readonly AccessTools.FieldRef<InventoryGrid, float> _space = AccessTools.FieldRefAccess<InventoryGrid, float>("m_elementSpace");

        [HarmonyPostfix]
        static void Postfix(InventoryGrid __instance)
        {
            var gui = InventoryGui.instance;
            if (gui == null || __instance != _containerGrid(gui)) return;
            var container = _currentContainer(gui);
            if (container == null || !_ourShips.Contains(Utils.GetPrefabName(container.transform.root.gameObject))) return;

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
            if (first == null || Mathf.Approximately(first.localScale.x, scale)) return; // already fitted

            float start = panel.rect.width / 2f - gridWidth * scale / 2f;
            for (int i = 0; i < elements.Count; i++)
            {
                var rect = elements[i].transform as RectTransform;
                if (rect == null) continue;
                int x = i % width, y = i / width;
                rect.localScale       = new Vector3(scale, scale, 1f);
                rect.anchoredPosition = new Vector2(start + x * space * scale, -y * space * scale);
            }
            Jotunn.Logger.LogInfo($"[TheGreatestShips] Inventory grid {width}x{height} is bigger than its panel ({panel.rect.width:0}x{panel.rect.height:0}); slots drawn at {scale:0.00} scale to fit.");
        }
    }

    /// <summary>
    /// A ship's ImpactEffect deals its ramming damage (the longship's is 50 blunt, scaled by
    /// speed) to any creature one of its colliders strikes above 1.5 m/s -- and every rail, the
    /// mast and the deck are the ship.  A tamed boar has 10 hit points.  A calm animal moves with
    /// the deck and is never struck; a frightened one runs, and on a ship under sail the mast or
    /// the bow rail meets it at the ship's full speed: one hit, dead.  Players are spared by a
    /// flag on the effect; livestock is not.  On this mod's ships, a tamed creature is: the hit
    /// is skipped outright, knockback included.  Wild creatures are rammed as in vanilla.
    /// </summary>
    [HarmonyPatch(typeof(ImpactEffect), "OnCollisionEnter")]
    internal static class ImpactEffectSparesLivestockPatch
    {
        static readonly HashSet<string> _ourShips =
            new HashSet<string>(System.Linq.Enumerable.Select(ShipDefinitions.All, d => d.PrefabName));

        [HarmonyPrefix]
        static bool Prefix(ImpactEffect __instance, Collision info)
        {
            if (info == null || info.contactCount == 0) return true;

            string name = __instance.name;
            int clone = name.IndexOf('(');
            if (clone > 0) name = name.Substring(0, clone).Trim();
            if (!_ourShips.Contains(name)) return true;

            var other = info.GetContact(0).otherCollider;
            var hit = other != null ? Projectile.FindHitObject(other) : null;
            var character = hit != null ? hit.GetComponent<Character>() : null;
            return character == null || !character.IsTamed();
        }
    }
}
