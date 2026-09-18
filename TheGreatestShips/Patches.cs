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

            var grown = new List<GameObject>(slots);
            while (grown.Count < needed)
            {
                var last  = grown[grown.Count - 1];
                var clone = Object.Instantiate(last, last.transform.parent);
                clone.name = last.name;
                clone.transform.SetSiblingIndex(last.transform.GetSiblingIndex() + 1);
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
}
