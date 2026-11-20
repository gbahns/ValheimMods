using System;
using System.Collections.Generic;
using UnityEngine;

namespace TheGreatestPortal
{
    /// <summary>
    /// Portal pins on the large map. They use pin types of their own, so they never mix with
    /// player-placed portal pins, cannot be deleted or checked by vanilla clicks (they are not
    /// saved pins), and are unaffected by the icon filter buttons.
    ///
    /// The number is 1000, and it is a reservation rather than a guess. This was 150, chosen as
    /// "clear of TheGreatestMap's 100+ range" - but that mod does not use a fixed range. It hands
    /// out one type per distinct icon key a map has recorded, counting up from 100, so it reaches
    /// 150 on a client whose map has seen 51 kinds of thing and then both mods own the same
    /// number. What follows is not subtle: registering an icon for a type removes any existing
    /// entry for it, and TheGreatestMap matches pins by type when it asks whether a marker is
    /// already somewhere. TheGreatestMap now refuses to allocate at or above 1000, so this is the
    /// one number both mods agree on. Nothing is saved with a type in it, on either side, so the
    /// change needs no migration.
    ///
    /// 1000 is the floor rather than the whole of it: one type is handed out per portal prefab,
    /// counting up from there, because a portal is drawn with the game's own build-menu picture
    /// of that very prefab and vanilla draws one picture per type. A stone portal and a wood one
    /// therefore tell themselves apart without a word, a modded portal looks like itself, and
    /// nothing here has to know any of their names. Everything from 1000 up is ours:
    /// TheGreatestMap refuses to allocate at or above it.
    /// </summary>
    internal static class PortalPins
    {
        internal const int PinTypeBase = 1000;

        /// <summary>One pin type per portal prefab, in the order the prefabs were first seen.</summary>
        private static readonly Dictionary<string, int> _typeFor = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>
        /// The types whose picture is one of the game's own renders rather than a flat silhouette.
        ///
        /// <para>A render must not be tinted. Painting a pin means multiplying a color over it,
        /// and the portal color is #FF6600 by default -- no blue in it at all -- so laying that
        /// over a render leaves an orange smudge where the picture was. A silhouette is flat
        /// white precisely so a color can be laid over it; a render already is the picture, and
        /// the hovered portal still says so by growing and lighting its halo.</para>
        /// </summary>
        private static readonly HashSet<int> _rendered = new HashSet<int>();

        /// <summary>The map the types were registered against; a new one registers them again.</summary>
        private static Minimap _registeredOn;

        /// <summary>Counted once: Enum.GetValues allocates, and EnsureArrays runs from UpdatePins.</summary>
        private static readonly int VanillaTypeCount = Enum.GetValues(typeof(Minimap.PinType)).Length;
        private static readonly Dictionary<long, Minimap.PinData> _pins = new Dictionary<long, Minimap.PinData>();
        private static readonly Dictionary<Minimap.PinData, long> _ids = new Dictionary<Minimap.PinData, long>();

        // Scratch for the diff in Show, kept rather than allocated: it runs on every snapshot.
        private static readonly HashSet<long> _live = new HashSet<long>();
        private static readonly List<long> _gone = new List<long>();

        internal static int Count => _pins.Count;

        internal static bool IsOurs(Minimap.PinData pin) => pin != null && (int)pin.m_type >= PinTypeBase;

        /// <summary>A new Minimap instance (world load): start the types over against it.</summary>
        internal static void OnMinimapStart(Minimap map)
        {
            _pins.Clear();
            _ids.Clear();
            Forget(map);
            // Before anything is added to this map, and that is the whole point of doing it here
            // -- see EnsureArrays.
            EnsureArrays(map);
        }

        /// <summary>
        /// Drop every registered type, because the map they were registered on has gone. The
        /// pictures live in that map's own icon list and do not outlive it.
        /// </summary>
        private static void Forget(Minimap map)
        {
            _typeFor.Clear();
            _rendered.Clear();
            _registeredOn = map;
        }

        /// <summary>
        /// The pin type carrying this portal's picture, allocating one the first time its prefab
        /// is seen.
        ///
        /// <para>Done as portals arrive rather than up front, because the picture comes from the
        /// prefab, and which prefabs are out there is the catalog's word -- which reaches a
        /// client well after its map exists.</para>
        /// </summary>
        private static int TypeFor(Minimap map, PortalInfo p)
        {
            // A catalog too old to name the prefab still separates the two vanilla kinds, which
            // is what the picture told apart before it told prefabs apart.
            string key = !string.IsNullOrEmpty(p.Prefab) ? p.Prefab : (p.AllowAllItems ? "\0stone" : "\0wood");
            if (_typeFor.TryGetValue(key, out int had)) return had;

            int type = PinTypeBase + _typeFor.Count;
            _typeFor[key] = type;
            var icon = IconFor(p, out bool rendered);
            if (rendered) _rendered.Add(type);
            map.m_icons.RemoveAll(x => (int)x.m_name == type);
            map.m_icons.Add(new Minimap.SpriteData { m_name = (Minimap.PinType)type, m_icon = icon });
            EnsureArrays(map);
            return type;
        }

