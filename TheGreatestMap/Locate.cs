using System.Collections.Generic;
using UnityEngine;

namespace TheGreatestMap
{
    /// <summary>
    /// "Where are all the dungeons?" answered without putting anything else away.
    ///
    /// The find box already narrows the map to one thing, which is the right tool when you want
    /// to study it. This is for the other question, asked in passing: show me where these are,
    /// now, while I keep looking at everything else. So nothing is put away and nothing is
    /// changed -- the markers of one kind swell and pulse for a few seconds and then settle back.
    ///
    /// A pulse rather than a steady highlight because a crowded map is already full of color, and
    /// movement is the one thing that carries across a screen of five hundred icons. Size does
    /// most of the work: a marker that grows and shrinks is visible at the edge of vision in a
    /// way a recolored one is not.
    ///
    /// Hidden markers are shown for as long as the pulse lasts and hidden again when it ends.
    /// Asking where something is, and being told nothing because you had switched that kind off
    /// an hour ago, would be a strange answer -- and the hiding is a standing preference rather
    /// than a thing to be undone and redone by hand to glance at the map.
    ///
    /// Painted every frame, like the portal pulse and the player spotlight, because the game
    /// repaints every marker's color and size as it lays the map out and would undo a one-off
    /// change at the next redraw.
    ///
    /// The markers being pointed at are also drawn in front of the rest, once each, and left
    /// there when the pulse ends -- see <see cref="Raise"/>.
    /// </summary>
    internal static class Locate
    {
        private static Category? _kind;
        private static string _icon;
        private static Category? _biomeKind;
        private static Heightmap.Biome _biome;
        private static bool _players;
        private static Minimap.PinType? _vanilla;
        private static bool _vanillaWasOff;
        private static float _until;

        /// <summary>
        /// Whether there is anything to point at. Every way of asking is in here, including the
        /// two that are a list rather than a kind -- the places the world marks, and pins drawn
        /// by something else that asked for a ping. Leaving the places out of this meant a row
        /// pinging the world's center said how far away it was and then pulsed nothing, because
        /// the pulse is gated on being armed and that row arms nothing else.
        /// </summary>
        private static bool Armed => _kind.HasValue || _icon != null || _biomeKind.HasValue
                                     || _players || _vanilla.HasValue
                                     || _spots.Count > 0 || (_theirs != null && _theirs.Count > 0);

        internal static bool On => Time.unscaledTime < _until && Armed;

        /// <summary>True while the ping is pointing at this whole kind.</summary>
        internal static bool Pointing(Category kind) => On && _kind.HasValue && _kind.Value == kind;

        /// <summary>True while the ping is pointing at one type -- a frost cave rather than a dungeon.</summary>
        internal static bool PointingAt(string icon) =>
            On && _icon != null && IconRegistry.SameKey(_icon, icon);

        /// <summary>Is the pulse pointing at this kind in this biome?</summary>
        internal static bool PointingAt(Category kind, Heightmap.Biome biome) =>
            On && _biomeKind.HasValue && _biomeKind.Value == kind && _biome == biome;

        /// <summary>True while the ping is pointing at one of the map's own pin types.</summary>
        internal static bool Pointing(Minimap.PinType type) =>
            On && _vanilla.HasValue && _vanilla.Value == type;

        /// <summary>
        /// True while the ping is pointing at this very list of pins, asked by whoever drew them.
        /// It is the list itself and not its contents, so a later ping over something else reads
        /// as false even where the pins are still up.
        ///
        /// <para>What it is for: pins that exist only to be pointed at can come down when the
        /// pointing ends. The survey preview is the one, and this is how it knows.</para>
        /// </summary>
        internal static bool Pointing(List<Minimap.PinData> pins) =>
            On && pins != null && ReferenceEquals(_theirs, pins);

        private static float Seconds =>
            TgmConfig.LocateSeconds != null ? Mathf.Max(1f, TgmConfig.LocateSeconds.Value) : 6f;

        internal static void Reset()
        {
            _kind = null;
            _icon = null;
            _biomeKind = null;
            _players = false;
            Unhide();
            _spots.Clear();
            _theirs = null;
            _raised.Clear();
            _until = 0f;
        }

