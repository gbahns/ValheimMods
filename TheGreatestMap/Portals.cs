using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace TheGreatestMap
{
    /// <summary>
    /// Portals are the one thing on the map that is not a memory. Everything else this mod
    /// records stays where you found it, so a marker written once stays true. A portal is
    /// player-built: it gets renamed, torn down and rebuilt, and a marker written once goes
    /// wrong within days. The game already keeps an authoritative list of every portal in the
    /// world, so the server reads that and tells the clients, and each client then:
    ///
    ///  * corrects the portal markers it already holds, renaming them and erasing the ones whose
    ///    portal is gone, so a known portal is never stale; and
    ///  * with "Map All Portals" on, draws every other current portal as well. Those extra pins
    ///    are drawn only, never written to your map and never shared, so switching the setting
    ///    off leaves nothing behind and nobody else inherits them.
    ///
    /// The correction runs whoever built the portal. The extra drawing is what turns the honest
    /// "only what someone saw" rule off, which is why it is a setting.
    /// </summary>
    internal static class Portals
    {
        private const string RpcList = "TGM_Portals";      // server -> everybody: every portal now
        private const string RpcWant = "TGM_PortalsWant";  // client -> server: send me that list

        /// <summary>How close a marker must be to a portal to be that portal. Portals never move.</summary>
        private const float MatchRadius = 4f;
        private const float ServerScanSeconds = 5f;
        private const float RequestSeconds = 10f;

        private sealed class Live
        {
            public Vector3 Pos;
            public string Name = "";
        }

        private static readonly List<Live> _live = new List<Live>();
        private static readonly List<Minimap.PinData> _drawn = new List<Minimap.PinData>();
        private static bool _haveList;
        private static string _lastServerSignature = "";
        private static float _nextScan;
        private static float _nextRequest;
        private static bool _redrawWanted;
        private static bool _drawnWanted;

        internal static bool HaveList => _haveList;
        internal static int Count => _live.Count;

        internal static void Reset()
        {
            _live.Clear();
            _drawn.Clear();
            _haveList = false;
            _lastServerSignature = "";
            _nextScan = 0f;
            _nextRequest = 0f;
            _redrawWanted = false;
        }

        internal static void Register()
        {
            var rpc = ZRoutedRpc.instance;
            if (rpc == null) return;
            rpc.Register<ZPackage>(RpcList, RPC_List);
            rpc.Register(RpcWant, RPC_Want);
        }

        // ── server ──────────────────────────────────────────────────────────────────

        private static bool IsServer => ZNet.instance != null && ZNet.instance.IsServer();

        /// <summary>Every portal the world holds, from the game's own list.</summary>
        private static List<Live> Scan()
        {
            var list = new List<Live>();
            var zdoMan = ZDOMan.instance;
            if (zdoMan == null) return list;
            foreach (var zdo in zdoMan.GetPortalList())
            {
                if (zdo == null || !zdo.IsValid()) continue;
                list.Add(new Live { Pos = zdo.GetPosition(), Name = zdo.GetString(ZDOVars.s_tag, "") ?? "" });
            }
            return list;
        }

        private static string Signature(List<Live> list)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var p in list)
                sb.Append(Mathf.RoundToInt(p.Pos.x)).Append(',').Append(Mathf.RoundToInt(p.Pos.z)).Append('=').Append(p.Name).Append(';');
            return sb.ToString();
        }

        private static void Send(long target, List<Live> list)
        {
            if (ZRoutedRpc.instance == null) return;
            var pkg = new ZPackage();
            pkg.Write(list.Count);
            foreach (var p in list)
            {
                pkg.Write(p.Pos);
                pkg.Write(p.Name ?? "");
            }
            ZRoutedRpc.instance.InvokeRoutedRPC(target, RpcList, pkg);
        }

        private static void RPC_Want(long sender)
        {
            if (!IsServer) return;
            Send(sender, Scan());
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
                _live.Add(new Live { Pos = pos, Name = pkg.ReadString() });
            }
            _haveList = true;
            _redrawWanted = true;
            Reconcile();
        }

        internal static void Update()
        {
            if (ZNet.instance == null) return;

            if (IsServer && Time.time >= _nextScan)
            {
                _nextScan = Time.time + ServerScanSeconds;
                var list = Scan();
                string signature = Signature(list);
                if (signature != _lastServerSignature)
                {
                    _lastServerSignature = signature;
                    Send(ZRoutedRpc.Everybody, list);
                }
            }
            else if (!IsServer && !_haveList && Time.time >= _nextRequest && ZRoutedRpc.instance != null)
            {
                _nextRequest = Time.time + RequestSeconds;
                ZRoutedRpc.instance.InvokeRoutedRPC(RpcWant);
            }

            if (_redrawWanted || DrawingWanted() != _drawnWanted) Redraw();
        }

        /// <summary>
        /// Bring the portal markers already on this map into line with the world: rename the ones
        /// whose portal was renamed, erase the ones whose portal is gone. Only markers this mod
        /// recorded are touched, never a pin somebody placed by hand with the portal icon, and the
        /// erasure travels like any other, so a stale marker goes away for everyone.
        /// </summary>
        private static void Reconcile()
        {
            if (!_haveList || !PersonalMap.Loaded) return;
            var stale = new List<string>();
            var renamed = new List<KeyValuePair<string, string>>();
            foreach (var pin in ClientPins.All)
            {
                if (!pin.Auto || ClientPins.KindOf(pin) != Category.Portal) continue;
                var live = Nearest(pin.Pos);
                if (live == null) { stale.Add(pin.Id); continue; }
                string name = string.IsNullOrEmpty(live.Name) ? "Portal" : live.Name;
                if (pin.Name != name) renamed.Add(new KeyValuePair<string, string>(pin.Id, name));
            }
            foreach (var kv in renamed) ClientPins.RenameMarker(kv.Key, kv.Value);
            foreach (var id in stale) ClientPins.RemoveStale(id);
            if (stale.Count > 0 || renamed.Count > 0)
            {
                TheGreatestMapMod.Log.LogInfo($"[TheGreatestMap] Portals: renamed {renamed.Count} marker(s), erased {stale.Count} that no longer exist.");
                _redrawWanted = true;
            }
        }

        private static Live Nearest(Vector3 pos)
        {
            Live best = null;
            float bestDistance = MatchRadius;
            foreach (var p in _live)
            {
                float d = Geo.FlatDistance(p.Pos, pos);
                if (d <= bestDistance) { bestDistance = d; best = p; }
            }
            return best;
        }

        // ── the extra, draw-only pins ───────────────────────────────────────────────

        private static bool DrawingWanted()
        {
            return _haveList
                && TgmConfig.MapAllPortals != null && TgmConfig.MapAllPortals.Value
                && (TgmConfig.ShowAllMarkers == null || TgmConfig.ShowAllMarkers.Value)
                && !PortalPickerOpen();
        }

        internal static void Redraw()
        {
            _redrawWanted = false;
            var map = Minimap.instance;
            if (map == null) return;
            foreach (var pin in _drawn) if (Access.Pins(map).Contains(pin)) map.RemovePin(pin);
            _drawn.Clear();
            _drawnWanted = DrawingWanted();
            if (!_drawnWanted) return;

            int type = IconRegistry.TypeFor(TgmConfig.CategoryIcon.TryGetValue(Category.Portal, out var icon)
                ? icon.Value : Categories.DefaultIcon(Category.Portal));
            foreach (var p in _live)
            {
                if (ClientPins.HasOwnPinNear(null, p.Pos, MatchRadius)) continue; // already a marker of ours
                // m_save false: never written to the profile, never shared, gone when the setting is.
                var pin = map.AddPin(p.Pos, (Minimap.PinType)type, string.IsNullOrEmpty(p.Name) ? "Portal" : p.Name, false, false, 0L);
                if (pin != null) _drawn.Add(pin);
            }
        }

        /// <summary>The map data was rebuilt under us (world load, table read): our pins are gone.</summary>
        internal static void OnPinsCleared()
        {
            _drawn.Clear();
            _drawnWanted = false;
            _redrawWanted = true;
        }

        /// <summary>
        /// Tint these the same pale gold as recorded markers, so every portal this mod draws
        /// looks alike whether you have been to it or not. Called from the styling pass, after
        /// vanilla has built the icons.
        /// </summary>
        internal static void Style(Color tint)
        {
            foreach (var pin in _drawn)
                if (pin != null && pin.m_iconElement != null) pin.m_iconElement.color = tint;
        }

        // ── TheGreatestPortal, if it happens to be installed ────────────────────────

        private static PropertyInfo _pickerOpen;
        private static bool _probed;

        /// <summary>
        /// True while TheGreatestPortal is showing its own portal picker over the map. That
        /// overlay wants a clean map and hides the saved pins itself, so this mod stands down
        /// rather than drawing a second set of portals over its first. Found by name, so there is
        /// no reference to that mod and no need for it to be installed.
        /// </summary>
        internal static bool PortalPickerOpen()
        {
            if (!_probed)
            {
                _probed = true;
                var type = AccessTools.TypeByName("TheGreatestPortal.MapPicker");
                if (type != null) _pickerOpen = AccessTools.Property(type, "IsSelecting");
            }
            if (_pickerOpen == null) return false;
            try { return (bool)_pickerOpen.GetValue(null); }
            catch { return false; }
        }
    }

    [HarmonyPatch(typeof(Game), "Start")]
    internal static class Game_Start_Portals_Patch
    {
        private static void Postfix()
        {
            Portals.Reset();
            Portals.Register();
        }
    }
}
