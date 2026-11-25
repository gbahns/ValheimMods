using System.Collections.Generic;
using UnityEngine;

namespace TheGreatestMap
{
    /// <summary>
    /// A marker crossed off by clicking it, held on the map for a few seconds and blinking.
    ///
    /// <para><b>The edit that hides its own undo.</b> A left click on the map crosses the nearest
    /// marker off, which is the easiest edit to make by accident. What takes it back is another
    /// click on the same marker -- vanilla's own toggle -- so where that kind of crossed-off
    /// marker is hidden, the edit removes the very thing you would click to reverse it, in the
    /// same frame, before the eye has found it.</para>
    ///
    /// <para><b>Two ways it disappears, not one.</b> The hiding above is the obvious one. The
    /// other is thinning, whose first rule is that a crossed-off marker loses its patch to any
    /// neighbor that is not crossed off -- so the click does not hide the marker so much as make
    /// it lose a contest it had been winning, and zoomed out, where the patches are wide, there
    /// is nearly always a neighbor to lose to. That one fires whether or not crossed-off markers
    /// are hidden, which makes it the one most players meet. A marker in its grace is held
    /// against both.</para>
    ///
    /// <para><b>Why it pulses.</b> Keeping it on the map solves that and causes a second problem:
    /// a crossed-off marker sitting there looks exactly like one the player has chosen to keep
    /// seeing, so somebody who hides theirs would read the grace as the hiding being broken. A
    /// marker that breathes says "going" rather than "staying", which is a thing no ordinary
    /// marker does, and it makes the short window legible rather than mysterious -- the marker is
    /// visibly on its way out, so nobody waits for it to settle.</para>
    ///
    /// <para>Only the markers that would otherwise be gone pulse, which is why <see cref="Paint"/>
    /// asks the hiding question again: a marker rescued from thinning alone is one the player
    /// expects to see anyway, and pulsing it would be explaining something nobody noticed. The
    /// pulse is there to account for a marker that has no business being on the map.</para>
    ///
    /// <para><b>Separate from <see cref="Reveals"/>, which is the same shape for a different
    /// reason.</b> That one shows a marker you have just recorded though its kind is switched
    /// off, so writing something down always shows you what you wrote; it lasts half a minute
    /// because there is nothing to undo and no hurry. This is an undo window, and a shorter,
    /// louder one. Two settings rather than one because the two numbers answer different
    /// questions.</para>
    /// </summary>
    internal static class JustCrossedOff
    {
        private static readonly Dictionary<PinKey, float> _until = new Dictionary<PinKey, float>();
        private static readonly List<PinKey> _lapsed = new List<PinKey>();

        private static float Seconds =>
            TgmConfig.RevealCrossedOff != null ? TgmConfig.RevealCrossedOff.Value : 0f;

        private static bool Pulsing =>
            TgmConfig.PulseCrossedOff == null || TgmConfig.PulseCrossedOff.Value;

        internal static void Reset() => _until.Clear();

        /// <summary>Hold this marker on the map for the grace, if there is one.</summary>
        internal static void Add(PinKey id)
        {
            float seconds = Seconds;
            if (id.IsNone || seconds <= 0f) return;
            _until[id] = Time.unscaledTime + seconds;
            ClientPins.Restyle();
        }

        /// <summary>Is this marker still inside its undo window?</summary>
        internal static bool Holds(PinKey id)
        {
            return _until.Count > 0 && _until.TryGetValue(id, out float until) && Time.unscaledTime < until;
        }

        /// <summary>
        /// The blink, and the end of it.
        ///
        /// <para>Painted every frame while anything is held, like the locate ping and the portal
        /// pulse, because the game redraws a marker's own color and alpha whenever it lays the map
        /// out and anything written once would be wiped. Costs nothing while nothing is held,
        /// which is almost always.</para>
        /// </summary>
        internal static void Paint()
        {
            if (_until.Count == 0) return;

            float now = Time.unscaledTime;
            _lapsed.Clear();
            foreach (var kv in _until) if (now >= kv.Value) _lapsed.Add(kv.Key);
            if (_lapsed.Count > 0)
            {
                // Dropped and the map asked to lay out again, which is what finally hides them:
                // the styling pass is the only thing that decides whether a marker is drawn.
                foreach (var id in _lapsed) _until.Remove(id);
                ClientPins.Restyle();
                if (_until.Count == 0) return;
            }

            if (!Pulsing) return;
            // Size rather than brightness, and that is not a matter of taste. The styling pass
            // fades a crossed-off marker from the color it already has, so an alpha written here
            // every frame would be faded again at every layout and the marker would walk away
            // darker each time -- the compounding Locate.Paint warns about, in the one place
            // where the base is not recomputed. A marker's size is read from its kind's setting
            // on every pass, so writing it is absolute and nothing accumulates.
            //
            // A plain sine, two beats every three seconds: the marker spends as long large as it
            // does small, which reads as breathing. A sharper curve reads as an alarm, and this
            // is a marker on its way out rather than something gone wrong.
            float wave = (Mathf.Sin(now * Mathf.PI * 1.33f) + 1f) * 0.5f;
            float grow = 1f + 0.45f * wave;
            foreach (var kv in _until)
            {
                var pin = ClientPins.PinDataFor(kv.Key);
                if (pin == null || pin.m_uiElement == null) continue;
                if (!pin.m_uiElement.gameObject.activeSelf) continue;
                // Still a marker the player would otherwise not be seeing. Switching crossed-off
                // markers back on mid-grace makes this one ordinary again, and an ordinary marker
                // has no business drawing attention to itself.
                if (!ClientPins.TryGet(kv.Key, out var shared) || !CrossedOff.IsHidden(shared)) continue;
                // Against the size the marker is configured to be, never against the size it is
                // this frame, which would compound exactly as the alpha would have.
                float size = ClientPins.ScaleOf(shared) * grow;
                pin.m_uiElement.localScale = new Vector3(size, size, 1f);
            }
        }
    }
}
