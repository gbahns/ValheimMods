using System.Collections.Generic;
using UnityEngine;

namespace TheGreatestMap
{
    /// <summary>
    /// What <c>tgm_survey apply</c> would write, drawn on the map before anything is written.
    ///
    /// A dry run already says what it would do, and said it in numbers: "1,432 markers to write,
    /// Berries: 611, Dungeons: 23". That answers how much and not the only question worth asking
    /// before writing fourteen hundred markers onto a map, which is what it will look like --
    /// whether the berries come out as a readable scatter or as a green crust over the Meadows,
    /// whether the spawners land where you wanted something to test against. A count cannot be
    /// looked at. The map can.
    ///
    /// So the dry run puts them up: a pin at each spot, wearing the icon that marker would wear,
    /// washed out and pulsing once like a ping so they can be told from the markers that are
    /// really there. Nothing is written, nothing is shared and nothing is saved -- these are the
    /// same kind of not-really-there pin the erasure ghosts are, and they come off at the next
    /// run, at <c>tgm_survey off</c>, or when the world does.
    ///
    /// <para>There is a cap, because a preview that stalls the client is worse than no preview:
    /// a run naming fifty thousand would otherwise put fifty thousand elements in the one parent
    /// the map keeps its pins in. The nearest <see cref="DrawCap"/> are drawn -- the candidates
    /// arrive nearest-first -- and the report says how many were left out.</para>
    /// </summary>
    internal static class SurveyPreview
    {
        /// <summary>One marker a run would write: where it would stand and what it would wear.</summary>
        internal sealed class Spot
        {
            public Vector3 Pos;
            public string Icon;
        }

        /// <summary>
        /// How many of them are drawn at once. Valheim holds thousands of pins without complaint
        /// -- a well-used shared map is three thousand -- but this is on top of those, and the
        /// cost of a pin is paid again on every map move. Two thousand is more than anybody can
        /// read off a map in one look and well inside what the map already carries.
        /// </summary>
        private const int DrawCap = 2000;

        private static readonly List<Minimap.PinData> _drawn = new List<Minimap.PinData>();

        /// <summary>The pins on the map now, so nothing else counts them as the player's own.</summary>
        internal static IEnumerable<Minimap.PinData> DrawnPins => _drawn;

        internal static int Count => _drawn.Count;

        /// <summary>Forget them without touching the map, for a world that is going away.</summary>
        internal static void Reset() => _drawn.Clear();

        /// <summary>Take the preview off the map.</summary>
        internal static void Clear()
        {
            if (_drawn.Count == 0) return;
            var map = Minimap.instance;
            if (map != null)
                foreach (var pin in _drawn)
                    if (Access.Pins(map).Contains(pin)) map.RemovePin(pin);
            // Locate was handed this very list to point at, so emptying it ends that ping too
            // rather than leaving it pulsing at pins that are no longer there.
            _drawn.Clear();
            ClientPins.Restyle();
        }

        /// <summary>
        /// Draw a run's worth of would-be markers and ping them. Returns how many went up, which
        /// is fewer than it was given when the cap falls.
        /// </summary>
        internal static int Show(List<Spot> spots, string what)
        {
            Clear();
            var map = Minimap.instance;
            if (map == null || spots == null || spots.Count == 0) return 0;
            for (int i = 0; i < spots.Count && _drawn.Count < DrawCap; i++)
            {
                var spot = spots[i];
                if (spot == null) continue;
                // No name: the game hangs a pin's name beside it on the large map, and a
                // thousand captions over each other is a thing to read rather than a map to
                // look at. What they are is in the report, by kind and by type.
                //
                // m_save false, like the erasure ghosts and the portal marks: never written to
                // the profile, never shared, gone when these are.
                var pin = map.AddPin(spot.Pos, (Minimap.PinType)IconRegistry.TypeFor(spot.Icon),
                                     "", false, false, 0L);
                if (pin != null) _drawn.Add(pin);
            }
            ClientPins.Restyle();
            Locate.Pins(_drawn, what);
            return _drawn.Count;
        }

        /// <summary>
        /// Wash them out after the game has laid the map out, where the erasure ghosts are
        /// greyed. These wear the real icon, because the icon is half of what is being previewed,
        /// so what has to say "not yet" is the weight: a marker that is not there should read as
        /// faint beside the ones that are, and the ping is what makes it findable anyway.
        /// </summary>
        internal static void Paint()
        {
            if (_drawn.Count == 0) return;
            var pale = new Color(1f, 1f, 1f, 0.45f);
            for (int i = 0; i < _drawn.Count; i++)
            {
                var pin = _drawn[i];
                if (pin != null && pin.m_iconElement != null) pin.m_iconElement.color = pale;
            }
        }
    }
}
