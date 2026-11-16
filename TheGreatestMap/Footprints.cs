using System.Collections.Generic;
using UnityEngine;

namespace TheGreatestMap
{
    /// <summary>
    /// How much ground a build piece actually stands on, and which cells that reaches.
    ///
    /// <para>The sweep used to ask one question of each piece: which cell is its center point
    /// in. That is cheap and it is wrong in a way that gets worse the finer the grid is. A two
    /// meter floor covers four one meter cells and marks one of them, so a hall recorded at a
    /// meter comes apart into a lattice of dots with its own floor showing through -- the grid
    /// gets finer and the picture gets worse. The center rule is the reason the recording could
    /// never go below the size of a piece.</para>
    ///
    /// <para>So a piece is asked for its footprint instead: the prefab's own bounds, turned the
    /// way the piece was placed, flattened onto the ground, and every cell that rectangle
    /// touches is marked. The picture stops depending on how densely somebody happened to place
    /// pieces and starts depending on where the building is, which is the thing being drawn.</para>
    ///
    /// <para>Bounds come off the prefab's meshes rather than its colliders, because a collider
    /// on a prefab that was never instantiated has no usable world bounds, and because the
    /// triggers hung off some pieces are much bigger than the piece. They are measured once per
    /// prefab and kept: there are a few hundred buildable prefabs and hundreds of thousands of
    /// pieces, so this is the difference between a table lookup and a walk of the hierarchy.</para>
    /// </summary>
    internal static class Footprints
    {
        /// <summary>
        /// A prefab's extent on the ground, in its own coordinates, before it is turned or
        /// placed. Y is dropped here rather than at the caller: a roof panel is modeled at an
        /// angle and its footprint is still the patch of ground it shades.
        /// </summary>
        private struct Footprint
        {
            public Vector3 Low;     // local corner, all three axes -- rotation mixes them
            public Vector3 High;
            public bool Known;
        }

        /// <summary>
        /// A piece bigger than this across is not a piece, it is a mistake or a mod, and
        /// stamping it would paint a district. Those fall back to the single center cell, which
        /// is what the recording did for everything before this existed.
        /// </summary>
        private const float MostMeters = 64f;

        /// <summary>
        /// Cells one piece may mark. A floor reaches four at a meter a cell; the cap is here so
        /// that an unexpectedly large prefab cannot turn one ZDO into a thousand cells.
        /// </summary>
        private const int MostCells = 512;

        private static readonly Dictionary<int, Footprint> _known = new Dictionary<int, Footprint>();

        private static readonly Vector3[] _corner = new Vector3[8];
        private static readonly Vector2[] _flat = new Vector2[8];

        internal static int Measured => _known.Count;

        internal static void Forget() => _known.Clear();

        /// <summary>
        /// Mark every cell the piece stands on, and say how many that was. Zero means the
        /// prefab could not be measured and the caller should fall back to the center cell.
        /// </summary>
        internal static int Stamp(int prefab, Vector3 pos, Quaternion rot, List<int> into)
        {
            into.Clear();
            var box = Measure(prefab);
            if (!box.Known) return 0;

            // The eight corners of the prefab's own box, turned the way the piece was placed and
            // dropped onto the ground. Turning the box rather than its footprint is what keeps a
            // stair or a roof honest: those are modeled at an angle, so their height is part of
            // how much ground they cover once they are rotated.
            int n = 0;
            for (int i = 0; i < 8; i++)
            {
                var local = new Vector3(
                    (i & 1) == 0 ? box.Low.x : box.High.x,
                    (i & 2) == 0 ? box.Low.y : box.High.y,
                    (i & 4) == 0 ? box.Low.z : box.High.z);
                var world = pos + rot * local;
                _corner[n] = world;
                _flat[n] = new Vector2(world.x, world.z);
                n++;
            }

            float minX = _flat[0].x, maxX = minX, minZ = _flat[0].y, maxZ = minZ;
            for (int i = 1; i < 8; i++)
            {
                if (_flat[i].x < minX) minX = _flat[i].x;
                if (_flat[i].x > maxX) maxX = _flat[i].x;
                if (_flat[i].y < minZ) minZ = _flat[i].y;
                if (_flat[i].y > maxZ) maxZ = _flat[i].y;
            }
            if (maxX - minX > MostMeters || maxZ - minZ > MostMeters) return 0;

            float cell = MapPaint.CellSize;
            int cx0 = Mathf.FloorToInt(minX / cell), cx1 = Mathf.FloorToInt(maxX / cell);
            int cz0 = Mathf.FloorToInt(minZ / cell), cz1 = Mathf.FloorToInt(maxZ / cell);
            long span = (long)(cx1 - cx0 + 1) * (cz1 - cz0 + 1);
            if (span > MostCells) return 0;

            // The piece's own two ground axes, which is what makes a wall at forty-five degrees
            // mark the cells under the wall rather than the cells under the square it fits in.
            // A piece tipped right over projects one of them to nothing; then the test simply
            // uses fewer axes, which can only let a cell through, never exclude a real one.
            Vector2 alongX = Flatten(rot * Vector3.right);
            Vector2 alongZ = Flatten(rot * Vector3.forward);

            for (int cz = cz0; cz <= cz1; cz++)
                for (int cx = cx0; cx <= cx1; cx++)
                {
                    float x0 = cx * cell, x1 = x0 + cell;
                    float z0 = cz * cell, z1 = z0 + cell;
                    if (!Overlaps(x0, x1, z0, z1, alongX, alongZ)) continue;
                    into.Add(MapPaint.KeyOf(cx, cz));
                }

            return into.Count;
        }