        /// <summary>
        /// The picture for a portal, best answer first.
        ///
        /// <para>The game's own build-menu render of that very prefab wins: it is what the thing
        /// looks like, the player clicked it once already to build the portal, and it separates
        /// stone from wood without anything here naming either. Failing that -- a portal nobody
        /// can build has no build picture -- TheGreatestMap's picture for an ore-carrying portal,
        /// so the two mods still agree about a portal the game will not draw; then this mod's own
        /// trilithon, which at least says "ore" where vanilla's portal glyph would say only
        /// "portal"; and for an ordinary portal that glyph, which is exactly right.</para>
        /// </summary>
        private static Sprite IconFor(PortalInfo p, out bool rendered)
        {
            rendered = false;
            var render = PieceIcon(p.Prefab);
            if (render != null) { rendered = true; return render; }
            if (p.AllowAllItems)
                return Access.StonePortalIcon() ?? UiKit.StonePortal();
            var glyph = VanillaIcon(Minimap.PinType.Icon4) ?? VanillaIcon(Minimap.PinType.Icon3);
            if (glyph == null)
                TheGreatestPortalMod.Log.LogWarning(
                    $"[TheGreatestPortal] No picture found for portal prefab '{p.Prefab}'; its marker will be blank.");
            return glyph;
        }

        /// <summary>The build menu's own picture of a prefab, or null if it is not a buildable piece.</summary>
        private static Sprite PieceIcon(string prefab)
        {
            if (string.IsNullOrEmpty(prefab)) return null;
            try
            {
                var scene = ZNetScene.instance;
                var go = scene != null ? scene.GetPrefab(prefab) : null;
                var piece = go != null ? go.GetComponent<Piece>() : null;
                return piece != null ? piece.m_icon : null;
            }
            catch (Exception e)
            {
                TheGreatestPortalMod.Log.LogWarning($"[TheGreatestPortal] Could not read the build picture for '{prefab}': {e.Message}");
                return null;
            }
        }

        private static Sprite VanillaIcon(Minimap.PinType type)
        {
            var map = Minimap.instance;
            if (map == null || map.m_icons == null) return null;
            foreach (var data in map.m_icons)
                if (data.m_name == type) return data.m_icon;
            return null;
        }

        /// <summary>
        /// Grow Minimap's per-type visibility array so vanilla can index our types. Never shrinks
        /// it.
        ///
        /// <para><b>It has to reach 1000 before a single pin is added to this map, even though we
        /// have no types yet.</b> AddPin checks the type against this array's length and, rather
        /// than refusing, quietly demotes anything past the end to Icon3 and keeps the name -- so
        /// an array that is too short does not drop a marker, it turns it into a plain dot with a
        /// label still attached. Nothing says which mod's pin that was. Our own base is 1000, so
        /// a length of 1000 is the floor we need regardless; that it also covers everything any
        /// other mod allocates below our reservation is the reason the first growth happens at
        /// Minimap.Start, ahead of the saved map data arriving, rather than when our first portal
        /// shows up.</para>
        /// </summary>
        internal static void EnsureArrays(Minimap map)
        {
            if (map == null) return;
            int need = Math.Max(PinTypeBase + _typeFor.Count, VanillaTypeCount);
            var arr = Access.VisibleIconTypes(map);
            if (arr == null || arr.Length < need)
            {
                var grown = new bool[need];
                for (int i = 0; i < grown.Length; i++)
                    grown[i] = arr == null || i >= arr.Length || arr[i];
                Access.VisibleIconTypes(map) = grown;
                arr = grown;
            }
            foreach (int type in _typeFor.Values) arr[type] = true;
        }

