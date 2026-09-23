// PauseStateFile.cs
//
// Drop-in for the Pause My Server mod: publishes the server's pause state to
// a small JSON file so anything outside the game can read it without having
// to scrape the log.
//
//   <BepInEx>\config\PauseMyServer.state.json
//   {"paused":true,"by":"Genius","wanting":1,"players":2,
//    "since":"2026-09-23T17:45:12.482Z","written":"2026-09-23T17:46:02.114Z",
//    "heartbeat":5,"version":"1.4.2"}
//
// Why a heartbeat and not just writes on change: a reader can then tell the
// difference between "the mod says running" and "the mod isn't running", and
// no transition can ever be missed the way a log line can. The file's age is
// the whole trust model - fresh means live, stale means ignore me.
//
// Wiring it up (three call sites):
//
//   1. In Awake(), after the config is read:
//          PauseStateFile.Version = PluginVersion;   // or whatever you call it
//          PauseStateFile.Publish(false, null, 0, 0);
//
//   2. Wherever the world actually pauses or resumes - the same place that
//      logs "World paused" / "World resumed":
//          PauseStateFile.Publish(paused, wantedBy, wantingCount, playerCount);
//
//   3. In the plugin's Update():
//          PauseStateFile.Heartbeat();
//
//      Update() is deliberate: it keeps running at timeScale 0, and
//      realtimeSinceStartup keeps advancing, so the heartbeat survives the
//      pause it is reporting. A coroutine with WaitForSeconds would not.
//
//   4. Optional, in OnDestroy():
//          PauseStateFile.Shutdown();

using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace PauseMyServer
{
    internal static class PauseStateFile
    {
        /// <summary>Seconds between rewrites. Readers treat 3 missed beats as dead.</summary>
        internal const int HeartbeatSeconds = 5;

        /// <summary>Set false from config to stop publishing entirely.</summary>
        internal static bool Enabled = true;

        /// <summary>Reported in the file so a reader can log which build wrote it.</summary>
        internal static string Version = "";

        private static readonly string FilePath =
            Path.Combine(BepInEx.Paths.ConfigPath, "PauseMyServer.state.json");

        private static bool _paused;
        private static string _by = "";
        private static int _wanting;
        private static int _players;
        private static DateTime _sinceUtc = DateTime.UtcNow;
        private static float _nextBeat;

        /// <summary>
        /// Record the current state and write it out. Every call writes, so call it when one of
        /// these four values actually changed, never once a frame. Re-publishing unchanged values
        /// is harmless - "since" only moves when the paused flag itself flips - but it is a file
        /// write nobody needed.
        /// </summary>
        internal static void Publish(bool paused, string by, int wanting, int players)
        {
            if (!Enabled) return;

            if (paused != _paused) _sinceUtc = DateTime.UtcNow;
            _paused = paused;
            _by = paused ? (by ?? "") : "";
            _wanting = wanting;
            _players = players;
            Write();
        }

        /// <summary>Call from Update(). Rewrites at most every HeartbeatSeconds.</summary>
        internal static void Heartbeat()
        {
            if (!Enabled) return;
            if (Time.realtimeSinceStartup < _nextBeat) return;
            Write();
        }

        /// <summary>Remove the file so readers fall back immediately.</summary>
        internal static void Shutdown()
        {
            try { if (File.Exists(FilePath)) File.Delete(FilePath); }
            catch { /* a leftover file just goes stale on its own */ }
        }

        private static void Write()
        {
            _nextBeat = Time.realtimeSinceStartup + HeartbeatSeconds;

            var sb = new StringBuilder(220);
            sb.Append('{');
            sb.Append("\"paused\":").Append(_paused ? "true" : "false");
            sb.Append(",\"by\":\"").Append(Escape(_by)).Append('"');
            sb.Append(",\"wanting\":").Append(_wanting.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"players\":").Append(_players.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"since\":\"").Append(Iso(_sinceUtc)).Append('"');
            sb.Append(",\"written\":\"").Append(Iso(DateTime.UtcNow)).Append('"');
            sb.Append(",\"heartbeat\":").Append(HeartbeatSeconds.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"version\":\"").Append(Escape(Version)).Append('"');
            sb.Append('}');

            try
            {
                // Write beside the target, then swap, so a reader can never
                // catch a half-written file.
                var tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
                else File.Move(tmp, FilePath);
            }
            catch
            {
                // Publishing state is never worth taking the server down for.
            }
        }

        private static string Iso(DateTime utc)
        {
            return utc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        }

        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length + 8);
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"':  sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b");  break;
                    case '\f': sb.Append("\\f");  break;
                    case '\n': sb.Append("\\n");  break;
                    case '\r': sb.Append("\\r");  break;
                    case '\t': sb.Append("\\t");  break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
