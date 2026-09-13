using System.Collections.Generic;
using UnityEngine;

namespace TheGreatestMap
{
    /// <summary>
    /// A marker you have just recorded is drawn for a while even when its kind or its icon is
    /// switched off, so writing something down always shows you what you wrote and where. The
    /// reveal is local, in memory only, and covers markers this character records, not ones that
    /// arrive from someone else's map.
    ///
    /// It does not override the two choices that say "not this one, ever": the map-pin button's
    /// master switch, and a marker you hid by hand. Markers drawn on one of vanilla's own icons
    /// (structures, portals, camps, boss altars) cannot be revealed at all while vanilla's icon
    /// button has that icon switched off, because vanilla destroys those markers before this mod
    /// is asked anything.
    /// </summary>
    internal static class Reveals
    {
        private static readonly Dictionary<string, float> _until = new Dictionary<string, float>();
        private static readonly List<string> _lapsed = new List<string>();

        internal static void Reset() => _until.Clear();

        internal static void Add(string id)
        {
            float seconds = TgmConfig.RevealNewMarkers != null ? TgmConfig.RevealNewMarkers.Value : 0f;
            if (string.IsNullOrEmpty(id) || seconds <= 0f) return;
            _until[id] = Time.unscaledTime + seconds;
            ClientPins.Restyle();
        }

        internal static bool IsRevealed(string id)
        {
            return id != null && _until.TryGetValue(id, out float until) && Time.unscaledTime < until;
        }

        /// <summary>Drop lapsed reveals and ask for a redraw, so a revealed marker goes away on time.</summary>
        internal static void Update()
        {
            if (_until.Count == 0) return;
            float now = Time.unscaledTime;
            _lapsed.Clear();
            foreach (var kv in _until) if (now >= kv.Value) _lapsed.Add(kv.Key);
            if (_lapsed.Count == 0) return;
            foreach (var id in _lapsed) _until.Remove(id);
            ClientPins.Restyle();
        }
    }
}
