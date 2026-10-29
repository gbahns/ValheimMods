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
    /// Not at a ship's rudder, though. The arrow is made bigger to be found among markers, and
    /// the sea has hardly any: there is nothing out there to lose it in, so the size buys nothing
    /// and costs the precision you want while steering. Both arrows go back to normal for as long
    /// as you have the helm -- the game shows your own arrow and the ship's together while you
    /// steer, so leaving one large would have been the odd pair of the two.
    ///
    /// Set every frame the map is open, cheaply and only when it differs. Vanilla writes the
    /// arrow's rotation and position but never its scale, so this is not a fight -- it is simply
    /// the safe place to put it back if anything ever does, and it is what lets the size come and
    /// go with the helm without anything having to notice you took it or let it go.
    /// </summary>
    internal static class PlayerArrow
    {
        private static float Size =>
            TgmConfig.PlayerArrowSize != null ? Mathf.Clamp(TgmConfig.PlayerArrowSize.Value, 50, 400) / 100f : 1f;

        /// <summary>
        /// Has the local player the helm? The game's own test for steering a ship, the one it uses
        /// to credit the sailing-at-the-helm stat: a passenger standing on the deck is not at the
        /// rudder and keeps the larger arrow.
        /// </summary>
        private static bool AtTheRudder() =>
            Player.m_localPlayer != null && Player.m_localPlayer.GetControlledShip() != null;

        internal static void Update()
        {
            var map = Minimap.instance;
            if (map == null || map.m_mode != Minimap.MapMode.Large) return;
            float scale = AtTheRudder() ? 1f : Size;
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