        /// <summary>
        /// Draws one pin per catalog portal, labelled by <paramref name="label"/>.
        ///
        /// A portal that has not changed keeps the pin object it already had. This used to clear
        /// the lot and build them again, which was fine when it ran on opening the map - but it
        /// also runs on every catalog snapshot, and the server sends one whenever anybody
        /// anywhere builds or removes a portal. On a server where portals come and go, every pin
        /// and every label on everyone's map was destroyed and recreated each time, which reads
        /// as a flicker: the marker and its name blink out and come back.
        /// </summary>
        internal static void Show(Func<PortalInfo, string> label)
        {
            var map = Minimap.instance;
            if (map == null) return;
            // A map this has not registered against holds none of our pictures, whether that is
            // a world load Start did not reach or another mod having built the map.
            if (!ReferenceEquals(_registeredOn, map)) Forget(map);
            EnsureArrays(map);

            bool changed = false;
            _live.Clear();
            foreach (var p in Catalog.All)
            {
                _live.Add(p.Id);
                string text = label(p);
                int want = TypeFor(map, p);
                // A pin's type is fixed once vanilla has built it, so a portal that has somehow
                // changed prefab is rebuilt rather than retexted. It cannot in practice -- a
                // portal is the thing it was built as -- but the catalog is the server's word,
                // not an assumption this has to make.
                if (_pins.TryGetValue(p.Id, out var existing) && existing != null
                    && (int)existing.m_type == want && Retext(existing, text))
                {
                    // Position and name are all a pin carries that a catalog update can change.
                    if (existing.m_pos != p.Pos) { existing.m_pos = p.Pos; changed = true; }
                    continue;
                }
                if (existing != null) { map.RemovePin(existing); _ids.Remove(existing); _pins.Remove(p.Id); }
                var pin = map.AddPin(p.Pos, (Minimap.PinType)want, text, save: false, isChecked: false, 0L);
                if (pin == null) continue;
                _pins[p.Id] = pin;
                _ids[pin] = p.Id;
                changed = true;
            }

            // Portals that are no longer in the catalog: torn down, or renamed away by an id change.
            _gone.Clear();
            foreach (var kv in _pins) if (!_live.Contains(kv.Key)) _gone.Add(kv.Key);
            foreach (long id in _gone)
            {
                if (_pins.TryGetValue(id, out var pin) && pin != null) { map.RemovePin(pin); _ids.Remove(pin); }
                _pins.Remove(id);
                changed = true;
            }

            if (changed) Access.PinUpdateRequired(map) = true;
        }

        /// <summary>
        /// Gives a pin a new label, or says it cannot.
        ///
        /// Vanilla reads m_name once, in PinNameData.SetTextAndGameObject, and never looks at it
        /// again - so the text has to be written to the label itself. A pin that was built with no
        /// name has no label object at all, and only AddPin makes one, so renaming that one means
        /// building the pin again; false says so and the caller does that for just that pin.
        /// </summary>
        private static bool Retext(Minimap.PinData pin, string text)
        {
            text = text ?? "";
            if (pin.m_name == text) return true;
            if (string.IsNullOrEmpty(pin.m_name) || string.IsNullOrEmpty(text)) return false;
            var label = pin.m_NamePinData;
            if (label == null || label.PinNameText == null) return false;
            pin.m_name = text;
            label.PinNameText.text = Localization.instance.Localize(text);
            return true;
        }

        internal static void Clear()
        {
            var map = Minimap.instance;
            if (map != null)
            {
                foreach (var pin in _pins.Values) map.RemovePin(pin);
                Access.PinUpdateRequired(map) = true;
            }
            _pins.Clear();
            _ids.Clear();
        }

        /// <summary>The catalog portal whose pin is nearest to a world point, within the radius.</summary>
        internal static PortalInfo Closest(Vector3 worldPos, float radius)
        {
            PortalInfo best = null;
            float bestDist = float.MaxValue;
            foreach (var kv in _pins)
            {
                var p = Catalog.Get(kv.Key);
                if (p == null) continue;
                float d = Utils.DistanceXZ(worldPos, p.Pos);
                if (d < radius && d < bestDist) { best = p; bestDist = d; }
            }
            return best;
        }

        internal static Minimap.PinData PinFor(long id) => _pins.TryGetValue(id, out var pin) ? pin : null;

        /// <summary>
        /// Paints every portal pin, the hovered one included: hovering makes a portal bigger and
        /// lights it up, it does not change what color that portal is. Vanilla repaints its
        /// markers white whenever it lays the map out again, so this runs each frame.
        /// </summary>
        internal static void Tint(Color color)
        {
            foreach (var kv in _pins)
            {
                var icon = kv.Value.m_iconElement;
                if (icon == null) continue;
                var want = Tintable(kv.Value) ? color : Color.white;
                if (icon.color != want) icon.color = want;
            }
        }

        /// <summary>
        /// Whether a pin's picture takes a color laid over it: a flat silhouette does, one of the
        /// game's own renders does not. See <see cref="_rendered"/> for why that matters.
        /// </summary>
        internal static bool Tintable(Minimap.PinData pin) =>
            pin != null && !_rendered.Contains((int)pin.m_type);
    }
}
