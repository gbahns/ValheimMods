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
        /// Nine materials in the game, three colors on the map. Marble, ash and ancient stone are
        /// stone to anyone looking at a map, and the two extra woods are wood; drawing nine
        /// shades of two colors would be detail nobody could read at a pixel per shed.
        /// </summary>
        private static BuiltKind Paint(WearNTear.MaterialType material)
        {
            switch (material)
            {
                case WearNTear.MaterialType.Stone:
                case WearNTear.MaterialType.Marble:
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
                found[prefab.name.GetStableHashCode()] = Paint(wear.m_materialType);
            }

            var named = _namedPrefabsField != null
                ? _namedPrefabsField.GetValue(scene) as Dictionary<int, GameObject>
                : null;
            if (named != null) foreach (var kv in named) Consider(kv.Value);
            if (scene.m_prefabs != null) foreach (var prefab in scene.m_prefabs) Consider(prefab);

            _kinds = found;
            TheGreatestMapMod.Log.LogInfo($"[TheGreatestMap] {found.Count} buildable prefab(s) recognized for map painting.");
            return _kinds;
        }

        private sealed class Sweep
        {
            public int Sector;
            public readonly Dictionary<int, BuiltKind> Found = new Dictionary<int, BuiltKind>();
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
                    if (_sweep.Found.Count >= MaxCells && !_sweep.Found.ContainsKey(key)) continue;
                    // Stone shows through wood: a stone keep with a wooden roof is a stone keep.
                    if (_sweep.Found.TryGetValue(key, out var had) && Heavier(had, material)) continue;
                    _sweep.Found[key] = material;
                }
                if (++looked >= SectorsPerFrame) return false;   // pick this up next frame
            }

            Ground.End();
            var picture = buildings
                ? Built(_sweep.Found, _sweep.Density)
                : new Dictionary<int, BuiltKind>();
            int pieces = _sweep.Pieces;
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

        private static bool Heavier(BuiltKind had, BuiltKind now)
        {
            int Rank(BuiltKind k) => k == BuiltKind.Stone ? 3 : k == BuiltKind.Iron ? 2 : k == BuiltKind.Wood ? 1 : 0;
            return Rank(had) >= Rank(now);
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
                var pkg = EncodeWithout(skip, out int cells, out int raw);
                if (pkg.Size() <= MaxBytes || attempt >= Sacrificial.Length)
                {
                    Announce(pkg.Size(), raw, cells, skip);
                    return pkg;
                }
                skip.Add(Sacrificial[attempt]);
            }
        }

        private static ZPackage EncodeWithout(HashSet<BuiltKind> skip, out int cells, out int raw)
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

            var pkg = new ZPackage();
            pkg.Write(WireVersion);
            pkg.WriteCompressed(inner);
            return pkg;
        }

        /// <summary>Said when the picture grows, not every time it is sent.</summary>
        private static void Announce(int size, int raw, int cells, HashSet<BuiltKind> skip)
        {
            if (size <= _loudestSent && skip.Count == _lastSkipped) return;
            _loudestSent = System.Math.Max(_loudestSent, size);
            _lastSkipped = skip.Count;
            string lost = "";
            foreach (var kind in skip) lost += (lost.Length > 0 ? ", " : "") + kind;
            TheGreatestMapMod.Log.LogInfo(
                $"[TheGreatestMap] Map picture: {cells} cell(s) at {MapPaint.CellSize:0.#} m, " +
                $"{raw / 1024} KB packed down to {size / 1024} KB." +
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

        /// <summary>What the painting is being told, for tgm_paint.</summary>
        internal static List<string> Describe()
        {
            var lines = new List<string>();
            if (!MapPaint.On())
            {
                lines.Add("the painting is switched off (Server Surveys Player Developments, the server's): nothing is swept for, " +
                          "sampled or sent.");
                return lines;
            }
            lines.Add(IsServer
                ? $"server: sweeping every {SweepSeconds:0} s, " +
                  $"drawing a cell only where {LeastInCell()} piece(s) stand in it and " +
                  $"{LeastPieces()} within a cell of it" +
                  (_sweep != null ? $", one in progress ({_sweep.Pieces} player-built piece(s) in {_sweep.Found.Count} cells so far)" : "")
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
