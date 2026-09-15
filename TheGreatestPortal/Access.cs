using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace TheGreatestPortal
{
    /// <summary>
    /// Accessors for non-public game members. The publicized reference assembly lets the code
    /// compile against them, but the live assembly_valheim.dll still has them private, and
    /// direct access then throws at runtime. FieldRefAccess builds delegates that bypass the
    /// visibility check; methods go through reflection.
    /// </summary>
    internal static class Access
    {
        internal static readonly AccessTools.FieldRef<Minimap, List<Minimap.PinData>> Pins =
            AccessTools.FieldRefAccess<Minimap, List<Minimap.PinData>>("m_pins");
        internal static readonly AccessTools.FieldRef<Minimap, bool[]> VisibleIconTypes =
            AccessTools.FieldRefAccess<Minimap, bool[]>("m_visibleIconTypes");
        internal static readonly AccessTools.FieldRef<Minimap, bool> PinUpdateRequired =
            AccessTools.FieldRefAccess<Minimap, bool>("m_pinUpdateRequired");

        // Bound as a delegate rather than called through reflection: the hover highlight asks for
        // the world position under the pointer every frame, and Invoke would allocate every time.
        private delegate Vector3 ScreenToWorld(Minimap map, Vector3 screen);
        private static readonly ScreenToWorld _screenToWorldPoint = BindScreenToWorldPoint();

        private static ScreenToWorld BindScreenToWorldPoint()
        {
            try
            {
                var method = AccessTools.Method(typeof(Minimap), "ScreenToWorldPoint", new[] { typeof(Vector3) });
                if (method != null) return AccessTools.MethodDelegate<ScreenToWorld>(method, null, virtualCall: false);
                TheGreatestPortalMod.Log.LogWarning("[TheGreatestPortal] Minimap.ScreenToWorldPoint was not found; clicking portals on the map is disabled.");
            }
            catch (Exception e)
            {
                TheGreatestPortalMod.Log.LogWarning($"[TheGreatestPortal] Could not bind Minimap.ScreenToWorldPoint; clicking portals on the map is disabled: {e.Message}");
            }
            return null;
        }

        /// <summary>The world position under a screen point on the large map.</summary>
        internal static Vector3 ScreenToWorldPoint(Minimap map, Vector3 screen)
        {
            if (map == null || _screenToWorldPoint == null) return Vector3.zero;
            return _screenToWorldPoint(map, screen);
        }

        // ── TheGreatestMap, if it happens to be installed ───────────────────────────

        private static Func<Color> _tgmPortalColor;
        private static bool _probedTgm;

        /// <summary>
        /// The color TheGreatestMap paints portal markers, so this mod's portal pins match the
        /// ones already on the map rather than sitting next to them in plain white. That color
        /// can fade between two shades, so it is asked for every frame. Found by name: there is
        /// no reference to that mod and no need for it to be installed, in which case portal pins
        /// stay white, the color vanilla gives every pin.
        /// </summary>
        internal static Color PortalPinColor()
        {
            if (!_probedTgm)
            {
                _probedTgm = true;
                var type = AccessTools.TypeByName("TheGreatestMap.Portals");
                if (type != null)
                {
                    var method = AccessTools.Method(type, "CurrentColor");
                    if (method != null && method.ReturnType == typeof(Color) && method.GetParameters().Length == 0)
                    {
                        try { _tgmPortalColor = AccessTools.MethodDelegate<Func<Color>>(method); }
                        catch (Exception e) { TheGreatestPortalMod.Log.LogWarning($"[TheGreatestPortal] Could not read TheGreatestMap's portal color; pins stay white: {e.Message}"); }
                    }
                    // An older TheGreatestMap has no portal color of its own. Not a fault, just white pins.
                    else TheGreatestPortalMod.Log.LogInfo("[TheGreatestPortal] TheGreatestMap is installed but has no portal color; pins stay white.");
                }
            }
            if (_tgmPortalColor == null) return Color.white;
            try { return _tgmPortalColor(); }
            catch { _tgmPortalColor = null; return Color.white; }
        }

        /// <summary>Vanilla's click radius for pins on the large map, in world metres.</summary>
        internal static float PinInteractRadius(Minimap map)
        {
            float r = map.m_removeRadius * (map.LargeZoom * 2f);
            if (ZInput.IsTouchActive()) r *= 1.3f;
            return r;
        }
    }
}
