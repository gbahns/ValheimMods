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
    /// </summary>
    internal static class Locate
    {
        private static Category? _kind;
        private static string _icon;
        private static bool _players;
        private static float _until;

        private static bool Armed => _kind.HasValue || _icon != null || _players;

        internal static bool On => Time.unscaledTime < _until && Armed;

        /// <summary>True while the ping is pointing at this whole kind.</summary>
        internal static bool Pointing(Category kind) => On && _kind.HasValue && _kind.Value == kind;

        private static float Seconds =>
            TgmConfig.LocateSeconds != null ? Mathf.Max(1f, TgmConfig.LocateSeconds.Value) : 6f;

        internal static void Reset()
        {
            _kind = null;
            _icon = null;
            _players = false;
            _until = 0f;
        }

        /// <summary>Light up every marker of one kind. Ships and carts count, though nothing of ours drew them.</summary>
        internal static void Kind(Category kind)
        {
            Clear();
            _kind = kind;
            FindSpots(kind);
            Begin(Categories.Label(kind), Count());
        }

        /// <summary>Light up every marker wearing one icon -- one type rather than a whole kind.</summary>
        internal static void Icon(string icon, string label)
        {
            Clear();
            _icon = IconRegistry.Normalize(icon);
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
            _players = false;
            _spots.Clear();
        }

        private static readonly List<Vector3> _spots = new List<Vector3>();

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
            if (!Categories.VanillaMarks(kind)) return;
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
        }

        /// <summary>A pin the game drew for one of those places, rather than one of ours.</summary>
        private static bool IsSpot(Minimap.PinData pin)
        {
            if (pin == null || _spots.Count == 0 || pin.m_type != Minimap.PinType.None) return false;
            foreach (var spot in _spots)
                if (Geo.FlatDistance(spot, pin.m_pos) < 1f) return true;
            return false;
        }

        private static int Count()
        {
            int n = _spots.Count;
            foreach (var pin in ClientPins.All) if (Matches(pin)) n++;
            if (_kind.HasValue && Categories.IsLive(_kind.Value))
                foreach (var shown in Transports.DrawnPins) if (Wanted(shown)) n++;
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
            if (_kind.HasValue && Categories.IsLive(_kind.Value))
                foreach (var shown in Transports.DrawnPins)
                {
                    if (!Wanted(shown) || shown.Pin == null) continue;
                    float d = Geo.FlatDistance(from, shown.Pin.m_pos);
                    if (d < best) { best = d; at = shown.Pin.m_pos; }
                }
            foreach (var spot in _spots)
            {
                float d = Geo.FlatDistance(from, spot);
                if (d < best) { best = d; at = spot; }
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
            if (_icon != null) return IconRegistry.SameKey(ClientPins.IconOf(pin), _icon);
            if (!_kind.HasValue) return false;
            var kind = ClientPins.KindOf(pin);
            return kind.HasValue && kind.Value == _kind.Value;
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

            if (_kind.HasValue && Categories.IsLive(_kind.Value))
                foreach (var shown in Transports.DrawnPins)
                    if (Wanted(shown)) Pulse(shown.Pin, grow, toWhite);

            if (_players || _spots.Count > 0)
            {
                var map = Minimap.instance;
                if (map != null)
                    foreach (var pin in Access.Pins(map))
                    {
                        if (pin == null) continue;
                        if (_players ? pin.m_type == Minimap.PinType.Player : IsSpot(pin))
                            Pulse(pin, grow * (_players ? ClientPins.PlayerScale : 1f), toWhite);
                    }
            }
        }

        private static void Pulse(Minimap.PinData pin, float size, float toWhite)
        {
            if (pin == null || pin.m_uiElement == null) return;
            if (!pin.m_uiElement.gameObject.activeSelf) return;
            pin.m_uiElement.localScale = new Vector3(size, size, 1f);
            if (pin.m_iconElement != null)
                pin.m_iconElement.color = Color.Lerp(pin.m_iconElement.color, Color.white, toWhite);
        }
    }
}
