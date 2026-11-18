using System;
using System.Collections.Generic;
using UnityEngine;

namespace TheGreatestMap
{
    /// <summary>
    /// The ground players have shaped -- hoed paths, paved road and farmland -- read from the
    /// world's own terrain records rather than from the dirt under somebody's feet.
    ///
    /// <para>This replaces a sampler that walked around each player reading the loaded terrain a
    /// few dozen cells at a time. That sampler was honest about its one real limit and wrong
    /// about the bargain: terrain only exists where it is loaded, so it could only ever learn
    /// about ground somebody had recently stood on, and it reasoned that a road nobody walks was
    /// not worth drawing. But the whole point of putting a road on a shared map is to tell
    /// somebody about a road they have *not* walked, and a server that has held a world for a
    /// year already knows about every stroke of the hoe ever made in it.</para>
    ///
    /// <para>It knows because the game stores terrain edits as objects. Every zone that anybody
    /// has dug, levelled or painted carries a <c>_TerrainCompiler</c> with the whole record
    /// compressed into one ZDO field, <c>TCData</c>: a grid of per-vertex height deltas and a
    /// grid of paint colors, covering the zone at a meter or two per point. The server holds all
    /// of them whether or not anyone is near, which means the entire world's paths can be read in
    /// one pass of the sweep that already walks the ZDOs looking for buildings -- no walking, no
    /// sampling, no rationing, and ground somebody paved a year ago on a continent nobody has
    /// visited since is on the map.</para>
    ///
    /// <para>Reading a record costs a decompress and a parse, which is far too much to do to
    /// every zone every sweep. It does not have to be: a ZDO carries a <c>DataRevision</c> that
    /// the game bumps when the record changes, so a zone is parsed once and then skipped until
    /// somebody actually hoes something in it. A world where nobody is digging costs one integer
    /// comparison per zone per sweep.</para>
    ///
    /// <para>What is read is the paint grid only. The height deltas are parsed past rather than
    /// kept -- drawing raised ground and dug ponds is a separate question from drawing paths, and
    /// this reads the record in one pass either way.</para>
    /// </summary>
    internal static class Ground
    {
        /// <summary>The prefab every terrain-edit record lives on, by the hash a ZDO carries.</summary>
        internal static readonly int CompilerHash = "_TerrainCompiler".GetStableHashCode();

        /// <summary>
        /// What one zone's record came to, kept so an unchanged zone is never parsed twice.
        /// </summary>
        private sealed class Record
        {
            public uint Revision;
            public Dictionary<int, BuiltKind> Cells;
            /// <summary>
            /// The cells the earthwork test held back, kept rather than dropped so that the
            /// pass which knows what is built where can still ask for them.
            /// </summary>
            public Dictionary<int, BuiltKind> Earthwork;
            /// <summary>Dirt points left out because the ground there was worked, not walked.</summary>
            public int Shaped;
            /// <summary>Dirt points on worked ground that moved far enough to draw anyway.</summary>
            public int Kept;
            /// <summary>Dirt points the ground was never moved under: somebody's path.</summary>
            public int Walked;
            /// <summary>Dirt points the ground was worked at and left flat: somebody's yard.</summary>
            public int Flat;
        }

        private static readonly Dictionary<ZDOID, Record> _records = new Dictionary<ZDOID, Record>();

        /// <summary>Zones seen in the pass now running, so vanished ones can be dropped after it.</summary>
        private static readonly HashSet<ZDOID> _seen = new HashSet<ZDOID>();

        /// <summary>
        /// Keeps a patch that ends exactly on a cell boundary from claiming the cell beyond it.
        /// </summary>
        private const float Nudge = 0.0001f;

        /// <summary>
        /// How far from a worked point its paint is still that point's doing, in grid steps,
        /// which are a meter.
        ///
        /// <para>A terrain op paints a wider circle than it moves. Swing at the ground beside a
        /// tin vein and the dirt reaches a couple of meters while the height only changes where
        /// the blow landed, so the rim of every scrape carries paint and no modified flag -- and
        /// a test that asked each point about itself called that rim somebody's path and drew
        /// the outline of every hole anybody had ever dug.</para>
        ///
        /// <para>Two meters covers the ops that do this. The cost is that a hoed path running
        /// within two meters of an earthwork is read as part of it, which is a trade worth
        /// making: that path has an earthwork next to it either way.</para>
        /// </summary>
        private const int Reach = 2;

