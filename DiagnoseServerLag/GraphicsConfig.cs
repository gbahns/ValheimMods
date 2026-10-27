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
        /// Taken from GraphicsSettingsManager's own reads - every PlatformPrefs.GetInt/GetBool it
        /// makes when it loads settings - rather than from the enum names or from guessing. That
        /// distinction is not pedantic: the first version of this table mapped Vegetation to
        /// VegetationPatched because that key existed and looked right, and it was wrong. The game
        /// stores vegetation quality under ClutterQuality; VegetationPatched is a separate flag
        /// that happened to sit nearby in the registry. So the capture reported Vegetation=1 for a
        /// machine actually running quality 3, which is worse than the missing value it replaced,
        /// and is the exact failure the rest of this file is written to avoid.
        ///
        /// Checking the whole set against the loader instead of one setting at a time also turned
        /// up two that had been silently absent from every capture since the settings block was
        /// added, for the same reason and unnoticed because nobody had asked after them:
        /// DepthOfField is stored as DOF and SoftParticles as SoftPart.
        ///
        /// PlayerPrefs keys are case sensitive, which is why Vsync and VSync are two different
        /// lookups and why FpsLimit finds nothing under its own name.
        ///
        /// SSAO is the one entry that is not a rename. Both SSAO and SSAO_2 exist, holding
        /// different values on the same machine, which reads like a migration to a new key and a
        /// new scale - the loader confirms it, reading SSAO_2 first and only falling back to the
        /// old boolean SSAO when it is absent. The newer key wins here too, and the log says so
        /// whenever the two disagree.
        ///
        /// If a setting ever stops appearing in a capture, check it against the loader before
        /// assuming the machine did not report it.
        /// </summary>
        private static readonly Dictionary<string, string> Aliases = new Dictionary<string, string>
        {
            { "Vegetation",    "ClutterQuality" },
            { "LOD",           "LodBias" },
            { "FpsLimit",      "FPSLimit" },
            { "Vsync",         "VSync" },
            { "DepthOfField",  "DOF" },
            { "SoftParticles", "SoftPart" },
            { "SSAO",          "SSAO_2" },
        };

        /// <summary>The "Custom" preset: the only one that leaves the player's own values in force.</summary>
        private const int CustomPresetID = 100;

        /// <summary>
        /// Which graphics preset is in force, and what it changes about the values above.
        ///
        /// Everything else in this line comes out of PlatformPrefs, which is where the settings
        /// screen keeps the individual sliders. That is not necessarily what the game is running.
        /// GraphicsSettingsManager.ApplyGraphicsSettingsToCurrentSession applies
        /// GetCurrentSettingsWithCurrentPresetApplied, and for any preset other than Custom that
        /// overwrites the player's values with the preset's - without writing them back. So a
        /// player on "High" keeps whatever sliders they last customized sitting in the prefs,
        /// unused, and a capture that reads only the prefs describes a configuration that is not
        /// running.
        ///
        /// This cost a wrong diagnosis on 2026-10-27. Three of five players were reported as having
        /// lodBias "overridden" against what their menu implied, which read as a mod forcing quality
        /// up at runtime - GraphicsConfigPlus does exactly that and says so, so it fit. It was
        /// vanilla: those three were on presets, the other two on Custom, which is precisely why it
        /// showed on some machines and not others. The note sent Greg looking for a mod he had
        /// already disabled.
        ///
        /// The lesson is narrower than "check before blaming a mod": a disagreement between two
        /// sources is only evidence once you know which one the game reads.
        /// </summary>
        private static string PresetNote()
        {
            var mgr = GraphicsSettingsManager.Instance;
            if (mgr == null) return "";
            if (mgr.CurrentPresetID == CustomPresetID) return "preset=Custom";

            GraphicsSettingsPreset found;
            GraphicsSettingsState live = mgr.GetCurrentSettingsWithCurrentPresetApplied(true, out found);
            string name = found == null ? mgr.CurrentPresetID.ToString() : found.ToString();
            GraphicsSettingsState raw = mgr.CurrentPlayerSettingsRaw;

            var diff = new List<string>();
            if (live.m_lod != raw.m_lod) diff.Add($"LOD {raw.m_lod}->{live.m_lod}");
            if (live.m_shadowQuality != raw.m_shadowQuality) diff.Add($"ShadowQuality {raw.m_shadowQuality}->{live.m_shadowQuality}");
            if (live.m_lights != raw.m_lights) diff.Add($"Lights {raw.m_lights}->{live.m_lights}");
            if (live.m_vegetation != raw.m_vegetation) diff.Add($"Vegetation {raw.m_vegetation}->{live.m_vegetation}");
            if (live.m_pointLights != raw.m_pointLights) diff.Add($"PointLights {raw.m_pointLights}->{live.m_pointLights}");
            if (live.m_pointLightShadows != raw.m_pointLightShadows) diff.Add($"PointLightShadows {raw.m_pointLightShadows}->{live.m_pointLightShadows}");
            if (live.m_ssao != raw.m_ssao) diff.Add($"SSAO {raw.m_ssao}->{live.m_ssao}");
            if (live.m_clothQuality != raw.m_clothQuality) diff.Add($"ClothQuality {raw.m_clothQuality}->{live.m_clothQuality}");
            if (live.m_simulationDistance != raw.m_simulationDistance) diff.Add($"SimulationDistance {raw.m_simulationDistance}->{live.m_simulationDistance}");
            if (live.m_presentSettings.m_vsync != raw.m_presentSettings.m_vsync)
                diff.Add($"Vsync {Yes(raw.m_presentSettings.m_vsync)}->{Yes(live.m_presentSettings.m_vsync)}");
            if (live.m_presentSettings.m_fpsLimit != raw.m_presentSettings.m_fpsLimit)
                diff.Add($"FpsLimit {raw.m_presentSettings.m_fpsLimit}->{live.m_presentSettings.m_fpsLimit}");

            return diff.Count == 0
                ? $"preset={name}"
                : $"preset={name}, which is what is running and not the values above: " + string.Join(" ", diff.ToArray());
        }

        private static string Yes(bool b) { return b ? "on" : "off"; }

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

                // What is ACTUALLY in force, read off Unity rather than off the prefs.
                //
                // Everything above is what the settings menu was told. A mod that overrides a
                // quality setting at runtime - GraphicsConfigPlus does exactly this for LOD,
                // shadows and vsync, and says so - never touches the pref, so the capture reports
                // the menu's value and misses the one doing the work. Two captures twenty minutes
                // apart once came back byte-identical in this block while the frame time moved
                // 2.7 ms, and nothing here could say what had changed.
                //
                // These are printed plainly, for comparison between machines. There used to be a
                // note here claiming the menu "implies" some other lodBias and that the live value
                // was therefore overridden; see PresetNote for why that was wrong and what replaced
                // it.
                parts.Add($"live:lodBias={QualitySettings.lodBias:0.##}");
                parts.Add($"live:shadowDist={QualitySettings.shadowDistance:0}");
                parts.Add($"live:cascades={QualitySettings.shadowCascades}");
                parts.Add($"live:shadowRes={QualitySettings.shadowResolution}");
                parts.Add($"live:pixelLights={QualitySettings.pixelLightCount}");
                parts.Add($"live:aniso={QualitySettings.anisotropicFiltering}");
                parts.Add($"live:softParticles={(QualitySettings.softParticles ? "on" : "off")}");

                // Last, because it qualifies everything before it.
                string preset = PresetNote();
                if (!string.IsNullOrEmpty(preset)) parts.Add(preset);
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
