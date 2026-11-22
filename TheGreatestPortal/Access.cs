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
        private static Func<string, Sprite> _tgmPortalIcon;
        private static bool _probedPortalIcon;
        private static Func<string, Color> _tgmPortalColorFor;
        private static bool _probedPortalColorFor;
        private static Func<int> _tgmLookEpoch;
        private static bool _probedLookEpoch;
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
        /// The picture TheGreatestMap draws this portal with, or null if it is not installed or
        /// is too old to answer.
        ///
        /// <para>That mod owns what a portal looks like, and this mod asks rather than decides.
        /// It has a setting with four answers in it -- plain colored pins, the game's own render,
        /// and two weights of the color traced around that render -- and its own portal markers
        /// stand down while this mod's picker is open. So if the two disagreed, one portal would
        /// change appearance as the picker opened and closed, which reads as a bug rather than as
        /// two mods. Asked afresh every time the pins are built: the setting moves while the game
        /// is running.</para>
        /// </summary>
        internal static Sprite PortalIcon(string prefab)
        {
            if (!_probedPortalIcon)
            {
                _probedPortalIcon = true;
                var type = AccessTools.TypeByName("TheGreatestMap.Portals");
                var method = type != null ? AccessTools.Method(type, "PortalIcon", new[] { typeof(string) }) : null;
                if (method != null && method.ReturnType == typeof(Sprite))
                {
                    try { _tgmPortalIcon = AccessTools.MethodDelegate<Func<string, Sprite>>(method); }
                    catch (Exception e) { TheGreatestPortalMod.Log.LogWarning($"[TheGreatestPortal] Could not read TheGreatestMap's portal pictures; drawing our own: {e.Message}"); }
                }
            }
            if (_tgmPortalIcon == null) return null;
            try { return _tgmPortalIcon(prefab ?? ""); }
            catch { _tgmPortalIcon = null; return null; }
        }

        /// <summary>
        /// A number from TheGreatestMap that changes whenever the picture it would give us
        /// changes, or null if it cannot say. Compared, never counted: it promises only to
        /// differ.
        ///
        /// <para>Worth asking every frame rather than when the pins are next built, because the
        /// pictures with the portal color traced around them are baked there, and moving a color
        /// destroys the old sprite and its texture rather than leaking one per frame of a drag.
        /// We are holding those sprites. So nudging a color picker with the portal map open does
        /// not leave our pins looking stale -- it leaves them drawing nothing at all, for as long
        /// as the map stays open, which is exactly as long as a player would be nudging it.</para>
        ///
        /// <para>That mod bumps this after it has baked the replacements, never before, so a
        /// number that has changed always means the new pictures already exist. The only gap
        /// left is the ordinary one of having read it earlier in the same frame than it was
        /// bumped.</para>
        ///
        /// <para>No such method means a mod old enough to bake nothing and destroy nothing, so
        /// absence reads as "never changes" and the check made when the pins are built is enough
        /// on its own.</para>
        /// </summary>
        internal static int? LookEpoch()
        {
            if (!_probedLookEpoch)
            {
                _probedLookEpoch = true;
                var type = AccessTools.TypeByName("TheGreatestMap.Portals");
                var method = type != null ? AccessTools.Method(type, "LookEpoch") : null;
                if (method != null && method.ReturnType == typeof(int) && method.GetParameters().Length == 0)
                {
                    try { _tgmLookEpoch = AccessTools.MethodDelegate<Func<int>>(method); }
                    catch (Exception e) { TheGreatestPortalMod.Log.LogWarning($"[TheGreatestPortal] Could not read TheGreatestMap's marker style signal; following it only as the pins are built: {e.Message}"); }
                }
            }
            if (_tgmLookEpoch == null) return null;
            try { return _tgmLookEpoch(); }
            catch { _tgmLookEpoch = null; return null; }
        }

        /// <summary>
        /// The color TheGreatestMap paints that picture, for this one portal, or null if it
        /// cannot say.
        ///
        /// <para>Per portal rather than one color for the lot, because there is no single answer
        /// any more: an ore-carrying portal has its own color, and a picture of a portal is not
        /// painted at all but breathed in brightness, since painting a picture takes the picture
        /// away. Reading one color for everything is how a portal came to be blue on one map and
        /// orange on the other. Asked once a frame per portal, which that mod is built for.</para>
        /// </summary>
        internal static Color? PortalColor(string prefab)
        {
            if (!_probedPortalColorFor)
            {
                _probedPortalColorFor = true;
                var type = AccessTools.TypeByName("TheGreatestMap.Portals");
                var method = type != null ? AccessTools.Method(type, "PortalColor", new[] { typeof(string) }) : null;
                if (method != null && method.ReturnType == typeof(Color))
                {
                    try { _tgmPortalColorFor = AccessTools.MethodDelegate<Func<string, Color>>(method); }
                    catch (Exception e) { TheGreatestPortalMod.Log.LogWarning($"[TheGreatestPortal] Could not read TheGreatestMap's portal colors; using our own: {e.Message}"); }
                }
            }
            if (_tgmPortalColorFor == null) return null;
            try { return _tgmPortalColorFor(prefab ?? ""); }
            catch { _tgmPortalColorFor = null; return null; }
        }

        /// <summary>
        /// The picture TheGreatestMap draws an ore-carrying portal with, so the two mods mark a
        /// stone portal the same way and a player moving between the ordinary map and this mod's
        /// own sees one convention rather than two.
        ///
        /// Asked for afresh each time rather than cached, because that mod builds its pictures
        /// once the map exists and this can be called before it has. Null is the ordinary answer
        /// on a game without it, and the caller draws its own instead.
        ///
        /// Superseded by <see cref="PortalIcon"/>, which answers for any portal rather than this
        /// one kind. Kept because a TheGreatestMap from before that seam existed still answers
        /// this one, and half an agreement is better than none.
        /// </summary>
        internal static Sprite StonePortalIcon()
        {
            var type = AccessTools.TypeByName("TheGreatestMap.Portals");
            if (type == null) return null;
            var method = AccessTools.Method(type, "StoneIcon");
            if (method == null || method.ReturnType != typeof(Sprite) || method.GetParameters().Length != 0)
                return null;
            try { return method.Invoke(null, null) as Sprite; }
            catch (Exception e)
            {
                TheGreatestPortalMod.Log.LogWarning($"[TheGreatestPortal] Could not read TheGreatestMap's stone portal icon; drawing our own: {e.Message}");
                return null;
            }
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