        /// <summary>
        /// How far an earthwork reaches, in grid steps, which are a meter.
        ///
        /// <para>A separate and wider question from <see cref="Reach"/>, and the two were one
        /// number for a while, which is what made the map wrong about a leveled yard. Reach
        /// asks "was the ground touched near here", which only has to cover how much wider an
        /// op paints than it digs. This asks "is there a hole or a wall near here", and the
        /// answer has to cover the whole of the paint one dig throws around -- otherwise the
        /// outer ring of every scrape, flagged as considered and never actually moved, comes
        /// out as flat ground somebody made, which is the thin wandering line around every
        /// hole in the world all over again.</para>
        ///
        /// <para>Five meters covers a pickaxe's splatter with room to spare, and it is only
        /// ever asked of paint standing on ground no height op touched -- the splatter itself.
        /// Asking it of every point was too blunt by far: one spot dug a couple of meters deep
        /// took an eleven meter square of yard down with it, which is how a courtyard came out
        /// with holes in it.</para>
        /// </summary>
        private const int EarthworkReach = 5;

        /// <summary>
        /// What the ground around one point has had done to it.
        ///
        /// <para>Up and down are kept apart because levelling a slope does both at once -- it
        /// fills the low side and cuts the high one -- so a patch can honestly be a rise and a
        /// dig together, and which of those the player cares about is theirs to say.</para>
        ///
        /// <para>Near and wide are kept apart because they answer for two different points.
        /// Ground a height op considered answers for itself: whatever it did here is what
        /// happened here, and a hole somebody dug four meters away is that hole's business and
        /// not this yard's. Paint standing on ground no op ever considered cannot answer for
        /// itself at all -- it is the splatter thrown out around an op -- so it borrows the
        /// verdict of the nearest work, which is what the wide reach is for.</para>
        /// </summary>
        private struct Around
        {
            /// <summary>A height op considered some point within <see cref="Reach"/>.</summary>
            public bool Touched;
            /// <summary>...and this very point was one of them.</summary>
            public bool Here;
            /// <summary>The most the ground moved within a meter.</summary>
            public float Up, Down;
            /// <summary>The most it moved within <see cref="EarthworkReach"/>.</summary>
            public float WideUp, WideDown;
        }

        private static Around Look(bool[] worked, float[] moved, int i, int pitch)
        {
            int x = i % pitch, y = i / pitch;
            var it = new Around { Here = worked[i] };
            for (int dy = -EarthworkReach; dy <= EarthworkReach; dy++)
            {
                int ny = y + dy;
                if (ny < 0 || ny >= pitch) continue;
                for (int dx = -EarthworkReach; dx <= EarthworkReach; dx++)
                {
                    int nx = x + dx;
                    if (nx < 0 || nx >= pitch) continue;
                    int at = ny * pitch + nx;
                    if (!worked[at]) continue;
                    if (dy >= -Reach && dy <= Reach && dx >= -Reach && dx <= Reach) it.Touched = true;
                    float by = moved[at];
                    if (by > it.WideUp) it.WideUp = by;
                    if (-by > it.WideDown) it.WideDown = -by;
                    if (dy < -1 || dy > 1 || dx < -1 || dx > 1) continue;
                    if (by > it.Up) it.Up = by;
                    if (-by > it.Down) it.Down = -by;
                }
            }
            return it;
        }

        private static int _parsed;
        private static int _skipped;
        private static int _failed;

        // The parse now depends on two settings, and the records are cached against the zone's
        // revision -- which does not change when a setting does. Without this, switching shaped
        // ground on would show nothing until somebody happened to dig in every zone.
        private static bool _readRaised;
        private static bool _readDug;
        private static float _readLeast = -1f;

        /// <summary>
        /// What the parse just now counted, handed to the record it belongs to. Three numbers
        /// because two of them would not say which way a wrong answer was wrong: dirt the
        /// ground was never moved under is the only kind that reads as somebody's path, and if
        /// that number is implausibly large then the test is letting earthworks through rather
        /// than the world really being full of trails.
        /// </summary>
        /// <summary>The earthwork cells of the parse just now, handed to its record.</summary>
        private static Dictionary<int, BuiltKind> _shapedCells;

        private static int _shapedHere;
        private static int _keptHere;
        private static int _walkedHere;
        private static int _flatHere;

