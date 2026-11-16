using System.Collections.Generic;
using UnityEngine;

namespace TheGreatestMap
{
    /// <summary>
    /// The edges of what players have built, as lines rather than as a field of squares.
    ///
    /// <para>Filling cells gets the ground right and still does not look like anything: a hall
    /// comes out as a ragged patch of color because the grid, not the building, decides where the
    /// edge falls. A map somebody drew has an outline -- the wall is a stroke of ink and the
    /// inside is a wash -- and an outline is the one thing a field of squares cannot be, however
    /// small the squares get.</para>
    ///
    /// <para>So the boundary is walked rather than sampled. Every cell side with nothing of the
    /// same kind on the other side of it is an edge; chained end to end those edges close into
    /// loops, one around each region and one around each hole in it, and the loops are then
    /// straightened. Straightening is the whole point: the raw loop is a staircase, and a
    /// staircase is what reads as fuzzy even at a pixel per meter. Collapsing a run of steps into
    /// the line it was approximating turns a jagged edge into a wall, including a wall that runs
    /// at an angle to the grid, which is most of them -- buildings are not aligned to the cells
    /// anybody chose to record them on.</para>
    ///
    /// <para>Nothing here knows about the screen. The loops come out in cell coordinates and are
    /// traced once, when the server's picture changes, rather than per frame or per zoom; what
    /// the map does with them afterwards is the drawing's business. That split is what keeps a
    /// boundary walk and a line simplifier off the map-dragging path.</para>
    /// </summary>
    internal static class MapOutline
    {
        /// <summary>
        /// One straightened wall, in cell coordinates: corner (x, z) is x cells east and z cells
        /// north of the world origin.
        /// </summary>
        internal struct Stroke
        {
            public float X0, Z0, X1, Z1;
        }

        /// <summary>
        /// How far a corner may sit off the line before it is a corner rather than a step, in
        /// cells.
        ///
        /// <para>The number comes from the shape of the problem, and the obvious guess at it is
        /// wrong. Half a cell is how far a straight wall's <em>cells</em> sit from the line they
        /// approximate -- but what gets straightened is the boundary, and the boundary runs
        /// around their corners. On a staircase at forty-five degrees the outer corner of each
        /// step stands a diagonal half-cell off the line, which is one over root two, about
        /// 0.71. Anything under that refuses to straighten the very staircases this exists to
        /// remove: at 0.6 a twelve-step diagonal came out as thirty-eight walls instead of
        /// five.</para>
        ///
        /// <para>So: just clear of 0.71, and no further. The tolerance is in cells, so at four
        /// meters a cell every tenth of it is another forty centimeters a corner may slide, on
        /// top of the two meters the grid has already cost.</para>
        /// </summary>
        private const float Tolerance = 0.75f;

        /// <summary>
        /// Regions smaller than this get the fill and no outline. A stroke around a lone cell is
        /// a little black box, and a scattering of them reads as noise next to the building they
        /// are scattered around; the fill still shows they are there.
        /// </summary>
        private const float LeastCells = 4f;

        /// <summary>
        /// A loop longer than this is not a building, and straightening it would cost more than
        /// the outline is worth.
        /// </summary>
        private const int MostCorners = 8000;

        private static readonly Dictionary<BuiltKind, List<Stroke>> _strokes =
            new Dictionary<BuiltKind, List<Stroke>>();

        private static int _tracedHash = int.MinValue;
        private static float _tracedCell = -1f;

        internal static List<Stroke> Of(BuiltKind kind) =>
            _strokes.TryGetValue(kind, out var list) ? list : null;

        internal static int Count
        {
            get { int n = 0; foreach (var kv in _strokes) n += kv.Value.Count; return n; }
        }

        internal static void Forget()
        {
            _strokes.Clear();
            _tracedHash = int.MinValue;
            _tracedCell = -1f;
        }

