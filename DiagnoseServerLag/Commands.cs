using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace DiagnoseServerLag
{
    /// <summary>
    /// Console commands (F5 in game, and the dedicated server's own console). Prefixed dsl_.
    ///
    /// These matter more than they would in most mods, because the moment you actually want a
    /// diagnosis is the moment the game is too sticky to enjoy clicking around a panel in, and
    /// because the server has a console and no screen. Everything the report shows is reachable
    /// here as text.
    /// </summary>
    internal static class Commands
    {
        internal static void Register()
        {
            new Terminal.ConsoleCommand("dsl", "Diagnose Server Lag: open or close the lag report",
                (Terminal.ConsoleEvent)(args =>
                {
                    if (Player.m_localPlayer == null) { args.Context?.AddString("No character loaded."); return; }
                    LagPanel.Toggle();
                }));

            new Terminal.ConsoleCommand("dsl_why", "Diagnose Server Lag: print the verdict and its evidence",
                (Terminal.ConsoleEvent)(args =>
                {
                    var sb = new StringBuilder();
                    sb.AppendLine(Verdict.CoverageNote());
                    foreach (var f in Verdict.Diagnose())
                    {
                        sb.AppendLine($"[{f.Confidence}%] {f.Headline}");
                        foreach (var e in f.Evidence) sb.AppendLine($"    - {e}");
                        if (!string.IsNullOrEmpty(f.Advice)) sb.AppendLine($"    => {f.Advice}");
                    }
                    args.Context?.AddString(sb.ToString().TrimEnd());
                }));

            new Terminal.ConsoleCommand("dsl_now", "Diagnose Server Lag: the last second measured on this machine",
                (Terminal.ConsoleEvent)(args =>
                {
                    if (!Sampler.TryNewest(out var s)) { args.Context?.AddString("Nothing measured yet."); return; }
                    args.Context?.AddString(
                        $"frames {s.FrameMsAvg:0.0} ms avg / {s.FrameMsMax:0} ms worst over {s.Frames} frames, {s.Stalls} stalls\n" +
                        $"ping {(s.HasPing ? s.Ping + " ms" : "not measurable")}, quality {s.LocalQuality * 100f:0.0}%/{s.RemoteQuality * 100f:0.0}%\n" +
                        $"queue {Stats.Bytes(s.SendQueue)}, rate {Stats.Bytes(s.SendRate)}/s, in {Stats.Bytes(s.InByteSec)}/s, out {Stats.Bytes(s.OutByteSec)}/s\n" +
                        $"objects {s.Zdos} known / {s.Instances} built, {s.ZdosSent}/s sent, {s.ZdosRecv}/s received, {s.ChangeQueue} unacknowledged\n" +
                        $"peers {s.Peers}, history {Sampler.History.Count}s");
                }));

            new Terminal.ConsoleCommand("dsl_server", "Diagnose Server Lag: what the server last reported about itself",
                (Terminal.ConsoleEvent)(args =>
                {
                    var r = LagNetwork.Latest;
                    if (r == null) { args.Context?.AddString(Verdict.CoverageNote()); return; }
                    var sb = new StringBuilder();
                    sb.AppendLine(Verdict.CoverageNote());
                    sb.AppendLine($"tick {r.TickMsAvg:0.0} ms now / {r.BaselineTickMs:0.0} ms median over {r.WindowSeconds}s, worst {r.WorstTickMs:0} ms, {r.StallsInWindow} stalls");
                    sb.AppendLine($"world {r.Zdos} objects, {r.ZdosSent}/s sent, {r.ZdosRecv}/s received, {r.PeerCount} players, {(r.Dedicated ? "dedicated" : "player-hosted")}");
                    sb.AppendLine($"worst player queue {Stats.Bytes(r.WorstSendQueue)}, total send rate {Stats.Bytes(r.TotalSendRate)}/s");
                    if (r.PeerDetailWithheld) sb.AppendLine("the per-player table was not shared with you");
                    foreach (var p in r.Peers)
                        sb.AppendLine($"  {p.Name}: ping {(p.HasPing ? p.Ping + " ms" : "?")}, quality {p.Quality * 100f:0.0}%, " +
                                      $"queued {Stats.Bytes(p.SendQueue)}, rate {Stats.Bytes(p.SendRate)}/s, {p.DistanceFromCenter:0} m from center");
                    args.Context?.AddString(sb.ToString().TrimEnd());
                }));

            new Terminal.ConsoleCommand("dsl_dump", "Diagnose Server Lag: write the measured seconds to a CSV",
                (Terminal.ConsoleEvent)(args =>
                {
                    string path = Dump();
                    args.Context?.AddString(path == null ? "Could not write the dump; see the log." : $"Wrote {path}");
                }));

            new Terminal.ConsoleCommand("dsl_bench", "Diagnose Server Lag: summarize the last N seconds (default 120) and write them to a CSV",
                (Terminal.ConsoleEvent)(args =>
                {
                    int seconds = 120;
                    if (args.Length > 1 && int.TryParse(args[1], out int n)) seconds = Mathf.Clamp(n, 5, Sampler.History.Capacity);
                    string text = Bench(seconds, out string path);
                    // Logged as well as printed: on a dedicated server the console scrolls, and the
                    // log file is what survives to be compared with the other machine later.
                    DiagnoseServerLagMod.Log.LogInfo("[DiagnoseServerLag] bench\n" + text);
                    args.Context?.AddString(text + (path == null ? "" : "\nwrote " + path));
                }));

            new Terminal.ConsoleCommand("dsl_bench_server", "Diagnose Server Lag: ask the server to capture itself (admin only)",
                (Terminal.ConsoleEvent)(args =>
                {
                    int seconds = 120;
                    if (args.Length > 1 && int.TryParse(args[1], out int n)) seconds = n;
                    LagNetwork.AskCapture(seconds);
                    args.Context?.AddString("Asked the server for a capture; its reply prints here when it arrives.");
                }));

            new Terminal.ConsoleCommand("dsl_cpu", "Diagnose Server Lag: what this process is costing the machine",
                (Terminal.ConsoleEvent)(args =>
                {
                    if (!Machine.Readable) { args.Context?.AddString($"Process counters unavailable: {Machine.UnreadableReason}"); return; }
                    if (!Sampler.TryNewest(out var s) || !s.HasCpu) { args.Context?.AddString("Nothing measured yet."); return; }
                    args.Context?.AddString(
                        $"CPU {s.CpuMsPerSec:0} ms/s = {Machine.CoreShare(s.CpuMsPerSec) * 100f:0.0}% of one core, " +
                        $"{Machine.MachineShare(s.CpuMsPerSec) * 100f:0.0}% of {Machine.ProcessorCount} cores\n" +
                        $"headroom about {Machine.Headroom(s.CpuMsPerSec):0.0}x the current load before one core is full\n" +
                        $"GC {s.Gc0}/{s.Gc1}/{s.Gc2} this second, heap {Stats.Bytes(s.HeapBytes)}, working set {Stats.Bytes(s.WorkingSetBytes)}");
                }));

            new Terminal.ConsoleCommand("dsl_reset", "Diagnose Server Lag: forget the measurements and start again",
                (Terminal.ConsoleEvent)(args =>
                {
                    Sampler.Reset();
                    args.Context?.AddString("History cleared. The baseline rebuilds over the next minute.");
                }));
        }

        /// <summary>
        /// The one block of text worth comparing between two servers.
        ///
        /// Everything here is chosen to survive the comparison. Tick time is reported but labeled,
        /// because on a capped server it is a property of the configuration rather than of the
        /// load; CPU share and headroom are the numbers that actually differ between two machines
        /// carrying the same world. The load being carried is printed alongside, so a difference in
        /// CPU can be read against whether the two servers were doing the same amount of work.
        /// </summary>
        internal static string Bench(int seconds, out string csvPath)
        {
            csvPath = null;
            var window = Sampler.History.Recent(seconds);
            if (window.Count == 0) return "Nothing measured yet.";

            var sb = new StringBuilder();
            bool dedicated = Sampler.IsDedicatedHere;
            bool server = Sampler.IsServerHere;
            string role = dedicated ? "dedicated server" : server ? "host (also a client)" : "client";

            float tickMed = Stats.Median(window, x => x.FrameMsAvg);
            float tickWorst = Stats.Max(window, x => x.FrameMsMax);
            int stalls = 0;
            foreach (var w in window) stalls += w.Stalls;
            // The same test the verdict uses: a worst tick close to the median with no stalls is a
            // frame cap, and saying so is what stops the number being read as a performance figure.
            bool capped = stalls == 0 && tickWorst <= tickMed * DslConfig.SteadyTickRatio.Value + 2f;

            Sampler.TryNewest(out var now);
            sb.AppendLine($"DiagnoseServerLag {DiagnoseServerLagMod.ModVersion} bench - last {window.Count}s");
            sb.AppendLine($"  role            {role}, {Machine.ProcessorCount} cores");
            sb.AppendLine($"  world           {now.Zdos} objects, {now.Peers} players connected");
            sb.AppendLine($"  tick            {tickMed:0.0} ms median, {tickWorst:0} ms worst, {stalls} stalls"
                          + (capped ? "   (capped - not a load measurement)" : ""));

            bool haveCpu = false;
            foreach (var w in window) if (w.HasCpu) { haveCpu = true; break; }
            if (Machine.Readable && haveCpu)
            {
                float cpu = Stats.Median(window, x => x.CpuMsPerSec);
                float cpuPeak = Stats.Max(window, x => x.CpuMsPerSec);
                sb.AppendLine($"  CPU             {cpu:0} ms/s = {Machine.CoreShare(cpu) * 100f:0.0}% of one core, {Machine.MachineShare(cpu) * 100f:0.0}% of the machine");
                sb.AppendLine($"  CPU peak        {cpuPeak:0} ms/s = {Machine.CoreShare(cpuPeak) * 100f:0.0}% of one core");
                sb.AppendLine($"  headroom        about {Machine.Headroom(cpu):0.0}x the current load before one core is full");
                sb.AppendLine($"  GC per minute   {Stats.Mean(window, x => x.Gc0) * 60f:0} gen0, {Stats.Mean(window, x => x.Gc1) * 60f:0} gen1, {Stats.Mean(window, x => x.Gc2) * 60f:0.0} gen2");
                sb.AppendLine($"  memory          heap {Stats.Bytes(Stats.Median(window, x => x.HeapBytes))}, working set {Stats.Bytes(Stats.Median(window, x => x.WorkingSetBytes))}");
            }
            else
            {
                string why = string.IsNullOrEmpty(Machine.UnreadableReason) ? "" : " (" + Machine.UnreadableReason + ")";
                sb.AppendLine($"  CPU             not measurable{why}");
            }

            sb.AppendLine($"  object traffic  {Stats.Median(window, x => x.ZdosSent):0}/s out, {Stats.Median(window, x => x.ZdosRecv):0}/s in");
            if (server) sb.AppendLine($"  worst queue     {Stats.Bytes(Stats.Max(window, x => x.SendQueue))}");

            csvPath = Dump(seconds);
            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Writes every second still in the ring to a CSV next to the config.
        ///
        /// The verdict is meant to be enough, but a cause that only shows up once an evening is
        /// better argued about with the raw seconds in hand than from memory, and a CSV is
        /// something you can hand to somebody else.
        /// </summary>
        internal static string Dump(int seconds = 0)
        {
            try
            {
                string dir = Path.Combine(BepInEx.Paths.ConfigPath, "DiagnoseServerLag");
                Directory.CreateDirectory(dir);
                string role = Sampler.IsDedicatedHere ? "dedicated" : Sampler.IsServerHere ? "host" : "client";
                string path = Path.Combine(dir, $"lag-{role}-{DateTime.Now:yyyyMMdd-HHmmss}.csv");

                var window = seconds > 0 ? Sampler.History.Recent(seconds) : Sampler.History.Recent(Sampler.History.Capacity);
                var c = CultureInfo.InvariantCulture;
                var sb = new StringBuilder();

                // A metadata block, so a file can be compared with one from another machine without
                // anyone having to remember which was which. Commented with '#' so a spreadsheet and
                // the compare script both skip straight to the columns.
                Sampler.TryNewest(out var now);
                sb.AppendLine($"# mod,{DiagnoseServerLagMod.ModVersion}");
                sb.AppendLine($"# captured,{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"# role,{role}");
                sb.AppendLine($"# cores,{Machine.ProcessorCount}");
                sb.AppendLine($"# os,{SystemInfo.operatingSystem}");
                sb.AppendLine($"# cpu_name,{SystemInfo.processorType}");
                sb.AppendLine($"# seconds,{window.Count}");
                sb.AppendLine($"# zdos,{now.Zdos}");
                sb.AppendLine($"# peers,{now.Peers}");
                sb.AppendLine($"# cpu_readable,{(Machine.Readable ? 1 : 0)}");
                sb.AppendLine("#");

                sb.AppendLine("second,frames,frame_avg_ms,frame_max_ms,stalls,ping_ms,ping_measured,ping_round_trip,quality_local,quality_remote," +
                              "in_bytes_sec,out_bytes_sec,send_queue_bytes,send_rate_bytes_sec,zdos,instances,zdos_sent_sec,zdos_recv_sec,change_queue,peers," +
                              "cpu_ms_per_sec,cpu_measured,gc0,gc1,gc2,heap_bytes,working_set_bytes");

                foreach (var s in window)
                {
                    sb.AppendLine(string.Join(",", new[]
                    {
                        s.At.ToString("0.0", c), s.Frames.ToString(c),
                        s.FrameMsAvg.ToString("0.00", c), s.FrameMsMax.ToString("0.00", c), s.Stalls.ToString(c),
                        s.Ping.ToString(c), s.HasPing ? "1" : "0", s.PingFromRoundTrip ? "1" : "0",
                        s.LocalQuality.ToString("0.0000", c), s.RemoteQuality.ToString("0.0000", c),
                        s.InByteSec.ToString("0", c), s.OutByteSec.ToString("0", c),
                        s.SendQueue.ToString(c), s.SendRate.ToString(c),
                        s.Zdos.ToString(c), s.Instances.ToString(c),
                        s.ZdosSent.ToString(c), s.ZdosRecv.ToString(c), s.ChangeQueue.ToString(c),
                        s.Peers.ToString(c),
                        s.CpuMsPerSec.ToString("0.0", c), s.HasCpu ? "1" : "0",
                        s.Gc0.ToString(c), s.Gc1.ToString(c), s.Gc2.ToString(c),
                        s.HeapBytes.ToString(c), s.WorkingSetBytes.ToString(c),
                    }));
                }

                File.WriteAllText(path, sb.ToString());
                DiagnoseServerLagMod.Log.LogInfo($"[DiagnoseServerLag] Wrote {window.Count} seconds to {path}");
                return path;
            }
            catch (Exception e)
            {
                DiagnoseServerLagMod.Log.LogError($"[DiagnoseServerLag] Could not write the dump: {e}");
                return null;
            }
        }
    }
}