        /// <summary>Whether ground somebody built up -- a terrace, an earth wall -- is drawn.</summary>
        private static bool Raised() =>
            TgmConfig.PaintRaisedGround != null && TgmConfig.PaintRaisedGround.Value;

        /// <summary>Whether ground somebody cut away -- a pit, a mining scrape -- is drawn.</summary>
        private static bool Dug() =>
            TgmConfig.PaintDugGround != null && TgmConfig.PaintDugGround.Value;

        /// <summary>How far the ground has to have moved before that counts as shaping it.</summary>
        private static float ShapedLeast() =>
            TgmConfig.PaintShapedLeastMeters != null
                ? Mathf.Max(0f, TgmConfig.PaintShapedLeastMeters.Value)
                : 1f;

        /// <summary>
        /// What the record says about one point, and what the rule made of it.
        ///
        /// <para>This exists because "why is my yard green" has been answered by reasoning
        /// three times now and the reasoning has been wrong twice. The inputs are four numbers
        /// and a flag; printing them where somebody is standing turns the question into a
        /// reading.</para>
        /// </summary>
        internal sealed class Probe
        {
            public int Index = -1;
            public bool Reached;
            public bool Painted;
            public float R, G, B, A;
            public BuiltKind Kind;
            public bool Worked;
            public bool Here;
            public float Up, Down;
            public string Verdict = "the record does not cover that point";

            /// <summary>How far this point is from the one that was asked about, in meters.</summary>
            public float Distance;

            /// <summary>Where this point is, for the one found rather than the one asked for.</summary>
            public float X, Z;

            /// <summary>How far apart the game's paint samples are here.</summary>
            public float Step;
        }

        /// <summary>
        /// Read the terrain record under a point and say what it makes of the ground there.
        ///
        /// <para>Deliberately not the cached record: the cache holds the cells the rule kept,
        /// and the question being asked is about the ones it did not. This parses the zone
        /// again, which costs a decompress and is a console command.</para>
        /// </summary>
        internal static List<string> Explain(Vector3 at)
        {
            var lines = new List<string>();
            var comp = TerrainComp.FindTerrainCompiler(at);
            if (comp == null)
            {
                lines.Add($"ground at {at.x:0.#}, {at.z:0.#}: no terrain record in this zone, so nobody has " +
                          "ever shaped or painted it. The map draws nothing here, and that is right.");
                return lines;
            }
            var view = comp.GetComponent<ZNetView>();
            var zdo = view != null && view.IsValid() ? view.GetZDO() : null;
            if (zdo == null)
            {
                lines.Add("ground: this zone's terrain record is not loaded enough to read.");
                return lines;
            }

            var probe = new Probe();
            var near = new Probe();
            _probe = probe;
            _near = near;
            _nearBest = float.MaxValue;
            _probeAt = at;
            try { Read(zdo); }
            finally { _probe = null; _near = null; }

            lines.Add($"ground at {at.x:0.#}, {at.z:0.#} (zone record {comp.transform.position.x:0.#}, " +
                      $"{comp.transform.position.z:0.#}):");
            if (!probe.Reached)
            {
                lines.Add("  " + probe.Verdict);
                return lines;
            }
            lines.Add(probe.Painted
                ? $"  paint r {probe.R:0.##} g {probe.G:0.##} b {probe.B:0.##} a {probe.A:0.##}" +
                  $" -> {(probe.Kind == BuiltKind.None ? "no kind" : probe.Kind.ToString().ToLowerInvariant())}"
                : "  no paint recorded at this point");
            lines.Add($"  ground touched within {Reach} m: {(probe.Worked ? "yes" : "no")}" +
                      (probe.Here ? "; worked here, and moved" : $"; splatter, so judged by the work within {EarthworkReach} m:") +
                      $" up {probe.Up:0.##} m, down {probe.Down:0.##} m" +
                      $" (earthwork over {ShapedLeast():0.##} m)");
            lines.Add("  " + probe.Verdict);

            // The point underfoot is not always the point being asked about. The game keeps its
            // paint on a grid, and the map colors the whole patch each sample speaks for, so a
            // speck on the map a meter across can come from one sample that nobody is standing
            // exactly on. Reading the vertex underfoot then answers truthfully about a vertex
            // nobody painted, which is how this command came to say "nothing is painted here"
            // to somebody standing on a painted speck.
            if (!probe.Painted)
            {
                if (near.Reached)
                {
                    lines.Add($"  the nearest painted point is {near.Distance:0.##} m away at " +
                              $"{near.X:0.#}, {near.Z:0.#} (samples are {probe.Step:0.##} m apart), and that " +
                              "is what the speck you are looking at is drawn from:");
                    lines.Add($"    paint r {near.R:0.##} g {near.G:0.##} b {near.B:0.##} a {near.A:0.##}" +
                              $" -> {(near.Kind == BuiltKind.None ? "no kind" : near.Kind.ToString().ToLowerInvariant())}");
                    lines.Add($"    ground touched within {Reach} m of it: {(near.Worked ? "yes" : "no")}" +
                              (near.Here ? "; worked there, and moved" : $"; splatter, so judged by the work within {EarthworkReach} m:") +
                              $" up {near.Up:0.##} m, down {near.Down:0.##} m");
                    lines.Add("    " + near.Verdict);
                }
                else
                {
                    lines.Add("  and nothing at all is painted in this zone's record, so whatever you are " +
                              "looking at on the map is not ground paint. Try tgm_paint, which answers for " +
                              "player building.");
                }
            }
            return lines;
        }