        /// <summary>
        /// Trace, if the picture has changed since the last time. Called from the drawing, which
        /// is throttled already; the comparison is what makes calling it there cheap.
        /// </summary>
        internal static void Trace(Dictionary<int, BuiltKind> cells, int hash)
        {
            if (hash == _tracedHash && Mathf.Approximately(_tracedCell, MapPaint.CellSize)) return;
            _tracedHash = hash;
            _tracedCell = MapPaint.CellSize;
            _strokes.Clear();
            if (cells == null || cells.Count == 0) return;

            var mine = new HashSet<int>();
            foreach (BuiltKind kind in System.Enum.GetValues(typeof(BuiltKind)))
            {
                // Buildings only. An outline says "this is a thing with walls", which a road
                // is not: roads and fields read as the ribbons and patches they are, and a road
                // one cell wide that wanders diagonally is not even one region to trace -- cells
                // that touch at a corner touch at a corner, so it comes apart into a row of
                // little outlined boxes, which is worse than the plain color it replaced.
                if (kind == BuiltKind.None || !Solid(kind)) continue;
                mine.Clear();
                foreach (var kv in cells) if (kv.Value == kind) mine.Add(kv.Key);
                if (mine.Count == 0) continue;

                var found = new List<Stroke>();
                Walk(mine, found);
                if (found.Count > 0) _strokes[kind] = found;
            }
        }

        /// <summary>
        /// Every boundary edge of one kind, chained into closed loops and straightened.
        ///
        /// <para>The edges are directed, each one laid down with the filled cell on its left, so
        /// the chaining has no choices to make at an ordinary corner and the loops come out
        /// consistently wound: counter-clockwise around a region, clockwise around a hole in one.
        /// Holes fall out of the same walk as the outsides do, which is why there is no separate
        /// pass for them and no flood fill anywhere here.</para>
        /// </summary>
        private static void Walk(HashSet<int> mine, List<Stroke> into)
        {
            var outgoing = new Dictionary<long, List<long>>();
            foreach (int key in mine)
            {
                MapPaint.Cell(key, out int cx, out int cz);
                if (!mine.Contains(MapPaint.Neighbor(key, 0, -1))) Edge(outgoing, cx, cz, cx + 1, cz);
                if (!mine.Contains(MapPaint.Neighbor(key, 1, 0))) Edge(outgoing, cx + 1, cz, cx + 1, cz + 1);
                if (!mine.Contains(MapPaint.Neighbor(key, 0, 1))) Edge(outgoing, cx + 1, cz + 1, cx, cz + 1);
                if (!mine.Contains(MapPaint.Neighbor(key, -1, 0))) Edge(outgoing, cx, cz + 1, cx, cz);
            }

            var loop = new List<Vector2>();
            while (outgoing.Count > 0)
            {
                long start = 0;
                bool any = false;
                foreach (var kv in outgoing) { start = kv.Key; any = true; break; }
                if (!any) break;

                loop.Clear();
                long at = start;
                while (loop.Count <= MostCorners)
                {
                    if (!outgoing.TryGetValue(at, out var next) || next.Count == 0) break;
                    long to = next[next.Count - 1];
                    next.RemoveAt(next.Count - 1);
                    if (next.Count == 0) outgoing.Remove(at);
                    loop.Add(Corner(at));
                    at = to;
                    if (at == start) break;
                }
                if (loop.Count >= 4 && loop.Count <= MostCorners) Emit(loop, into);
            }
        }

        private static void Edge(Dictionary<long, List<long>> outgoing, int x0, int z0, int x1, int z1)
        {
            long from = Pack(x0, z0);
            if (!outgoing.TryGetValue(from, out var ends)) outgoing[from] = ends = new List<long>(1);
            ends.Add(Pack(x1, z1));
        }

        private static long Pack(int x, int z) => ((long)x << 32) | (uint)z;

        private static Vector2 Corner(long packed) => new Vector2((int)(packed >> 32), (int)(uint)packed);

