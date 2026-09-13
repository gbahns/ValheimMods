using System.Collections.Generic;
using UnityEngine;

namespace TheGreatestMap
{
    /// <summary>
    /// Individual buildings inside a location. The game has no building identity, so one is
    /// derived: starting from the piece that was seen, flood-fill through neighboring
    /// world-built pieces (walls, floors, roofs, chests, doors; fences and poles are skipped so
    /// they do not chain buildings together). The connected cluster is the building, marked at
    /// its center. Results are cached per piece for the session.
    /// </summary>
    internal static class Buildings
    {
        internal sealed class Result
        {
            public Vector3 Centroid;
            public int Pieces;
            public string Id;
        }

        private const float LinkDistance = 3f;   // pieces closer than this belong together
        private const int MaxPieces = 400;

        private static readonly Dictionary<int, Result> _cache = new Dictionary<int, Result>();
        private static readonly Collider[] _buffer = new Collider[128];
        private static int _mask = -1;

        private static int Mask
        {
            get
            {
                if (_mask < 0) _mask = LayerMask.GetMask("piece", "piece_nonsolid", "Default", "static_solid", "Default_small");
                return _mask;
            }
        }

        internal static void Clear() => _cache.Clear();

        internal static Result Peek(GameObject piece)
        {
            var root = RootOf(piece);
            return root != null && _cache.TryGetValue(root.GetInstanceID(), out var r) ? r : null;
        }

        private const int MinPieces = 4;

        /// <summary>Why the last Resolve returned null, for tgm_look.</summary>
        internal static string LastRejectReason;

        /// <summary>
        /// The building this piece belongs to, or null if the piece is not part of one: fences,
        /// poles and the like never start a building, and a cluster only counts as a building
        /// when it has a few pieces and at least one floor, wall, roof or door.
        /// </summary>
        internal static Result Resolve(GameObject piece, Vector3 locationCenter, float locationRadius)
        {
            LastRejectReason = null;
            var root = RootOf(piece);
            if (root == null) return null;
            if (_cache.TryGetValue(root.GetInstanceID(), out var cached))
            {
                if (cached == null) LastRejectReason = "already judged not part of a building";
                return cached;
            }
            if (!IsBuildingPiece(root))
            {
                LastRejectReason = $"'{Utils.GetPrefabName(root)}' is a fence, pole or similar, not part of a building";
                return null;
            }

            float maxDistance = locationRadius + 8f;
            var members = new List<GameObject>();
            var seen = new HashSet<int> { root.GetInstanceID() };
            var frontier = new Queue<GameObject>();
            frontier.Enqueue(root);
            while (frontier.Count > 0 && members.Count < MaxPieces)
            {
                var current = frontier.Dequeue();
                members.Add(current);
                int n = Physics.OverlapSphereNonAlloc(current.transform.position, LinkDistance, _buffer, Mask);
                for (int i = 0; i < n; i++)
                {
                    var other = RootOf(_buffer[i].gameObject);
                    if (other == null) continue;
                    int id = other.GetInstanceID();
                    if (seen.Contains(id)) continue;
                    seen.Add(id);
                    if (!IsBuildingPiece(other)) continue;
                    if (Geo.FlatDistance(other.transform.position, locationCenter) > maxDistance) continue;
                    frontier.Enqueue(other);
                }
            }

            bool hasStructure = false;
            foreach (var m in members)
            {
                string n = Utils.GetPrefabName(m).ToLowerInvariant();
                if (n.Contains("floor") || n.Contains("wall") || n.Contains("roof") || n.Contains("door")) { hasStructure = true; break; }
            }
            if (members.Count < MinPieces || !hasStructure)
            {
                LastRejectReason = $"{members.Count} connected piece(s) with{(hasStructure ? "" : "out")} a floor, wall, roof or door: not a building";
                foreach (var m in members) _cache[m.GetInstanceID()] = null;
                return null;
            }

            var sum = Vector3.zero;
            foreach (var m in members) sum += m.transform.position;
            var centroid = sum / members.Count;
            var result = new Result
            {
                Centroid = centroid,
                Pieces = members.Count,
                Id = "b" + Mathf.RoundToInt(centroid.x / 2f) + "," + Mathf.RoundToInt(centroid.z / 2f),
            };
            foreach (var m in members) _cache[m.GetInstanceID()] = result;
            return result;
        }

        private static GameObject RootOf(GameObject go)
        {
            if (go == null) return null;
            var nview = go.GetComponentInParent<ZNetView>();
            if (nview != null) return nview.gameObject;
            var piece = go.GetComponentInParent<Piece>();
            if (piece != null) return piece.gameObject;
            var wear = go.GetComponentInParent<WearNTear>();
            return wear != null ? wear.gameObject : null;
        }

        private static bool IsBuildingPiece(GameObject root)
        {
            if (!Catalog.IsWorldPiece(root)) return false;
            string name = Utils.GetPrefabName(root).ToLowerInvariant();
            return !(name.Contains("fence") || name.Contains("pole") || name.Contains("stake")
                  || name.Contains("gate") || name.Contains("path") || name.Contains("sign"));
        }
    }
}
