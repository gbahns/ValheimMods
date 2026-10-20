using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace DiagnoseServerLag
{
    /// <summary>
    /// Where the main thread's time actually goes, per named method - and, on request, per mod
    /// patch inside a named method.
    ///
    /// Every other measurement in this mod says how long a frame took and, since 0.10.13, whether
    /// the thread was computing or waiting. Neither can say WHICH work grew, and that is the one
    /// question a week of captures could not answer.
    ///
    /// What those captures did establish: a client's frame time rises about 1.6 ms over ninety
    /// minutes, a restart resets it, the GPU is never involved, and the rise is spread across the
    /// whole frame-time distribution rather than concentrated in a tail - the floor rose 4.8% while
    /// the median rose 4.7%. That rules out collection pauses and means steady per-frame work,
    /// which is attributable to specific calls. ZDO count, managed heap, travel, base visits,
    /// simulation distance and every graphics setting had each been tried as a cause and failed.
    ///
    /// The first reading found something nobody was looking for: Hud.Update at 26 ms per call, two
    /// thirds of a 39 ms frame, constant rather than growing. A constant is invisible to every
    /// comparison - which is exactly why a week of differencing one capture against another never
    /// saw it, and why an absolute measurement was needed.
    ///
    /// Usage is a pair of windows, never one absolute: `dsl_hotspots on`, play a few minutes,
    /// `dsl_hotspots`. Timing adds a little to every call, so a single reading is slightly high
    /// while a difference between two is not.
    ///
    /// OFF by default. It patches hot vanilla methods, and `drill` patches other mods' patches,
    /// neither of which a diagnostic should do to somebody who only wanted their frame time.
    /// </summary>
    internal static class Hotspots
    {
        /// <summary>
        /// One timed thing. <see cref="Depth"/> is how deeply it nests inside the entries above
        /// it, which matters because a parent's time INCLUDES its children's: ZNet.Update contains
        /// ZDOMan.Update, ZNetScene.Update contains CreateDestroyObjects, and that contains
        /// CreateObjects and RemoveObjects. Totalling the column would double-count. The nesting
        /// is recorded rather than avoided because a parent that grew while none of its children
        /// did is itself a finding - the cost is in the parent's own body.
        /// </summary>
        private sealed class Entry
        {
            public string Label;
            public int Depth;
            public long Ticks;
            public long Calls;
            /// <summary>Set for entries added by <see cref="Drill"/>, so they can be grouped.</summary>
            public string ParentLabel;
            public Entry(string label, int depth, string parent = null)
            { Label = label; Depth = depth; ParentLabel = parent; }
        }

        private struct Target
        {
            public string Label, TypeName, Method;
            public int Depth;
            public Target(string label, string type, string method, int depth)
            { Label = label; TypeName = type; Method = method; Depth = depth; }
        }

        /// <summary>
        /// Chosen to be called once or a few times per frame. Nothing per-object is here on
        /// purpose: WearNTear.UpdateWear would be the most interesting entry in a 4,000-piece base
        /// and is called thousands of times a frame, where a Stopwatch per call would cost more
        /// than the thing it measures and move the number it is trying to read.
        /// </summary>
        private static readonly Target[] Targets =
        {
            new Target("ZNet.Update",                      "ZNet",          "Update",               0),
            new Target("  ZDOMan.Update",                  "ZDOMan",        "Update",               1),
            new Target("  ZSteamSocket.UpdateAllSockets",  "ZSteamSocket",  "UpdateAllSockets",     1),
            new Target("ZNetScene.Update",                 "ZNetScene",     "Update",               0),
            new Target("  ZNetScene.CreateDestroyObjects", "ZNetScene",     "CreateDestroyObjects", 1),
            new Target("    ZNetScene.CreateObjects",      "ZNetScene",     "CreateObjects",        2),
            new Target("    ZNetScene.RemoveObjects",      "ZNetScene",     "RemoveObjects",        2),
            new Target("ZoneSystem.Update",                "ZoneSystem",    "Update",               0),
            new Target("  ZoneSystem.CreateLocalZones",    "ZoneSystem",    "CreateLocalZones",     1),
            new Target("  ZoneSystem.CreateGhostZones",    "ZoneSystem",    "CreateGhostZones",     1),
            new Target("Game.Update",                      "Game",          "Update",               0),
            new Target("Player.Update",                    "Player",        "Update",               0),
            new Target("Minimap.Update",                   "Minimap",       "Update",               0),
            new Target("Hud.Update",                       "Hud",           "Update",               0),
            new Target("EnvMan.Update",                    "EnvMan",        "Update",               0),
            new Target("ClutterSystem.LateUpdate",         "ClutterSystem", "LateUpdate",           0),
            new Target("Pathfinding.Update",               "Pathfinding",   "Update",               0),
            new Target("MonoUpdaters.Update",              "MonoUpdaters",  "Update",               0),
            new Target("Heightmap.Regenerate",             "Heightmap",     "Regenerate",           0),
            new Target("LightLod.UpdateLights",            "LightLod",      "UpdateLights",         0),
        };

        private static readonly List<Entry> _entries = new List<Entry>();

        /// <summary>
        /// Harmony hands the prefix the method it wrapped, so one shared pair of patches serves
        /// every target and the slot is found by lookup. A dictionary probe costs tens of
        /// nanoseconds, which against methods called once a frame is not measurable.
        /// </summary>
        private static readonly Dictionary<MethodBase, int> _slot = new Dictionary<MethodBase, int>();

        private static Harmony _harmony;
        private static float _since = -1f;
        private static readonly List<string> _missing = new List<string>();
        private static readonly List<string> _drilled = new List<string>();

        internal static bool Running => _harmony != null;

        private static HarmonyMethod Enter_ =>
            new HarmonyMethod(typeof(Hotspots).GetMethod(nameof(Enter), BindingFlags.NonPublic | BindingFlags.Static));
        private static HarmonyMethod Exit_ =>
            new HarmonyMethod(typeof(Hotspots).GetMethod(nameof(Exit), BindingFlags.NonPublic | BindingFlags.Static));

        internal static string Start()
        {
            if (_harmony != null) { Reset(); return "hotspot timing was already on; counters reset."; }

            _harmony = new Harmony("DeathMonger.DiagnoseServerLag.hotspots");
            _slot.Clear(); _entries.Clear(); _missing.Clear(); _drilled.Clear();

            int patched = 0;
            foreach (var t in Targets)
            {
                var method = Find(t.TypeName, t.Method);
                if (method == null) { _missing.Add(t.Label.Trim()); continue; }
                if (TimeIt(method, t.Label, t.Depth, null)) patched++;
                else _missing.Add(t.Label.Trim());
            }

            Reset();
            string note = "hotspot timing on: " + patched + " of " + Targets.Length + " methods.";
            if (_missing.Count > 0) note += " Not found in this build: " + string.Join(", ", _missing.ToArray()) + ".";
            note += " Play for a few minutes, then run dsl_hotspots to read it.";
            DiagnoseServerLagMod.Log.LogInfo("[DiagnoseServerLag] " + note);
            return note;
        }

        private static MethodBase Find(string typeName, string method)
        {
            try
            {
                var type = AccessTools.TypeByName(typeName);
                var m = type == null ? null : AccessTools.Method(type, method);
                if (m == null || m.IsAbstract || m.ContainsGenericParameters) return null;
                return m;
            }
            catch { return null; }
        }

        private static bool TimeIt(MethodBase method, string label, int depth, string parent)
        {
            if (_slot.ContainsKey(method)) return false;        // already timed
            try
            {
                int i = _entries.Count;
                _entries.Add(new Entry(label, depth, parent));
                _slot[method] = i;
                _harmony.Patch(method, prefix: Enter_, postfix: Exit_);
                return true;
            }
            catch (Exception e)
            {
                DiagnoseServerLagMod.Log.LogWarning(
                    "[DiagnoseServerLag] could not time " + label.Trim() + ": " + e.Message);
                if (_entries.Count > 0 && _entries[_entries.Count - 1].Label == label)
                {
                    _slot.Remove(method);
                    _entries.RemoveAt(_entries.Count - 1);
                }
                return false;
            }
        }

        /// <summary>
        /// Times every mod patch on one already-timed method, so a hot entry names a culprit
        /// instead of only a method.
        ///
        /// This works because Harmony does not inline a patch body into the method it patches - it
        /// generates a wrapper that CALLS the prefix and postfix methods. Those are ordinary static
        /// methods, so they can be patched in turn, and timing them attributes the parent's cost to
        /// the mods inside it. It is the alternative to disabling mods one at a time, which needs a
        /// restart per mod and cannot separate two that are both hot.
        ///
        /// Two things it cannot see, and both are reported as unattributed rather than hidden:
        /// transpilers, which run once when the patch is applied and bake their output into the
        /// method body so there is nothing left to time at run time; and hooks applied by anything
        /// other than Harmony, which GetPatchInfo does not know about. Whatever is left after the
        /// timed patches are subtracted is the vanilla body plus those.
        /// </summary>
        internal static string Drill(string typeName, string methodName)
        {
            if (_harmony == null) return "Hotspot timing is off. Run: dsl_hotspots on";
            var method = Find(typeName, methodName);
            if (method == null) return "No method called " + typeName + "." + methodName + " in this build.";

            var info = Harmony.GetPatchInfo(method);
            if (info == null) return "Nothing has patched " + typeName + "." + methodName + ".";

            string parentLabel = typeName + "." + methodName;
            int added = 0, skipped = 0;
            added += DrillOne(info.Prefixes, "prefix", parentLabel, ref skipped);
            added += DrillOne(info.Postfixes, "postfix", parentLabel, ref skipped);
            added += DrillOne(info.Finalizers, "finalizer", parentLabel, ref skipped);

            int transpilers = info.Transpilers == null ? 0 : info.Transpilers.Count;
            if (added > 0 && !_drilled.Contains(parentLabel)) _drilled.Add(parentLabel);

            Reset();      // the new entries start from zero, so everything must
            var sb = new StringBuilder();
            sb.Append("timing ").Append(added).Append(" patch(es) inside ").Append(parentLabel);
            if (skipped > 0) sb.Append("; ").Append(skipped).Append(" could not be patched");
            if (transpilers > 0)
                sb.Append("; ").Append(transpilers).Append(" transpiler(s) are baked into the body and cannot be timed");
            sb.Append(". Counters reset - play a few minutes, then dsl_hotspots.");
            DiagnoseServerLagMod.Log.LogInfo("[DiagnoseServerLag] " + sb);
            return sb.ToString();
        }

        private static int DrillOne(IList<Patch> patches, string kind, string parentLabel, ref int skipped)
        {
            if (patches == null) return 0;
            int n = 0;
            foreach (var p in patches)
            {
                if (p == null || p.PatchMethod == null) continue;
                string owner = string.IsNullOrEmpty(p.owner) ? "(unknown)" : p.owner;
                // Skip this timer's own wrappers: timing them would measure the measurement.
                if (owner.IndexOf("DiagnoseServerLag.hotspots", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                string label = "    [" + kind + "] " + owner;
                if (TimeIt(p.PatchMethod, label, 1, parentLabel)) n++;
                else skipped++;
            }
            return n;
        }

        /// <summary>Who has patched each timed method, without timing any of it.</summary>
        internal static string Patches()
        {
            var sb = new StringBuilder();
            sb.AppendLine("who has patched the timed methods");
            sb.AppendLine("  the timer includes these, so a hot method may be hot because of them");
            sb.AppendLine();
            foreach (var t in Targets)
            {
                var method = Find(t.TypeName, t.Method);
                if (method == null) continue;
                var info = Harmony.GetPatchInfo(method);
                if (info == null) continue;
                var owners = new List<string>();
                Owners(owners, info.Prefixes, "prefix");
                Owners(owners, info.Postfixes, "postfix");
                Owners(owners, info.Transpilers, "transpiler");
                Owners(owners, info.Finalizers, "finalizer");
                if (owners.Count == 0) continue;
                sb.AppendLine("  " + t.Label.Trim() + "   (" + t.TypeName + "." + t.Method + ")");
                foreach (var o in owners) sb.AppendLine("      " + o);
            }
            sb.AppendLine();
            sb.AppendLine("  To time them individually: dsl_hotspots drill <Type>.<Method>");
            sb.AppendLine("  Transpilers cannot be timed - they run once and bake into the body.");
            return sb.ToString();
        }

        private static void Owners(List<string> into, IList<Patch> patches, string kind)
        {
            if (patches == null) return;
            foreach (var p in patches)
            {
                if (p == null) continue;
                string owner = string.IsNullOrEmpty(p.owner) ? "(unknown)" : p.owner;
                if (owner.IndexOf("DiagnoseServerLag.hotspots", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                into.Add(kind.PadRight(11) + owner);
            }
        }

        internal static string Stop()
        {
            if (_harmony == null) return "hotspot timing is already off.";
            try { _harmony.UnpatchSelf(); }
            catch (Exception e)
            {
                DiagnoseServerLagMod.Log.LogWarning(
                    "[DiagnoseServerLag] could not remove the hotspot patches: " + e.Message);
            }
            _harmony = null;
            _slot.Clear();
            return "hotspot timing off.";
        }

        internal static void Reset()
        {
            foreach (var e in _entries) { e.Ticks = 0L; e.Calls = 0L; }
            _since = Time.unscaledTime;
        }

        // Stopwatch rather than Time, because this measures a span inside one frame and
        // Time.unscaledTime does not change within a frame at all.
        private static void Enter(MethodBase __originalMethod, out long __state)
        {
            __state = Stopwatch.GetTimestamp();
        }

        private static void Exit(MethodBase __originalMethod, long __state)
        {
            int i;
            if (!_slot.TryGetValue(__originalMethod, out i)) return;
            var e = _entries[i];
            e.Ticks += Stopwatch.GetTimestamp() - __state;
            e.Calls++;
        }

        /// <summary>
        /// Milliseconds of main-thread time per second of wall clock, in declaration order so the
        /// nesting stays visible. Sorting by cost would scatter a parent away from its children
        /// and make the double-counting impossible to read.
        /// </summary>
        internal static string Report()
        {
            if (_since < 0f) return "Hotspot timing has not been started. Run: dsl_hotspots on";
            float span = Time.unscaledTime - _since;
            if (span < 1f) return "Only " + span.ToString("0.0") + "s measured so far - give it a few minutes.";

            double perSec = 1000.0 / Stopwatch.Frequency;
            var sb = new StringBuilder();
            sb.AppendLine("main-thread time by method, over " + span.ToString("0") + "s"
                          + (_harmony == null ? "  (timing is now OFF; this is the last window)" : ""));
            sb.AppendLine("  indented entries are INSIDE the one above - do not total the column");
            sb.AppendLine();
            sb.AppendLine("  " + "method".PadRight(44) + "ms/s      per call     calls/s");

            foreach (var e in _entries)
            {
                if (e.Calls == 0L) continue;
                double ms = e.Ticks * perSec / span;
                double each = e.Ticks * perSec / e.Calls;
                sb.AppendLine("  " + e.Label.PadRight(44)
                              + ms.ToString("0.00").PadLeft(6)
                              + each.ToString("0.000").PadLeft(12)
                              + (e.Calls / span).ToString("0.0").PadLeft(12));
            }

            // For each drilled method, say how much of it the timed patches did NOT account for.
            // That remainder is the vanilla body plus anything untimeable - transpiled changes and
            // non-Harmony hooks - and naming it stops the table reading as though it were complete.
            foreach (var parent in _drilled)
            {
                Entry head = null;
                double kids = 0.0;
                foreach (var e in _entries)
                {
                    if (e.ParentLabel == parent) kids += e.Ticks * perSec / span;
                    else if (e.Label.Trim() == parent) head = e;
                }
                if (head == null || head.Calls == 0L) continue;
                double total = head.Ticks * perSec / span;
                sb.AppendLine();
                sb.AppendLine("  " + parent + ": " + total.ToString("0.00") + " ms/s total, "
                              + kids.ToString("0.00") + " in timed patches, "
                              + Math.Max(0.0, total - kids).ToString("0.00")
                              + " unattributed (vanilla body, transpilers, non-Harmony hooks)");
            }

            sb.AppendLine();
            sb.AppendLine("  Timing adds a little to each call, so these are slightly high in absolute");
            sb.AppendLine("  terms. Compare two windows rather than one against zero. Nothing per-object");
            sb.AppendLine("  is timed - WearNTear.UpdateWear and the like are called too often.");
            return sb.ToString();
        }
    }
}
