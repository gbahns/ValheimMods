using System.Collections.Generic;
using UnityEngine;

namespace TheGreatestMap
{
    /// <summary>The tgm_look report: what the crosshair hits and every reason it would or would not be recorded.</summary>
    internal static class Diagnostics
    {
        private static readonly RaycastHit[] _hits = new RaycastHit[256];

        /// <summary>The components classification cares about, if this object carries any.</summary>
        private static string Components(GameObject go)
        {
            var parts = new List<string>();
            if (go.GetComponent<MineRock5>() != null) parts.Add("MineRock5");
            if (go.GetComponent<MineRock>() != null) parts.Add("MineRock");
            if (go.GetComponent<Pickable>() != null) parts.Add("Pickable");
            if (go.GetComponent<Destructible>() != null) parts.Add("Destructible");
            if (go.GetComponent<DropOnDestroyed>() != null) parts.Add("DropOnDestroyed");
            if (go.GetComponent<Piece>() != null) parts.Add("Piece");
            if (go.GetComponent<Location>() != null) parts.Add("Location");
            if (go.GetComponent<LocationProxy>() != null) parts.Add("LocationProxy");
            if (go.GetComponent<ZNetView>() != null) parts.Add("ZNetView");
            return parts.Count == 0 ? " — no components of interest" : " — " + string.Join(", ", parts.ToArray());
        }

        internal static List<string> Describe()
        {
            var lines = new List<string>();
            var player = Player.m_localPlayer;
            var cam = GameCamera.instance;
            if (player == null || cam == null) { lines.Add("No player or camera."); return lines; }

            lines.Add("can write now: " + (Recorder.CanWrite(player, out string blocked) ? "yes" : "NO - " + blocked));
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
            lines.Add($"nearest hit: '{go.name}' at {bestDistance:0.0} m, layer {LayerMask.LayerToName(go.layer)}, prefab '{Catalog.PrefabName(go)}'");
            var piece = go.GetComponentInParent<Piece>();
            var wear = go.GetComponentInParent<WearNTear>();
            var location = go.GetComponentInParent<Location>();
            lines.Add($"  piece: {(piece != null ? (piece.IsPlacedByPlayer() ? "player-built" : "world") : "none")}, wearntear: {(wear != null)}, container: {(go.GetComponentInParent<Container>() != null)}, door: {(go.GetComponentInParent<Door>() != null)}, location parent: {(location != null ? Utils.GetPrefabName(location.gameObject) : "none")}");
            lines.Add($"  world piece: {Catalog.IsWorldPiece(go)}");

            // Classification looks for a component on the object or any of its parents, so when it
            // finds nothing the useful question is what the chain actually holds. A collider that
            // belongs to scenery rather than to a deposit looks identical until you see this.
            var t = go.transform;
            for (int depth = 0; t != null && depth < 8; depth++, t = t.parent)
                lines.Add($"    {(depth == 0 ? "hit" : "parent " + depth)}: '{t.name}'{Components(t.gameObject)}");

            // Whether a plant grows back decides whether its marker stays true after harvesting.
            // The respawn time is set per prefab in the game's own assets, so the only way to know
            // is to ask the thing in front of you.
            var pickable = go.GetComponentInParent<Pickable>();
            if (pickable != null)
            {
                string gives = pickable.m_itemPrefab != null ? Utils.GetPrefabName(pickable.m_itemPrefab) : "nothing";
                float respawn = pickable.m_respawnTimeMinutes;
                string regrows = respawn <= 0f
                    ? "never regrows once picked"
                    : $"regrows after {respawn:0} minutes ({respawn / 60f:0.0} hours)";
                lines.Add($"  pickable: gives {gives} x{pickable.m_amount}, {regrows}");
            }

            foreach (var near in LocationIndex.Nearest(best.point, 3))
            {
                float d = Geo.FlatDistance(best.point, near.Pos);
                lines.Add($"  indexed location '{near.Prefab}' {d:0.0} m from hit, radius {near.Radius:0.0}{(d <= near.Radius ? " (contains hit)" : "")}");
            }
            if (LocationIndex.Count == 0) lines.Add("  no indexed locations at all (LocationProxy hook not firing?)");

            lines.Add("  what the catalog makes of it: " + Catalog.Explain(go));

            if (!Catalog.TryClassify(go, out var found))
            {
                lines.Add("classified: no (nothing recordable)" + (Buildings.LastRejectReason != null ? ": " + Buildings.LastRejectReason : ""));
                return lines;
            }
            lines.Add($"classified: {found.Cat} '{found.Name}' icon {found.Icon} at {found.Pos}");
            var building = found.Cat == Category.Structure ? Buildings.Peek(go) : null;
            if (building != null) lines.Add($"  building: {building.Pieces} connected pieces, center {building.Centroid}, id {building.Id}");
            float lookDistance = TgmConfig.LookDistanceFor(found.Cat);
            lines.Add($"  look distance for {found.Cat}: {lookDistance:0} m -> {(bestDistance <= lookDistance ? "in range" : "TOO FAR to count as seen")}");
            lines.Add($"  kind enabled: {(TgmConfig.CategoryEnabled.TryGetValue(found.Cat, out var en) && en.Value)}");
            lines.Add($"  pending (found, not yet recorded): {DiscoveryLedger.IsPending(found.Key)}, recorded this session: {DiscoveryLedger.IsRecorded(found.Key)}");
            // The same center and radius the recorder uses, so this reports what would actually
            // happen rather than something close to it. Locations dedupe over their whole radius.
            float spacing = TgmConfig.MarkerSpacing.TryGetValue(found.Cat, out var s) ? s.Value : 1f;
            float dedupeRadius = Mathf.Max(spacing, found.Radius);
            Vector3 dedupeAt = found.DedupeCenter;
            lines.Add($"  dedupe: {dedupeRadius:0.#} m around {dedupeAt}");
            lines.Add($"  marker with this icon already there: {ClientPins.HasPinNear(found.Icon, dedupeAt, dedupeRadius)}");
            lines.Add($"  erased spot here (blocks recording): {ClientPins.IsSuppressed(found.Icon, dedupeAt, dedupeRadius)}");
            lines.Add($"  any marker of this kind nearby: {ClientPins.DescribeNear(found.Cat, dedupeAt, Mathf.Max(dedupeRadius, 30f))}");
            return lines;
        }
    }
}
