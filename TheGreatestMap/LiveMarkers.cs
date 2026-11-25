using UnityEngine;

namespace TheGreatestMap
{
    /// <summary>
    /// How big the live markers are drawn, and whether they appear on the minimap.
    ///
    /// <para><b>Why they need a pass of their own.</b> A ship, a cart and a portal are not markers
    /// this mod stores: the server says where they are now, and the client puts a pin up and
    /// throws it away at the next sweep. So they miss everything <c>StylePins</c> does for a
    /// stored marker -- the size a kind is drawn at and the minimap switch among it -- and until
    /// 4.15.0 those two settings therefore did not exist for them at all. Every other kind under
    /// Dynamic Markers already had both: a campfire and a workbench are recorded like anything
    /// else, and the other players' markers are vanilla's own, with a size setting of their
    /// own.</para>
    ///
    /// <para><b>Up to 400%, where a recorded marker stops at 100.</b> Same argument as
    /// <c>Player Marker Size</c>, which has had that range for the same reason: these are the few
    /// moving things on a map of still ones, and a ship you are trying to find at sea is worth
    /// more than the clutter it costs. A recorded marker is capped low because there are thousands
    /// of them and making one bigger is making a crowd worse.</para>
    ///
    /// <para><b>Costed like the other after-layout passes.</b> This runs on every move of the map,
    /// which on the minimap is every step the player takes. It does nothing at all while the live
    /// kinds are at their normal size and on the minimap, it touches only a pin whose scale or
    /// state is already wrong, and when the settings go back to normal it runs once more to put
    /// everything right rather than leaving a pin shrunk or hidden. That last part is what
    /// <see cref="_wasChanging"/> is for, and <c>WorldPins.AfterLayout</c> carries the same latch
    /// for the same reason.</para>
    /// </summary>
    internal static class LiveMarkers
    {
        /// <summary>
        /// Whether the last pass had anything to do. One more runs after the answer turns to no,
        /// so a kind put back to its normal size is actually drawn that way again.
        /// </summary>
        private static bool _wasChanging;

        internal static void AfterLayout(Minimap map)
        {
            if (map == null) return;

            float ships = Scale(Category.Ship);
            float carts = Scale(Category.Cart);
            float portals = Scale(Category.Portal);
            bool keepShips = OnMinimap(Category.Ship);
            bool keepCarts = OnMinimap(Category.Cart);
            bool keepPortals = OnMinimap(Category.Portal);

            // Read from the settings and not from which map is open: the minimap and the large map
            // take turns all the time, and a latch that followed them would never settle.
            bool changing = ships != 1f || carts != 1f || portals != 1f
                            || !keepShips || !keepCarts || !keepPortals;
            if (!changing && !_wasChanging) return;
            _wasChanging = changing;

            bool smallMap = map.m_mode != Minimap.MapMode.Large;
            foreach (var shown in Transports.DrawnPins)
                Apply(shown.Pin,
                      shown.Cart ? carts : ships,
                      !smallMap || (shown.Cart ? keepCarts : keepShips));
            foreach (var pin in Portals.DrawnPins)
                Apply(pin, portals, !smallMap || keepPortals);
        }

        /// <summary>
        /// One pin at the size and the showing it should have. Both are checked before being set:
        /// writing a transform or calling SetActive dirties the canvas, and the whole reason this
        /// is affordable on every step is that it almost never has to.
        /// </summary>
        private static void Apply(Minimap.PinData pin, float scale, bool show)
        {
            // No element yet is the ordinary case for a pin put up this frame -- vanilla builds
            // those in its own pass -- and it will be here at the next layout.
            if (pin == null || pin.m_uiElement == null) return;
            var icon = pin.m_uiElement.gameObject;
            if (icon.activeSelf != show) icon.SetActive(show);
            if (!show)
            {
                // A hidden pin's name would otherwise hang in the air where the pin was.
                var label = pin.m_NamePinData != null ? pin.m_NamePinData.PinNameGameObject : null;
                if (label != null && label.activeSelf) label.SetActive(false);
                return;
            }
            var at = pin.m_uiElement.localScale;
            if (!Mathf.Approximately(at.x, scale)) pin.m_uiElement.localScale = new Vector3(scale, scale, 1f);
        }

        /// <summary>The size this live kind is set to, as a fraction of normal.</summary>
        private static float Scale(Category kind)
        {
            return TgmConfig.LiveSize != null && TgmConfig.LiveSize.TryGetValue(kind, out var size)
                ? Mathf.Clamp(size.Value, 50, 400) / 100f
                : 1f;
        }

        private static bool OnMinimap(Category kind)
        {
            return TgmConfig.LiveOnMinimap == null
                   || !TgmConfig.LiveOnMinimap.TryGetValue(kind, out var show)
                   || show.Value;
        }
    }
}
