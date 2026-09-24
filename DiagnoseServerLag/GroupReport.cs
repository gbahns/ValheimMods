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
                string ai = c.NearbyAI > 0 ? $"   simulating {c.OwnedAI}/{c.NearbyAI} creatures" : "";
                sb.AppendLine($"  {Trim(c.Name, 16),-16} frame {c.FrameMedianMs,5:0.0} ms   stalls {c.TotalStalls,3}   {cpu}" +
                              (c.RoundTripMs > 0 ? $"   rt {c.RoundTripMs} ms" : "") + ai);
            }

            int reduced = 0;
            foreach (var c2 in clients) if (!c2.Full) reduced++;
            if (reduced > 0)
                sb.AppendLine($"  ({reduced} client(s) sent the reduced 0.5.x format; their extra columns are empty, not zero)");

            if (clients.Count < expected)
                sb.AppendLine($"  ({expected - clients.Count} client(s) did not answer: no mod, an older version, or Share My Performance off)");

            // ── what only the group can say ─────────────────────────────────────────
            sb.AppendLine();
            sb.AppendLine(Correlate(window, clients));

            string ownership = Ownership(clients);
            if (ownership != null) { sb.AppendLine(); sb.AppendLine(ownership); }

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

        /// <summary>
        /// Whether one machine is carrying the group's creature simulation.
        ///
        /// Valheim runs a creature's AI only on the owner of its ZDO, ownership goes to whoever was
        /// in range when it had none, and nothing balances it afterwards. A group that piles into
        /// one zone therefore leaves one person simulating all of it - on whichever machine
        /// happened to arrive first, which is nobody's decision and usually nobody's best computer.
        /// The server does not take this load: on the real server it owned 82 objects out of
        /// 395,778.
        ///
        /// Reported only when someone actually holds a disproportionate share, so a spread-out
        /// group - where this design works exactly as intended - is not nagged about nothing.
        /// </summary>
        private static string Ownership(List<ClientSeries> clients)
        {
            int total = 0, holders = 0;
            ClientSeries top = null;
            foreach (var c in clients)
            {
                total += c.OwnedAI;
                if (c.OwnedAI > 0) holders++;
                if (top == null || c.OwnedAI > top.OwnedAI) top = c;
            }
            if (top == null || total < 5 || clients.Count < 2) return null;

            float share = (float)top.OwnedAI / total;
            var sb = new StringBuilder();
            sb.AppendLine($"CREATURE SIMULATION: {total} creature(s) across {clients.Count} clients, " +
                          $"{holders} of them carrying any.");
            foreach (var c in clients)
                if (c.OwnedAI > 0)
                    sb.AppendLine($"  {Trim(c.Name, 16),-16} {c.OwnedAI,4} owned of {c.NearbyAI} loaded nearby");

            if (share >= 0.7f && clients.Count > 1)
            {
                sb.AppendLine($"  {Trim(top.Name, 16)} is running {share * 100f:0}% of it. Valheim simulates a creature only on");
                sb.AppendLine("  the machine that owns it, so everyone else's fights are being computed there, and");
                sb.AppendLine("  their hits are routed through that connection. Ownership goes to whoever was in");
                sb.AppendLine("  range first and is never rebalanced - spreading out, or letting the strongest");
                sb.AppendLine("  machine enter a zone first, is the only lever there is.");
            }
            return sb.ToString().TrimEnd();
        }

        private static long ToSecond(long utcTicks) => utcTicks / TimeSpan.TicksPerSecond;

        private static string Trim(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "(unnamed)";
            return s.Length <= max ? s : s.Substring(0, max);
        }

        /// <summary>
        /// One row per machine per second, with every column, so the alignment survives the console
        /// scrolling away and nobody has to go back and ask for more.
        ///
        /// Every field rather than a chosen few, because the alternative is asking each teammate to
        /// run a command on their own machine and send the file on. That does work - every machine
        /// records continuously and dsl_bench reads backwards over what is already there - but it
        /// costs four people's attention, and anyone who has logged off in the meantime took their
        /// history with them, since the ring lives in memory. The admin's one command has to end
        /// with everything anyone is going to want.
        ///
        /// Long format rather than a column per machine: the set of machines is not known until the
        /// capture happens, and a spreadsheet pivots this in a click. A client too old to send the
        /// full record leaves the extra columns empty rather than zero, so a gap is never read as a
        /// measurement.
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
                sb.AppendLine("utc,machine,frame_avg_ms,frame_max_ms,stalls,frames,ping_ms,ping_measured,"
                            + "ping_round_trip,quality_local,quality_remote,in_bytes_sec,out_bytes_sec,"
                            + "send_queue_bytes,send_rate_bytes_sec,zdos,instances,zdos_sent_sec,zdos_recv_sec,"
                            + "change_queue,peers,cpu_ms_per_sec,cpu_measured,gc0,gc1,gc2,collections,"
                            + "heap_bytes,working_set_bytes,owned_ai,nearby_ai,feed_ms,owned_objects,nearby_objects");

                foreach (var s in window) sb.AppendLine(Row("server", s, c));

                foreach (var cl in clients)
                {
                    if (cl.Full)
                    {
                        foreach (var s in cl.Samples) sb.AppendLine(Row(Csv(cl.Name), s, c));
                    }
                    else
                    {
                        // Layout 1: only the correlation columns exist. The rest are left empty,
                        // which a reader can tell apart from a measured zero.
                        foreach (var sec in cl.Seconds)
                            sb.AppendLine(string.Join(",", new[]
                            {
                                Iso(sec.UtcTicks), Csv(cl.Name), "", sec.FrameMaxMs.ToString("0.00", c),
                                sec.Stalls.ToString(c), "", "", "", "", "", "", "", "", "", "", "", "",
                                "", "", "", "", sec.CpuMsPerSec.ToString("0.0", c), "", "", "", "",
                                sec.Collections.ToString(c), "", "", "", "",
                            }));
                    }
                }

                File.WriteAllText(path, sb.ToString());
                return path;
            }
            catch (Exception e)
            {
                DiagnoseServerLagMod.Log.LogError($"[DiagnoseServerLag] Could not write the group CSV: {e}");
                return null;
            }
        }

        /// <summary>One machine-second, every column, in the order the header declares.</summary>
        private static string Row(string machine, Sample s, CultureInfo c) =>
            string.Join(",", new[]
            {
                Iso(s.UtcTicks), machine,
                s.FrameMsAvg.ToString("0.00", c), s.FrameMsMax.ToString("0.00", c),
                s.Stalls.ToString(c), s.Frames.ToString(c),
                s.Ping.ToString(c), s.HasPing ? "1" : "0", s.PingFromRoundTrip ? "1" : "0",
                s.LocalQuality.ToString("0.0000", c), s.RemoteQuality.ToString("0.0000", c),
                s.InByteSec.ToString("0", c), s.OutByteSec.ToString("0", c),
                s.SendQueue.ToString(c), s.SendRate.ToString(c),
                s.Zdos.ToString(c), s.Instances.ToString(c),
                s.ZdosSent.ToString(c), s.ZdosRecv.ToString(c), s.ChangeQueue.ToString(c),
                s.Peers.ToString(c),
                s.CpuMsPerSec.ToString("0.0", c), s.HasCpu ? "1" : "0",
                s.Gc0.ToString(c), s.Gc1.ToString(c), s.Gc2.ToString(c),
                Machine.Collections(s).ToString(c),
                s.HeapBytes.ToString(c), s.WorkingSetBytes.ToString(c),
                s.OwnedAI.ToString(c), s.NearbyAI.ToString(c),
                s.FeedMs.ToString("0", c),
                s.OwnedObjects.ToString(c), s.NearbyObjects.ToString(c),
            });

        private static string Iso(long ticks) =>
            ticks <= 0 ? "" : new DateTime(ticks, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ssZ");

        private static string Csv(string s) =>
            string.IsNullOrEmpty(s) ? "" : (s.IndexOf(',') >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s);
    }
}