        /// <summary>Light up every marker of one kind. Ships and carts count, though nothing of ours drew them.</summary>
        internal static void Kind(Category kind)
        {
            Clear();
            _kind = kind;
            FindSpots(kind);
            // And the game's own pins for this kind where it has a type of its own for them: a
            // vegvisir pin is a boss altar somebody read a stone for, and a row that says Boss
            // Altars means those as much as the markers this mod wrote.
            if (kind == Category.BossAltar) _vanilla = Minimap.PinType.Boss;
            Begin(Categories.Label(kind), Count());
        }

        /// <summary>
        /// Light up a set of places the game marked, for a row that stands for those and nothing
        /// else -- the world's center, Hildir's quests. Positions rather than pins, which is how
        /// the game's landmarks are recognized everywhere in this mod: they are all one pin type
        /// and where they stand is the only thing that says which is which.
        /// </summary>
        internal static void Spots(string label, List<Vector3> where)
        {
            Clear();
            if (where != null) _spots.AddRange(where);
            Begin(label, Count());
        }

        /// <summary>
        /// Light up pins something else drew and holds: the survey preview is the case -- markers
        /// that are not on the map at all yet, put up to be looked at before they are written.
        ///
        /// <para>The list is held, not copied, so whoever drew them ends the ping by taking them
        /// down: a pulse cannot outlive the pins it points at.</para>
        /// </summary>
        internal static void Pins(List<Minimap.PinData> pins, string label)
        {
            Clear();
            _theirs = pins;
            Begin(label, Count());
        }

        /// <summary>Light up every marker wearing one icon -- one type rather than a whole kind.</summary>
        /// <summary>Point at one kind where it stands in one biome -- the marker list's biome rows.</summary>
        internal static void Biome(Category kind, Heightmap.Biome biome, string label)
        {
            Clear();
            _biomeKind = kind;
            _biome = biome;
            Begin(label, Count());
        }

        internal static void Icon(string icon, string label)
        {
            Clear();
            _icon = IconRegistry.Normalize(icon);
            // And the places the game marks itself that wear this picture. A row under Boss
            // Altars or Hildir's Quests is one place rather than a type of marker, and with this
            // mod drawing none of them there is no marker of ours to point at -- the pin standing
            // there is the game's own.
            OwnMarks.SpotsWithIcon(_icon, _spots);
            Begin(label, Count());
        }

        /// <summary>
        /// Light up the map's own pins of one type -- every house pin a player dropped by hand,
        /// every landmark the game drew. Theirs rather than ours, so there is nothing of this
        /// mod's to match against: the game's pins themselves are what the pulse points at.
        /// </summary>
        internal static void Vanilla(Minimap map, Minimap.PinType type, string label)
        {
            Clear();
            _vanilla = type;
            // Switched off, these are not dimmed but gone -- the game destroys the marker rather
            // than hiding it -- so being asked where they are has to turn them back on, and the
            // end of the pulse puts them away again. That is the rule the rest of this class
            // already keeps; it just takes a different lever here.
            if (!VanillaPins.Drawn(map, type))
            {
                _vanillaWasOff = true;
                VanillaPins.SetDrawn(map, type, true);
            }
            Begin(label, Count());
        }

        /// <summary>Light up the other players, which are vanilla's own markers rather than ours.</summary>
        internal static void Players(Minimap map)
        {
            Clear();
            _players = true;
            Begin("Players", PlayerSpotlight.Players(map));
        }

        private static void Clear()
        {
            _kind = null;
            _icon = null;
            _biomeKind = null;
            _players = false;
            Unhide();
            _spots.Clear();
            _theirs = null;
            _raised.Clear();
        }

        /// <summary>
        /// Put back a vanilla pin type that was switched off and turned on for the pulse. Every
        /// way out of a ping comes through here, so a type cannot be left switched on by a pulse
        /// that the next one interrupted.
        /// </summary>
        private static void Unhide()
        {
            if (_vanilla.HasValue && _vanillaWasOff) VanillaPins.SetDrawn(Minimap.instance, _vanilla.Value, false);
            _vanilla = null;
            _vanillaWasOff = false;
        }

        /// <summary>One of the map's own pins of the type being pointed at, rather than one of ours.</summary>
        private static bool IsVanilla(Minimap.PinData pin)
        {
            return pin != null && _vanilla.HasValue && pin.m_type == _vanilla.Value && !ClientPins.IsOurs(pin);
        }