        private static Vector2 Flatten(Vector3 axis)
        {
            var flat = new Vector2(axis.x, axis.z);
            return flat.sqrMagnitude < 0.0001f ? Vector2.zero : flat.normalized;
        }

        /// <summary>
        /// Separating axis test between the cell square and the piece's flattened box. Only the
        /// four axes that matter are tried -- the two of the square and the two of the piece --
        /// which is exact for anything standing upright and generous for anything tipped.
        /// </summary>
        private static bool Overlaps(float x0, float x1, float z0, float z1, Vector2 alongX, Vector2 alongZ)
        {
            float pieceMinX = _flat[0].x, pieceMaxX = pieceMinX;
            float pieceMinZ = _flat[0].y, pieceMaxZ = pieceMinZ;
            for (int i = 1; i < 8; i++)
            {
                if (_flat[i].x < pieceMinX) pieceMinX = _flat[i].x;
                if (_flat[i].x > pieceMaxX) pieceMaxX = _flat[i].x;
                if (_flat[i].y < pieceMinZ) pieceMinZ = _flat[i].y;
                if (_flat[i].y > pieceMaxZ) pieceMaxZ = _flat[i].y;
            }
            if (pieceMaxX < x0 || pieceMinX > x1) return false;
            if (pieceMaxZ < z0 || pieceMinZ > z1) return false;
            return Apart(x0, x1, z0, z1, alongX) == false
                && Apart(x0, x1, z0, z1, alongZ) == false;
        }

        /// <summary>True when the square and the piece do not overlap along this one axis.</summary>
        private static bool Apart(float x0, float x1, float z0, float z1, Vector2 axis)
        {
            if (axis == Vector2.zero) return false;

            float a0 = float.MaxValue, a1 = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                float d = _flat[i].x * axis.x + _flat[i].y * axis.y;
                if (d < a0) a0 = d;
                if (d > a1) a1 = d;
            }

            float b0 = float.MaxValue, b1 = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                float sx = (i & 1) == 0 ? x0 : x1;
                float sz = (i & 2) == 0 ? z0 : z1;
                float d = sx * axis.x + sz * axis.y;
                if (d < b0) b0 = d;
                if (d > b1) b1 = d;
            }

            return a1 < b0 || b1 < a0;
        }

        /// <summary>
        /// The prefab's own bounds, measured once. Meshes rather than colliders, and the whole
        /// hierarchy rather than the root, because a piece is usually a root with no renderer of
        /// its own and the shape hanging underneath it.
        /// </summary>
        private static Footprint Measure(int prefab)
        {
            if (_known.TryGetValue(prefab, out var box)) return box;

            box = new Footprint();
            var scene = ZNetScene.instance;
            var go = scene != null ? scene.GetPrefab(prefab) : null;
            if (go == null)
            {
                // Not cached as unknown: the scene may simply not be up yet, and a prefab that
                // answers later should be measured then rather than written off now.
                return box;
            }

            var root = go.transform;
            var low = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var high = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            bool any = false;

            foreach (var filter in go.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = filter.sharedMesh;
                if (mesh == null) continue;
                var bounds = mesh.bounds;
                var into = root.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var at = new Vector3(
                        (i & 1) == 0 ? bounds.min.x : bounds.max.x,
                        (i & 2) == 0 ? bounds.min.y : bounds.max.y,
                        (i & 4) == 0 ? bounds.min.z : bounds.max.z);
                    var p = into.MultiplyPoint3x4(at);
                    low = Vector3.Min(low, p);
                    high = Vector3.Max(high, p);
                    any = true;
                }
            }

            if (!any)
            {
                // A few pieces are a box collider and nothing to look at -- a ward, a hidden
                // support. They still stand somewhere.
                foreach (var hit in go.GetComponentsInChildren<BoxCollider>(true))
                {
                    var into = root.worldToLocalMatrix * hit.transform.localToWorldMatrix;
                    var half = hit.size * 0.5f;
                    for (int i = 0; i < 8; i++)
                    {
                        var at = hit.center + new Vector3(
                            (i & 1) == 0 ? -half.x : half.x,
                            (i & 2) == 0 ? -half.y : half.y,
                            (i & 4) == 0 ? -half.z : half.z);
                        var p = into.MultiplyPoint3x4(at);
                        low = Vector3.Min(low, p);
                        high = Vector3.Max(high, p);
                        any = true;
                    }
                }
            }

            if (any)
            {
                box.Low = low;
                box.High = high;
                box.Known = true;
            }
            _known[prefab] = box;
            return box;
        }
    }
}
