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
        private const string RpcPaved = "TGM_Paved";       // client -> server: paved ground I walked over

        private const float RequestSeconds = 10f;

        /// <summary>
        /// A ceiling on the picture, so a server that has been built on for years cannot grow a
        /// payload nobody can receive. Thirty thousand cells is about two square kilometers of
        /// solid building, far past any real world, and five bytes each is a 150 KB message sent
        /// only when something changes.
        /// </summary>
        private const int MaxCells = 30000;

        private static bool IsServer => ZNet.instance != null && ZNet.instance.IsServer();

        // ── what everyone holds ─────────────────────────────────────────────────────

        private static readonly Dictionary<int, BuiltKind> _cells = new Dictionary<int, BuiltKind>();

        /// <summary>Paved cells reported by players, kept apart because the sweep does not produce them.</summary>
        private static readonly HashSet<int> _paved = new HashSet<int>();

        private static bool _have;
        private static float _nextRequest;
        private static int _signature;
        private static bool _pavedDirty;

        internal static int Count => _cells.Count;
        internal static int PavedCount => _paved.Count;

        internal static void Reset()
        {
            _cells.Clear();
            _paved.Clear();
            _have = false;
            _nextRequest = 0f;
            _signature = 0;
            _pavedDirty = false;
            _kinds = null;
            _sweep = null;
            _nextSweep = 0f;
            Roads.Reset();
        }

        internal static void Register()
        {
            var rpc = ZRoutedRpc.instance;
            if (rpc == null) return;
            Rpc.Register<ZPackage>(rpc, RpcCells, RPC_Cells);
            Rpc.Register(rpc, RpcWant, RPC_Want);
            Rpc.Register<ZPackage>(rpc, RpcPaved, RPC_Paved);
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
            if (TgmConfig.PaintBuildings != null && !TgmConfig.PaintBuildings.Value)
            {
                // Switched off for the server: stop sweeping, and let the one empty picture go
                // out so clients wipe what they were drawing rather than keep a stale map.
                if (_sweep == null && _cells.Count == _paved.Count) return false;
                _sweep = null;
                Rebuild(new Dictionary<int, BuiltKind>());
                return true;
            }
            var kinds = Kinds();
            if (kinds.Count == 0) return false;
            var sectors = Sectors();
            if (sectors == null) return false;

            if (_sweep == null)
            {
                if (Time.time < _nextSweep) return false;
                _sweep = new Sweep();
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

            var picture = Built(_sweep.Found, _sweep.Density);
            int pieces = _sweep.Pieces;
            _sweep = null;
            _nextSweep = Time.time + SweepSeconds;
            if (pieces > 0 && picture.Count >= MaxCells)
                TheGreatestMapMod.Log.LogWarning(
                    $"[TheGreatestMap] Player building fills more than {MaxCells} cells; the map painting stops there.");
            return Rebuild(picture);
        }

        private static int LeastPieces() =>
            TgmConfig.PaintLeastPieces != null ? Mathf.Max(1, TgmConfig.PaintLeastPieces.Value) : 6;

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
        /// </summary>
        private static Dictionary<int, BuiltKind> Built(Dictionary<int, BuiltKind> found, Dictionary<int, int> density)
        {
            int least = LeastPieces();
            if (least <= 1) return found;
            var kept = new Dictionary<int, BuiltKind>();
            foreach (var kv in found)
            {
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

        /// <summary>Fold the roads in with the buildings and see whether the whole picture moved.</summary>
        private static bool Rebuild(Dictionary<int, BuiltKind> buildings)
        {
            bool roads = TgmConfig.PaintRoads == null || TgmConfig.PaintRoads.Value;
            if (roads)
                foreach (int key in _paved)
                    if (!buildings.ContainsKey(key) && buildings.Count < MaxCells) buildings[key] = BuiltKind.Paved;

            int signature = Hash(buildings);
            _cells.Clear();
            foreach (var kv in buildings) _cells[kv.Key] = kv.Value;
            _pavedDirty = false;
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
                // A player reporting new road does not wait for the next sweep: the sweep is
                // minutes apart on purpose, and a road that takes five minutes to appear after
                // you paved it looks broken rather than thorough.
                if (_pavedDirty && _sweep == null)
                {
                    var current = new Dictionary<int, BuiltKind>();
                    foreach (var kv in _cells) if (kv.Value != BuiltKind.Paved) current[kv.Key] = kv.Value;
                    if (Rebuild(current)) Broadcast();
                }
                if (StepSweep()) Broadcast();
            }
            else if (!_have && Time.time >= _nextRequest && ZRoutedRpc.instance != null)
            {
                _nextRequest = Time.time + RequestSeconds;
                ZRoutedRpc.instance.InvokeRoutedRPC(RpcWant);
            }

            Roads.Update(Report);
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
            var pkg = new ZPackage();
            pkg.Write(_cells.Count);
            foreach (var kv in _cells)
            {
                pkg.Write(kv.Key);
                pkg.Write((byte)kv.Value);
            }
            ZRoutedRpc.instance.InvokeRoutedRPC(target, RpcCells, pkg);
        }

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
            int n = pkg.ReadInt();
            for (int i = 0; i < n && pkg.GetPos() < pkg.Size(); i++)
            {
                int key = pkg.ReadInt();
                cells[key] = (BuiltKind)pkg.ReadByte();
            }
            _have = true;
            MapPaint.Take(cells);
        }

        // ── roads coming in ─────────────────────────────────────────────────────────

        /// <summary>Tell the server about paved ground walked over, or keep it if we are the server.</summary>
        private static void Report(List<int> cells)
        {
            if (cells == null || cells.Count == 0) return;
            if (IsServer) { Keep(cells); return; }
            if (ZRoutedRpc.instance == null) return;
            var pkg = new ZPackage();
            pkg.Write(cells.Count);
            foreach (int key in cells) pkg.Write(key);
            ZRoutedRpc.instance.InvokeRoutedRPC(RpcPaved, pkg);
        }

        private static void RPC_Paved(long sender, ZPackage pkg)
        {
            if (!IsServer || pkg == null) return;
            var cells = new List<int>();
            int n = pkg.ReadInt();
            for (int i = 0; i < n && pkg.GetPos() < pkg.Size(); i++) cells.Add(pkg.ReadInt());
            Keep(cells);
        }

        private static void Keep(List<int> cells)
        {
            int added = 0;
            foreach (int key in cells)
                if (_paved.Count < MaxCells && _paved.Add(key)) added++;
            if (added > 0) { _pavedDirty = true; PavedStore.Dirty = true; }
        }

        internal static HashSet<int> PavedCells => _paved;

        /// <summary>Put back what was saved from a previous run of the server.</summary>
        internal static void LoadPaved(IEnumerable<int> cells)
        {
            foreach (int key in cells) _paved.Add(key);
            if (_paved.Count > 0) _pavedDirty = true;
        }

        /// <summary>What the painting is being told, for tgm_paint.</summary>
        internal static List<string> Describe()
        {
            var lines = new List<string>();
            lines.Add(IsServer
                ? $"server: {_paved.Count} paved cell(s) remembered, sweeping every {SweepSeconds:0} s, " +
                  $"drawing a cell only where {LeastPieces()} piece(s) stand within a cell of it" +
                  (_sweep != null ? $", one in progress ({_sweep.Pieces} player-built piece(s) in {_sweep.Found.Count} cells so far)" : "")
                : "client: " + (_have ? "have the server's picture" : "WAITING for the server's picture"));
            lines.AddRange(Roads.Describe());
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
