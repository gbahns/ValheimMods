using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace DiagnoseServerLag
{
    /// <summary>
    /// What each player has their graphics set to.
    ///
    /// Hardware alone does not explain a frame time. Two machines of similar capability can differ
    /// twofold because one is drawing 5120x1440 with Level of Detail at Very High and the other is
    /// not, and without the settings a capture presents that as a hardware fault. This is the other
    /// half of the MACHINES block: what the machine is, and what it was asked to do.
    ///
    /// The list is taken from the game's own two enums rather than written out here, so a setting
    /// Iron Gate adds appears by itself and one they remove disappears. Valheim stores each under
    /// PlatformPrefs keyed by the enum member's name - GraphicsSettingBool.SSAO is read with
    /// PlatformPrefs.GetBool("SSAO", ...) - which is what makes walking the enums possible.
    ///
    /// PlatformPrefs lives outside assembly_valheim, so it is reached by reflection rather than by
    /// adding a reference for one call. A key that cannot be read is left out rather than guessed
    /// at: a wrong setting in a comparison is worse than a missing one.
    /// </summary>
    internal static class GraphicsConfig
    {
        private static MethodInfo _getInt, _getBool;
        private static bool _looked;

        private static readonly object[] _two = new object[2];

        private static void Look()
        {
            if (_looked) return;
            _looked = true;
            try
            {
                var t = AccessTools.TypeByName("PlatformPrefs");
                if (t == null) return;
                _getInt = AccessTools.Method(t, "GetInt", new[] { typeof(string), typeof(int) });
                _getBool = AccessTools.Method(t, "GetBool", new[] { typeof(string), typeof(bool) });
            }
            catch { }
        }

        private static int? Int(string key)
        {
            if (_getInt == null) return null;
            try
            {
                _two[0] = key; _two[1] = int.MinValue;
                int v = (int)_getInt.Invoke(null, _two);
                return v == int.MinValue ? (int?)null : v;   // absent, rather than zero
            }
            catch { return null; }
        }

        private static bool? Bool(string key)
        {
            if (_getBool == null) return null;
            try
            {
                // Asked twice with opposite defaults: equal answers mean the key is really there,
                // different ones mean it is absent and we are reading our own default back.
                _two[0] = key; _two[1] = false;
                bool a = (bool)_getBool.Invoke(null, _two);
                _two[1] = true;
                bool b = (bool)_getBool.Invoke(null, _two);
                return a == b ? (bool?)a : null;
            }
            catch { return null; }
        }

        /// <summary>
        /// Every graphics setting this machine has, as one compact line, or "" if none can be read.
        /// </summary>
        internal static string Describe()
        {
            Look();
            var parts = new List<string>();
            try
            {
                parts.Add($"{Screen.width}x{Screen.height}");
                parts.Add(Screen.fullScreen ? "fullscreen" : "windowed");

                foreach (var v in Enum.GetValues(typeof(GraphicsSettingInt)))
                {
                    string name = v.ToString();
                    if (name == "None") continue;
                    int? got = Int(name);
                    if (got.HasValue) parts.Add($"{name}={got.Value}");
                }
                foreach (var v in Enum.GetValues(typeof(GraphicsSettingBool)))
                {
                    string name = v.ToString();
                    if (name == "None") continue;
                    bool? got = Bool(name);
                    if (got.HasValue) parts.Add($"{name}={(got.Value ? "on" : "off")}");
                }

                // Unity's own, which the game's enums do not cover.
                parts.Add($"vsyncCount={QualitySettings.vSyncCount}");
                if (Application.targetFrameRate > 0) parts.Add($"fpsCap={Application.targetFrameRate}");
            }
            catch (Exception e)
            {
                DiagnoseServerLagMod.Log.LogWarning($"[DiagnoseServerLag] could not read graphics settings: {e.Message}");
            }

            if (parts.Count <= 2) return "";      // resolution alone is not worth a line
            var sb = new StringBuilder();
            for (int i = 0; i < parts.Count; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(parts[i]);
            }
            return sb.ToString();
        }
    }
}
