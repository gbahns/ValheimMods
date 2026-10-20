using UnityEngine;

namespace TheGreatestMap
{
    /// <summary>
    /// The arrow that says where you are on the big map.
    ///
    /// It is drawn at one size whatever the map is doing, which is fine on a bare map and poor on
    /// a crowded one: pan and zoom around a few hundred markers and the one thing you need to
    /// find again is a small arrow among them. Every marker on this map can be sized by the
    /// player; the arrow could not, because it is not a marker at all -- vanilla draws it as its
    /// own piece of the map rather than as a pin.
    ///
    /// Both arrows are covered: the one for a player on foot and the one shown while aboard a
    /// ship, which is a separate object and would otherwise snap back to small the moment you
    /// stepped on a boat.
    ///
    /// Set every frame the map is open, cheaply and only when it differs. Vanilla writes the
    /// arrow's rotation and position but never its scale, so this is not a fight -- it is simply
    /// the safe place to put it back if anything ever does.
    /// </summary>
    internal static class PlayerArrow
    {
        private static float Size =>
            TgmConfig.PlayerArrowSize != null ? Mathf.Clamp(TgmConfig.PlayerArrowSize.Value, 50, 400) / 100f : 1f;

        internal static void Update()
        {
            var map = Minimap.instance;
            if (map == null || map.m_mode != Minimap.MapMode.Large) return;
            float scale = Size;
            Apply(map.m_largeMarker, scale);
            Apply(map.m_largeShipMarker, scale);
        }

        /// <summary>Put the arrow back to normal, for when the setting goes back to 100.</summary>
        internal static void Reset()
        {
            var map = Minimap.instance;
            if (map == null) return;
            Apply(map.m_largeMarker, 1f);
            Apply(map.m_largeShipMarker, 1f);
        }

        private static void Apply(RectTransform arrow, float scale)
        {
            if (arrow == null) return;
            var at = arrow.localScale;
            if (!Mathf.Approximately(at.x, scale)) arrow.localScale = new Vector3(scale, scale, 1f);
        }
    }
}
