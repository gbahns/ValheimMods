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
    /// A hold wider than the inventory panel (the Busse's is nine slots; vanilla never goes past
    /// eight) overflows it: InventoryGrid.UpdateGui centers the grid in the panel, so half a slot
    /// falls off each side.  The grid root is pinned every frame, so the slots themselves are
    /// scaled down and re-spaced to fit the panel's width, keeping the same centering.  Runs only
    /// after UpdateGui has (re)built the elements: they come back at scale 1.
    /// </summary>
    [HarmonyPatch(typeof(InventoryGrid), "UpdateGui")]
    internal static class InventoryGridFitPatch
    {
        // Private in the game; the publicized reference assembly only makes them compile.
        static readonly AccessTools.FieldRef<InventoryGrid, List<InventoryElement>> _elements =
            AccessTools.FieldRefAccess<InventoryGrid, List<InventoryElement>>("m_elements");
        static readonly AccessTools.FieldRef<InventoryGrid, int> _width  = AccessTools.FieldRefAccess<InventoryGrid, int>("m_width");
        static readonly AccessTools.FieldRef<InventoryGrid, int> _height = AccessTools.FieldRefAccess<InventoryGrid, int>("m_height");
        static readonly AccessTools.FieldRef<InventoryGrid, float> _space = AccessTools.FieldRefAccess<InventoryGrid, float>("m_elementSpace");

        [HarmonyPostfix]
        static void Postfix(InventoryGrid __instance)
        {
            var elements = _elements(__instance);
            if (elements == null || elements.Count == 0) return;
            int width  = _width(__instance);
            int height = _height(__instance);
            if (width <= 0 || height <= 0) return;

            var panel = __instance.transform as RectTransform;
            float space     = _space(__instance);
            float gridWidth = width * space;
            if (panel == null || panel.rect.width <= 0f || gridWidth <= panel.rect.width) return;

            float scale = panel.rect.width / gridWidth;
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
            Jotunn.Logger.LogInfo($"[TheGreatestShips] Inventory grid {width}x{height} is wider than its panel; slots drawn at {scale:0.00} scale to fit.");
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
