using System.Collections.Generic;
using UnityEngine;

namespace TheGreatestMap
{
    /// <summary>
    /// Keep a round minimap's circle clean, whoever drew what is in it.
    ///
    /// Vanilla's minimap is square, and everything the game decides to draw on it belongs there.
    /// Make it round, as Round Minimap does, and the game carries on reasoning about the square:
    /// markers in the corners are still kept, and now stand on the HUD outside the circle with
    /// nothing under them -- vanilla's deaths and pings, other mods' boats, anyone's.
    ///
    /// So none of this happens unless the minimap really is round. Clipping a square minimap to a
    /// circle would hide the corners it is right to show.
    ///
    /// Markers are found by looking in the roots they are drawn in rather than in the game's list
    /// of pins, because not everything there is a pin: a mod can parent its own markers to the same
    /// root, and those escape a list-based sweep while looking exactly as wrong.
    ///
    /// The one rule that keeps this from fighting anybody: it only ever hides, and only ever shows
    /// again what it hid itself. Whatever another mod wanted hidden stays hidden, and nothing we
    /// never touched is turned on by us.
    /// </summary>
    internal static class MinimapClip
    {
        private static readonly HashSet<GameObject> _hidden = new HashSet<GameObject>();

        internal static void Reset() => _hidden.Clear();

        internal static void Update()
        {
            var map = Minimap.instance;
            bool wanted = TgmConfig.ClipMinimapMarkers == null || TgmConfig.ClipMinimapMarkers.Value;
            if (map == null || map.m_mode != Minimap.MapMode.Small || !wanted || !IsRound()) { ShowAgain(); return; }

            var root = map.m_pinRootSmall;
            if (root == null) { ShowAgain(); return; }
            var size = root.rect.size;
            float radius = Mathf.Min(size.x, size.y) * 0.5f;
            if (radius <= 1f) { ShowAgain(); return; }
            var middle = size * 0.5f;

            // Only the markers. The game draws no labels on the minimap at all, so there is
            // nothing of theirs to clip -- and touching them risks switching on a label it had
            // deliberately switched off.
            Sweep(root, middle, radius);
        }

        private static void Sweep(RectTransform root, Vector2 middle, float radius)
        {
            if (root == null) return;
            foreach (Transform child in root)
            {
                var rect = child as RectTransform;
                if (rect == null) continue;
                var go = child.gameObject;
                // Half the marker, so it is gone by the time it would hang over the rim rather than
                // being cut in two by it.
                float half = rect.rect.width * 0.5f * Mathf.Abs(rect.localScale.x);
                bool outside = (rect.anchoredPosition - middle).magnitude > Mathf.Max(0f, radius - half);
                if (outside)
                {
                    if (go.activeSelf) { go.SetActive(false); _hidden.Add(go); }
                }
                else if (_hidden.Remove(go) && !go.activeSelf)
                {
                    go.SetActive(true);
                }
            }
        }

        private static System.Reflection.PropertyInfo _applied;
        private static bool _probed;

        /// <summary>
        /// True while something has actually made the minimap round. Asked of Round Minimap by
        /// name, so there is no reference to it and it need not be installed -- and when it is not,
        /// the minimap is the square the game drew and nothing here should touch it.
        /// </summary>
        private static bool IsRound()
        {
            if (!_probed)
            {
                _probed = true;
                var type = HarmonyLib.AccessTools.TypeByName("RoundMinimap.MinimapShape");
                if (type != null) _applied = HarmonyLib.AccessTools.Property(type, "IsApplied");
            }
            if (_applied == null) return false;
            try { return (bool)_applied.GetValue(null); }
            catch (System.Exception) { return false; }
        }

        /// <summary>Give back everything we put out, and forget it: ours to undo, nobody else's.</summary>
        private static void ShowAgain()
        {
            if (_hidden.Count == 0) return;
            foreach (var go in _hidden)
                if (go != null && !go.activeSelf) go.SetActive(true);
            _hidden.Clear();
        }
    }
}
