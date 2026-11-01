using HarmonyLib;
using UnityEngine;

namespace TheGreatestMap
{
    /// <summary>
    /// A piece a player built keeps its marker as long as the piece. Building one is noted like
    /// any other find (see Piece_SetCreator_Patch) and written the next time the map comes out;
    /// taking it down removes its marker for everyone, the way a portal's goes. Nothing is
    /// suppressed, so one built again on the same spot is recorded again.
    ///
    /// <para>Campfires and workbenches both: a base is rebuilt and moved as it grows, and a
    /// marker left behind for a bench that was taken down last week says the spawning is being
    /// held off somewhere it is not -- which is the one thing these markers are for.</para>
    /// </summary>
    internal static class BuiltPieces
    {
        private const float MatchRadius = 1.5f;

        internal static void NoteRemoved(WearNTear wear)
        {
            if (wear == null || Player.m_localPlayer == null) return;
            // The components that make one of these what it is. Asked before the catalog, because
            // a base holds hundreds of walls and floors and none of them is a marker.
            if (wear.GetComponent<Fireplace>() == null && wear.GetComponent<CraftingStation>() == null) return;
            if (!Catalog.TryClassify(wear.gameObject, out var found) || !Categories.IsBuilt(found.Cat)) return;
            DiscoveryLedger.Forget(found.Key);
            if (!PersonalMap.Loaded) return;
            SharedPin best = null;
            float bestDistance = MatchRadius;
            foreach (var pin in ClientPins.All)
            {
                if (!pin.Auto || ClientPins.KindOf(pin) != found.Cat) continue;
                float d = Geo.FlatDistance(pin.Pos, found.Pos);
                if (d <= bestDistance) { bestDistance = d; best = pin; }
            }
            if (best == null) return;
            ClientPins.RemoveStale(best.Key);
            ClientPins.Restyle();
            TheGreatestMapMod.Message($"{found.Name} taken down; its marker is gone.");
        }
    }

    // The same two moments a portal's removal is caught: Destroy runs only on whoever owns the
    // piece, and Remove is the hammer, which need not be the owner.
    [HarmonyPatch(typeof(WearNTear), "Destroy")]
    internal static class WearNTear_Destroy_Built_Patch
    {
        private static void Prefix(WearNTear __instance) => BuiltPieces.NoteRemoved(__instance);
    }

    [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.Remove))]
    internal static class WearNTear_Remove_Built_Patch
    {
        private static void Prefix(WearNTear __instance) => BuiltPieces.NoteRemoved(__instance);
    }
}
