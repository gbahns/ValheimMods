using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace DiagnoseServerLag
{
    /// <summary>
    /// The server and every client that answered, over the same seconds, with the one conclusion
    /// that needs all of them.
    ///
    /// Any single machine's capture can say that it had a bad time. None of them can say whether
    /// anyone else did, and that is the question that decides what to do about it: stalls happening
    /// on four machines in the same second are one shared event - the server, or the path everyone
    /// crosses - and the same four stalls scattered across four different seconds are four local
    /// problems that happen to coexist. The numbers are identical either way; only the alignment
    /// separates them, and only a group capture has it.
    ///
    /// Alignment is by UTC second, so it depends on the machines' clocks agreeing. Normal time sync
    /// is far inside the one-second resolution used here, and a client whose clock is badly out
    /// shows up as a machine that never coincides with anyone - which is worth noticing rather than
    /// hiding, so no attempt is made to correct for skew.
    /// </summary>
    internal static class GroupReport
    {
        /// <summary>Seconds in which this many machines stalled together count as one shared event.</summary>
        private const int Together = 2;

        internal static string Build(int seconds, List<ClientSeries> clients, int expected, out string csvPath)
        {
            csvPath = null;
            var sb = new StringBuilder();
            var window = Sampler.History.Recent(seconds);

            sb.AppendLine($"DiagnoseServerLag {DiagnoseServerLagMod.ModVersion} group capture - {window.Count}s, " +
                          $"{clients.Count} of {expected} client{(expected == 1 ? "" : "s")} answered");
            sb.AppendLine();

            // ── the server's own row ────────────────────────────────────────────────
            float tickMed = Stats.Median(window, x => x.FrameMsAvg);
            int serverStalls = 0;
            foreach (var w in window) serverStalls += w.Stalls;
            float serverCpu = Stats.Median(window, x => x.CpuMsPerSec);
            sb.AppendLine($"  {"server",-16} tick {tickMed,6:0.0} ms   stalls {serverStalls,3}   " +
                          (serverCpu > 0f ? $"CPU {Machine.CoreShare(serverCpu) * 100f,5:0.0}% of a core" : "CPU n/a") +
                          $"   {Stats.Median(window, x => x.Zdos):0} objects");

            foreach (var c in clients)
            {
                string cpu = c.HasCpu && c.CpuMedianMsPerSec > 0f
                    ? $"CPU {c.CpuMedianMsPerSec / 10f,5:0.0}% of a core"
                    : "CPU n/a";
                sb.AppendLine($"  {Trim(c.Name, 16),-16} frame {c.FrameMedianMs,5:0.0} ms   stalls {c.TotalStalls,3}   {cpu}" +
                              (c.RoundTripMs > 0 ? $"   rt {c.RoundTripMs} ms" : ""));
            }

            if (clients.Count < expected)
                sb.AppendLine($"  ({expected - clients.Count} client(s) did not answer: no mod, an older version, or Share My Performance off)");

            // ── what only the group can say ─────────────────────────────────────────
            sb.AppendLine();
            sb.AppendLine(Correlate(window, clients));

            csvPath = WriteCsv(window, clients);
            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// The verdict a group capture exists for: were the stalls shared or separate?
        ///
        /// The server counts as one of the machines. A second where the server stalled and every
        /// client stalled with it is the strongest evidence this mod can produce for a server-side
        /// cause, and it is not reachable from any one capture.
        /// </summary>
        private static string Correlate(List<Sample> window, List<ClientSeries> clients)
        {
            // Who stalled, by UTC second. The server first, then each client.
            var stalledBy = new Dictionary<long, List<string>>();
            void Mark(long second, string who)
            {
                if (!stalledBy.TryGetValue(second, out var list)) stalledBy[second] = list = new List<string>();
                if (!list.Contains(who)) list.Add(who);
            }

            foreach (var s in window)
                if (s.Stalls > 0 && s.UtcTicks > 0) Mark(ToSecond(s.UtcTicks), "server");
            foreach (var c in clients)
                foreach (var sec in c.Seconds)
                    if (sec.Stalls > 0 && sec.UtcTicks > 0) Mark(ToSecond(sec.UtcTicks), Trim(c.Name, 16));

            int machines = clients.Count + 1;
            var shared = new List<KeyValuePair<long, List<string>>>();
            var alone = new Dictionary<string, int>();
            foreach (var kv in stalledBy)
            {
                if (kv.Value.Count >= Together) shared.Add(kv);
                else
                {
                    string who = kv.Value[0];
                    alone[who] = alone.TryGetValue(who, out int n) ? n + 1 : 1;
                }
            }

            if (stalledBy.Count == 0)
                return "Nobody stalled. Nothing to attribute.";

            var sb = new StringBuilder();
            if (shared.Count > 0)
            {
                shared.Sort((a, b) => a.Key.CompareTo(b.Key));
                sb.AppendLine($"SHARED: {shared.Count} second(s) where two or more machines stalled together.");
                int shown = 0;
                foreach (var kv in shared)
                {
                    if (shown++ >= 5) { sb.AppendLine($"  ... and {shared.Count - 5} more"); break; }
                    sb.AppendLine($"  {new DateTime(kv.Key * TimeSpan.TicksPerSecond, DateTimeKind.Utc).ToLocalTime():HH:mm:ss}  " +
                                  string.Join(", ", kv.Value.ToArray()));
                }
                sb.AppendLine("  Machines do not hitch in the same second by chance. Look at the server and at the");
                sb.AppendLine("  network path everyone shares, not at any one computer.");
            }

            if (alone.Count > 0)
            {
                if (shared.Count > 0) sb.AppendLine();
                sb.AppendLine("ALONE: stalls nobody else had, which are local to that machine.");
                foreach (var kv in alone)
                    sb.AppendLine($"  {kv.Key,-16} {kv.Value} second(s) stalling by itself");
                if (shared.Count == 0 && machines > 1)
                    sb.AppendLine("  No second had two machines stalling together, so nothing here points at the server.");
            }

            if (machines == 1)
                sb.AppendLine("Only this machine reported, so nothing can be told apart. Ask again with players connected.");
            return sb.ToString().TrimEnd();
        }

        private static long ToSecond(long utcTicks) => utcTicks / TimeSpan.TicksPerSecond;

        private static string Trim(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "(unnamed)";
            return s.Length <= max ? s : s.Substring(0, max);
        }

        /// <summary>
        /// One row per machine per second, so the alignment survives the console scrolling away.
        /// Long format rather than a column per machine: the set of machines is not known until the
        /// capture happens, and a spreadsheet pivots this in a click.
        /// </summary>
        private static string WriteCsv(List<Sample> window, List<ClientSeries> clients)
        {
            try
            {
                string dir = Path.Combine(BepInEx.Paths.ConfigPath, "DiagnoseServerLag");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, $"group-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
                var c = CultureInfo.InvariantCulture;
                var sb = new StringBuilder();
                sb.AppendLine($"# mod,{DiagnoseServerLagMod.ModVersion}");
                sb.AppendLine($"# captured,{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"# machines,{clients.Count + 1}");
                sb.AppendLine("#");
                sb.AppendLine("utc,machine,frame_max_ms,stalls,cpu_ms_per_sec,collections");

                foreach (var s in window)
                    sb.AppendLine(string.Join(",", new[]
                    {
                        Iso(s.UtcTicks), "server",
                        s.FrameMsMax.ToString("0.00", c), s.Stalls.ToString(c),
                        s.CpuMsPerSec.ToString("0.0", c), Machine.Collections(s).ToString(c),
                    }));

                foreach (var cl in clients)
                    foreach (var sec in cl.Seconds)
                        sb.AppendLine(string.Join(",", new[]
                        {
                            Iso(sec.UtcTicks), Csv(cl.Name),
                            sec.FrameMaxMs.ToString("0.00", c), sec.Stalls.ToString(c),
                            sec.CpuMsPerSec.ToString("0.0", c), sec.Collections.ToString(c),
                        }));

                File.WriteAllText(path, sb.ToString());
                return path;
            }
            catch (Exception e)
            {
                DiagnoseServerLagMod.Log.LogError($"[DiagnoseServerLag] Could not write the group CSV: {e}");
                return null;
            }
        }

        private static string Iso(long ticks) =>
            ticks <= 0 ? "" : new DateTime(ticks, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ssZ");

        private static string Csv(string s) =>
            string.IsNullOrEmpty(s) ? "" : (s.IndexOf(',') >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s);
    }
}
