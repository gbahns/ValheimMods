using HarmonyLib;
using UnityEngine;

namespace ForesakenShrines
{
    /// <summary>
    /// Hard-blocks shrine placement if the site fails either of:
    ///   1. Natural terrain — no player-placed floor/platform beneath the shrine.
    ///   2. Sky exposure   — no roof piece directly above the shrine.
    /// </summary>
    [HarmonyPatch(typeof(Player), nameof(Player.PlacePiece))]
    internal static class ShrinePlacement
    {
        private static readonly System.Reflection.FieldInfo _placementGhostField =
            AccessTools.Field(typeof(Player), "m_placementGhost");

        [HarmonyPrefix]
        static bool Prefix(Player __instance)
        {
            var ghost = _placementGhostField.GetValue(__instance) as GameObject;
            if (ghost == null) return true;

            // Only intercept our own shrine pieces.
            if (ghost.GetComponent<ShrineInteractable>() == null) return true;

            var pos = ghost.transform.position;

            if (ShrineConfig.RequireNaturalTerrain.Value && !IsNaturalTerrain(pos))
            {
                __instance.Message(MessageHud.MessageType.Center, "$shrine_placement_nofloor");
                return false;
            }

            if (ShrineConfig.RequireSkyExposure.Value && !HasSkyExposure(pos))
            {
                __instance.Message(MessageHud.MessageType.Center, "$shrine_placement_noroof");
                return false;
            }

            return true;
        }

        // Returns false if any player-placed piece (floor, platform, etc.) underlies the shrine.
        private static bool IsNaturalTerrain(Vector3 pos)
        {
            var hits = Physics.OverlapSphere(
                pos + Vector3.down * 0.6f,
                radius: 0.5f,
                LayerMask.GetMask("piece", "piece_nonsolid"));

            foreach (var col in hits)
            {
                var p = col.GetComponentInParent<Piece>();
                if (p != null && p.IsPlacedByPlayer())
                    return false;
            }
            return true;
        }

        // Returns false if a roof piece sits above the shrine (same logic as vanilla altar check).
        private static bool HasSkyExposure(Vector3 pos)
        {
            // Cover.IsUnderRoof samples several points above pos; a shrine is ~2 m tall so
            // we lift the sample point to catch low-headroom roofs that clear the base.
            return !Cover.IsUnderRoof(pos + Vector3.up * 1.5f);
        }
    }

    /// <summary>
    /// When the game sets a global key (boss defeated), re-evaluate shrine unlock states so
    /// newly killed bosses immediately unlock their shrine recipe without requiring a relog.
    /// </summary>
    [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.SetGlobalKey), typeof(string))]
    internal static class ZoneSystemSetGlobalKeyPatch
    {
        [HarmonyPostfix]
        static void Postfix(string name)
        {
            Jotunn.Logger.LogInfo($"[ForesakenShrines] ZoneSystem.SetGlobalKey('{name}') fired — refreshing unlocks.");
            ShrinePieces.UpdateUnlocks();
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
    internal static class PlayerOnSpawnedPatch
    {
        [HarmonyPostfix]
        static void Postfix()
        {
            Jotunn.Logger.LogInfo("[ForesakenShrines] Player.OnSpawned fired — refreshing unlocks.");
            ShrinePieces.UpdateUnlocks();
        }
    }

    /// <summary>
    /// After Valheim positions the placement ghost each frame, snap shrine ghosts back to terrain
    /// height.  BossStone-derived prefabs have an internal placement offset that pushes the ghost
    /// root ~2.3 m underground; this corrects both the visual and the position that PlacePiece
    /// writes to the ZDO.
    /// </summary>
    [HarmonyPatch(typeof(Player), "UpdatePlacementGhost")]
    internal static class PlacementGhostHeightPatch
    {
        private static readonly System.Reflection.FieldInfo _ghostField =
            AccessTools.Field(typeof(Player), "m_placementGhost");

        [HarmonyPostfix]
        static void Postfix(Player __instance)
        {
            var ghost = _ghostField.GetValue(__instance) as GameObject;
            if (ghost == null || !ghost.activeSelf) return;
            if (ghost.GetComponent<ShrineInteractable>() == null) return;

            if (!Physics.Raycast(ghost.transform.position + Vector3.up * 100f, Vector3.down,
                    out var hit, 200f, LayerMask.GetMask("terrain")))
                return;

            float delta = ghost.transform.position.y - hit.point.y;
            if (delta < -0.1f)
                ghost.transform.position = new Vector3(ghost.transform.position.x, hit.point.y, ghost.transform.position.z);
        }
    }
}
