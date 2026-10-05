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
        /// The key a setting is really stored under, where that is not its enum member name.
        ///
        /// Three settings never appeared in a capture - Vegetation, LOD and FpsLimit - and walking
        /// the enums was not the problem. The enum member and the stored key are simply spelled
        /// differently, and because these end up in PlayerPrefs, whose keys are case sensitive,
        /// "Vsync" and "VSync" are two unrelated lookups as far as a read is concerned. Taken from
        /// the keys Unity had actually written, not guessed:
        ///
        ///   Vegetation -> VegetationPatched,  LOD -> LodBias,
        ///   FpsLimit   -> FPSLimit,           Vsync -> VSync
        ///
        /// SSAO is a different case and the reason this prefers the alias rather than only falling
        /// back to it: both SSAO and SSAO_2 exist, holding different values, which reads like the
        /// setting having been migrated to a new key and a new scale. The newer key is taken as the
        /// live one. If an SSAO value here ever looks wrong, that assumption is the first thing to
        /// check - and the log says so whenever the two disagree.
        /// </summary>
        private static readonly Dictionary<string, string> Aliases = new Dictionary<string, string>
        {
            { "Vegetation", "VegetationPatched" },
            { "LOD",        "LodBias" },
            { "FpsLimit",   "FPSLimit" },
            { "Vsync",      "VSync" },
            { "SSAO",       "SSAO_2" },
        };

        private static string Alias(string name)
        {
            string a;
            return Aliases.TryGetValue(name, out a) ? a : null;
        }

        /// <summary>
        /// Notes once, per setting, that the enum-named key and the aliased one disagree - so a
        /// value that turns out to be stale leaves a trail instead of being silently preferred.
        /// </summary>
        private static readonly HashSet<string> _reported = new HashSet<string>();

        private static void NoteDisagreement(string name, string alias, object underName, object underAlias)
        {
            if (!_reported.Add(name)) return;
            DiagnoseServerLagMod.Log.LogInfo(
                $"[DiagnoseServerLag] {name} is stored twice: {name}={underName} and {alias}={underAlias}. " +
                $"Reporting {alias}, on the assumption it is the current key.");
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
                    string alias = Alias(name);
                    int? plain = Int(name);
                    int? aliased = alias == null ? null : Int(alias);
                    if (plain.HasValue && aliased.HasValue && plain.Value != aliased.Value)
                        NoteDisagreement(name, alias, plain.Value, aliased.Value);
                    // Reported under the enum name either way: that is what the settings screen
                    // calls it, and nobody comparing two machines should have to know the alias.
                    int? got = aliased ?? plain;
                    if (got.HasValue) parts.Add($"{name}={got.Value}");
                }
                foreach (var v in Enum.GetValues(typeof(GraphicsSettingBool)))
                {
                    string name = v.ToString();
                    if (name == "None") continue;
                    string alias = Alias(name);
                    bool? plain = Bool(name);
                    bool? aliased = alias == null ? null : Bool(alias);
                    if (plain.HasValue && aliased.HasValue && plain.Value != aliased.Value)
                        NoteDisagreement(name, alias, plain.Value, aliased.Value);
                    bool? got = aliased ?? plain;
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