        /// <summary>
        /// Straighten one closed loop and add its walls, unless it encloses too little to be
        /// worth an outline.
        /// </summary>
        private static void Emit(List<Vector2> loop, List<Stroke> into)
        {
            float twice = 0f;
            for (int i = 0; i < loop.Count; i++)
            {
                var a = loop[i];
                var b = loop[(i + 1) % loop.Count];
                twice += a.x * b.y - b.x * a.y;
            }
            if (Mathf.Abs(twice) * 0.5f < LeastCells) return;

            var simple = Tidy(Straighten(loop));
            if (simple.Count < 3) return;
            for (int i = 0; i < simple.Count; i++)
            {
                var a = simple[i];
                var b = simple[(i + 1) % simple.Count];
                into.Add(new Stroke { X0 = a.x, Z0 = a.y, X1 = b.x, Z1 = b.y });
            }
        }

        /// <summary>
        /// Douglas-Peucker over a closed loop.
        ///
        /// <para>It wants two fixed ends and a loop has none, so the loop is cut at its first
        /// point and at the point farthest from it, and each half is reduced on its own. Cutting
        /// at the far point rather than halfway matters: the two anchors end up on opposite sides
        /// of the shape, so neither half doubles back on itself, which is the case the algorithm
        /// gets wrong.</para>
        /// </summary>
        private static List<Vector2> Straighten(List<Vector2> loop)
        {
            int far = 0;
            float best = -1f;
            for (int i = 1; i < loop.Count; i++)
            {
                float d = (loop[i] - loop[0]).sqrMagnitude;
                if (d > best) { best = d; far = i; }
            }

            // The second half runs past the end of the list on purpose: its closing point is the
            // loop's first one, which Reduce reaches by wrapping.
            var kept = new List<Vector2>();
            Reduce(loop, 0, far, kept);
            Reduce(loop, far, loop.Count, kept);
            return kept;
        }

        /// <summary>
        /// Keep the corners between <paramref name="a"/> and <paramref name="b"/> that the line
        /// between them misses by more than the tolerance. The end is not added; the caller owns
        /// it, because it is the next span's beginning.
        /// </summary>
        private static void Reduce(List<Vector2> pts, int a, int b, List<Vector2> into)
        {
            if (b <= a) return;
            var from = pts[a];
            var to = pts[b % pts.Count];

            float worst = -1f;
            int at = a;
            for (int i = a + 1; i < b; i++)
            {
                float d = Away(pts[i], from, to);
                if (d > worst) { worst = d; at = i; }
            }

            if (worst > Tolerance)
            {
                // Each half owns its own beginning, so the corner they share is added by the
                // second of them and never twice.
                Reduce(pts, a, at, into);
                Reduce(pts, at, b, into);
            }
            else into.Add(from);
        }

        /// <summary>
        /// Drop corners that are not corners.
        ///
        /// <para>Douglas-Peucker keeps the point it started from whether or not it is a bend,
        /// and the start is wherever the boundary walk happened to begin -- so a plain rectangle
        /// comes out of it with five sides, one of them a wall split in two for no reason. It
        /// draws the same, and it is still worth not carrying.</para>
        /// </summary>
        private static List<Vector2> Tidy(List<Vector2> loop)
        {
            if (loop.Count < 3) return loop;
            var kept = new List<Vector2>(loop.Count);
            for (int i = 0; i < loop.Count; i++)
            {
                var before = loop[(i + loop.Count - 1) % loop.Count];
                var after = loop[(i + 1) % loop.Count];
                if (Away(loop[i], before, after) > 0.01f) kept.Add(loop[i]);
            }
            return kept.Count >= 3 ? kept : loop;
        }

        /// <summary>Something built, as opposed to ground that was shaped.</summary>
        internal static bool Solid(BuiltKind kind) =>
            kind == BuiltKind.Wood || kind == BuiltKind.Stone
            || kind == BuiltKind.Iron || kind == BuiltKind.Marble;

        /// <summary>How far a point sits off the line between two others.</summary>
        private static float Away(Vector2 p, Vector2 a, Vector2 b)
        {
            float dx = b.x - a.x, dy = b.y - a.y;
            float len = dx * dx + dy * dy;
            if (len < 0.000001f) return (p - a).magnitude;
            float t = Mathf.Clamp01(((p.x - a.x) * dx + (p.y - a.y) * dy) / len);
            return (p - new Vector2(a.x + t * dx, a.y + t * dy)).magnitude;
        }
    }
}