        private static Probe _probe;

        /// <summary>
        /// The nearest painted sample to the point asked about, filled as the parse goes past.
        ///
        /// <para>Kept beside the exact answer rather than instead of it: "there is no paint
        /// under your feet, and the paint making that speck is eight tenths of a meter away"
        /// are two facts and the second one is useless without the first.</para>
        /// </summary>
        private static Probe _near;
        private static float _nearBest;
        private static Vector3 _probeAt;

        internal static void Reset()
        {
            _records.Clear();
            _seen.Clear();
            _parsed = 0;
            _skipped = 0;
            _failed = 0;
        }

        /// <summary>A pass is starting: nothing has been seen yet.</summary>
        internal static void Begin()
        {
            _seen.Clear();
            _parsed = 0;
            _skipped = 0;
            if (_readRaised != Raised() || _readDug != Dug()
                || !Mathf.Approximately(_readLeast, ShapedLeast()))
            {
                _readRaised = Raised();
                _readDug = Dug();
                _readLeast = ShapedLeast();
                _records.Clear();
            }
        }

        /// <summary>
        /// Offer one ZDO from the sweep. Returns false if it is not a terrain record, so the
        /// caller can go on to ask whether it is a building.
        /// </summary>
        internal static bool Consider(ZDO zdo)
        {
            if (zdo.GetPrefab() != CompilerHash) return false;
            var id = zdo.m_uid;
            _seen.Add(id);

            // The one comparison that makes reading every zone in the world affordable.
            if (_records.TryGetValue(id, out var had) && had.Revision == zdo.DataRevision)
            {
                _skipped++;
                return true;
            }

            var cells = Read(zdo);
            if (cells == null) return true;   // unreadable; leave whatever we had alone
            _records[id] = new Record
            {
                Revision = zdo.DataRevision,
                Cells = cells,
                Earthwork = _shapedCells,
                Shaped = _shapedHere,
                Kept = _keptHere,
                Walked = _walkedHere,
                Flat = _flatHere,
            };
            _parsed++;
            return true;
        }

