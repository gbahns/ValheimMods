using System.Collections.Generic;
using System.Reflection;
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

        private static readonly MethodInfo _screenToWorldPoint = AccessTools.Method(typeof(Minimap), "ScreenToWorldPoint", new[] { typeof(Vector3) });

        /// <summary>The world position under a screen point on the large map.</summary>
        internal static Vector3 ScreenToWorldPoint(Minimap map, Vector3 screen)
        {
            if (map == null || _screenToWorldPoint == null) return Vector3.zero;
            return (Vector3)_screenToWorldPoint.Invoke(map, new object[] { screen });
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
