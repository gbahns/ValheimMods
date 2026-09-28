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

        /// <summary>Where the large map is looking, as an offset from the player.</summary>
        internal static readonly AccessTools.FieldRef<Minimap, Vector3> MapOffset =
            AccessTools.FieldRefAccess<Minimap, Vector3>("m_mapOffset");

        /// <summary>What is left of a fling after the player lets go of a drag.</summary>
        internal static readonly AccessTools.FieldRef<Minimap, Vector3> MoveInertia =
            AccessTools.FieldRefAccess<Minimap, Vector3>("m_moveInertia");

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
        private static Action<string> _claimObituary;
        private static bool _probedObituaries;

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

        /// <summary>
        /// Says what this mod's own killing should read as, for TheObituaries. Valheim records
        /// only the blow, so a death dealt by a mod carries no cause at all and would otherwise be
        /// announced with the blank line kept for deaths nothing is known about. Found by name,
        /// like the map color above: without that mod nothing is announced, which is how it was.
        /// </summary>
        internal static void ClaimObituary(string line)
        {
            if (!_probedObituaries)
            {
                _probedObituaries = true;
                var type = AccessTools.TypeByName("TheObituaries.Api");
                if (type != null)
                {
                    var method = AccessTools.Method(type, "NextDeath", new[] { typeof(string) });
                    if (method != null)
                    {
                        try { _claimObituary = AccessTools.MethodDelegate<Action<string>>(method); }
                        catch (Exception e) { TheGreatestPortalMod.Log.LogWarning($"[TheGreatestPortal] Could not reach TheObituaries; deaths are announced as it sees fit: {e.Message}"); }
                    }
                    // An older TheObituaries has no line to claim. Not a fault, just a plainer obituary.
                    else TheGreatestPortalMod.Log.LogInfo("[TheGreatestPortal] TheObituaries is installed but takes no death line; deaths are announced as it sees fit.");
                }
            }
            if (_claimObituary == null) return;
            try { _claimObituary(line); }
            catch { _claimObituary = null; }
        }

        // Moving the map means setting its offset and telling it to look there, exactly as
        // vanilla's own drag does. Bound as a delegate: a pan calls it every frame.
        private delegate void CenterMapCall(Minimap map, Vector3 point);
        private static readonly CenterMapCall _centerMap = BindCenterMap();

        private static CenterMapCall BindCenterMap()
        {
            try
            {
                var method = AccessTools.Method(typeof(Minimap), "CenterMap", new[] { typeof(Vector3) });
                if (method != null) return AccessTools.MethodDelegate<CenterMapCall>(method, null, virtualCall: false);
                TheGreatestPortalMod.Log.LogWarning("[TheGreatestPortal] Minimap.CenterMap was not found; the map will jump to a portal rather than pan.");
            }
            catch (Exception e)
            {
                TheGreatestPortalMod.Log.LogWarning($"[TheGreatestPortal] Could not bind Minimap.CenterMap; the map will jump rather than pan: {e.Message}");
            }
            return null;
        }

        internal static bool CanPan => _centerMap != null;

        internal static void CenterMap(Minimap map, Vector3 point)
        {
            if (map == null || _centerMap == null) return;
            _centerMap(map, point);
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
