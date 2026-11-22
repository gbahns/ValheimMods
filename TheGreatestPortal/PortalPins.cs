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

        /// <summary>What one kind of portal is drawn as: its pin type and the picture on it.</summary>
        private struct Look
        {
            public int Type;
            public Sprite Icon;
            /// <summary>The picture is a build render we chose ourselves, with nobody to ask.</summary>
            public bool Rendered;
            public bool Ore;
        }

        /// <summary>One pin type per portal prefab, in the order the prefabs were first seen.</summary>
        private static readonly Dictionary<string, Look> _looks = new Dictionary<string, Look>(StringComparer.Ordinal);

        /// <summary>Keys lifted out of _looks so the dictionary can be written to while walking it.</summary>
        private static readonly List<string> _keyScratch = new List<string>();

        /// <summary>
        /// The types wearing a render we picked ourselves, which must not be painted: multiplying
        /// #FF6600 over a picture of a portal leaves an orange smudge where the picture was.
        ///
        /// <para>Only consulted when TheGreatestMap cannot be asked -- it is not installed, or is
        /// older than the seam. That mod answers this question inside the color it hands back,
        /// and answers it better: a render gets a neutral brightness pulse rather than being left
        /// flat white, so the picture survives and still breathes.</para>
        /// </summary>
        private static readonly HashSet<int> _rendered = new HashSet<int>();

        /// <summary>The map the types were registered against; a new one registers them again.</summary>
        private static Minimap _registeredOn;

        /// <summary>The last look TheGreatestMap reported, to notice it changing under us.</summary>
        private static int _lookEpoch;
        private static bool _haveEpoch;

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
            _looks.Clear();
            _rendered.Clear();
            _haveEpoch = false;
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
            string key = KeyFor(p);
            if (_looks.TryGetValue(key, out var had)) return had.Type;

            var look = new Look { Type = PinTypeBase + _looks.Count, Ore = p.AllowAllItems };
            look.Icon = IconFor(p.Prefab, look.Ore, out look.Rendered);
            _looks[key] = look;
            if (look.Rendered) _rendered.Add(look.Type);
            Apply(map, look);
            EnsureArrays(map);
            return look.Type;
        }

        /// <summary>
        /// A catalog too old to name the prefab still separates the two vanilla kinds, which is
        /// what the picture told apart before it told prefabs apart. The marker on those two is a
        /// character no prefab name can contain.
        /// </summary>
        private static string KeyFor(PortalInfo p) =>
            !string.IsNullOrEmpty(p.Prefab) ? p.Prefab : (p.AllowAllItems ? "\0stone" : "\0wood");

        private static string PrefabOf(string key) => key.Length > 0 && key[0] == '\0' ? "" : key;

        private static void Apply(Minimap map, Look look)
        {
            map.m_icons.RemoveAll(x => (int)x.m_name == look.Type);
            map.m_icons.Add(new Minimap.SpriteData { m_name = (Minimap.PinType)look.Type, m_icon = look.Icon });
        }

        /// <summary>
        /// Follow TheGreatestMap when the look of a portal changes under us.
        ///
        /// <para>Its Portal Marker Style is a setting, and a setting moves while the game is
        /// running. The picture is read once when a pin type is handed out, so without this a
        /// player would change that setting, reopen the picker, and see the old pictures until
        /// the next world load. Runs when the pins are built rather than per frame -- which
        /// includes every time the picker opens, and that is when a settings change is seen.</para>
        /// </summary>
        /// <summary>
        /// Ask TheGreatestMap whether the look has moved, and follow it if so. An int compared
        /// per frame, against a sprite resolve per prefab, so this is cheaper than doing it when
        /// the pins are built -- and it closes the window where an outlined picture has been
        /// destroyed under us to a single frame rather than to however long the map stays open.
        /// </summary>
        private static void FollowLook(Minimap map)
        {
            var epoch = Access.LookEpoch();
            if (!epoch.HasValue) return;
            if (_haveEpoch && epoch.Value == _lookEpoch) return;
            _haveEpoch = true;
            _lookEpoch = epoch.Value;
            Restyle(map);
        }

        private static void Restyle(Minimap map)
        {
            if (_looks.Count == 0) return;
            _keyScratch.Clear();
            foreach (var key in _looks.Keys) _keyScratch.Add(key);
            foreach (var key in _keyScratch)
            {
                var look = _looks[key];
                var icon = IconFor(PrefabOf(key), look.Ore, out bool rendered);
                // Inert as things stand, and kept deliberately. TheGreatestMap's rebake always
                // hands back a new object, and its one path that returns an existing one returns
                // a game asset it would never destroy -- so this does not fire today, and it is
                // not evidence that sprites are recycled over there, because they are not. It
                // stays because the resolver is on the far side of a reflection seam neither mod
                // compiles against, and because Unity's == is the only thing that would catch a
                // destroyed sprite: ReferenceEquals sees a live C# reference and says nothing has
                // changed, while the object draws nothing.
                bool dead = look.Icon == null;
                if (icon == null || (!dead && ReferenceEquals(icon, look.Icon))) continue;
                look.Icon = icon;
                look.Rendered = rendered;
                _looks[key] = look;
                if (rendered) _rendered.Add(look.Type); else _rendered.Remove(look.Type);
                Apply(map, look);
                // Vanilla reads the picture out of the pin, not out of the icon list, once the
                // pin exists -- so the ones already drawn are told directly rather than rebuilt.
                foreach (var kv in _pins)
                {
                    var pin = kv.Value;
                    if (pin == null || (int)pin.m_type != look.Type) continue;
                    pin.m_icon = icon;
                    if (pin.m_iconElement != null) pin.m_iconElement.sprite = icon;
                }
            }
        }

        /// <summary>
        /// The picture for a portal, best answer first.
        ///
        /// <para>TheGreatestMap's answer wins outright, because what a portal looks like is that
        /// mod's to decide and a player has a setting for it there. Its markers stand down while
        /// this mod's picker is up, so the handoff happens live on one screen: if the two
        /// disagreed, portals would change shape as a list opened, which reads as a bug rather
        /// than as two mods.</para>
        ///
        /// <para>Everything after it is for a game without that mod, or with one too old to ask.
        /// The game's own build-menu render of the prefab comes first -- it is what the thing
        /// looks like, the player clicked it once already to build the portal, and it separates
        /// stone from wood without anything here naming either. Failing that -- a portal nobody
        /// can build has no build picture -- the older stone-only seam, so half an agreement
        /// survives; then this mod's own trilithon, which at least says "ore" where vanilla's
        /// portal glyph would say only "portal"; and for an ordinary portal that glyph, which is
        /// exactly right.</para>
        /// </summary>
        private static Sprite IconFor(string prefab, bool ore, out bool rendered)
        {
            rendered = false;
            var theirs = Access.PortalIcon(prefab);
            if (theirs != null) return theirs;
            var render = PieceIcon(prefab);
            if (render != null) { rendered = true; return render; }
            if (ore)
                return Access.StonePortalIcon() ?? UiKit.StonePortal();
            var glyph = VanillaIcon(Minimap.PinType.Icon4) ?? VanillaIcon(Minimap.PinType.Icon3);
            if (glyph == null)
                TheGreatestPortalMod.Log.LogWarning(
                    $"[TheGreatestPortal] No picture found for portal prefab '{prefab}'; its marker will be blank.");
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
            int need = Math.Max(PinTypeBase + _looks.Count, VanillaTypeCount);
            var arr = Access.VisibleIconTypes(map);
            if (arr == null || arr.Length < need)
            {
                var grown = new bool[need];
                for (int i = 0; i < grown.Length; i++)
                    grown[i] = arr == null || i >= arr.Length || arr[i];
                Access.VisibleIconTypes(map) = grown;
                arr = grown;
            }
            foreach (var look in _looks.Values) arr[look.Type] = true;
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
            Restyle(map);

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
        internal static void Tint()
        {
            var map = Minimap.instance;
            if (map != null) FollowLook(map);
            foreach (var kv in _pins)
            {
                var icon = kv.Value.m_iconElement;
                if (icon == null) continue;
                var want = ColorFor(kv.Key, kv.Value);
                if (icon.color != want) icon.color = want;
            }
        }

        internal static Color ColorFor(long id) => ColorFor(id, PinFor(id));

        /// <summary>
        /// What color this one portal's marker is painted.
        ///
        /// <para>TheGreatestMap's answer, per portal, because there is no single answer any more:
        /// an ore-carrying portal has a color of its own there, and a picture of a portal is not
        /// painted a hue at all but breathed in brightness. Reading one color for every portal is
        /// how a portal came to be blue on that map and orange on this one.</para>
        ///
        /// <para>Without that mod to ask, the old rule: the color it used to give for everything
        /// -- white, when it is not installed at all -- and flat white over a render, which is
        /// the best that can be done without its brightness pulse.</para>
        /// </summary>
        private static Color ColorFor(long id, Minimap.PinData pin)
        {
            var p = Catalog.Get(id);
            var theirs = p != null ? Access.PortalColor(p.Prefab) : null;
            if (theirs.HasValue) return theirs.Value;
            return pin != null && _rendered.Contains((int)pin.m_type) ? Color.white : Access.PortalPinColor();
        }
    }
}
