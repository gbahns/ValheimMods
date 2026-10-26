using System.Collections.Generic;
using UnityEngine;

namespace TheGreatestMap
{
    /// <summary>
    /// Draw what has been erased, so a player can look at it instead of guessing.
    ///
    /// An erasure is the one thing on a map that cannot be seen. It holds a marker back on every
    /// copy of the map forever, and until now the only way to know one existed was to read the
    /// file with a script. That is how 373 of them went unexamined on the live map for days, and
    /// how it took three passes over the bytes to work out that they were not players erasing
    /// markers at all -- 309 of the 373 were written in three single minutes by a migration.
    /// Drawn on the map, that would have been obvious at a glance: a cloud of marks in places
    /// nobody was standing, all stamped the same evening.
    ///
    /// Nothing here changes the map. The pins are not saved, not shared, and gone the moment the
    /// switch goes off or the world unloads.
    /// </summary>
    internal static class Erasures
    {
        private static readonly List<Minimap.PinData> _drawn = new List<Minimap.PinData>();
        private static bool _on;

        internal static bool On => _on;

        internal static void Reset()
        {
            _drawn.Clear();
            _on = false;
        }

        internal static void Toggle()
        {
            _on = !_on;
            Redraw();
            TheGreatestMapMod.Message(_on
                ? $"Showing {_drawn.Count} erased marker(s)."
                : "Erased markers hidden again.");
        }

        /// <summary>Take the marks down and, if the switch is on, put them up again.</summary>
        internal static void Redraw()
        {
            var map = Minimap.instance;
            if (map == null) return;
            foreach (var pin in _drawn)
                if (Access.Pins(map).Contains(pin)) map.RemovePin(pin);
            _drawn.Clear();
            if (!_on || !PersonalMap.Loaded) { ClientPins.Restyle(); return; }

            int type = IconRegistry.TypeFor(IconKey);
            foreach (var kv in PersonalMap.Store.Tombstones)
            {
                var key = kv.Key;
                // m_save false, like the portal marks: never written to the profile, never
                // shared, and gone when the switch is.
                var pin = map.AddPin(key.Pos, (Minimap.PinType)type, Caption(key, kv.Value), false, false, 0L);
                if (pin != null) _drawn.Add(pin);
            }
            ClientPins.Restyle();
        }

        /// <summary>What it was and when it went, which is the whole question being asked.</summary>
        private static string Caption(PinKey key, long at)
        {
            string what = string.IsNullOrEmpty(key.Source) ? "never identified" : key.Source;
            string when;
            try { when = new System.DateTime(at).ToLocalTime().ToString("MMM d HH:mm"); }
            catch (System.Exception) { when = "?"; }
            return $"{what}, erased {when}";
        }

        /// <summary>
        /// A plain dot, because these are not markers and should not be mistaken for any kind of
        /// one. They are washed out further by the painter below.
        /// </summary>
        private const string IconKey = "like:Icon3";

        /// <summary>
        /// Grey them down after the game has laid the map out, the same moment the portals are
        /// painted. A mark for something that is gone should read as absence rather than compete
        /// with the markers that are still there.
        /// </summary>
        internal static void Paint()
        {
            if (!_on) return;
            var grey = new Color(0.62f, 0.62f, 0.66f, 0.55f);
            for (int i = 0; i < _drawn.Count; i++)
            {
                var pin = _drawn[i];
                if (pin != null && pin.m_iconElement != null) pin.m_iconElement.color = grey;
            }
        }
    }
}
