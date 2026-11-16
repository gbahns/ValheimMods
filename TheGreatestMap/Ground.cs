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
        }

        private static readonly Dictionary<ZDOID, Record> _records = new Dictionary<ZDOID, Record>();

        /// <summary>Zones seen in the pass now running, so vanished ones can be dropped after it.</summary>
        private static readonly HashSet<ZDOID> _seen = new HashSet<ZDOID>();

        private static int _parsed;
        private static int _skipped;
        private static int _failed;

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
            _records[id] = new Record { Revision = zdo.DataRevision, Cells = cells };
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
        internal static void AddTo(Dictionary<int, BuiltKind> picture, int maxCells)
        {
            _dropped = 0;
            Pour(picture, maxCells, BuiltKind.Paved);
            Pour(picture, maxCells, BuiltKind.Dirt);
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
                var packed = zdo.GetByteArray(ZDOVars.s_TCData);
                if (packed == null || packed.Length == 0) return null;
                var pkg = new ZPackage(Utils.Decompress(packed));

                pkg.ReadInt();        // the record's own version, 1 so far
                pkg.ReadInt();        // how many operations made it
                pkg.ReadVector3();    // where the last one landed
                pkg.ReadSingle();     // and how wide it was

                int heights = pkg.ReadInt();
                for (int i = 0; i < heights; i++)
                    if (pkg.ReadBool()) { pkg.ReadSingle(); pkg.ReadSingle(); }

                int painted = pkg.ReadInt();
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

                var cells = new Dictionary<int, BuiltKind>();
                for (int i = 0; i < painted; i++)
                {
                    if (!pkg.ReadBool()) continue;
                    float r = pkg.ReadSingle(), g = pkg.ReadSingle(), b = pkg.ReadSingle();
                    pkg.ReadSingle();                        // alpha: cleared vegetation, not ground
                    var kind = KindOf(r, g, b);
                    if (kind == BuiltKind.None) continue;
                    int x = i % pitch, y = i / pitch;
                    var at = new Vector3(origin.x + (x - half) * scale, origin.y,
                                         origin.z + (y - half) * scale);
                    int key = MapPaint.Key(at);
                    // Paved beats dirt beats farmland where a cell holds more than one, so a
                    // road laid across a field reads as a road.
                    if (cells.TryGetValue(key, out var had) && Rank(had) >= Rank(kind)) continue;
                    cells[key] = kind;
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

        /// <summary>
        /// Which of the game's six paints this is.
        ///
        /// <para>Each is a corner of the color cube -- dirt is red, farmland green, paving blue --
        /// so the channel has to be both high and the highest. Being merely high is not enough:
        /// deep snow is white, and a test that only asked whether red was over a half would have
        /// drawn a hoed path across every mountain in the world.</para>
        /// </summary>
        private static BuiltKind KindOf(float r, float g, float b)
        {
            if (b > 0.5f && b > g && b > r) return BuiltKind.Paved;
            if (g > 0.5f && g > r && g > b) return BuiltKind.Farm;
            if (r > 0.5f && r > g && r > b) return BuiltKind.Dirt;
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
            int cells = 0;
            foreach (var record in _records.Values) cells += record.Cells.Count;
            lines.Add($"ground: {_records.Count} terrain record(s) known, {cells} painted cell(s); " +
                      $"last pass parsed {_parsed} and skipped {_skipped} unchanged" +
                      (_failed > 0 ? $"; {_failed} would not read" : ""));
            lines.Add("  paved " + OnOff(TgmConfig.PaintRoads) +
                      ", dirt paths " + OnOff(TgmConfig.PaintDirtPaths) +
                      ", farmland " + OnOff(TgmConfig.PaintFarmland));
            return lines;
        }

        private static string OnOff(BepInEx.Configuration.ConfigEntry<bool> entry) =>
            entry == null || entry.Value ? "on" : "off";
    }
}
