using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace TheGreatestMap
{
    /// <summary>
    /// Where players have built, and what out of, swept on the server and painted by every
    /// client. The drawing is <see cref="MapPaint"/>; this is where the picture comes from.
    ///
    /// <para>Modeled on Transports, and for the same reason: what players build changes. A hall
    /// raised today is a ruin next month, and a marker written once would be wrong forever with
    /// no honest way to notice. So nothing here is recorded, nothing is shared through the map
    /// store, and nothing survives the server forgetting it -- the sweep simply says what stands
    /// in the world right now, and the clients draw that.</para>
    ///
    /// <para>Which prefabs to sweep is worked out by looking rather than by a list written here:
    /// anything in ZNetScene wearing both a Piece and a WearNTear is something a player can put
    /// up, and WearNTear already knows whether it is wood, stone or iron, which is exactly the
    /// question the colors ask. That covers the game's own four hundred pieces and whatever a
    /// mod adds, where a hardcoded list would quietly miss half a base.</para>
    ///
    /// <para>And a piece only counts if a player raised it. Every piece the world generator
    /// places -- the ruins, the camps, the crypts -- is the same prefab with no creator on its
    /// ZDO, so the one field tells a player's longhouse from an abandoned one.</para>
    ///
    /// <para>Roads come the other way round. Paved ground is terrain, and terrain only exists
    /// where it is loaded, so there is no sweep to do: each client reports the paved ground it
    /// walks over, the server keeps it and hands it to everybody. A road appears on the map the
    /// first time anyone uses it, which is the same bargain the crop sweep makes, and for the
    /// same reason.</para>
    /// </summary>
    internal static class Builds
    {
        private const string RpcCells = "TGM_Built";       // server -> everybody: the whole picture
        private const string RpcWant = "TGM_BuiltWant";    // client -> server: send it to me

        private const float RequestSeconds = 10f;

        /// <summary>
        /// A ceiling on the picture, so a server that has been built on for years cannot grow a
        /// payload nobody can receive. Thirty thousand cells is about two square kilometers of
        /// solid building, far past any real world, and five bytes each is a 150 KB message sent
        /// only when something changes.
        /// </summary>
        private const int MaxCells = 400000;

        /// <summary>
        /// What one picture may take on the wire, once compressed.
        ///
        /// <para>Well under the half megabyte that is fatal to a Steam connection -- a message
        /// that size is refused forever and stalls the send queue behind it -- and under it by
        /// enough that a world which grows between two sweeps cannot cross the line. This, and
        /// not the cell count, is the real limit: how many cells fit depends entirely on how
        /// they are arranged, and a hundred thousand cells of one tidy road compress to less
        /// than ten thousand scattered ones.</para>
        /// </summary>
        private const int MaxBytes = 200 * 1024;

        private static bool IsServer => ZNet.instance != null && ZNet.instance.IsServer();

        // ── what everyone holds ─────────────────────────────────────────────────────

        private static readonly Dictionary<int, BuiltKind> _cells = new Dictionary<int, BuiltKind>();

        private static bool _have;
        private static float _nextRequest;
        private static int _signature;

        internal static int Count => _cells.Count;

        internal static void Reset()
        {
            _cells.Clear();
            _have = false;
            _nextRequest = 0f;
            _signature = 0;
            _kinds = null;
            _sweep = null;
            _nextSweep = 0f;
            Ground.Reset();
        }

        internal static void Register()
        {
            var rpc = ZRoutedRpc.instance;
            if (rpc == null) return;
            Rpc.Register<ZPackage>(rpc, RpcCells, RPC_Cells);
            Rpc.Register(rpc, RpcWant, RPC_Want);
        }

        // ── the sweep (server) ──────────────────────────────────────────────────────

        private static Dictionary<int, BuiltKind> _kinds;

        private static readonly System.Reflection.FieldInfo _namedPrefabsField =
            AccessTools.Field(typeof(ZNetScene), "m_namedPrefabs");

        /// <summary>
        /// The game's own index of every ZDO in the world, by sector.
        ///
        /// <para>Reached by reflection rather than by the public helper next to it, and the
        /// difference is not style. GetAllZDOsWithPrefabIterative walks every ZDO in the world
        /// looking for one prefab: fine for the dozen ship prefabs Transports asks about, ruinous
        /// here, because there are four hundred buildable prefabs and this world holds over a
        /// million ZDOs -- four hundred million visits for one sweep. Walking the sectors once
        /// and asking each ZDO what it is costs one million, which is the same answer for four
        /// hundredth of the work.</para>
        /// </summary>
        private static readonly System.Reflection.FieldInfo _sectorsField =
            AccessTools.Field(typeof(ZDOMan), "m_objectsBySector");

        private static bool _saidNoSectors;

        private static List<ZDO>[] Sectors()
        {
            var man = ZDOMan.instance;
            if (man == null || _sectorsField == null) return null;
            var sectors = _sectorsField.GetValue(man) as List<ZDO>[];
            if (sectors == null && !_saidNoSectors)
            {
                _saidNoSectors = true;
                TheGreatestMapMod.Log.LogWarning(
                    "[TheGreatestMap] The game's ZDO sector index could not be read, so player building " +
                    "cannot be swept for. Paved roads still work; nothing else is affected.");
            }
            return sectors;
        }

        /// <summary>
        /// Nine materials in the game, four colors on the map. Ash and ancient stone are stone to
        /// anyone looking at a map and the extra woods are wood, because drawing nine shades of
        /// two colors would be detail nobody could read at a pixel per shed.
        ///
        /// <para>Black marble is the exception and gets its own, because it does not look like
        /// stone to anyone who has seen it: it is the one material you cannot have until the
        /// Mistlands, so a black hall on the map says when it was built and not only what of.
        /// </para>
        /// </summary>
        private static BuiltKind Paint(WearNTear.MaterialType material)
        {
            switch (material)
            {
                case WearNTear.MaterialType.Marble:
                    return BuiltKind.Marble;
                case WearNTear.MaterialType.Stone:
                case WearNTear.MaterialType.Ashstone:
                case WearNTear.MaterialType.Ancient:
                    return BuiltKind.Stone;
                case WearNTear.MaterialType.Iron:
                    return BuiltKind.Iron;
                default:
                    return BuiltKind.Wood;   // Wood, HardWood, Timberwood, Ice
            }
        }

        /// <summary>Every buildable prefab by the hash a ZDO carries, so a ZDO can be named in one lookup.</summary>
        private static Dictionary<int, BuiltKind> Kinds()
        {
            if (_kinds != null) return _kinds;
            var found = new Dictionary<int, BuiltKind>();
            var scene = ZNetScene.instance;
            if (scene == null) return found;   // not cached: ask again once the scene exists
            var seen = new HashSet<string>();

            void Consider(GameObject prefab)
            {
                if (prefab == null || !seen.Add(prefab.name)) return;
                if (prefab.GetComponent<Piece>() == null) return;
                var wear = prefab.GetComponent<WearNTear>();
                if (wear == null) return;
                // A ship and a cart are built pieces like any other and wear out like any other,
                // so they arrive here looking exactly like a wall. They are not building: they
                // move, and Transports already draws them where they are. Painting them as well
                // left a brown rectangle wherever anybody had parked.
                if (prefab.GetComponentInChildren<Ship>(true) != null) return;
                if (prefab.GetComponentInChildren<Vagon>(true) != null) return;
                var kind = Paint(wear.m_materialType);
                // The one place a name is read instead of a component, and it is read because
                // the material is wrong rather than because the material is missing: the
                // reinforced black marble pieces and the two carved heads are tagged iron, for
                // the iron in them. Nobody looking at a black marble hall sees iron. A map draws
                // what a thing looks like, so appearance wins over what it is held together
                // with, and three pieces behind one prefix is a cheaper exception than a wrong
                // color on every dvergr build.
                if (prefab.name.StartsWith("blackmarble", System.StringComparison.OrdinalIgnoreCase))
                    kind = BuiltKind.Marble;
                int hash = prefab.name.GetStableHashCode();
                found[hash] = kind;

                // A forge, a hearth, an oven, a kiln, a smelter, a cooking station: things that
                // stand in a building rather than being one. They are made of whatever they are
                // made of -- the forge is iron, the hearth is stone -- and that material is about
                // the fixture and says nothing about the hall around it. Letting them vote is
                // what drew a wooden longhouse as a stone one and then as an iron one, which is
                // how they came to be noticed at all.
                //
                // They still count toward how much is standing here, because a room full of
                // stations is a room: the test for whether there is a building is a different
                // question from what the building is made of, and these answer the first and not
                // the second.
                //
                // By component rather than by a list of names, so it covers a modded station
                // nobody here has heard of.
                if (IsFixture(prefab)) _fixtures.Add(hash);
                // Kept by name as well, so "why is my hall gray" can be answered by looking
                // rather than by guessing which piece carries which material. The material is
                // set on the prefab asset, which means nothing outside a running game can read
                // it -- not the assembly, not the source, not me.
                if (!_named.TryGetValue(kind, out var names)) _named[kind] = names = new List<string>();
                names.Add(prefab.name);
            }

            var named = _namedPrefabsField != null
                ? _namedPrefabsField.GetValue(scene) as Dictionary<int, GameObject>
                : null;
            if (named != null) foreach (var kv in named) Consider(kv.Value);
            if (scene.m_prefabs != null) foreach (var prefab in scene.m_prefabs) Consider(prefab);

            _kinds = found;
            TheGreatestMapMod.Log.LogInfo(
                $"[TheGreatestMap] {found.Count} buildable prefab(s) recognized for map painting, " +
                $"of which {_fixtures.Count} are fixtures that count toward a building without coloring it.");
            return _kinds;
        }

        private sealed class Sweep
        {
            public int Sector;
            /// <summary>
            /// What stands in each cell, by kind. A count and not just the winner, because a
            /// cell holding one stone block and a dozen wooden floors is a wooden floor to
            /// anybody looking at it -- keeping only the heaviest made every mixed building
            /// read as the hardest thing anyone had put in it.
            /// </summary>
            public readonly Dictionary<int, Tally> Found = new Dictionary<int, Tally>();
            /// <summary>How many player-built pieces stand in each cell, which is what tells a hut from a torch.</summary>
            public readonly Dictionary<int, int> Density = new Dictionary<int, int>();
            public int Pieces;
        }

        /// <summary>
        /// Non-empty sectors looked at per frame, the same budget the game uses for its own
        /// iterative walk. The whole index is tens of thousands of sectors, so a sweep takes a
        /// few seconds of frames and no single frame notices.
        /// </summary>
        private const int SectorsPerFrame = 400;

        private static Sweep _sweep;
        private static float _nextSweep;

        private static float SweepSeconds =>
            TgmConfig.BuildSweepSeconds != null ? Mathf.Max(10f, TgmConfig.BuildSweepSeconds.Value) : 120f;

        /// <summary>
        /// One pass over the world's ZDOs, a few hundred sectors per frame. A developed world
        /// holds hundreds of thousands of pieces, so doing this in one frame is a stall every
        /// player would feel, and doing it per prefab is four hundred times the work for the
        /// same answer.
        /// </summary>
        private static bool StepSweep()
        {
            if (!MapPaint.On())
            {
                // Switched off altogether: stop sweeping, and let one empty picture go out so
                // that every client puts its map back rather than keeping the last thing it drew.
                if (_sweep == null && _cells.Count == 0) return false;
                _sweep = null;
                Ground.Reset();
                return Rebuild(new Dictionary<int, BuiltKind>());
            }
            // Buildings can be switched off while paths stay on, so this gates the building half
            // of the walk rather than the walk: the terrain records are the other half of the
            // same pass and they are not the same setting.
            bool buildings = TgmConfig.PaintBuildings == null || TgmConfig.PaintBuildings.Value;
            var kinds = Kinds();
            if (kinds.Count == 0) return false;
            var sectors = Sectors();
            if (sectors == null) return false;

            if (_sweep == null)
            {
                if (Time.time < _nextSweep) return false;
                // Before the first key is computed, never during: a sweep is one grid throughout.
                MapPaint.UseCellSize(TgmConfig.PaintDetailMeters != null
                    ? TgmConfig.PaintDetailMeters.Value
                    : MapPaint.DefaultCellSize);
                _sweep = new Sweep();
                Ground.Begin();
            }

            int looked = 0;
            while (_sweep.Sector < sectors.Length)
            {
                var list = sectors[_sweep.Sector];
                _sweep.Sector++;
                if (list == null || list.Count == 0) continue;
                // No enumerator is held across frames and nothing else runs inside this loop, so
                // the list cannot be mutated underneath it; by index anyway, which costs nothing
                // and cannot throw if that ever stops being true.
                for (int i = 0; i < list.Count; i++)
                {
                    var zdo = list[i];
                    if (zdo == null || !zdo.IsValid()) continue;
                    // The ground first. It is one hash comparison to rule out, the same as the
                    // building lookup, and a terrain record is never also a piece.
                    if (Ground.Consider(zdo)) continue;
                    if (!buildings) continue;
                    if (!kinds.TryGetValue(zdo.GetPrefab(), out var material)) continue;
                    // The one field that tells a player's hall from the generator's ruin: every
                    // piece the world generator placed has no builder on it.
                    if (zdo.GetLong(ZDOVars.s_creator, 0L) == 0L) continue;
                    _sweep.Pieces++;
                    int key = MapPaint.Key(zdo.GetPosition());
                    _sweep.Density.TryGetValue(key, out int standing);
                    _sweep.Density[key] = standing + 1;
                    // Counted above, not voted below.
                    if (_fixtures.Contains(zdo.GetPrefab())) continue;
                    if (_sweep.Found.Count >= MaxCells && !_sweep.Found.ContainsKey(key)) continue;
                    _sweep.Found.TryGetValue(key, out var standingHere);
                    standingHere.Add(material);
                    _sweep.Found[key] = standingHere;
                }
                if (++looked >= SectorsPerFrame) return false;   // pick this up next frame
            }

            Ground.End();
            // Each cell settles on whatever it mostly is, now that the whole world has been seen.
            var found = new Dictionary<int, BuiltKind>();
            foreach (var kv in _sweep.Found)
            {
                var kind = kv.Value.Winner();
                if (kind != BuiltKind.None) found[kv.Key] = kind;
            }
            var picture = buildings
                ? Built(found, _sweep.Density)
                : new Dictionary<int, BuiltKind>();
            int pieces = _sweep.Pieces;
            _lastPieces = pieces;
            _lastCells = picture.Count;
            _sweep = null;
            _nextSweep = Time.time + SweepSeconds;
            // Ground goes in after the buildings so that a hall standing on paving stays a hall.
            Ground.AddTo(picture, MaxCells);
            if (pieces > 0 && picture.Count >= MaxCells)
                TheGreatestMapMod.Log.LogWarning(
                    $"[TheGreatestMap] Player building fills more than {MaxCells} cells; the map painting stops there.");
            return Rebuild(picture);
        }

        /// <summary>
        /// The grid these two thresholds were chosen on.
        ///
        /// <para>Both are counts of pieces standing in a fixed number of cells, so neither means
        /// anything until a cell means an area: three pieces in an eight meter cell is one piece
        /// per twenty-one square meters, and the same three in a four meter cell is four times
        /// that. Halving the grid therefore quadrupled both thresholds without anybody changing
        /// a setting, and what that drops first is the thin things -- a single wall, a narrow
        /// jetty, a walkway two meters across -- which is precisely what the neighborhood test
        /// was written to keep.</para>
        ///
        /// <para>Scaling by area here means the number keeps meaning what it meant when somebody
        /// chose it, whatever the grid does afterwards. Reading the thresholds as densities is
        /// also what they always were; they were only ever written down as counts because the
        /// grid happened to be fixed at the time.</para>
        /// </summary>
        private const float TunedAtCellSize = 8f;

        private static int ForGrid(int atTunedSize)
        {
            float side = MapPaint.CellSize / TunedAtCellSize;
            return Mathf.Max(1, Mathf.RoundToInt(atTunedSize * side * side));
        }

        private static int LeastPieces() =>
            ForGrid(TgmConfig.PaintLeastPieces != null ? Mathf.Max(1, TgmConfig.PaintLeastPieces.Value) : 6);

        /// <summary>How much has to stand in the cell itself, as against its neighborhood.</summary>
        private static int LeastInCell() =>
            ForGrid(TgmConfig.PaintLeastInCell != null ? Mathf.Max(1, TgmConfig.PaintLeastInCell.Value) : 3);

        /// <summary>
        /// Throw away the cells that hold a thing rather than a place.
        ///
        /// <para>Everything a player raises counts as building to the sweep, because everything a
        /// player raises is a piece: the campfire somebody lit while exploring, the workbench put
        /// down to repair a hammer, the lone portal in a field, the torch on a hilltop. Each of
        /// those was coming out as a brown rectangle on the map in a place with nothing in it,
        /// which is worse than drawing nothing -- a map people cannot trust is a map they stop
        /// reading.</para>
        ///
        /// <para>What tells those apart from a building is not what they are but how much of it
        /// there is: a hut is twenty pieces in a few meters, a campfire is one. So a cell is kept
        /// when the nine cells around it -- about twenty-four meters square, which is a small
        /// house -- hold enough pieces between them. The neighborhood rather than the cell alone,
        /// so that a long wall or a jetty running off the side of a base stays drawn instead of
        /// being chopped off where the pieces thin out.</para>
        ///
        /// <para>And the cell itself has to hold something worth drawing, which the neighborhood
        /// test cannot ask: a single piece was enough, so a four meter road clipping the corner
        /// of the cells on either side of it painted those cells whole, and a road laid in a
        /// straight line came out eight meters wider in places than in others. A road or a wall
        /// passing through a cell leaves several pieces in it; a corner caught in passing leaves
        /// one or two. That is the difference this measures, and it is the difference between a
        /// drawn edge that follows what was built and one that wanders.</para>
        /// </summary>
        private static Dictionary<int, BuiltKind> Built(Dictionary<int, BuiltKind> found, Dictionary<int, int> density)
        {
            int least = LeastPieces();
            int here = LeastInCell();
            if (least <= 1 && here <= 1) return found;
            var kept = new Dictionary<int, BuiltKind>();
            foreach (var kv in found)
            {
                if (here > 1 && (!density.TryGetValue(kv.Key, out int mine) || mine < here)) continue;
                int around = 0;
                for (int dz = -1; dz <= 1 && around < least; dz++)
                    for (int dx = -1; dx <= 1 && around < least; dx++)
                        if (density.TryGetValue(MapPaint.Neighbor(kv.Key, dx, dz), out int standing))
                            around += standing;
                if (around >= least) kept[kv.Key] = kv.Value;
            }
            return kept;
        }

        /// <summary>Keep the new picture, and say whether it differs from the last one.</summary>
        private static bool Rebuild(Dictionary<int, BuiltKind> picture)
        {
            int signature = Hash(picture);
            _cells.Clear();
            foreach (var kv in picture) _cells[kv.Key] = kv.Value;
            if (signature == _signature) return false;
            _signature = signature;
            return true;
        }

        private static int Hash(Dictionary<int, BuiltKind> cells)
        {
            // Order-independent, because a dictionary has none: every cell contributes the same
            // however it is walked, so the same world never looks like news.
            int h = cells.Count;
            foreach (var kv in cells) h ^= (kv.Key * 397) ^ ((int)kv.Value << 24);
            return h;
        }

        // ── keeping up ──────────────────────────────────────────────────────────────

        internal static void Update()
        {
            if (ZNet.instance == null) return;

            if (IsServer)
            {
                if (StepSweep()) Broadcast();
            }
            else if (!_have && Time.time >= _nextRequest && ZRoutedRpc.instance != null)
            {
                _nextRequest = Time.time + RequestSeconds;
                ZRoutedRpc.instance.InvokeRoutedRPC(RpcWant);
            }

            MapPaint.Update();
        }

        private static void Broadcast()
        {
            Send(ZRoutedRpc.Everybody);
            MapPaint.Take(_cells);   // a host plays on its own server
        }

        private static void Send(long target)
        {
            if (ZRoutedRpc.instance == null) return;
            ZRoutedRpc.instance.InvokeRoutedRPC(target, RpcCells, Encode());
        }

        private const int WireVersion = 1;

        /// <summary>
        /// What gets dropped first when the picture will not fit, worst first. Buildings are not
        /// on the list: they are the smallest part of the picture and the part a map is for.
        /// </summary>
        private static readonly BuiltKind[] Sacrificial =
            { BuiltKind.Farm, BuiltKind.Dirt, BuiltKind.Paved };

        /// <summary>
        /// The whole picture, small enough to send.
        ///
        /// <para>A cell used to go out as five loose bytes, which on the grid this now draws
        /// would be a megabyte for a developed world. Three things make it fit, and they
        /// compound. The cells are grouped by what is on them, so the kind is written once per
        /// group rather than once per cell. Within a group the keys are sorted and written as
        /// the step from the one before, and since building and road are runs of neighboring
        /// cells those steps are nearly all small numbers whose top bytes are zero. Then the lot
        /// is deflated, which is what turns all those zeros into nothing. Paint is contiguous by
        /// nature, so this is the shape the data was always in -- it was only ever written in a
        /// form that hid it.</para>
        ///
        /// <para>If it still will not fit, whole kinds come off rather than an arbitrary tail:
        /// losing every field leaves a map that is honest about what it shows, and losing a
        /// scattering of cells from everything leaves roads full of holes.</para>
        /// </summary>
        private static ZPackage Encode()
        {
            var skip = new HashSet<BuiltKind>();
            for (int attempt = 0; ; attempt++)
            {
                var pkg = EncodeWithout(skip, out int cells, out int raw, out byte[] flat);
                if (pkg.Size() <= MaxBytes || attempt >= Sacrificial.Length)
                {
                    Announce(pkg.Size(), raw, cells, skip, flat);
                    return pkg;
                }
                skip.Add(Sacrificial[attempt]);
            }
        }

        private static ZPackage EncodeWithout(HashSet<BuiltKind> skip, out int cells, out int raw,
                                              out byte[] flat)
        {
            var byKind = new Dictionary<byte, List<int>>();
            foreach (var kv in _cells)
            {
                if (skip.Contains(kv.Value)) continue;
                if (!byKind.TryGetValue((byte)kv.Value, out var keys))
                    byKind[(byte)kv.Value] = keys = new List<int>();
                keys.Add(kv.Key);
            }

            var inner = new ZPackage();
            inner.Write(MapPaint.CellSize);
            inner.Write(byKind.Count);
            cells = 0;
            foreach (var kv in byKind)
            {
                var keys = kv.Value;
                keys.Sort();
                inner.Write(kv.Key);
                inner.Write(keys.Count);
                int last = 0;
                for (int i = 0; i < keys.Count; i++) { inner.Write(keys[i] - last); last = keys[i]; }
                cells += keys.Count;
            }
            raw = inner.Size();
            flat = inner.GetArray();

            var pkg = new ZPackage();
            pkg.Write(WireVersion);
            pkg.WriteCompressed(inner);
            return pkg;
        }

        /// <summary>Said when the picture grows, not every time it is sent.</summary>
        private static void Announce(int size, int raw, int cells, HashSet<BuiltKind> skip, byte[] flat)
        {
            if (size <= _loudestSent && skip.Count == _lastSkipped) return;
            _loudestSent = System.Math.Max(_loudestSent, size);
            _lastSkipped = skip.Count;
            string lost = "";
            foreach (var kind in skip) lost += (lost.Length > 0 ? ", " : "") + kind;
            // What the game's other codec would have cost. ZPackage.WriteCompressed, which is
            // also what the markers go through, is gzip at CompressionLevel.Fastest; sitting
            // unused beside it in the same class is Compress2, which is Brotli at Optimal.
            // Delta-encoded sorted keys are long runs of small repeating values, which is the
            // case the two differ most on -- and how much they differ decides whether a finer
            // grid fits in a message. Measured on the real picture rather than guessed at, and
            // only on the rare send that logs, because Optimal is not free.
            string other = "";
            try
            {
                int brotli = Utils.Compress2(flat).Length;
                other = $" (the same bytes under the game's other codec, Brotli: {brotli / 1024} KB, " +
                        $"{(size > 0 ? (float)brotli / size : 1f):0.00}x)";
            }
            catch (System.Exception) { }

            TheGreatestMapMod.Log.LogInfo(
                $"[TheGreatestMap] Map picture: {cells} cell(s) at {MapPaint.CellSize:0.#} m, " +
                $"{raw / 1024} KB packed down to {size / 1024} KB{other}." +
                (lost.Length > 0
                    ? $" Over the {MaxBytes / 1024} KB a message may take, so this leaves out: {lost}. " +
                      "Raise Paint Detail Meters for a coarser grid that fits more of the world."
                    : ""));
        }

        private static int _loudestSent;
        private static int _lastSkipped = -1;

        /// <summary>A client asking gets the last picture rather than a sweep of its own.</summary>
        private static void RPC_Want(long sender)
        {
            if (!IsServer) return;
            Send(sender);
        }

        private static void RPC_Cells(long sender, ZPackage pkg)
        {
            if (pkg == null) return;
            // Everybody includes us: InvokeRoutedRPC runs the local handler too. Broadcast has
            // already handed MapPaint the picture straight from the sweep, so re-reading our own
            // package would be the same answer for the cost of parsing it -- and on a developed
            // world that package is the largest thing this mod sends.
            if (IsServer) return;
            var cells = new Dictionary<int, BuiltKind>();
            try
            {
                int version = pkg.ReadInt();
                if (version != WireVersion)
                {
                    TheGreatestMapMod.Log.LogWarning(
                        "[TheGreatestMap] The server sent a map picture written in a way this version does not " +
                        "read (" + version + "); nothing is painted. Markers are not affected.");
                    return;
                }
                var inner = pkg.ReadCompressedPackage();
                // The grid first: every key after it means nothing without it.
                MapPaint.UseCellSize(inner.ReadSingle());
                int kinds = inner.ReadInt();
                for (int k = 0; k < kinds && inner.GetPos() < inner.Size(); k++)
                {
                    var kind = (BuiltKind)inner.ReadByte();
                    int n = inner.ReadInt();
                    int last = 0;
                    for (int i = 0; i < n && inner.GetPos() < inner.Size(); i++)
                    {
                        last += inner.ReadInt();
                        cells[last] = kind;
                    }
                }
            }
            catch (System.Exception e)
            {
                TheGreatestMapMod.Log.LogWarning(
                    "[TheGreatestMap] The server's map picture would not be read (" + e.Message +
                    "); nothing is painted. Markers are not affected.");
                return;
            }
            _have = true;
            MapPaint.Take(cells);
        }

        private static readonly Dictionary<BuiltKind, List<string>> _named =
            new Dictionary<BuiltKind, List<string>>();

        /// <summary>
        /// What the last finished sweep found. Kept because the ratio of pieces to cells is the
        /// number every question about how finely to record the world turns on, and it was being
        /// counted and then dropped.
        /// </summary>
        private static int _lastPieces;
        private static int _lastCells;

        /// <summary>Pieces that count toward there being a building but not toward what it is made of.</summary>
        private static readonly HashSet<int> _fixtures = new HashSet<int>();

        /// <summary>
        /// Is this something that stands in a building rather than something that is one?
        ///
        /// <para>The first pass at this asked for four components and caught nothing, because
        /// the pieces doing the damage were chests. A black metal chest is iron, and a base has
        /// several; that was most of an iron building appearing in a hall with no iron in it.
        /// Cooking stations and incinerators are their own components too, neither of them the
        /// crafting station they resemble.</para>
        ///
        /// <para>Components rather than prefab names, so a modded chest or oven is covered
        /// without anybody adding it to anything.</para>
        /// </summary>
        private static bool IsFixture(GameObject prefab) =>
            prefab.GetComponentInChildren<CraftingStation>(true) != null
            || prefab.GetComponentInChildren<StationExtension>(true) != null
            || prefab.GetComponentInChildren<Smelter>(true) != null
            || prefab.GetComponentInChildren<Fireplace>(true) != null
            || prefab.GetComponentInChildren<Container>(true) != null
            || prefab.GetComponentInChildren<CookingStation>(true) != null
            || prefab.GetComponentInChildren<Incinerator>(true) != null
            || prefab.GetComponentInChildren<Fermenter>(true) != null
            || prefab.GetComponentInChildren<Beehive>(true) != null
            || prefab.GetComponentInChildren<ItemStand>(true) != null;

        /// <summary>
        /// Every buildable prefab that paints as one material, for tgm_paint &lt;kind&gt;.
        /// </summary>
        internal static List<string> Pieces(string want)
        {
            var lines = new List<string>();
            Kinds();   // the list is filled as a side effect of building the lookup
            var kinds = new List<BuiltKind>();
            foreach (var kv in _named)
                if (string.IsNullOrEmpty(want) || kv.Key.ToString().ToLowerInvariant().StartsWith(want.ToLowerInvariant()))
                    kinds.Add(kv.Key);
            if (kinds.Count == 0)
            {
                lines.Add($"no building material is called \"{want}\". Try wood, stone or iron.");
                return lines;
            }
            foreach (var kind in kinds)
            {
                var names = _named[kind];
                names.Sort();
                int fixtures = 0;
                foreach (var name in names) if (_fixtures.Contains(name.GetStableHashCode())) fixtures++;
                lines.Add($"{kind}: {names.Count} piece(s)" +
                          (fixtures > 0 ? $", of which {fixtures} are fixtures and do not color anything" : ""));
                // Wrapped, because the console gives one line about as much room as a sentence.
                string row = "  ";
                foreach (var name in names)
                {
                    if (row.Length + name.Length + 2 > 110) { lines.Add(row); row = "  "; }
                    row += name + "  ";
                }
                if (row.Trim().Length > 0) lines.Add(row);
            }
            return lines;
        }

        /// <summary>What the painting is being told, for tgm_paint.</summary>
        internal static List<string> Describe()
        {
            var lines = new List<string>();
            if (!MapPaint.On())
            {
                lines.Add("the painting is switched off (Server Shares Player Developments, the server's): nothing is swept for, " +
                          "sampled or sent.");
                return lines;
            }
            lines.Add(IsServer
                ? $"server: sweeping every {SweepSeconds:0} s, " +
                  $"drawing a cell only where {LeastInCell()} piece(s) stand in it and " +
                  $"{LeastPieces()} within a cell of it" +
                  (_sweep != null ? $", one in progress ({_sweep.Pieces} player-built piece(s) in {_sweep.Found.Count} cells so far)" : "")
                  + (_lastPieces > 0
                      ? $"; last sweep: {_lastPieces} player-built piece(s) became {_lastCells} cell(s) " +
                        $"at {MapPaint.CellSize:0.#} m, {(_lastCells > 0 ? (float)_lastPieces / _lastCells : 0f):0.00} piece(s) a cell"
                      : "")
                : "client: " + (_have ? "have the server's picture" : "WAITING for the server's picture"));
            if (IsServer) lines.AddRange(Ground.Describe());
            return lines;
        }
    }

    [HarmonyPatch(typeof(Game), "Start")]
    internal static class Game_Start_Builds_Patch
    {
        private static void Postfix()
        {
            Builds.Reset();
            MapPaint.Reset();
            Builds.Register();
        }
    }
}
