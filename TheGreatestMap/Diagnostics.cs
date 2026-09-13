using System.Collections.Generic;
using UnityEngine;

namespace TheGreatestMap
{
    /// <summary>The tgm_look report: what the crosshair hits and every reason it would or would not be recorded.</summary>
    internal static class Diagnostics
    {
        private static readonly RaycastHit[] _hits = new RaycastHit[256];

        internal static List<string> Describe()
        {
            var lines = new List<string>();
            var player = Player.m_localPlayer;
            var cam = GameCamera.instance;
            if (player == null || cam == null) { lines.Add("No player or camera."); return lines; }

            lines.Add($"map out: {PocketMap.IsOut}, recording enabled: {TgmConfig.RecordEnabled.Value}, interior: {player.InInterior()}, indexed locations: {LocationIndex.Count}");

            var hover = player.GetHoverObject();
            lines.Add("hover object: " + (hover != null ? hover.name : "none"));

            float maxLook = TgmConfig.MaxLookDistance();
            int n = Physics.RaycastNonAlloc(cam.transform.position, cam.transform.forward, _hits, maxLook, Access.InteractMask(player));
            RaycastHit best = default;
            float bestDistance = float.MaxValue;
            bool any = false;
            for (int i = 0; i < n; i++)
            {
                var hit = _hits[i];
                if (hit.collider == null) continue;
                var body = hit.collider.attachedRigidbody;
                if (body != null && body.gameObject == player.gameObject) continue;
                if (hit.collider.GetComponentInParent<Player>() == player) continue;
                if (hit.distance < bestDistance) { bestDistance = hit.distance; best = hit; any = true; }
            }
            lines.Add($"ray: {n} hits within {maxLook:0} m" + (n >= _hits.Length ? " (buffer full)" : ""));
            if (!any) { lines.Add("nearest hit: nothing"); return lines; }

            var go = best.collider.gameObject;
            lines.Add($"nearest hit: '{go.name}' at {bestDistance:0.0} m, layer {LayerMask.LayerToName(go.layer)}, prefab '{Utils.GetPrefabName(go)}'");
            var piece = go.GetComponentInParent<Piece>();
            var wear = go.GetComponentInParent<WearNTear>();
            var location = go.GetComponentInParent<Location>();
            lines.Add($"  piece: {(piece != null ? (piece.IsPlacedByPlayer() ? "player-built" : "world") : "none")}, wearntear: {(wear != null)}, container: {(go.GetComponentInParent<Container>() != null)}, door: {(go.GetComponentInParent<Door>() != null)}, location parent: {(location != null ? Utils.GetPrefabName(location.gameObject) : "none")}");
            lines.Add($"  world piece: {Catalog.IsWorldPiece(go)}");

            foreach (var near in LocationIndex.Nearest(best.point, 3))
            {
                float d = Geo.FlatDistance(best.point, near.Pos);
                lines.Add($"  indexed location '{near.Prefab}' {d:0.0} m from hit, radius {near.Radius:0.0}{(d <= near.Radius ? " (contains hit)" : "")}");
            }
            if (LocationIndex.Count == 0) lines.Add("  no indexed locations at all (LocationProxy hook not firing?)");

            if (!Catalog.TryClassify(go, out var found))
            {
                lines.Add("classified: no (nothing recordable)" + (Buildings.LastRejectReason != null ? ": " + Buildings.LastRejectReason : ""));
                return lines;
            }
            lines.Add($"classified: {found.Cat} '{found.Name}' icon {found.Icon} at {found.Pos}");
            var building = found.Cat == Category.Structure ? Buildings.Peek(go) : null;
            if (building != null) lines.Add($"  building: {building.Pieces} connected pieces, centre {building.Centroid}, id {building.Id}");
            float lookDistance = TgmConfig.LookDistanceFor(found.Cat);
            lines.Add($"  look distance for {found.Cat}: {lookDistance:0} m -> {(bestDistance <= lookDistance ? "in range" : "TOO FAR to count as seen")}");
            lines.Add($"  kind enabled: {(TgmConfig.CategoryEnabled.TryGetValue(found.Cat, out var en) && en.Value)}");
            lines.Add($"  pending (found, not yet recorded): {DiscoveryLedger.IsPending(found.Key)}, recorded this session: {DiscoveryLedger.IsRecorded(found.Key)}");
            float spacing = TgmConfig.MarkerSpacing.TryGetValue(found.Cat, out var s) ? s.Value : 1f;
            lines.Add($"  marker with this icon within {spacing:0.#} m: {ClientPins.HasPinNear(found.Icon, found.Pos, spacing)}, erased spot here: {ClientPins.IsSuppressed(found.Icon, found.Pos, spacing)}");
            return lines;
        }
    }
}
