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

        internal static string Build(int seconds, List<ClientSeries> clients, int expected, out string csvPath, out string csvText)
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

            // Who is running what. A client on an older build is not refused anything - refusing a
            // message shape partitions players into groups that cannot see each other - but it is
            // the first thing to check when a row is missing a column, because a field added last
            // week is simply absent from a client that has not updated. Saying so here turns "why
            // is there no GPU for Brane" into a fact rather than an investigation.
            var stale = new List<string>();
            foreach (var c in clients)
                if (string.IsNullOrEmpty(c.ModVersion)) stale.Add($"{Trim(c.Name, 16)} (before 0.10.6)");
                else if (c.ModVersion != DiagnoseServerLagMod.ModVersion) stale.Add($"{Trim(c.Name, 16)} {c.ModVersion}");
            if (stale.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"  server is {DiagnoseServerLagMod.ModVersion}; behind it: {string.Join(", ", stale.ToArray())}");
            }

            // What these machines actually are. Comparing two players' frame times without it
            // invites the wrong conclusion: one capture had two clients doing identical CPU work
            // for double the frame time, which read as a hardware fault until it turned out one
            // was rendering 5120x1440 with texture mods.
            sb.AppendLine();
            sb.AppendLine("MACHINES");
            string me = Hardware.Describe();
            if (!string.IsNullOrEmpty(me)) sb.AppendLine($"  {"server",-16} {me}");
            foreach (var c in clients)
            {
                if (string.IsNullOrEmpty(c.Gpu) && c.RamMB == 0) continue;
                string res = c.ScreenWidth > 0 ? $"{c.ScreenWidth}x{c.ScreenHeight}" : "?";
                string vram = c.VramMB > 0 ? $" {c.VramMB / 1024f:0.#} GB" : "";
                string ram = c.RamMB > 0 ? $", {c.RamMB / 1024f:0.#} GB RAM" : "";
                sb.AppendLine($"  {Trim(c.Name, 16),-16} {c.CpuName} ({c.Cores}c){ram}, {c.Gpu}{vram}, {res}");
                // The settings on their own line: it is long, and it is the half that explains a
                // frame time once the hardware has been accounted for.
                if (!string.IsNullOrEmpty(c.GpuDetail))
                    sb.AppendLine($"  {"",-16} {c.GpuDetail}");
                if (!string.IsNullOrEmpty(c.Graphics))
                    sb.AppendLine($"  {"",-16} {c.Graphics}");
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

            csvPath = WriteCsv(window, clients, out csvText);
            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// The verdict a group capture exists for: were the stalls shared or separate?
        ///
        /// The server counts as one of the machines. A second where the server stalled and every
        /// client stalled with it is the strongest evidence this mod can produce for a server-side
        /// cause, and it is not reachable from any one capture.
        /// </summary>
        /// <summary>
        /// Whether the stalls happened together, judged against how often they would land together
        /// by chance.
        ///
        /// Counting co-occurrences alone is not evidence, and asserting it confidently is worse
        /// than saying nothing. The previous version printed "machines do not hitch in the same
        /// second by chance" whenever two ever did - and on real data the arithmetic ran the other
        /// way: one capture had 46 shared seconds where unrelated machines at those rates would
        /// have produced about 60, so the honest reading was that the stalls were *less* connected
        /// than coincidence, and the report declared the opposite. That failure is not a corner
        /// case either. It appears whenever one machine stalls far more than the rest, because a
        /// machine stalling in a third of all seconds collides with everybody by accident.
        ///
        /// So: take the seconds every machine actually reported, measure how often each stalled
        /// within them, and work out how many of those seconds would hold two or more stalls if the
        /// machines were unrelated - exactly, from the per-machine rates, since there are only ever
        /// a handful of machines. Then compare. Only a count well above that prediction says
        /// anything about the server or the path everyone shares.
        /// </summary>
        private static string Correlate(List<Sample> window, List<ClientSeries> clients)
        {
            // Which seconds each machine reported at all, and which of those it stalled in. The
            // first set is what makes a rate mean anything: a client that sent ten minutes cannot
            // be measured against an hour as though the other fifty were quiet.
            var reported = new Dictionary<string, HashSet<long>>();
            var stalled = new Dictionary<string, HashSet<long>>();

            void Note(string who, long second, bool didStall)
            {
                if (!reported.TryGetValue(who, out var r)) reported[who] = r = new HashSet<long>();
                r.Add(second);
                if (!didStall) return;
                if (!stalled.TryGetValue(who, out var t)) stalled[who] = t = new HashSet<long>();
                t.Add(second);
            }

            foreach (var s in window)
                if (s.UtcTicks > 0) Note("server", ToSecond(s.UtcTicks), s.Stalls > 0);
            foreach (var c in clients)
                foreach (var sec in c.Seconds)
                    if (sec.UtcTicks > 0) Note(Trim(c.Name, 16), ToSecond(sec.UtcTicks), sec.Stalls > 0);

            int machines = reported.Count;
            if (machines <= 1)
                return "Only this machine reported, so nothing can be told apart. Ask again with players connected.";

            // The seconds every machine covered. Anything outside it cannot be judged together.
            HashSet<long> common = null;
            foreach (var kv in reported)
            {
                if (common == null) { common = new HashSet<long>(kv.Value); continue; }
                common.IntersectWith(kv.Value);
            }
            int n = common == null ? 0 : common.Count;
            if (n == 0)
                return "These windows do not overlap, so nothing can be compared. Capture again while " +
                       "everyone is connected.";

            var names = new List<string>(reported.Keys);
            var rate = new Dictionary<string, double>();
            var hits = new Dictionary<string, int>();
            foreach (string who in names)
            {
                int inCommon = 0;
                if (stalled.TryGetValue(who, out var t))
                    foreach (long sec in t) if (common.Contains(sec)) inCommon++;
                hits[who] = inCommon;
                rate[who] = (double)inCommon / n;
            }

            // Observed: seconds inside the overlap where two or more machines stalled.
            var togetherAt = new List<KeyValuePair<long, List<string>>>();
            foreach (long sec in common)
            {
                List<string> who = null;
                foreach (string name in names)
                    if (stalled.TryGetValue(name, out var t) && t.Contains(sec))
                    {
                        if (who == null) who = new List<string>();
                        who.Add(name);
                    }
                if (who != null && who.Count >= Together)
                    togetherAt.Add(new KeyValuePair<long, List<string>>(sec, who));
            }

            // Expected, were the machines unrelated: the chance that two or more stall in the same
            // second, which is one minus the chance none does minus the chance exactly one does.
            double noneP = 1.0;
            foreach (string who in names) noneP *= 1.0 - rate[who];
            double exactlyOne = 0.0;
            bool certain = false;
            foreach (string who in names) if (rate[who] >= 1.0) certain = true;
            if (!certain)
                foreach (string who in names)
                    exactlyOne += noneP * rate[who] / (1.0 - rate[who]);
            double expected = n * Math.Max(0.0, 1.0 - noneP - exactlyOne);

            var sb = new StringBuilder();
            sb.AppendLine($"Compared over {n} second(s) that every machine reported.");
            foreach (string who in names)
                sb.AppendLine($"  {who,-16} stalled in {hits[who],4} of them ({rate[who] * 100:0.0}%)");
            sb.AppendLine();
            sb.AppendLine($"TOGETHER: {togetherAt.Count} second(s) with two or more machines stalling; " +
                          $"unrelated machines at these rates would give about {expected:0}.");

            if (togetherAt.Count > 0)
            {
                togetherAt.Sort((a, b) => a.Key.CompareTo(b.Key));
                int shown = 0;
                foreach (var kv in togetherAt)
                {
                    if (shown++ >= 5) { sb.AppendLine($"  ... and {togetherAt.Count - 5} more"); break; }
                    sb.AppendLine($"  {new DateTime(kv.Key * TimeSpan.TicksPerSecond, DateTimeKind.Utc).ToLocalTime():HH:mm:ss}  " +
                                  string.Join(", ", kv.Value.ToArray()));
                }
            }

            // The verdict is the comparison, never the count. The thresholds are deliberately wide:
            // this is a handful of machines over an hour, not a sample that supports a fine call.
            if (expected < 0.5 && togetherAt.Count == 0)
                sb.AppendLine("  Too few stalls anywhere to tell. Nothing points at the server.");
            else if (togetherAt.Count >= expected * 2.0 && togetherAt.Count >= 3)
                sb.AppendLine("  Well above chance. Look at the server and at the network path everyone " +
                              "shares, not at any one computer.");
            else if (togetherAt.Count <= expected * 0.5)
                sb.AppendLine("  Below chance: these machines stall independently, and whoever stalls most " +
                              "is not dragging the others down with them.");
            else
                sb.AppendLine("  About what chance predicts, so the overlap is coincidence. Read these as " +
                              "local stalls that happened to land in the same second.");

            // Solitary counts still earn their place: they say who to look at.
            var alone = new Dictionary<string, int>();
            foreach (long sec in common)
            {
                string only = null;
                int count = 0;
                foreach (string name in names)
                    if (stalled.TryGetValue(name, out var t) && t.Contains(sec)) { only = name; count++; }
                if (count == 1) alone[only] = alone.TryGetValue(only, out int k) ? k + 1 : 1;
            }
            if (alone.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("ALONE: stalls nobody else had that second, which are local to that machine.");
                foreach (var kv in alone)
                    sb.AppendLine($"  {kv.Key,-16} {kv.Value} second(s) stalling by itself");
            }
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
        /// <summary>
        /// Writes the group CSV here and hands back its text, so the same bytes can be sent to
        /// whoever asked for the capture. On a dedicated server "here" is a filesystem the person
        /// who typed the command cannot reach, which is the whole reason the text travels.
        /// </summary>
        private static string WriteCsv(List<Sample> window, List<ClientSeries> clients, out string csvText)
        {
            csvText = null;
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
                sb.AppendLine(CaptureCsv.Header);

                foreach (var s in window) sb.AppendLine(CaptureCsv.Row("server", s, c));

                foreach (var cl in clients)
                {
                    if (cl.Full)
                    {
                        foreach (var s in cl.Samples) sb.AppendLine(CaptureCsv.Row(cl.Name, s, c));
                    }
                    else
                    {
                        // Layout 1: only the correlation columns exist. The rest are left empty,
                        // which a reader can tell apart from a measured zero.
                        foreach (var sec in cl.Seconds)
                            sb.AppendLine(CaptureCsv.Sparse(
                                "utc", CaptureCsv.Iso(sec.UtcTicks),
                                "machine", CaptureCsv.Csv(cl.Name),
                                "frame_max_ms", sec.FrameMaxMs.ToString("0.00", c),
                                "stalls", sec.Stalls.ToString(c),
                                "cpu_ms_per_sec", sec.CpuMsPerSec.ToString("0.0", c),
                                "collections", sec.Collections.ToString(c)));
                    }
                }

                csvText = sb.ToString();
                File.WriteAllText(path, csvText);
                return path;
            }
            catch (Exception e)
            {
                DiagnoseServerLagMod.Log.LogError($"[DiagnoseServerLag] Could not write the group CSV: {e}");
                return null;
            }
        }

    }
}
