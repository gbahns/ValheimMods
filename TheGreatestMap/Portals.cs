using System;
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
    /// <summary>
    /// How a portal marker is drawn. Answers to one question -- is this marker for finding a
    /// portal, or for telling two portals apart -- and the player's map is theirs to answer it
    /// on. See the Portal Markers Look Like setting.
    ///
    /// <para>The five that carry a picture differ only in where the color goes, which is a thing
    /// to be looked at rather than argued about: they are here to be compared on a real map.</para>
    /// </summary>
    internal enum PortalLook
    {
        /// <summary>The plain portal pin, painted the portal color. Loud, and says nothing more.</summary>
        ColoredPins,

        /// <summary>The game's own render of that portal, in its own colors. Says which, quietly.</summary>
        Pictures,

        /// <summary>The render on a soft glow of the portal color.</summary>
        PictureOnHalo,

        /// <summary>The render on a solid coin of the portal color.</summary>
        PictureOnDisc,

        /// <summary>The render untouched, inside a band of the portal color.</summary>
        PictureInRing,

        /// <summary>The render with the portal color traced around its own outline.</summary>
        OutlinedPicture,

        /// <summary>That contour over a glow: the most color a picture can carry.</summary>
        OutlinedOnHalo,
    }

    internal static class Portals
    {
        private const string RpcList = "TGM_Portals";      // server -> everybody: every portal now
        private const string RpcWant = "TGM_PortalsWant";  // client -> server: send me that list

        /// <summary>How close a marker must be to a portal to be that portal. Portals never move.</summary>
        private const float MatchRadius = 4f;
        private const float ServerScanSeconds = 2f;
        private const float RequestSeconds = 10f;

        private sealed class Live
        {
            public Vector3 Pos;
            public string Name = "";
            /// <summary>
            /// Which portal it is, by prefab name.
            ///
            /// <para>This was a bool -- "does it take ore" -- for exactly one unreleased
            /// version. The prefab name answers that question and several better ones: a portal
            /// can wear the game's own render of itself, a modded portal looks like itself with
            /// nothing here naming it, and TheGreatestPortal keys on the same thing, so the two
            /// mods cannot disagree about a portal they are both looking at. Empty when the
            /// server is older than 4.12.0, or has a portal this client does not have.</para>
            /// </summary>
            public string Prefab = "";
        }

        private static readonly List<Live> _live = new List<Live>();
        private static readonly List<Minimap.PinData> _drawn = new List<Minimap.PinData>();
        private static bool _haveList;
        private static string _lastServerSignature = "";
        private static float _nextScan;
        private static float _nextRequest;
        private static bool _redrawWanted;

        /// <summary>The look and the two colors as they stood, to notice any of them moving.</summary>
        private static string _lastLook;

        /// <summary>The two colors as they stood, to tell a recolor from a change of look.</summary>
        private static string _lastColors;
        private static bool _drawnWanted;
        private static bool _reconcileWanted;

        internal static bool HaveList => _haveList;

        /// <summary>The pins drawn from the live list, for anything that works on what is on screen.</summary>
        internal static IEnumerable<Minimap.PinData> DrawnPins => _drawn;

        /// <summary>Whether this pin is one of ours, for anything sorting pins by what they are.</summary>
        internal static bool IsPortalPin(Minimap.PinData pin)
        {
            if (pin == null || _drawn.Count == 0) return false;
            for (int i = 0; i < _drawn.Count; i++) if (ReferenceEquals(_drawn[i], pin)) return true;
            return false;
        }
        internal static int Count => _live.Count;

        internal static void Reset()
        {
            _live.Clear();
            _drawn.Clear();
            _haveList = false;
            _reconcileWanted = false;
            _lastServerSignature = "";
            _nextScan = 0f;
            _nextRequest = 0f;
            _redrawWanted = false;
        }

        internal static void Register()
        {
            var rpc = ZRoutedRpc.instance;
            if (rpc == null) return;
            Rpc.Register<ZPackage>(rpc, RpcList, RPC_List);
            Rpc.Register(rpc, RpcWant, RPC_Want);
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
                list.Add(new Live
                {
                    Pos = zdo.GetPosition(),
                    Name = zdo.GetString(ZDOVars.s_tag, "") ?? "",
                    Prefab = PrefabName(zdo),
                });
            }
            return list;
        }

        /// <summary>Which portal this is, by prefab name, or empty if the world cannot say.</summary>
        private static string PrefabName(ZDO zdo)
        {
            var scene = ZNetScene.instance;
            if (scene == null || zdo == null) return "";
            var prefab = scene.GetPrefab(zdo.GetPrefab());
            return prefab != null ? prefab.name : "";
        }

        /// <summary>
        /// Whether this portal takes everything through: the fallback picture's question, now
        /// that the picture itself comes from the prefab.
        ///
        /// <para>m_allowAllItems is the question a portal on a map is actually asked -- "can I
        /// bring the copper" -- so a modded ore portal with no build render still falls back to
        /// the ore picture rather than the plain one, without this mod having heard of it. Public
        /// on TeleportWorld, so nothing is publicized and nothing is reflected.</para>
        /// </summary>
        internal static bool CarriesEverything(string prefabName)
        {
            var scene = ZNetScene.instance;
            if (scene == null || string.IsNullOrEmpty(prefabName)) return false;
            var prefab = scene.GetPrefab(prefabName);
            if (prefab == null) return false;
            var teleport = prefab.GetComponent<TeleportWorld>();
            return teleport != null && teleport.m_allowAllItems;
        }

        private static string Signature(List<Live> list)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var p in list)
                sb.Append(Mathf.RoundToInt(p.Pos.x)).Append(',').Append(Mathf.RoundToInt(p.Pos.z))
                  .Append('=').Append(p.Name).Append('/').Append(p.Prefab).Append(';');
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
            // After every record, not a third field inside each one. A reader is a cursor walking
            // the bytes in order, so a field written between two records moves everything after
            // it and an older client would read a portal's position out of the middle of a name
            // -- not a missing feature but a scrambled list. Written past the end, an older
            // client reads the count it understands, stops, and never looks at these; it draws
            // every portal with the plain picture, which is exactly what it did before. See the
            // DWAMS rule: read every version you ever wrote, enforce none of them.
            foreach (var p in list) pkg.Write(p.Prefab ?? "");
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
                string name = pkg.ReadString();
                _live.Add(new Live { Pos = pos, Name = name });
            }
            // Which portal each one is, if this server is new enough to have said. One that is
            // not leaves them empty and every portal draws with the plain picture, which is the
            // honest answer: that server never told us otherwise.
            for (int i = 0; i < n && pkg.GetPos() < pkg.Size(); i++) _live[i].Prefab = pkg.ReadString();
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

            if (_reconcileWanted && PersonalMap.Loaded) Reconcile();
            // A halo is baked in the color the setting held when it was baked, so a color moved
            // in the config has to be baked again -- and a different look means different
            // pictures altogether. Watched here rather than through SettingChanged, which can
            // arrive on the thread that reads the config file.
            string colors = MarkerColor() + "|" + StoneColor();
            string look = Look + "|" + colors;
            if (look != _lastLook)
            {
                bool first = _lastLook == null;
                bool recolored = !first && colors != _lastColors;
                _lastLook = look;
                _lastColors = colors;
                // Only a color change makes a baked picture wrong. Picking a different look asks
                // for different pictures under different keys, which are baked the first time
                // they are wanted -- so rebaking here would redo nothing but the ones being left
                // behind, and the dearest of those traces a contour around every pixel of a
                // render. Somebody comparing the looks pays that on every step through the list.
                if (recolored) IconRegistry.Rebake("glow:");
                if (!first) _redrawWanted = true;
            }
            if (_redrawWanted || DrawingWanted() != _drawnWanted) Redraw();
        }

        /// <summary>
        /// Clear away the recorded portal markers, which nothing writes any more.
        ///
        /// Until 4.1.0 portals were recorded like discoveries and then endlessly corrected to
        /// follow the world. Now they are drawn from the server's list and nothing is stored, so
        /// the markers left on old maps are just a second portal icon sitting under the live one.
        /// They go, once, and the erasure travels like any other so nobody has to do it twice.
        ///
        /// Only markers this mod recorded are touched, never a pin somebody placed by hand with
        /// the portal icon -- those are a note a player wrote and are nobody else's to remove.
        /// </summary>
        private static void Reconcile()
        {
            // The list can arrive before the character's map has loaded, and the server only sends
            // another when something changes, so a stale marker would have waited for an unrelated
            // portal to be built or removed. Remember that the work is owed and do it on load.
            if (!_haveList) return;
            if (!PersonalMap.Loaded) { _reconcileWanted = true; return; }
            _reconcileWanted = false;
            var stored = new List<PinKey>();
            foreach (var pin in ClientPins.All)
                if (pin.Auto && ClientPins.KindOf(pin) == Category.Portal) stored.Add(pin.Key);
            if (stored.Count == 0) return;
            // Erased, not suppressed: a suppression says do not record here again, and nothing
            // records portals any more -- the spot is not being refused, the record is retired.
            foreach (var key in stored) ClientPins.RemoveStale(key);
            TheGreatestMapMod.Log.LogInfo(
                $"[TheGreatestMap] Portals are drawn from the server's own list now, so the {stored.Count} " +
                "recorded portal marker(s) on this map have been retired. The portals themselves are still " +
                "shown, and stay right even when one is renamed or rebuilt.");
            _redrawWanted = true;
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

        /// <summary>
        /// Whether to draw them at all. "Show Portals", with the other dynamic markers -- the
        /// same switch ships and carts answer to. It used to be "Map All Portals", which asked
        /// whether to draw the portals nobody had recorded; that question stopped meaning
        /// anything when none of them are recorded.
        ///
        /// A ping outranks the switch, as it does for vessels: a portal that is not drawn does
        /// not exist to pulse, so without this a ping would look broken rather than obeyed.
        /// </summary>
        private static bool DrawingWanted()
        {
            if (!_haveList) return false;
            if (TgmConfig.ShowAllMarkers != null && !TgmConfig.ShowAllMarkers.Value) return false;
            if (TgmConfig.ShowKind.TryGetValue(Category.Portal, out var show)
                && !show.Value && !Locate.Pointing(Category.Portal)) return false;
            return !PortalPickerOpen();
        }

        /// <summary>
        /// The picture for one portal: the game's own render of it where there is one.
        ///
        /// <para>A portal is a thing the player built and has looked at from the build menu a
        /// hundred times, so the build menu's render of it is the one picture that needs no
        /// explaining -- and it tells the wood and the stone one apart by itself, without any
        /// rule about which is which. A modded portal looks like itself for free.</para>
        ///
        /// <para>The configured pictures are the fallback, for a portal nobody can build and
        /// which therefore has no render: those are sorted by whether they take ore through,
        /// which is the only thing worth knowing about a portal you cannot picture.</para>
        /// </summary>
        internal static string IconKey(string prefabName)
        {
            bool stone = CarriesEverything(prefabName);
            bool pictured = !string.IsNullOrEmpty(prefabName) && HasBuildPicture(prefabName);
            switch (Look)
            {
                case PortalLook.ColoredPins: return Fallback(stone);
                case PortalLook.Pictures: return pictured ? "piece:" + prefabName : Fallback(stone);
                default: return pictured ? "glow:" + Style(Look) + ":" + prefabName : Fallback(stone);
            }
        }

        /// <summary>What the player asked portal markers to look like.</summary>
        internal static PortalLook Look =>
            TgmConfig.PortalLook != null ? TgmConfig.PortalLook.Value : PortalLook.PictureOnHalo;

        /// <summary>Which way of dressing a render this look asks for.</summary>
        private static string Style(PortalLook look)
        {
            switch (look)
            {
                case PortalLook.PictureOnDisc: return "disc";
                case PortalLook.PictureInRing: return "ring";
                case PortalLook.OutlinedPicture: return "edge";
                case PortalLook.OutlinedOnHalo: return "edgehalo";
                default: return "halo";
            }
        }

        /// <summary>
        /// The color baked behind one portal's picture: its own, by whether it takes ore through.
        /// The steady color rather than the pulse -- a baked halo cannot breathe, and what does
        /// the breathing in the picture modes is the brightness of the whole icon.
        /// </summary>
        internal static Color GlowColor(string prefabName) =>
            CarriesEverything(prefabName) ? StoneColor() : MarkerColor();

        /// <summary>The color for a portal that takes ore and metal through.</summary>
        internal static Color StoneColor() =>
            TgmConfig.StonePortalColor != null ? TgmConfig.StonePortalColor.Value : DefaultStoneColor;

        private static readonly Color DefaultStoneColor = new Color(0f, 0.706f, 1f, 1f); // #00B4FF

        /// <summary>The configured picture for a portal with no render of its own.</summary>
        internal static string Fallback(bool stone)
        {
            if (stone)
                return TgmConfig.StonePortalIcon != null && !string.IsNullOrEmpty(TgmConfig.StonePortalIcon.Value)
                    ? TgmConfig.StonePortalIcon.Value : StoneIconDefault;
            return TgmConfig.CategoryIcon.TryGetValue(Category.Portal, out var icon) && !string.IsNullOrEmpty(icon.Value)
                ? icon.Value : Categories.DefaultIcon(Category.Portal);
        }

        private static bool HasBuildPicture(string prefabName)
        {
            var scene = ZNetScene.instance;
            if (scene == null) return false;
            var prefab = scene.GetPrefab(prefabName);
            var piece = prefab != null ? prefab.GetComponent<Piece>() : null;
            return piece != null && piece.m_icon != null;
        }

        /// <summary>
        /// The picture an ore-carrying portal wears, for TheGreatestPortal to draw the same one.
        ///
        /// <para>That mod marks portals itself whenever its own map is up, and this mod's portal
        /// pins stand down while it is -- so for any given player exactly one of the two is
        /// drawing. Two different glyphs would mean a stone portal looked like one thing on one
        /// map and another thing on the other, which is the sort of inconsistency nobody can
        /// debug from the inside. It reads this afresh every time rather than caching, so being
        /// asked before the pictures exist costs nothing but a null and an honest fallback to
        /// its own glyph.</para>
        ///
        /// <para>Called by reflection, by name, from a mod this one does not reference. Keep the
        /// name and the shape: static, no arguments, a Sprite or null.</para>
        /// </summary>
        internal static Sprite StoneIcon() => IconRegistry.Resolve(Minimap.instance, Fallback(true));

        internal const string StoneIconDefault = "drawn:stoneportal";

        internal static void Redraw()
        {
            _redrawWanted = false;
            var map = Minimap.instance;
            if (map == null) return;
            foreach (var pin in _drawn) if (Access.Pins(map).Contains(pin)) map.RemovePin(pin);
            _drawn.Clear();
            _drawnWanted = DrawingWanted();
            if (!_drawnWanted) return;

            _unpainted.Clear();
            _drawnStone.Clear();
            _portalIcons.Clear();
            _portalIcons.Add(Fallback(false));
            _portalIcons.Add(Fallback(true));
            foreach (var p in _live) _portalIcons.Add(IconKey(p.Prefab));
            foreach (var p in _live)
            {
                string iconKey = IconKey(p.Prefab);
                int type = IconRegistry.TypeFor(iconKey);
                if (AlreadyMarked(p.Pos)) continue;
                // m_save false: never written to the profile, never shared, gone when the setting is.
                //
                // The name is withheld at birth when labels are off, because that is the only
                // moment it can be: vanilla writes a label for a pin that was given a name at
                // AddPin and never revisits it. The pin still carries the name afterwards, so
                // hovering it still says which portal this is. Portals are the labels people
                // most want gone -- a base with six of them writes six words over itself.
                string name = string.IsNullOrEmpty(p.Name) ? "Portal" : p.Name;
                bool label = TgmConfig.ShowPortalLabels == null || TgmConfig.ShowPortalLabels.Value;
                var pin = map.AddPin(p.Pos, (Minimap.PinType)type, label ? name : "", false, false, 0L);
                if (pin == null) continue;
                pin.m_name = name;
                _drawn.Add(pin);
                // A render arrives in its own colors and its own light. Painting is a multiply,
                // so the portal color would take the picture away; remembered here so Paint
                // leaves it be.
                if (IconRegistry.CarriesItsOwnColor(iconKey)) _unpainted.Add(pin);
                if (CarriesEverything(p.Prefab)) _drawnStone.Add(pin);
            }
            BringToFront();
        }

        /// <summary>
        /// Put the portal pins above everything drawn in the same place.
        ///
        /// A portal is the one marker you are looking for when you need it, and it is also the
        /// one most likely to be buried: portals stand in bases, and a base is where the berries,
        /// the campfires and the workbenches all are. Later siblings draw on top, so the portals
        /// go last.
        ///
        /// Guarded by a single check, because the cost is not the call. Moving a child to the end
        /// shifts every sibling after it, and a full map has thousands of pins in that parent --
        /// doing it unconditionally on every layout, and layout runs on every map move, is the
        /// shape of problem that once stalled this client into dropped connections. If the last
        /// portal is already last, there is nothing out of order and nothing to do.
        /// </summary>
        internal static void BringToFront()
        {
            if (_drawn.Count == 0) return;
            var last = _drawn[_drawn.Count - 1].m_uiElement;
            if (last == null || last.parent == null) return;
            if (last.GetSiblingIndex() == last.parent.childCount - 1) return;
            foreach (var pin in _drawn)
                if (pin != null && pin.m_uiElement != null) pin.m_uiElement.SetAsLastSibling();
        }

        /// <summary>
        /// This portal already has a marker, so an extra pin would sit on top of one. It has to be
        /// a portal that is marked and not merely something at the same spot: a base puts a
        /// campfire, a bush and a portal within a few meters of each other, and any marker at all
        /// counting as this portal's was leaving a newly raised portal with nothing drawn until
        /// the pocket map next came out. A marker somebody placed by hand carries no kind, so
        /// those are matched by the portal icon instead.
        /// </summary>
        private static bool AlreadyMarked(Vector3 pos)
        {
            foreach (var pin in ClientPins.All)
            {
                if (Geo.FlatDistance(pin.Pos, pos) > MatchRadius) continue;
                if (ClientPins.KindOf(pin) == Category.Portal) return true;
                // Any portal picture. Somebody who wrote down "portal here" by hand has said
                // what they meant to say whichever picture they reached for, and drawing a live
                // pin on top of their note because they picked a different one would be this mod
                // arguing with the player about what a portal looks like.
                if (pin.Auto) continue;
                string worn = ClientPins.IconOf(pin);
                foreach (var key in _portalIcons)
                    if (IconRegistry.SameKey(worn, key)) return true;
            }
            return false;
        }

        // The pictures portals are wearing right now, for recognizing a hand-written note about
        // one, and the live pins that must not be painted.
        private static readonly HashSet<string> _portalIcons = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<Minimap.PinData> _unpainted = new HashSet<Minimap.PinData>();

        /// <summary>The drawn pins for portals that take ore through, which wear their own color.</summary>
        private static readonly HashSet<Minimap.PinData> _drawnStone = new HashSet<Minimap.PinData>();

        /// <summary>The map data was rebuilt under us (world load, table read): our pins are gone.</summary>
        internal static void OnPinsCleared()
        {
            _drawn.Clear();
            _unpainted.Clear();
            _drawnStone.Clear();
            _drawnWanted = false;
            _redrawWanted = true;
        }

        // ── color ───────────────────────────────────────────────────────────────────

        private static readonly Color DefaultColor = new Color(1f, 0.4f, 0f, 1f); // #FF6600

        /// <summary>
        /// Portal markers get their own color, because they are the points you travel between and
        /// are worth finding at a glance. With a pulse color set they fade back and forth, which
        /// picks them out of a map full of gold plants.
        /// </summary>
        private static Color CurrentColor()
        {
            var color = MarkerColor();
            var pulse = TgmConfig.PortalPulseColor != null ? TgmConfig.PortalPulseColor.Value : color;
            return Color.Lerp(color, pulse, Pulse());
        }

        /// <summary>
        /// Where in the breath we are: 0 at the marker's own color, 1 at the pulse color, and 0
        /// throughout when the player has asked for no pulse.
        /// </summary>
        private static float Pulse()
        {
            // A fully transparent pulse color is how the setting says "do not pulse"; it used to
            // say it with an empty string, which a color picker has no way to type.
            var pulse = TgmConfig.PortalPulseColor != null ? TgmConfig.PortalPulseColor.Value : new Color(0f, 0f, 0f, 0f);
            float period = TgmConfig.PortalPulseSeconds != null ? TgmConfig.PortalPulseSeconds.Value : 0f;
            if (pulse.a <= 0f || period <= 0.01f) return 0f;
            // Unscaled, so the fade carries on while the map is open with the game paused.
            return (Mathf.Sin(Time.unscaledTime * 2f * Mathf.PI / period) + 1f) * 0.5f;
        }

        /// <summary>
        /// The ore-carrying color, breathing. There is no second pulse color to configure and
        /// there should not be: the pair that reads as a pulse is two shades of one color, which
        /// is a thing to work out rather than to ask for, and the config already asks for plenty.
        /// </summary>
        private static Color StonePulse()
        {
            var color = StoneColor();
            var lighter = Color.Lerp(color, Color.white, 0.22f);
            lighter.a = color.a;
            return Color.Lerp(color, lighter, Pulse());
        }

        /// <summary>
        /// A picture breathing rather than a color: the whole icon dimmed and brought back.
        ///
        /// <para>Paint is a multiply, so a render cannot be pulsed in color -- a multiply can
        /// only darken, and anything but white takes the picture's own colors with it. Pulsing
        /// how dark it is costs the picture nothing and still catches the eye, which is what the
        /// pulse is for.</para>
        /// </summary>
        private static Color PicturePulse()
        {
            const float dim = 0.74f;
            float t = Pulse() * (1f - dim);
            return new Color(1f - t, 1f - t, 1f - t, 1f);
        }

        /// <summary>
        /// The color portal markers are drawn in, for something that wants to match them -- the
        /// row in the marker list, whose picture is the one thing in that list that is not gold.
        /// The steady color rather than the pulse: a row that breathed would pull the eye away
        /// from the names, which is the opposite of what the list is for.
        /// </summary>
        internal static Color MarkerColor() =>
            TgmConfig.PortalColor != null ? TgmConfig.PortalColor.Value : DefaultColor;

        /// <summary>
        /// Paint every portal marker, the ones on your map and the draw-only ones alike, so they
        /// all look the same whether you have been to that portal or not. Runs every frame rather
        /// than in the styling pass, because the styling pass only runs when the map is laid out
        /// again and a fade has to keep moving between those.
        /// </summary>
        internal static void Paint()
        {
            if (Minimap.instance == null) return;
            var color = CurrentColor();
            ClientPins.TintPortals(color);
            var stone = StonePulse();
            var picture = PicturePulse();
            foreach (var pin in _drawn)
            {
                if (pin == null || pin.m_iconElement == null) continue;
                // A picture keeps its own colors and breathes in brightness instead; a plain pin
                // is painted the color of the sort of portal it is.
                pin.m_iconElement.color = _unpainted.Contains(pin) ? picture
                    : _drawnStone.Contains(pin) ? stone : color;
            }
        }

        /// <summary>
        /// A portal was just taken down in front of this player. The server notices within a couple
        /// of seconds and tells everyone, but the person swinging the hammer should not watch a
        /// marker for something they just demolished, so their own map is corrected at once.
        /// </summary>
        /// <summary>What the portal system currently believes, for tgm_portals.</summary>
        internal static List<string> Describe(Vector3 near)
        {
            var lines = new List<string>
            {
                $"have a list from the server: {_haveList} ({_live.Count} portals)",
                $"this is the server: {IsServer}; next sweep in {Mathf.Max(0f, _nextScan - Time.time):0.0}s",
                $"Show Portals: {(!TgmConfig.ShowKind.TryGetValue(Category.Portal, out var showCfg) || showCfg.Value)}, " +
                $"Show Markers: {(TgmConfig.ShowAllMarkers == null || TgmConfig.ShowAllMarkers.Value)}, " +
                $"picker open: {PortalPickerOpen()}",
                $"drawing extra pins: wanted {DrawingWanted()}, currently drawn {_drawn.Count}",
            };
            int named = 0;
            foreach (var p in _live) if (!string.IsNullOrEmpty(p.Prefab)) named++;
            lines.Add($"portals the server named: {named} of {_live.Count}" +
                      (_haveList && named == 0 && _live.Count > 0
                          ? " -- a server older than 4.12.0 never says, so all of them wear " + Fallback(false)
                          : "") +
                      $"; unpainted renders drawn: {_unpainted.Count}");
            lines.Add($"fallback pictures: {Fallback(false)} plain, {Fallback(true)} for ore-carrying");
            lines.Add($"look: {Look}; colors {Colors.ToHex(MarkerColor())} plain, {Colors.ToHex(StoneColor())} " +
                      $"ore-carrying; {_drawnStone.Count} of {_drawn.Count} drawn pins are ore-carrying");
            var live = Nearest(near);
            lines.Add(live != null
                ? $"nearest portal in the list: '{live.Name}' [{(string.IsNullOrEmpty(live.Prefab) ? "unnamed" : live.Prefab)}" +
                  $" -> {IconKey(live.Prefab)}] {Geo.FlatDistance(live.Pos, near):0.#} m away"
                : "no portal in the list within 4 m of you");
            lines.Add($"a portal marker within 4 m: {AlreadyMarked(near)} (one there means no extra pin is drawn)");
            return lines;
        }

        /// <summary>Ask the server for the portal list now rather than waiting for its next sweep.</summary>
        internal static void AskForList()
        {
            if (!IsServer && ZRoutedRpc.instance != null) ZRoutedRpc.instance.InvokeRoutedRPC(RpcWant);
            else if (IsServer) _nextScan = 0f;
        }

        /// <summary>A portal was just raised: have the list refreshed so it appears without delay.</summary>
        internal static void NoteBuilt(Vector3 pos)
        {
            _redrawWanted = true;
            AskForList();
        }

        /// <summary>The name the world currently gives the portal at this spot, if there is one.</summary>
        internal static string LiveNameAt(Vector3 pos)
        {
            var live = Nearest(pos);
            return live == null ? null : (string.IsNullOrEmpty(live.Name) ? "Portal" : live.Name);
        }

        /// <summary>A piece is coming down: if it is a portal, treat it as gone from this moment.</summary>
        internal static void NoteDestroyedPiece(WearNTear wear)
        {
            if (wear == null || Player.m_localPlayer == null) return;
            if (wear.GetComponent<TeleportWorld>() == null) return;
            NoteDestroyed(wear.transform.position);
        }

        internal static void NoteDestroyed(Vector3 pos)
        {
            for (int i = _live.Count - 1; i >= 0; i--)
                if (Geo.FlatDistance(_live[i].Pos, pos) <= MatchRadius) _live.RemoveAt(i);
            _redrawWanted = true;
            Redraw();                 // now, not at the next tick: the player is watching
            ClientPins.Restyle();
            AskForList();             // the server's sweep may not have noticed yet
            if (!PersonalMap.Loaded) return;
            foreach (var pin in ClientPins.All)
            {
                if (!pin.Auto || ClientPins.KindOf(pin) != Category.Portal) continue;
                if (Geo.FlatDistance(pin.Pos, pos) > MatchRadius) continue;
                ClientPins.RemoveStale(pin.Key);
                TheGreatestMapMod.Log.LogInfo("[TheGreatestMap] A portal was destroyed here; its marker is gone.");
                break;
            }
        }

        // ── TheGreatestPortal, if it happens to be installed ────────────────────────

        private static PropertyInfo _pickerOpen;
        private static PropertyInfo _listHovered;
        private static bool _probed;

        /// <summary>
        /// True while TheGreatestPortal has its own portal list and pins over the map. It draws a
        /// pin for every portal itself, so the portal markers drawn from the server's list stand
        /// down while it is up rather than showing every portal twice. Only those stand down: its
        /// map shows everything else as it normally looks, recorded markers and all. Found by
        /// name, so there is no reference to that mod and no need for it to be installed.
        ///
        /// Asks for Active, not IsSelecting: that one is true only while a destination is being
        /// chosen, and left "Show on map" -- which opens the same list to look rather than to
        /// travel -- drawing both mods' markers on every portal, labelled differently by each.
        /// IsSelecting is the fallback for a version of that mod old enough not to say Active.
        /// </summary>
        internal static bool PortalPickerOpen()
        {
            if (!_probed)
            {
                _probed = true;
                var type = AccessTools.TypeByName("TheGreatestPortal.MapPicker");
                if (type != null)
                {
                    _pickerOpen = AccessTools.Property(type, "Active") ?? AccessTools.Property(type, "IsSelecting");
                    _listHovered = AccessTools.Property(type, "ListPointerOver");
                }
            }
            if (_pickerOpen == null) return false;
            try { return (bool)_pickerOpen.GetValue(null); }
            catch { return false; }
        }

        /// <summary>
        /// True while the pointer is over TheGreatestPortal's portal list, which covers the left of
        /// the map. What is under a panel is not under the pointer: the tooltip works from the map
        /// position the cursor sits over, which knows nothing about anything drawn on top of it.
        /// </summary>
        internal static bool PortalListHovered()
        {
            PortalPickerOpen();   // probes both properties the first time
            if (_listHovered == null) return false;
            try { return (bool)_listHovered.GetValue(null); }
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

    // Taking a piece down is the one moment the player can see a marker outlive the thing it
    // stands for, so the portal they just removed is dropped from their own map immediately
    // rather than waiting for the server's next sweep.
    [HarmonyPatch(typeof(WearNTear), "Destroy")]
    internal static class WearNTear_Destroy_Portal_Patch
    {
        private static void Prefix(WearNTear __instance) => Portals.NoteDestroyedPiece(__instance);
    }

    // Removing a piece with the hammer only runs Destroy on whoever owns it, which need not be the
    // player holding the hammer, so the intent is caught here as well.
    [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.Remove))]
    internal static class WearNTear_Remove_Portal_Patch
    {
        private static void Prefix(WearNTear __instance) => Portals.NoteDestroyedPiece(__instance);
    }
}
