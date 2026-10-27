using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace TheGreatestMap
{
    /// <summary>
    /// Ships and carts, drawn where they are rather than where they were found.
    ///
    /// Everything else this mod records is a memory: a deposit written down once stays true,
    /// because ore does not wander off. A longship does. So these are not recorded at all --
    /// the server sweeps the world for them, tells the clients, and each client draws what it
    /// was told. Nothing is written to the map, nothing is shared, nothing is left behind when
    /// the setting goes off. That also means the "found, not detected" rule is not in play:
    /// a vessel is the group's own property, and knowing where you parked it is not a discovery.
    ///
    /// Modeled on Portals, which does the same dance for portals and whose comments explain the
    /// shape. Two differences worth knowing:
    ///
    /// The world has no list of vessels to ask for. Portals get ZDOMan.GetPortalList() free;
    /// here the only way through is GetAllZDOsWithPrefabIterative, prefab by prefab, which walks
    /// 400 sectors per call and must be carried across frames -- this world holds over a million
    /// ZDOs and a sweep done in one frame would be a visible stall on the server.
    ///
    /// And which prefabs to sweep is worked out by looking, not by a list written here. Anything
    /// in ZNetScene wearing a Ship or a Vagon is a vessel, which covers the vanilla five, the ones
    /// TheGreatestShips adds, and whatever comes next. A hardcoded list would quietly miss a
    /// modded ship, and a ship that exists and never appears on the map is the worst failure this
    /// feature has.
    /// </summary>
    internal static class Transports
    {
        private const string RpcList = "TGM_Transports";      // server -> everybody: every vessel now
        private const string RpcWant = "TGM_TransportsWant";  // client -> server: send me that list
        private const string RpcMove = "TGM_TransportsMove";  // server -> everybody: these few have moved
        private const float RequestSeconds = 10f;

        /// <summary>
        /// How far a vessel must have moved since we last said so before it is worth saying
        /// again. A moored ship rides the swell and a cart settles on its wheels; neither is
        /// news, and below a meter nothing is visible on the map anyway.
        /// </summary>
        private const float MovedEnough = 1.5f;

        /// <summary>
        /// A number for the list, so a move can say "the third vessel" without carrying an
        /// identity. The server bumps it with every full list it sends; a move carrying a
        /// different number belongs to a list this client never saw and is dropped, which is
        /// the whole of the protocol. It also means the move message is nine bytes a vessel
        /// rather than the thirty a position and a name would cost.
        /// </summary>
        private static int _gen;
        private static float _nextLive;

        /// <summary>How often a moving vessel's position is passed on. Server side only.</summary>
        private static float LiveSeconds =>
            TgmConfig.TransportLiveSeconds != null ? Mathf.Max(0.1f, TgmConfig.TransportLiveSeconds.Value) : 0.25f;

        /// <summary>
        /// How coarsely a position is read when deciding whether anything changed. A moored ship
        /// is never quite still -- it rides the swell -- and comparing exact positions would make
        /// every sweep look like news and send the whole list to every client, forever. Four
        /// meters is below what anyone can see on the map and well above the bobbing.
        /// </summary>
        private const float SignatureGrid = 4f;

        private sealed class Live
        {
            public Vector3 Pos;
            public string Name = "";
            public bool Cart;
            /// <summary>Server side: the object this came from, so its position can be read again cheaply.</summary>
            public ZDOID Id;
            /// <summary>Client side: the pin drawing it, so a move can nudge that pin instead of rebuilding every one.</summary>
            public Minimap.PinData Pin;
        }

        private static readonly List<Live> _live = new List<Live>();
        /// <summary>A pin we drew, and whether it is a cart, so anything painting over them knows which is which.</summary>
        internal sealed class Shown
        {
            public Minimap.PinData Pin;
            public bool Cart;
        }

        private static readonly List<Shown> _drawn = new List<Shown>();

        internal static IEnumerable<Shown> DrawnPins => _drawn;
        private static bool _haveList;
        private static float _nextRequest;
        private static bool _redrawWanted;
        private static bool _drawnShips;
        private static bool _drawnCarts;

        internal static bool HaveList => _haveList;
        internal static int Count => _live.Count;

        internal static void Reset()
        {
            _live.Clear();
            _drawn.Clear();
            _haveList = false;
            _nextRequest = 0f;
            _redrawWanted = false;
            _drawnShips = false;
            _drawnCarts = false;
            _prefabs = null;
            _labels.Clear();
            _sweep = null;
            _nextSweep = 0f;
            _nextLive = 0f;
            _gen = 0;
            _lastServerSignature = "";
        }

        internal static void Register()
        {
            var rpc = ZRoutedRpc.instance;
            if (rpc == null) return;
            Rpc.Register<ZPackage>(rpc, RpcList, RPC_List);
            Rpc.Register(rpc, RpcWant, RPC_Want);
            Rpc.Register<ZPackage>(rpc, RpcMove, RPC_Move);
        }

        private static bool IsServer => ZNet.instance != null && ZNet.instance.IsServer();

        // ── which prefabs are vessels ───────────────────────────────────────────────

        private sealed class Vessel
        {
            public string Prefab;
            public bool Cart;
        }

        private static List<Vessel> _prefabs;

        // ZNetScene.m_namedPrefabs is private; AccessTools so runtime enforcement does not throw.
        private static readonly System.Reflection.FieldInfo _namedPrefabsField =
            AccessTools.Field(typeof(ZNetScene), "m_namedPrefabs");

        /// <summary>
        /// Every prefab in the world that is a vessel, found by looking for the components that
        /// make one rather than by name.
        ///
        /// Read from m_namedPrefabs rather than m_prefabs, which is the difference between
        /// finding a modded ship and not. m_prefabs is the list the scene was built from;
        /// m_namedPrefabs is what a ZDO's prefab hash is resolved through, so anything that can
        /// exist in the world at all is in it -- including a hull another mod cloned at runtime,
        /// which never joins the original list. Eight of TheGreatestShips' ships were invisible
        /// to this for exactly that reason: cloned long before we looked, and absent anyway.
        /// m_prefabs is still read as well, in case a prefab is somehow only there.
        ///
        /// Built once per world: ZNetScene is made again on a world load, and Reset throws this
        /// away with everything else.
        /// </summary>
        private static List<Vessel> Vessels()
        {
            if (_prefabs != null) return _prefabs;
            var found = new List<Vessel>();
            var scene = ZNetScene.instance;
            if (scene == null) return found;   // not cached: ask again once the scene exists
            var seen = new HashSet<string>();

            void Consider(GameObject prefab)
            {
                if (prefab == null || !seen.Add(prefab.name)) return;
                bool ship = prefab.GetComponentInChildren<Ship>(true) != null;
                bool cart = !ship && prefab.GetComponentInChildren<Vagon>(true) != null;
                if (!ship && !cart) return;
                found.Add(new Vessel { Prefab = prefab.name, Cart = cart });
            }

            var named = _namedPrefabsField != null
                ? _namedPrefabsField.GetValue(scene) as Dictionary<int, GameObject>
                : null;
            if (named != null) foreach (var kv in named) Consider(kv.Value);
            if (scene.m_prefabs != null) foreach (var prefab in scene.m_prefabs) Consider(prefab);

            _prefabs = found;
            TheGreatestMapMod.Log.LogInfo($"[TheGreatestMap] {found.Count} vessel prefab(s) to watch for: {Names(found)}.");
            return _prefabs;
        }

        private static string Names(List<Vessel> list)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var v in list)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(v.Prefab).Append(v.Cart ? " (cart)" : " (ship)");
            }
            return sb.Length > 0 ? sb.ToString() : "none";
        }

        // ── the server's sweep, carried across frames ───────────────────────────────

        /// <summary>
        /// One sweep in progress: which prefab it is up to, how far into that prefab's walk it
        /// has got, and what it has found so far. A sweep that finished mid-flight would be a
        /// list missing everything after the prefab it stopped on, so the result is only used
        /// once every prefab has been walked to the end.
        /// </summary>
        private sealed class Sweep
        {
            public int Prefab;
            public int Index;
            public readonly List<Live> Found = new List<Live>();
            public readonly List<ZDO> Batch = new List<ZDO>();
        }

        private static Sweep _sweep;
        private static float _nextSweep;
        private static string _lastServerSignature = "";

        private static float SweepSeconds =>
            TgmConfig.TransportSweepSeconds != null ? Mathf.Max(1f, TgmConfig.TransportSweepSeconds.Value) : 5f;

        /// <summary>One slice of work. True when a whole sweep has just finished.</summary>
        private static bool StepSweep()
        {
            var vessels = Vessels();
            if (vessels.Count == 0) return false;
            var zdoMan = ZDOMan.instance;
            if (zdoMan == null) return false;

            if (_sweep == null)
            {
                if (Time.time < _nextSweep) return false;
                _sweep = new Sweep();
            }

            while (_sweep.Prefab < vessels.Count)
            {
                var vessel = vessels[_sweep.Prefab];
                _sweep.Batch.Clear();
                bool done = zdoMan.GetAllZDOsWithPrefabIterative(vessel.Prefab, _sweep.Batch, ref _sweep.Index);
                foreach (var zdo in _sweep.Batch)
                {
                    if (zdo == null || !zdo.IsValid()) continue;
                    // The prefab name travels, not a label. What a thing is called belongs to the
                    // client: vanilla's longship is the prefab "VikingShip", whose own name is
                    // "$ship_longship", and only the client has the player's language to turn
                    // that into "Longship".
                    _sweep.Found.Add(new Live { Pos = zdo.GetPosition(), Name = vessel.Prefab, Cart = vessel.Cart, Id = zdo.m_uid });
                }
                if (!done) return false;   // more sectors to walk: pick this up next frame
                _sweep.Prefab++;
                _sweep.Index = 0;
                // One prefab per frame. The walk itself is bounded at 400 sectors, but a world
                // with a dozen vessel prefabs should not do a dozen of those back to back.
                return false;
            }

            // A sweep that found the same world as last time is thrown away rather than
            // installed. The entries carry the pin drawing each vessel, and replacing them with
            // equal-but-new ones would drop every one of those references on the floor: the
            // host's own pins would stop following a moving ship until something else forced a
            // redraw, which is exactly the bug this feature exists to fix.
            string signature = Signature(_sweep.Found);
            bool changed = signature != _lastServerSignature;
            if (changed)
            {
                _lastServerSignature = signature;
                _live.Clear();
                _live.AddRange(_sweep.Found);
            }
            _sweep = null;
            _nextSweep = Time.time + SweepSeconds;
            return changed;
        }

        private static string Signature(List<Live> list)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var v in list)
                sb.Append(Mathf.RoundToInt(v.Pos.x / SignatureGrid)).Append(',')
                  .Append(Mathf.RoundToInt(v.Pos.z / SignatureGrid)).Append('=')
                  .Append(v.Cart ? 'c' : 's').Append(v.Name).Append(';');
            return sb.ToString();
        }

        private static void Send(long target, List<Live> list)
        {
            if (ZRoutedRpc.instance == null) return;
            var pkg = new ZPackage();
            pkg.Write(list.Count);
            foreach (var v in list)
            {
                pkg.Write(v.Pos);
                pkg.Write(v.Name ?? "");
                pkg.Write(v.Cart);
            }
            // After the vessels, never between them: a client written before this reads its way
            // through the list exactly as it always did and simply stops with four bytes left
            // over. Putting the number inside the loop would have shifted every field after it
            // and handed older players a map of garbage.
            pkg.Write(_gen);
            ZRoutedRpc.instance.InvokeRoutedRPC(target, RpcList, pkg);
        }

        /// <summary>
        /// A client asking for the list gets what the last sweep found rather than a sweep of its
        /// own: a player joining is exactly when several ask at once, and a sweep each would be
        /// several walks of a million ZDOs for one answer they will all share.
        /// </summary>
        private static void RPC_Want(long sender)
        {
            if (!IsServer) return;
            Send(sender, _live);
        }

        // ── client ──────────────────────────────────────────────────────────────────

        private static void RPC_List(long sender, ZPackage pkg)
        {
            if (pkg == null) return;
            _live.Clear();
            int n = pkg.ReadInt();
            for (int i = 0; i < n; i++)
            {
                var pos = pkg.ReadVector3();
                string name = pkg.ReadString();
                // Read whatever shape arrives. A payload written by an older build ends here, and
                // refusing it would leave that player's map blank rather than merely cart-less.
                bool cart = pkg.GetPos() < pkg.Size() && pkg.ReadBool();
                _live.Add(new Live { Pos = pos, Name = name, Cart = cart });
            }
            // A server written before this sends no generation. Zero is as good a name for that
            // list as any; it will simply never match a move, and there will not be any.
            _gen = pkg.GetPos() < pkg.Size() ? pkg.ReadInt() : 0;
            _haveList = true;
            _redrawWanted = true;
        }

        /// <summary>
        /// The few vessels that have moved since the last word. Their pins are nudged where they
        /// stand rather than every pin being taken down and put up again: a redraw is a removal
        /// and an addition per vessel and it dirties the whole pin list, which at four times a
        /// second is the kind of thing that has stalled this client into disconnecting before.
        /// </summary>
        private static void RPC_Move(long sender, ZPackage pkg)
        {
            if (pkg == null || !_haveList) return;
            int gen = pkg.ReadInt();
            if (gen != _gen) return;   // about a list we do not have
            int n = pkg.ReadInt();
            for (int i = 0; i < n && pkg.GetPos() < pkg.Size(); i++)
            {
                int index = pkg.ReadInt();
                var pos = pkg.ReadVector3();
                if (index < 0 || index >= _live.Count) continue;
                var v = _live[index];
                v.Pos = pos;
                // Not null-coalescing: a pin the game has destroyed is only null to its own ==
                // operator, and the map rebuilds its pins whenever a table is read.
                if (v.Pin != null) v.Pin.m_pos = pos;
            }
        }

        internal static void Update()
        {
            if (ZNet.instance == null) return;

            if (IsServer)
            {
                if (StepSweep())
                {
                    _gen++;
                    Send(ZRoutedRpc.Everybody, _live);
                    _redrawWanted = true;   // a host plays on its own server
                }
                // Whatever the sweep is doing, what is already known can still be moving.
                StepLive();
            }
            else if (!_haveList && Time.time >= _nextRequest && ZRoutedRpc.instance != null)
            {
                _nextRequest = Time.time + RequestSeconds;
                ZRoutedRpc.instance.InvokeRoutedRPC(RpcWant);
            }

            // The list itself only changes when the server says so, and both paths above ask for
            // a redraw. What can change under us between frames is whether either kind is wanted:
            // a switch, the master, the find box. Two booleans answer that; comparing the whole
            // list every frame would be building a string per frame for nothing.
            if (_redrawWanted || Wanted(Category.Ship) != _drawnShips || Wanted(Category.Cart) != _drawnCarts) Redraw();
        }

        /// <summary>
        /// Pass on the vessels that have moved, without sweeping for them.
        ///
        /// <para>The sweep is expensive because finding a vessel means walking the world. Asking
        /// a vessel we have already found where it is now does not: it is a dictionary lookup
        /// per vessel, a handful of them, and the answer is live because the player sailing the
        /// ship is the one keeping that object up to date on the server.</para>
        ///
        /// <para>So the sweep stays slow and rare and goes on answering "what is there", while
        /// this answers "where is it" as often as anyone cares. A harbor of moored ships costs
        /// nothing at all: nothing has moved, so nothing is sent.</para>
        /// </summary>
        private static void StepLive()
        {
            if (_live.Count == 0 || Time.time < _nextLive) return;
            _nextLive = Time.time + LiveSeconds;
            var zdoMan = ZDOMan.instance;
            if (zdoMan == null) return;

            List<int> moved = null;
            for (int i = 0; i < _live.Count; i++)
            {
                var v = _live[i];
                if (v.Id == ZDOID.None) continue;
                var zdo = zdoMan.GetZDO(v.Id);
                if (zdo == null || !zdo.IsValid()) continue;   // gone: the next sweep will say so
                var now = zdo.GetPosition();
                if (Vector3.Distance(now, v.Pos) < MovedEnough) continue;
                v.Pos = now;
                if (moved == null) moved = new List<int>();
                moved.Add(i);
            }
            if (moved == null) return;

            if (ZRoutedRpc.instance != null)
            {
                var pkg = new ZPackage();
                pkg.Write(_gen);
                pkg.Write(moved.Count);
                foreach (int i in moved) { pkg.Write(i); pkg.Write(_live[i].Pos); }
                ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, RpcMove, pkg);
            }
            // A host plays on its own server, and its own pins are the ones it is looking at.
            foreach (int i in moved)
            {
                var v = _live[i];
                if (v.Pin != null) v.Pin.m_pos = v.Pos;
            }
        }

        internal static void AskForList()
        {
            if (!IsServer && ZRoutedRpc.instance != null) ZRoutedRpc.instance.InvokeRoutedRPC(RpcWant);
        }

        // ── drawing ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Whether this kind should be on the map at all: its own switch, the master switch, and
        /// the find box, which means "only this" and has to mean it for drawn pins too -- looking
        /// for frost caves with every cart in the world still showing is not looking for one thing.
        /// </summary>
        private static bool Wanted(Category kind) => WhyNot(kind) == null;

        /// <summary>
        /// Why this kind is not on the map, or null when it is. The gates live here rather than in
        /// Wanted so that the answer and the reason can never disagree: a diagnostic that lists
        /// its own copy of the rules eventually explains behavior the code no longer has.
        /// </summary>
        private static string WhyNot(Category kind)
        {
            if (!_haveList) return "no list from the server yet";
            if (TgmConfig.ShowAllMarkers != null && !TgmConfig.ShowAllMarkers.Value)
                return "every marker is hidden (right-click the marker button, or \"Show All Markers\")";
            // A ping outranks the switch, as it does for recorded markers: these come back for
            // the few seconds it lasts and go away again after. Without this a kind switched off
            // had nothing drawn for the pulse to light up, so the ping looked broken rather than
            // obeyed -- and unlike a marker, a vessel that is not drawn does not exist at all.
            if (TgmConfig.ShowKind.TryGetValue(kind, out var show) && !show.Value && !Locate.Pointing(kind))
                return $"\"Show {Categories.Label(kind)}\" is off";
            if (PlayerSpotlight.On) return "the player spotlight is up, which puts the rest of the map away";
            if (Focus.On && !Focus.ShowsKind(kind, IconFor(kind)))
                return $"the map is showing only {Focus.What()} (clear the find box, or tgm_find)";
            return null;
        }

        private static readonly Dictionary<string, string> _labels = new Dictionary<string, string>();

        /// <summary>
        /// What to call a vessel, in the player's own language. A ship is a build piece, so it
        /// carries its own name: vanilla's longship is the prefab "VikingShip" whose piece is
        /// called "$ship_longship", which is why reading the prefab name aloud gave "Viking Ship"
        /// for a thing nobody calls that. A modded hull names itself the same way, so this also
        /// gets "Fast Longship" right without knowing anything about the mod that added it.
        /// Falls back to the tidied prefab name for a vessel that is somehow not a piece.
        /// </summary>
        private static string Label(string prefab)
        {
            if (string.IsNullOrEmpty(prefab)) return "";
            if (_labels.TryGetValue(prefab, out var cached)) return cached;
            string label = Catalog.Prettify(prefab);
            try
            {
                var scene = ZNetScene.instance;
                var go = scene != null ? scene.GetPrefab(prefab) : null;
                var piece = go != null ? go.GetComponent<Piece>() : null;
                if (piece != null && !string.IsNullOrEmpty(piece.m_name))
                {
                    string localized = Localization.instance != null
                        ? Localization.instance.Localize(piece.m_name) : piece.m_name;
                    if (!string.IsNullOrEmpty(localized) && !localized.StartsWith("$")) label = localized;
                }
            }
            catch (System.Exception) { /* a label is not worth an exception */ }
            _labels[prefab] = label;
            return label;
        }

        private static string IconFor(Category kind)
        {
            return TgmConfig.CategoryIcon.TryGetValue(kind, out var icon) && !string.IsNullOrEmpty(icon.Value)
                ? icon.Value : Categories.DefaultIcon(kind);
        }

        internal static void Redraw()
        {
            _redrawWanted = false;
            var map = Minimap.instance;
            if (map == null) return;
            foreach (var v in _live) v.Pin = null;
            foreach (var shown in _drawn) if (Access.Pins(map).Contains(shown.Pin)) map.RemovePin(shown.Pin);
            _drawn.Clear();
            bool ships = _drawnShips = Wanted(Category.Ship);
            bool carts = _drawnCarts = Wanted(Category.Cart);
            if (!ships && !carts) return;

            int shipType = TypeFor(Category.Ship);
            int cartType = TypeFor(Category.Cart);
            foreach (var v in _live)
            {
                if (v.Cart ? !carts : !ships) continue;
                int type = v.Cart ? cartType : shipType;
                if (type <= 0) continue;
                // m_save false: never written to the profile, never shared, and vanilla's own
                // cartography-table data filters on it, so these cannot leak into a table read.
                // Labels off by default: a harbor of six ships writes six names across the coast,
                // and the icon already says what each one is.
                string label = TgmConfig.ShowTransportLabels != null && TgmConfig.ShowTransportLabels.Value
                    ? Label(v.Name) : "";
                var pin = map.AddPin(v.Pos, (Minimap.PinType)type, label, false, false, 0L);
                v.Pin = pin;
                if (pin != null) _drawn.Add(new Shown { Pin = pin, Cart = v.Cart });
            }
        }

        private static int TypeFor(Category kind)
        {
            int type = IconRegistry.TypeFor(IconFor(kind));
            return type > 0 ? type : IconRegistry.TypeFor(Categories.DefaultIcon(kind));
        }

        /// <summary>The map data was rebuilt under us (world load, table read): our pins are gone.</summary>
        internal static void OnPinsCleared()
        {
            foreach (var v in _live) v.Pin = null;
            _drawn.Clear();
            _drawnShips = false;
            _drawnCarts = false;
            _redrawWanted = true;
        }

        /// <summary>One vessel under the pointer: what to call it, what it is, and its prefab.</summary>
        internal sealed class Hovered
        {
            public string Title;
            public string Kind;
            public string Prefab;
        }

        /// <summary>
        /// The vessel nearest a point on the map, within the radius vanilla uses for clicking a
        /// pin, or null. Asked of the list rather than of the pins on the map, because these are
        /// drawn with m_save false and every lookup the game and this mod do over map pins skips
        /// those -- which is the whole reason they cannot be clicked or hovered like a marker.
        /// Only what is actually drawn can be hovered, so a hidden kind answers nothing.
        /// </summary>
        internal static Hovered HoveredAt(Vector3 world, float radius)
        {
            if (!_haveList || radius <= 0f) return null;
            Live best = null;
            float bestDistance = float.MaxValue;
            foreach (var v in _live)
            {
                if (v.Cart ? !_drawnCarts : !_drawnShips) continue;
                float d = Geo.FlatDistance(v.Pos, world);
                if (d < radius && d < bestDistance) { best = v; bestDistance = d; }
            }
            if (best == null) return null;
            return new Hovered
            {
                Title = Label(best.Name),
                Kind = Categories.Label(best.Cart ? Category.Cart : Category.Ship).TrimEnd('s'),
                Prefab = best.Name ?? "",
            };
        }

        /// <summary>What the mod knows about vessels, for tgm_status and the diagnostics.</summary>
        internal static List<string> Describe()
        {
            var lines = new List<string>();
            var vessels = Vessels();
            lines.Add($"Vessel prefabs watched: {Names(vessels)}");
            if (!_haveList) lines.Add("No list from the server yet.");
            else
            {
                int ships = 0, carts = 0;
                foreach (var v in _live) { if (v.Cart) carts++; else ships++; }
                lines.Add($"{ships} ship(s) and {carts} cart(s) in the world; {_drawn.Count} pin(s) drawn.");
                // Which prefabs actually turned up, so "no ships" can be read as the world
                // having none rather than the sweep having missed them.
                var counts = new Dictionary<string, int>();
                foreach (var v in _live)
                {
                    string name = string.IsNullOrEmpty(v.Name) ? "?" : v.Name;
                    counts.TryGetValue(name, out int n);
                    counts[name] = n + 1;
                }
                foreach (var kv in counts) lines.Add($"  {Label(kv.Key)} ({kv.Key}): {kv.Value}");
                foreach (var vessel in vessels)
                    if (!counts.ContainsKey(vessel.Prefab))
                        lines.Add($"  {Label(vessel.Prefab)} ({vessel.Prefab}): none in the world");
            }
            foreach (var kind in new[] { Category.Ship, Category.Cart })
            {
                string why = WhyNot(kind);
                lines.Add($"{Categories.Label(kind)}: " + (why == null ? "shown" : "not shown -- " + why));
            }
            if (IsServer)
            {
                lines.Add($"Sweeping every {SweepSeconds:0.#} s" + (_sweep != null ? $", one in progress (prefab {_sweep.Prefab + 1} of {vessels.Count})" : ""));
                int tracked = 0;
                foreach (var v in _live) if (v.Id != ZDOID.None) tracked++;
                lines.Add($"Passing on movement every {LiveSeconds:0.00} s for {tracked} of {_live.Count} vessel(s), " +
                          $"once they have moved {MovedEnough:0.#} m; list generation {_gen}");
            }
            else lines.Add($"List generation {_gen}" + (_haveList ? "" : " (none yet)"));
            return lines;
        }
    }

    [HarmonyPatch(typeof(Game), "Start")]
    internal static class Game_Start_Transports_Patch
    {
        private static void Postfix()
        {
            Transports.Reset();
            Transports.Register();
        }
    }
}