        private static IEnumerable<Minimap.PinData> VanillaFound()
        {
            if (!_vanilla.HasValue) yield break;
            var map = Minimap.instance;
            if (map == null) yield break;
            // From the pin list rather than from the marker list's tally, which was counted the
            // last time that list was refreshed: pinging refreshes nothing, by design.
            foreach (var pin in Access.Pins(map)) if (IsVanilla(pin)) yield return pin;
        }

        private static readonly List<Vector3> _spots = new List<Vector3>();

        /// <summary>Pins drawn by something else that asked for them to be pointed at.</summary>
        private static List<Minimap.PinData> _theirs;

        /// <summary>
        /// The markers already brought to the front for this pulse, by the object that was moved.
        /// The game destroys and rebuilds a marker's element as the map is laid out, so a rebuilt
        /// one is a different object and comes up again by itself.
        /// </summary>
        private static readonly HashSet<RectTransform> _raised = new HashSet<RectTransform>();

        /// <summary>
        /// How many markers are brought forward in one frame.
        ///
        /// <para>There is a budget because moving a child to the end of its parent shifts every
        /// sibling after it, and a well-used map keeps thousands of pins in that one parent:
        /// doing that three hundred times in a frame, for a kind with three hundred markers, is
        /// the shape of problem that once stalled this client into dropped connections. Twenty a
        /// frame clears any kind anybody has in a fraction of a second, which is inside the pulse
        /// and under what a player would call "when I pressed it".</para>
        /// </summary>
        private const int RaisePerFrame = 20;

        private static int _raiseFrame = -1;
        private static int _raiseLeft;

        /// <summary>
        /// Draw a pulsing marker in front of the others, once.
        ///
        /// <para>Because the pulse is a swell in size and a crowded map draws five hundred
        /// markers over each other: one behind its neighbors can grow and shrink underneath them
        /// and never be seen doing it, which is a button that did nothing as far as the player can
        /// tell.</para>
        ///
        /// <para>Nothing is put back when the pulse ends. The index a marker had means something
        /// only among the pins that were there when it was raised, and the game recreates them as
        /// the map is drawn, so "putting it back" would be guessing at an order that no longer
        /// exists -- and the side effect of leaving it is the one a reader would want anyway: what
        /// you last asked about stays on top.</para>
        /// </summary>
        private static void Raise(RectTransform ui)
        {
            if (ui == null || ui.parent == null || _raised.Contains(ui)) return;
            // Already on top: nothing to move, and nothing spent out of the budget either.
            if (ui.GetSiblingIndex() == ui.parent.childCount - 1) { _raised.Add(ui); return; }
            if (_raiseFrame != Time.frameCount) { _raiseFrame = Time.frameCount; _raiseLeft = RaisePerFrame; }
            if (_raiseLeft <= 0) return;   // the rest come up on the next frame
            _raiseLeft--;
            _raised.Add(ui);
            ui.SetAsLastSibling();
        }

        /// <summary>
        /// Where the game has put its own markers for this kind. Traders are the case: the game
        /// marks a trader the moment you find one, so this mod records nothing for them and the
        /// ping has to point at vanilla's pins instead of its own. Vanilla draws those from the
        /// world's location icons, as plain PinType.None pins at the location's position, so the
        /// positions are the way to recognize them.
        /// </summary>
        private static void FindSpots(Category kind)
        {
            _spots.Clear();
            if (!Categories.MarkedByGame(kind)) return;
            var zone = ZoneSystem.instance;
            if (zone == null) return;
            var icons = new Dictionary<Vector3, string>();
            try { zone.GetLocationIcons(icons); }
            catch (System.Exception e)
            {
                TheGreatestMapMod.Log.LogWarning($"[TheGreatestMap] Could not read the world's location icons: {e.Message}");
                return;
            }
            foreach (var kv in icons)
                foreach (var row in Catalog.Legend())
                    if (row.Cat == kind
                        && string.Equals(row.Prefab.TrimEnd('*'), kv.Value, System.StringComparison.OrdinalIgnoreCase))
                    { _spots.Add(kv.Key); break; }
            // And the places the game has marked that its location list cannot account for.
            // Hildir's quest pins are put up by her table rather than by the landmark pass, and a
            // site nobody has walked into is in no icon list at all -- so for her three the list
            // above was empty and the row's ping had nothing to point at.
            if (kind == Category.BossAltar) OwnMarks.Spots(WorldPins.Kind.Boss, _spots);
            else if (kind == Category.HildirQuest) OwnMarks.Spots(WorldPins.Kind.Hildir, _spots);
        }