        /// <summary>A pass is over: forget zones whose records are gone.</summary>
        internal static void End()
        {
            if (_records.Count == _seen.Count) return;
            var gone = new List<ZDOID>();
            foreach (var kv in _records) if (!_seen.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (var id in gone) _records.Remove(id);
        }

        /// <summary>
        /// Everything read so far, folded into the picture the server sends out.
        ///
        /// <para>A pass per kind, worst last, because the picture has a ceiling and a world with
        /// more shaped ground than will fit has to lose something. Dropping whatever a dictionary
        /// happened to walk last would thin every kind evenly and leave a road network full of
        /// holes; this way a world past the ceiling loses its farmland, then its dirt paths, and
        /// keeps every meter of paved road -- which is the one people navigate by.</para>
        /// </summary>
        internal static void AddTo(Dictionary<int, BuiltKind> picture, int maxCells,
                                   Func<int, bool> built)
        {
            _dropped = 0;
            _adopted = 0;
            Pour(picture, maxCells, BuiltKind.Paved);
            Pour(picture, maxCells, BuiltKind.Dirt);
            // Then the earthworks, where somebody has built. Whether a player meant a patch of
            // dirt is the question the raised and dug settings were standing in for, and how
            // far the ground moved is a poor way to ask it: a yard leveled into a slope has
            // real fill in it and a mining scrape has none, so the test that keeps scrapes off
            // the map also takes out half of somebody's courtyard. What is built around it
            // answers the actual question, and the map already counts that for exactly this
            // reason -- it is what tells a hut from a torch left on a hilltop.
            Adopt(picture, maxCells, built);
            Pour(picture, maxCells, BuiltKind.Farm);
            if (_dropped > 0 && _warned != _dropped)
            {
                _warned = _dropped;
                TheGreatestMapMod.Log.LogWarning(
                    $"[TheGreatestMap] The map picture is full at {maxCells} cells, so {_dropped} cell(s) of " +
                    "shaped ground are not drawn. Paved road is kept first, then dirt paths, then farmland. " +
                    "Switch off a kind you do not need (Paint Farmland, Paint Dirt Paths) to spend the room " +
                    "on the rest.");
            }
        }

        private static int _dropped;
        private static int _warned;
        private static int _adopted;

        /// <summary>Earthwork cells with building around them, which makes them somebody's yard.</summary>
        private static void Adopt(Dictionary<int, BuiltKind> picture, int maxCells, Func<int, bool> built)
        {
            if (built == null || !Wanted(BuiltKind.Dirt)) return;
            foreach (var record in _records.Values)
            {
                if (record.Earthwork == null) continue;
                foreach (var kv in record.Earthwork)
                {
                    if (picture.ContainsKey(kv.Key)) continue;
                    if (picture.Count >= maxCells) { _dropped++; continue; }
                    if (!built(kv.Key)) continue;
                    picture[kv.Key] = kv.Value;
                    _adopted++;
                }
            }
        }

        private static void Pour(Dictionary<int, BuiltKind> picture, int maxCells, BuiltKind kind)
        {
            if (!Wanted(kind)) return;
            foreach (var record in _records.Values)
            {
                foreach (var kv in record.Cells)
                {
                    if (kv.Value != kind) continue;
                    // Building wins over ground: a hall standing on paving is a hall.
                    if (picture.ContainsKey(kv.Key)) continue;
                    if (picture.Count >= maxCells) { _dropped++; continue; }
                    picture[kv.Key] = kv.Value;
                }
            }
        }

        private static bool Wanted(BuiltKind kind)
        {
            switch (kind)
            {
                case BuiltKind.Paved: return TgmConfig.PaintRoads == null || TgmConfig.PaintRoads.Value;
                case BuiltKind.Dirt: return TgmConfig.PaintDirtPaths == null || TgmConfig.PaintDirtPaths.Value;
                case BuiltKind.Farm: return TgmConfig.PaintFarmland == null || TgmConfig.PaintFarmland.Value;
                default: return false;
            }
        }

        /// <summary>
        /// Unpack one zone's record and reduce its paint grid to map cells.
        ///
        /// <para>The layout is <see cref="TerrainComp"/>'s own, and it is read rather than
        /// guessed at: a version, the operation count and where the last one landed, then the
        /// height grid as a flag per vertex with two floats behind each raised flag, then the
        /// paint grid the same way with a color behind each. The height half is stepped over
        /// without being kept, which still means reading it -- the paint grid cannot be found
        /// without walking past the heights, because each entry is only as long as its flag
        /// says.</para>
        /// </summary>
        private static Dictionary<int, BuiltKind> Read(ZDO zdo)
        {
            try
            {
                // Cleared first. Every path out of here that still makes a record reads this
                // afterwards, and a zone with nothing painted in it returns early -- which,
                // left over from the zone before, handed it that zone's earthwork cells.
                _shapedCells = null;
                var packed = zdo.GetByteArray(ZDOVars.s_TCData);
                if (packed == null || packed.Length == 0) return null;
                var pkg = new ZPackage(Utils.Decompress(packed));

                pkg.ReadInt();        // the record's own version, 1 so far
                pkg.ReadInt();        // how many operations made it
                pkg.ReadVector3();    // where the last one landed
                pkg.ReadSingle();     // and how wide it was

                // The heights come first and used to be read past. They are the whole of the
                // difference between a path and a day's work: the hoe's path and paved road only
                // paint, while raising, leveling and digging paint dirt as a side effect and move
                // the ground as well. So a dirt point whose ground never moved is somebody
                // deciding where to walk, and one whose ground did move is the scar of the work.
                int heights = pkg.ReadInt();
                // Two separate facts, and reading them as one let the fringe through. The record
                // stores a flag per point and only then the deltas, so a point can be flagged as
                // worked with both deltas at zero -- which is what the rim of a leveling or
                // smoothing op looks like, considered and not actually moved. Those points still
                // take dirt paint, so judging by the delta alone called every one of them a path
                // and drew a thin wandering line around everything anybody had dug.
                bool[] worked = heights > 0 ? new bool[heights] : null;
                float[] moved = heights > 0 ? new float[heights] : null;
                for (int i = 0; i < heights; i++)
                    if (pkg.ReadBool())
                    {
                        float level = pkg.ReadSingle(), smooth = pkg.ReadSingle();
                        worked[i] = true;
                        // Kept signed. TerrainComp accumulates m_levelDelta the way the ground
                        // actually went -- up for a wall or a terrace, down for a pit or a
                        // mining scrape -- and throwing that away threw away the one thing that
                        // tells the two apart. m_smoothDelta rides along at a meter either way.
                        moved[i] = level + smooth;
                    }

                int painted = pkg.ReadInt();
                // Same grid, point for point -- but only believe that if the counts agree.
                // TerrainComp sizes every one of these (width + 1) squared, so they do.
                if (heights != painted) { worked = null; moved = null; }
                if (painted <= 0) return new Dictionary<int, BuiltKind>();

                // The grid is square and one point wider than the zone, so its side comes out of
                // its own length -- no assumption about how wide the game builds a heightmap.
                int pitch = Mathf.RoundToInt(Mathf.Sqrt(painted));
                if (pitch * pitch != painted || pitch < 2) return null;
                int width = pitch - 1;
                float zone = ZoneSystem.instance != null ? ZoneSystem.instance.m_zoneSize : 64f;
                float scale = zone / width;
                int half = pitch / 2;                       // WorldToVertexMask's (m_width + 1) / 2
                var origin = zdo.GetPosition();

                // Where the probe's point lands in this grid, if one is asking. The same
                // arithmetic the loop below does in reverse, which is Heightmap's own
                // WorldToVertexMask.
                int probeAt = -1;
                int probePx = 0, probePy = 0;
                if (_probe != null)
                {
                    probePx = Mathf.RoundToInt((_probeAt.x - origin.x) / scale) + half;
                    probePy = Mathf.RoundToInt((_probeAt.z - origin.z) / scale) + half;
                    if (probePx >= 0 && probePy >= 0 && probePx < pitch && probePy < pitch)
                        probeAt = probePy * pitch + probePx;
                    _probe.Index = probeAt;
                    _probe.Step = scale;
                }

                float cell = Mathf.Max(0.0001f, MapPaint.CellSize);
                var cells = new Dictionary<int, BuiltKind>();
                var shaped = new Dictionary<int, BuiltKind>();
                _shapedCells = shaped;
                bool raised = Raised();
                bool dug = Dug();
                float least = ShapedLeast();
                _shapedHere = 0;
                _keptHere = 0;
                _walkedHere = 0;
                _flatHere = 0;
                for (int i = 0; i < painted; i++)
                {
                    var target = _probe != null && i == probeAt ? _probe : null;
                    bool asked = target != null;
                    if (!pkg.ReadBool())
                    {
                        if (asked) Say(target, worked, moved, i, pitch, false, 0f, 0f, 0f, 0f, BuiltKind.None,
                                       "nothing is painted here, so there is nothing for the map to color.");
                        continue;
                    }
                    // Painted, and not the vertex asked about: a candidate for the nearest
                    // painted point to it. Overwritten whenever a nearer one turns up, so what
                    // is left at the end of the parse is the nearest in the zone.
                    if (_near != null && !asked)
                    {
                        int sx = i % pitch, sy = i / pitch;
                        float dx = (sx - probePx) * scale, dz = (sy - probePy) * scale;
                        float away = dx * dx + dz * dz;
                        if (away < _nearBest)
                        {
                            _nearBest = away;
                            target = _near;
                            target.Distance = Mathf.Sqrt(away);
                            target.X = origin.x + (sx - half) * scale;
                            target.Z = origin.z + (sy - half) * scale;
                        }
                    }
                    asked = target != null;
                    float r = pkg.ReadSingle(), g = pkg.ReadSingle(), b = pkg.ReadSingle();
                    float alpha = pkg.ReadSingle();          // cleared vegetation, not ground
                    bool earthwork = false;
                    var kind = KindOf(r, g, b);
                    if (kind == BuiltKind.None)
                    {
                        if (asked) Say(target, worked, moved, i, pitch, true, r, g, b, alpha, kind,
                                       "painted, but no channel is both over a quarter and clearly the " +
                                       "highest, so this is not dirt, farmland or paving -- too faint to call.");
                        continue;
                    }
                    if (asked) Say(target, worked, moved, i, pitch, true, r, g, b, alpha, kind,
                                   Wanted(kind)
                                       ? kind.ToString().ToLowerInvariant() + ", and that kind is switched on."
                                       : kind.ToString().ToLowerInvariant() + ", but that kind is switched off.");
                    // Paving and farmland are only ever laid on purpose, so they are never asked
                    // this. Dirt is the one paint the game puts down on its own behalf.
                    if (kind == BuiltKind.Dirt && worked != null)
                    {
                        var near = Look(worked, moved, i, pitch);
                        bool any = near.Touched;
                        // Ground that was worked answers for itself; splatter borrows.
                        float up = near.Here ? near.Up : near.WideUp;
                        float down = near.Here ? near.Down : near.WideDown;
                        // Three things a player can mean by dirt, and only one of them is an
                        // earthwork. Ground nobody moved is a path somebody wore or hoed.
                        // Ground somebody worked at and left level is a yard, a terrace top, a
                        // building plot -- the flattest, most deliberate ground there is, and
                        // the old rule threw away every meter of it, because a level op flags
                        // the vertices it considered whether or not it had to move them and
                        // that was read as the rim of a hole. Only ground that actually went
                        // somewhere, up into a wall or down into a pit, is the earthwork the
                        // two settings are about.
                        bool flat = (up < least && down < least) || (up <= 0f && down <= 0f);
                        if (!any)
                        {
                            _walkedHere++;
                            if (asked) target.Verdict = "dirt the ground was never moved under: a path, drawn.";
                        }
                        else if (flat)
                        {
                            _flatHere++;
                            if (asked) target.Verdict = "dirt on ground somebody worked at and left level: a " +
                                                        "yard, drawn as a dirt path.";
                        }
                        else if ((raised && up >= least) || (dug && down >= least))
                        {
                            _keptHere++;
                            if (asked) target.Verdict = "dirt on an earthwork, and the setting for that kind of " +
                                                        "earthwork is on, so it is drawn.";
                        }
                        else
                        {
                            _shapedHere++;
                            earthwork = true;
                            if (asked)
                                target.Verdict = "dirt on an earthwork -- the ground " +
                                    (up >= least ? $"rose {up:0.##} m" : $"fell {down:0.##} m") +
                                    " nearby -- and " + (up >= least ? "Paint Raised Ground" : "Paint Dug Ground") +
                                    " is off. It is still drawn if somebody has built within " +
                                    $"{MapPaint.SupportMeters:0} m of it, which only the server can say.";
                        }
                    }
                    int x = i % pitch, y = i / pitch;
                    var at = new Vector3(origin.x + (x - half) * scale, origin.y,
                                         origin.z + (y - half) * scale);

                    // The patch this point speaks for, not the one cell it falls in. The game
                    // records paint on a grid of its own, a meter apart, and writing each
                    // sample to a single cell was fine while a cell was four meters and is a
                    // lattice of dots at half a meter: every other cell in both directions is
                    // one nobody sampled. The same mistake the pieces used to make, in the
                    // other half of the picture.
                    float half2 = scale * 0.5f;
                    int kx0 = Mathf.FloorToInt((at.x - half2) / cell);
                    int kx1 = Mathf.FloorToInt((at.x + half2 - Nudge) / cell);
                    int kz0 = Mathf.FloorToInt((at.z - half2) / cell);
                    int kz1 = Mathf.FloorToInt((at.z + half2 - Nudge) / cell);
                    var into = earthwork ? shaped : cells;
                    for (int kz = kz0; kz <= kz1; kz++)
                        for (int kx = kx0; kx <= kx1; kx++)
                        {
                            int key = MapPaint.KeyOf(kx, kz);
                            // Paved beats dirt beats farmland where a cell holds more than one,
                            // so a road laid across a field reads as a road.
                            if (into.TryGetValue(key, out var had) && Rank(had) >= Rank(kind)) continue;
                            into[key] = kind;
                        }
                }
                return cells;
            }
            catch (Exception e)
            {
                if (_failed++ == 0)
                    TheGreatestMapMod.Log.LogWarning(
                        "[TheGreatestMap] A terrain record would not be read (" + e.Message +
                        "); that zone's paths are not drawn. Nothing else is affected, and this is " +
                        "said once.");
                return null;
            }
        }

        /// <summary>Fill in what a probe asked about, from inside the parse that knows it.</summary>
        private static void Say(Probe into, bool[] worked, float[] moved, int i, int pitch, bool painted,
                                float r, float g, float b, float a, BuiltKind kind, string verdict)
        {
            if (into == null) return;
            into.Reached = true;
            into.Painted = painted;
            into.R = r; into.G = g; into.B = b; into.A = a;
            into.Kind = kind;
            into.Verdict = verdict;
            if (worked == null) return;
            var near = Look(worked, moved, i, pitch);
            into.Worked = near.Touched;
            into.Here = near.Here;
            into.Up = near.Here ? near.Up : near.WideUp;
            into.Down = near.Here ? near.Down : near.WideDown;
        }

        /// <summary>
        /// Which of the game's six paints this is.
        ///
        /// <para>Each is a corner of the color cube -- dirt is red, farmland green, paving blue --
        /// so the channel has to be both high enough to see and clearly the one somebody
        /// painted. Being merely high is not enough: deep snow is white, and a test that only
        /// asked whether red was over a half would have drawn a hoed path across every mountain
        /// in the world.</para>
        ///
        /// <para>The floor used to be a half, which is a stronger paint than the game needs to
        /// show dirt and left every edge of every worked patch a meter or two short -- the game
        /// paints with a falloff, so the last stretch of a yard fades out rather than stopping.
        /// A quarter is nearer what the eye sees on the ground. What keeps the mountains out is
        /// not the floor but the margin: snow is painted on all three channels at once, so no
        /// channel beats the others by anything.</para>
        /// </summary>
        private static BuiltKind KindOf(float r, float g, float b)
        {
            const float Least = 0.25f;
            const float Over = 0.1f;
            if (b >= Least && b > g + Over && b > r + Over) return BuiltKind.Paved;
            if (g >= Least && g > r + Over && g > b + Over) return BuiltKind.Farm;
            if (r >= Least && r > g + Over && r > b + Over) return BuiltKind.Dirt;
            return BuiltKind.None;
        }

        private static int Rank(BuiltKind kind)
        {
            switch (kind)
            {
                case BuiltKind.Paved: return 3;
                case BuiltKind.Dirt: return 2;
                case BuiltKind.Farm: return 1;
                default: return 0;
            }
        }

        internal static List<string> Describe()
        {
            var lines = new List<string>();
            int cells = 0, shaped = 0, kept = 0, walked = 0, flat = 0;
            foreach (var record in _records.Values)
            {
                cells += record.Cells.Count;
                shaped += record.Shaped;
                kept += record.Kept;
                walked += record.Walked;
                flat += record.Flat;
            }
            lines.Add($"ground: {_records.Count} terrain record(s) known, {cells} painted cell(s); " +
                      $"last pass parsed {_parsed} and skipped {_skipped} unchanged" +
                      (_failed > 0 ? $"; {_failed} would not read" : ""));
            lines.Add("  paved " + OnOff(TgmConfig.PaintRoads) +
                      ", dirt paths " + OnOff(TgmConfig.PaintDirtPaths) +
                      ", farmland " + OnOff(TgmConfig.PaintFarmland) +
                      ", raised ground " + OnOff(TgmConfig.PaintRaisedGround) +
                      ", dug ground " + OnOff(TgmConfig.PaintDugGround) +
                      (Raised() || Dug() ? $" over {ShapedLeast():0.##} m" : "") +
                      $"; dirt points: {walked} walked, {flat} leveled, " +
                      $"{shaped} earthwork ({_adopted} cell(s) of it adopted for the building around them), " +
                      $"{kept} earthwork but kept");
            return lines;
        }

        private static string OnOff(BepInEx.Configuration.ConfigEntry<bool> entry) =>
            entry == null || entry.Value ? "on" : "off";
    }
}
