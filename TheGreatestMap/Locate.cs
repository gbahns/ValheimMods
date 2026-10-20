using UnityEngine;

namespace TheGreatestMap
{
    /// <summary>
    /// "Where are all the dungeons?" answered without putting anything else away.
    ///
    /// The find box already narrows the map to one thing, which is the right tool when you want
    /// to study it. This is for the other question, asked in passing: show me where these are,
    /// now, while I keep looking at everything else. So nothing is hidden and nothing is changed
    /// -- the markers of one kind swell and pulse for a few seconds and then settle back.
    ///
    /// A pulse rather than a steady highlight because a crowded map is already full of color, and
    /// movement is the one thing that carries across a screen of five hundred icons. Size does
    /// most of the work: a marker that grows and shrinks is visible at the edge of vision in a
    /// way a recolored one is not.
    ///
    /// Painted every frame, like the portal pulse and the player spotlight, because the game
    /// repaints every marker's color and size as it lays the map out and would undo a one-off
    /// change at the next redraw.
    /// </summary>
    internal static class Locate
    {
        private static Category? _kind;
        private static string _icon;
        private static float _until;

        internal static bool On => Time.unscaledTime < _until && (_kind.HasValue || _icon != null);

        private static float Seconds =>
            TgmConfig.LocateSeconds != null ? Mathf.Max(1f, TgmConfig.LocateSeconds.Value) : 6f;

        internal static void Reset()
        {
            _kind = null;
            _icon = null;
            _until = 0f;
        }

        /// <summary>Light up every marker of one kind.</summary>
        internal static void Kind(Category kind)
        {
            _kind = kind;
            _icon = null;
            Begin(Categories.Label(kind));
        }

        /// <summary>Light up every marker wearing one icon -- one type rather than a whole kind.</summary>
        internal static void Icon(string icon, string label)
        {
            _icon = IconRegistry.Normalize(icon);
            _kind = null;
            Begin(label);
        }

        private static void Begin(string what)
        {
            _until = Time.unscaledTime + Seconds;
            int n = 0;
            foreach (var pin in ClientPins.All) if (Matches(pin)) n++;
            TheGreatestMapMod.Message(n == 0
                ? $"No {what.ToLowerInvariant()} on your map."
                : $"{n} {(n == 1 ? "marker" : "markers")}: {what}.");
        }

        private static bool Matches(SharedPin pin)
        {
            if (pin == null) return false;
            if (_icon != null) return IconRegistry.SameKey(ClientPins.IconOf(pin), _icon);
            if (!_kind.HasValue) return false;
            var kind = ClientPins.KindOf(pin);
            return kind.HasValue && kind.Value == _kind.Value;
        }

        /// <summary>
        /// Two beats a second, swelling to half again as large and brightening as it goes. The
        /// curve is a plain sine so the marker spends as long large as it does small -- a sharper
        /// one reads as a flicker, which is harder to follow across a map rather than easier.
        /// Fades out over the last second so it stops without a jolt.
        /// </summary>
        internal static void Paint()
        {
            if (!On) { if (_kind.HasValue || _icon != null) { Reset(); ClientPins.Restyle(); } return; }
            float left = _until - Time.unscaledTime;
            float strength = Mathf.Clamp01(left);
            float wave = (Mathf.Sin(Time.unscaledTime * Mathf.PI * 4f) + 1f) * 0.5f;
            float grow = 1f + 0.5f * wave * strength;

            foreach (var kv in ClientPins.Drawn)
            {
                var pin = kv.Key;
                if (pin == null || pin.m_uiElement == null) continue;
                if (!ClientPins.TryGet(kv.Value, out var shared) || !Matches(shared)) continue;
                if (!pin.m_uiElement.gameObject.activeSelf) continue;
                // Against the size this marker is configured to be, never against the size it is
                // this frame: multiplying what is already there would compound every frame and
                // the marker would swell off the map.
                float size = ClientPins.ScaleOf(shared) * grow;
                pin.m_uiElement.localScale = new Vector3(size, size, 1f);
                if (pin.m_iconElement != null)
                    pin.m_iconElement.color = Color.Lerp(pin.m_iconElement.color, Color.white, 0.6f * wave * strength);
            }
        }
    }
}
