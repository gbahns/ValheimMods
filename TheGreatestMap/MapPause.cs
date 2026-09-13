using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace TheGreatestMap
{
    /// <summary>
    /// Option: pause the game while the large map screen is open, the way the ESC menu does.
    /// It goes through vanilla's own Game.Pause / Game.Unpause, the two calls the menu makes, so
    /// alone in a solo or hosted game the game freezes by itself, and on a dedicated server it is
    /// Pause My Server, if installed, that decides: it hooks those same two calls and grants the
    /// pause only while you are the only player online. Nothing here knows about that mod.
    /// </summary>
    internal static class MapPause
    {
        private static bool _holding;

        internal static bool Holding => _holding;

        /// <summary>Hold the pause exactly while the option is on, the large map is open and there is a player; let go otherwise.</summary>
        internal static void Refresh()
        {
            var map = Minimap.instance;
            bool want = TgmConfig.PauseWhileMapOpen != null && TgmConfig.PauseWhileMapOpen.Value
                && map != null && map.m_mode == Minimap.MapMode.Large && Player.m_localPlayer != null;
            if (want == _holding) return;
            _holding = want;
            if (want) Game.Pause();
            else Game.Unpause();
        }

        internal static void Release()
        {
            if (!_holding) return;
            _holding = false;
            Game.Unpause();
        }
    }

    [HarmonyPatch(typeof(Minimap), nameof(Minimap.SetMapMode))]
    internal static class Minimap_SetMapMode_Pause_Patch
    {
        private static void Postfix() => MapPause.Refresh();
    }

    /// <summary>
    /// The map screen keeps its own clocks on scaled time: the flick-scroll inertia damping, the
    /// click-versus-drag and double-click timing, the input delay after "show point on map" and
    /// the short delay around the pin name box. At time scale zero the inertia would never damp
    /// (the map slides forever after a flick) and any two clicks would count as a double-click.
    /// Switching those methods to unscaled time keeps the map screen usable while the game is
    /// paused, by this mod or by an admin pause; at time scale one nothing changes.
    /// </summary>
    [HarmonyPatch]
    internal static class Minimap_UnscaledTime_Patch
    {
        private static readonly string[] Methods =
        {
            "Update", "UpdateNameInput", "UpdateMap", "UpdateEventPin", "UpdatePersistentEventPins", "OnMapLeftDown", "OnMapLeftUp",
        };

        private static readonly MethodInfo GetTime = AccessTools.PropertyGetter(typeof(Time), nameof(Time.time));
        private static readonly MethodInfo GetUnscaledTime = AccessTools.PropertyGetter(typeof(Time), nameof(Time.unscaledTime));
        private static readonly MethodInfo GetDeltaTime = AccessTools.PropertyGetter(typeof(Time), nameof(Time.deltaTime));
        private static readonly MethodInfo GetUnscaledDeltaTime = AccessTools.PropertyGetter(typeof(Time), nameof(Time.unscaledDeltaTime));

        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var name in Methods)
            {
                var method = AccessTools.Method(typeof(Minimap), name);
                if (method != null) yield return method;
                else TheGreatestMapMod.Log.LogWarning($"[TheGreatestMap] Minimap.{name} not found; the map screen may misbehave while paused.");
            }
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(GetTime)) instruction.operand = GetUnscaledTime;
                else if (instruction.Calls(GetDeltaTime)) instruction.operand = GetUnscaledDeltaTime;
                yield return instruction;
            }
        }
    }
}
