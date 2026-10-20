using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace DiagnoseServerLag
{
    /// <summary>
    /// Notices when a graphics setting changes, and says which one, at what second.
    ///
    /// Every capture records the graphics settings once, when the command runs. That answers "what
    /// were they" and never "when did they change", which turned out to be the question that
    /// mattered: a week of A/B testing was spent comparing captures that each spanned a change
    /// made somewhere in the middle, and three causal claims were made and retracted because the
    /// moment of the change was unknown and the frame time was drifting anyway.
    ///
    /// Two sources, because one is not enough:
    ///
    ///  * GraphicsSettingsManager.GraphicsSettingsChanged fires the instant a vanilla setting is
    ///    applied, which catches menu changes exactly.
    ///  * A poll every few seconds catches everything else. Mods that write QualitySettings
    ///    directly - GraphicsConfigPlus does this for LOD bias, shadow distance, cascades and
    ///    resolution, and applies them on a keypress - never raise that event, so the event alone
    ///    would miss precisely the settings that are hardest to track by hand.
    ///
    /// The description is the same one the capture prints, so a change is reported in the same
    /// vocabulary as the settings line it came from.
    /// </summary>
    internal static class GraphicsWatch
    {
        /// <summary>One recorded change, for the report.</summary>
        internal struct Change
        {
            public long UtcTicks;
            public string What;      // "Target3DResolutionVertical 900 -> 1080"
        }

        private const float PollSeconds = 5f;

        /// <summary>
        /// Bumped on every detected change. Recorded per second, so any window that contains more
        /// than one value spans a change and should not be read as a single configuration.
        /// </summary>
        internal static int Revision { get; private set; }

        private static readonly List<Change> _changes = new List<Change>();
        private static string _last;
        private static float _nextPoll;
        private static bool _hooked;
        private static bool _dirty;

        internal static void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            try { GraphicsSettingsManager.GraphicsSettingsChanged += OnChanged; }
            catch (Exception e)
            {
                // Not fatal: the poll below still catches everything, just up to PollSeconds late.
                DiagnoseServerLagMod.Log.LogWarning(
                    "[DiagnoseServerLag] could not subscribe to GraphicsSettingsChanged: " + e.Message);
            }
        }

        internal static void Unhook()
        {
            if (!_hooked) return;
            _hooked = false;
            try { GraphicsSettingsManager.GraphicsSettingsChanged -= OnChanged; } catch { }
        }

        /// <summary>The event only says "something changed", so the comparison still has to run.</summary>
        private static void OnChanged() { _dirty = true; }

        /// <summary>Called once a second from the sampler. Cheap unless something actually moved.</summary>
        internal static void Tick(float now)
        {
            bool due = now >= _nextPoll;
            if (!_dirty && !due) return;
            _dirty = false;
            _nextPoll = now + PollSeconds;

            string current;
            try { current = GraphicsConfig.Describe(); }
            catch { return; }
            if (string.IsNullOrEmpty(current)) return;

            if (_last == null) { _last = current; return; }   // first read is the baseline, not a change
            if (current == _last) return;

            string what = Diff(_last, current);
            _last = current;
            Revision++;
            var c = new Change { UtcTicks = DateTime.UtcNow.Ticks, What = what };
            _changes.Add(c);
            if (_changes.Count > 64) _changes.RemoveAt(0);
            DiagnoseServerLagMod.Log.LogInfo("[DiagnoseServerLag] graphics setting changed: " + what);
        }

        /// <summary>
        /// Names what moved between two settings lines, as "key old -> new" pairs.
        ///
        /// Both lines are space-separated key=value tokens from the same producer, so a token-wise
        /// comparison is exact rather than a text diff. A token without '=' - "fullscreen", or the
        /// "(menu implies 5 - overridden)" note - is compared as a bare flag, because those move
        /// too and a change in them is worth seeing.
        /// </summary>
        private static string Diff(string before, string after)
        {
            var a = Parse(before);
            var b = Parse(after);
            var keys = new List<string>();
            foreach (var k in a.Keys) if (!keys.Contains(k)) keys.Add(k);
            foreach (var k in b.Keys) if (!keys.Contains(k)) keys.Add(k);
            keys.Sort(StringComparer.Ordinal);

            var sb = new StringBuilder();
            foreach (var k in keys)
            {
                string av, bv;
                a.TryGetValue(k, out av);
                b.TryGetValue(k, out bv);
                if (av == bv) continue;
                if (sb.Length > 0) sb.Append("; ");
                sb.Append(k).Append(' ').Append(av ?? "(absent)").Append(" -> ").Append(bv ?? "(absent)");
            }
            return sb.Length == 0 ? "something not in the settings line" : sb.ToString();
        }

        private static Dictionary<string, string> Parse(string line)
        {
            var d = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(line)) return d;
            foreach (var tok in line.Split(' '))
            {
                if (tok.Length == 0) continue;
                int eq = tok.IndexOf('=');
                if (eq > 0) d[tok.Substring(0, eq)] = tok.Substring(eq + 1);
                else d[tok] = "present";
            }
            return d;
        }

        /// <summary>
        /// Changes inside a window, for the report. Empty means the window is one configuration and
        /// can be read as a whole; anything else means it cannot.
        /// </summary>
        internal static List<Change> Since(long utcTicksFrom)
        {
            var list = new List<Change>();
            foreach (var c in _changes) if (c.UtcTicks >= utcTicksFrom) list.Add(c);
            return list;
        }
    }
}