        /// <summary>
        /// A pin standing on one of those places, rather than a marker with a record behind it.
        ///
        /// <para>Any type: the landmark pin the game's own pass drew, the typed quest pin beside
        /// it, and this mod's marker where the swap drew one. It used to have to be PinType.None,
        /// which is the landmark pass's own type and nothing else's, so a row whose places the
        /// game had marked some other way pulsed nothing at all. Markers with a record behind
        /// them are left out because those are pulsed by their record.</para>
        /// </summary>
        private static bool IsSpot(Minimap.PinData pin)
        {
            if (pin == null || _spots.Count == 0 || ClientPins.IsOurs(pin)) return false;
            foreach (var spot in _spots)
                if (Geo.FlatDistance(spot, pin.m_pos) < 1f) return true;
            return false;
        }



        private static int Count()
        {
            int n = _spots.Count + (_theirs != null ? _theirs.Count : 0);
            foreach (var pin in ClientPins.All) if (Matches(pin)) n++;
            foreach (var pin in LivePins()) { if (pin != null) n++; }
            foreach (var pin in VanillaFound()) { if (pin != null) n++; }
            return n;
        }

        private static void Begin(string what, int found)
        {
            _until = Time.unscaledTime + Seconds;
            // A kind switched off still answers: the markers come back for as long as the pulse
            // lasts, so the styling pass has to run now rather than at the next change. Ships and
            // carts are drawn by their own pass, which has to be told as well.
            ClientPins.Restyle();
            Transports.Redraw();
            if (found == 0)
            {
                TheGreatestMapMod.Message($"No {what.ToLowerInvariant()} on your map.");
                return;
            }
            // Where the nearest one is, because a kind with four markers in a whole world will
            // usually have none of them on the screen, and a pulse nobody can see reads as a
            // button that does nothing.
            string where = Nearest();
            TheGreatestMapMod.Message($"{found} {(found == 1 ? "marker" : "markers")}: {what}" +
                                      (where != null ? $" - nearest {where}." : "."));
        }

        /// <summary>How far away and which way the closest of them is, from the player.</summary>
        private static string Nearest()
        {
            var player = Player.m_localPlayer;
            if (player == null) return null;
            var from = player.transform.position;
            float best = float.MaxValue;
            Vector3 at = Vector3.zero;
            foreach (var pin in ClientPins.All)
            {
                if (!Matches(pin)) continue;
                float d = Geo.FlatDistance(from, pin.Pos);
                if (d < best) { best = d; at = pin.Pos; }
            }
            foreach (var pin in LivePins())
            {
                float d = Geo.FlatDistance(from, pin.m_pos);
                if (d < best) { best = d; at = pin.m_pos; }
            }
            foreach (var spot in _spots)
            {
                float d = Geo.FlatDistance(from, spot);
                if (d < best) { best = d; at = spot; }
            }
            foreach (var pin in VanillaFound())
            {
                float d = Geo.FlatDistance(from, pin.m_pos);
                if (d < best) { best = d; at = pin.m_pos; }
            }
            if (_theirs != null)
                foreach (var pin in _theirs)
                {
                    if (pin == null) continue;
                    float d = Geo.FlatDistance(from, pin.m_pos);
                    if (d < best) { best = d; at = pin.m_pos; }
                }
            if (best == float.MaxValue) return null;
            return (best >= 1000f ? $"{best / 1000f:0.#} km" : $"{Mathf.RoundToInt(best)} m") + " " + Compass(at - from);
        }

        private static string Compass(Vector3 delta)
        {
            // Screen north is +z, as the map draws it, so this is the direction a player would
            // read off the map rather than off the world's axes.
            float angle = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
            if (angle < 0f) angle += 360f;
            string[] points = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
            return points[Mathf.RoundToInt(angle / 45f) % 8];
        }

