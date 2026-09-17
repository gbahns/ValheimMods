using HarmonyLib;
using UnityEngine;

namespace TheGreatestMap
{
    /// <summary>
    /// A campfire's marker lasts as long as the campfire. Building one is noted like any other
    /// find (see Piece_SetCreator_Patch) and written the next time the map comes out; taking one
    /// down removes its marker for everyone, the way a portal's goes. Nothing is suppressed, so a
    /// campfire built again on the same spot is recorded again.
    /// </summary>
    internal static class Campfires
    {
        private const float MatchRadius = 1.5f;

        internal static void NoteRemoved(WearNTear wear)
        {
            if (wear == null || Player.m_localPlayer == null || wear.GetComponent<Fireplace>() == null) return;
            if (!Catalog.TryClassify(wear.gameObject, out var found) || found.Cat != Category.Campfire) return;
            DiscoveryLedger.Forget(found.Key);
            if (!PersonalMap.Loaded) return;
            SharedPin best = null;
            float bestDistance = MatchRadius;
            foreach (var pin in ClientPins.All)
            {
                if (!pin.Auto || ClientPins.KindOf(pin) != Category.Campfire) continue;
                float d = Geo.FlatDistance(pin.Pos, found.Pos);
                if (d <= bestDistance) { bestDistance = d; best = pin; }
            }
            if (best == null) return;
            ClientPins.RemoveStale(best.Id);
            ClientPins.Restyle();
            TheGreatestMapMod.Message("Campfire taken down; its marker is gone.");
        }
    }

    // The same two moments a portal's removal is caught: Destroy runs only on whoever owns the
    // piece, and Remove is the hammer, which need not be the owner.
    [HarmonyPatch(typeof(WearNTear), "Destroy")]
    internal static class WearNTear_Destroy_Campfire_Patch
    {
        private static void Prefix(WearNTear __instance) => Campfires.NoteRemoved(__instance);
    }

    [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.Remove))]
    internal static class WearNTear_Remove_Campfire_Patch
    {
        private static void Prefix(WearNTear __instance) => Campfires.NoteRemoved(__instance);
    }
}