        /// <summary>True while this marker is one of the ones being pointed at.</summary>
        internal static bool Matches(SharedPin pin)
        {
            if (pin == null || _players) return false;
            if (_biomeKind.HasValue)
            {
                var standsIn = ClientPins.KindOf(pin);
                return standsIn.HasValue && standsIn.Value == _biomeKind.Value
                       && Biomes.Of(pin.Pos) == _biome;
            }
            if (_icon != null) return IconRegistry.SameKey(ClientPins.IconOf(pin), _icon);
            if (!_kind.HasValue) return false;
            var kind = ClientPins.KindOf(pin);
            return kind.HasValue && kind.Value == _kind.Value;
        }

        /// <summary>
        /// The pins a live kind has on screen at this moment, whoever drew them. Ships and carts
        /// come from the transport sweep and portals from the portal list, and pinging should not
        /// have to care which: both are pins the server told us about and neither is on the map
        /// in the sense the rest of this class means.
        /// </summary>
        private static IEnumerable<Minimap.PinData> LivePins()
        {
            if (!_kind.HasValue || !Categories.IsLive(_kind.Value)) yield break;
            if (_kind.Value == Category.Portal)
            {
                foreach (var pin in Portals.DrawnPins) if (pin != null) yield return pin;
                yield break;
            }
            foreach (var shown in Transports.DrawnPins)
                if (Wanted(shown) && shown.Pin != null) yield return shown.Pin;
        }

        private static bool Wanted(Transports.Shown shown)
        {
            if (shown == null || !_kind.HasValue) return false;
            return _kind.Value == (shown.Cart ? Category.Cart : Category.Ship);
        }

        /// <summary>
        /// Two beats a second, swelling to half again as large and brightening as it goes. The
        /// curve is a plain sine so the marker spends as long large as it does small -- a sharper
        /// one reads as a flicker, which is harder to follow across a map rather than easier.
        /// Fades out over the last second so it stops without a jolt.
        /// </summary>
        internal static void Paint()
        {
            if (!On)
            {
                // Over: put the map back, which also hides again whatever was only shown for this.
                if (Armed) { Reset(); ClientPins.Restyle(); Transports.Redraw(); }
                return;
            }
            float strength = Mathf.Clamp01(_until - Time.unscaledTime);
            float wave = (Mathf.Sin(Time.unscaledTime * Mathf.PI * 4f) + 1f) * 0.5f;
            float grow = 1f + 0.5f * wave * strength;
            float toWhite = 0.6f * wave * strength;

            foreach (var kv in ClientPins.Drawn)
            {
                if (!ClientPins.TryGet(kv.Value, out var shared) || !Matches(shared)) continue;
                // Against the size this marker is configured to be, never against the size it is
                // this frame: multiplying what is already there would compound every frame and
                // the marker would swell off the map.
                Pulse(kv.Key, ClientPins.ScaleOf(shared) * grow, toWhite);
            }

            foreach (var pin in LivePins()) Pulse(pin, grow, toWhite);

            if (_theirs != null) foreach (var pin in _theirs) Pulse(pin, grow, toWhite);

            if (_players || _spots.Count > 0 || _vanilla.HasValue)
            {
                var map = Minimap.instance;
                if (map != null)
                    foreach (var pin in Access.Pins(map))
                    {
                        if (pin == null) continue;
                        // Each asked on its own account rather than one instead of another: a
                        // kind can point at both at once -- the boss altars are the places the
                        // world marks and the pins a vegvisir drew, and the row means both.
                        if (_players && pin.m_type == Minimap.PinType.Player)
                        {
                            Pulse(pin, grow * ClientPins.PlayerScale, toWhite);
                            continue;
                        }
                        if (_vanilla.HasValue && IsVanilla(pin)) { Pulse(pin, grow, toWhite); continue; }
                        if (_spots.Count > 0 && IsSpot(pin)) Pulse(pin, grow, toWhite);
                    }
            }
        }

        private static void Pulse(Minimap.PinData pin, float size, float toWhite)
        {
            if (pin == null || pin.m_uiElement == null) return;
            if (!pin.m_uiElement.gameObject.activeSelf) return;
            Raise(pin.m_uiElement);
            pin.m_uiElement.localScale = new Vector3(size, size, 1f);
            if (pin.m_iconElement != null)
                pin.m_iconElement.color = Color.Lerp(pin.m_iconElement.color, Color.white, toWhite);
        }
    }
}
