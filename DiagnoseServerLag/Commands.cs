using System;
using System.Globalization;
using System.IO;
using System.Text;

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

            new Terminal.ConsoleCommand("dsl_reset", "Diagnose Server Lag: forget the measurements and start again",
                (Terminal.ConsoleEvent)(args =>
                {
                    Sampler.Reset();
                    args.Context?.AddString("History cleared. The baseline rebuilds over the next minute.");
                }));
        }

        /// <summary>
        /// Writes every second still in the ring to a CSV next to the config.
        ///
        /// The verdict is meant to be enough, but a cause that only shows up once an evening is
        /// better argued about with the raw seconds in hand than from memory, and a CSV is
        /// something you can hand to somebody else.
        /// </summary>
        internal static string Dump()
        {
            try
            {
                string dir = Path.Combine(BepInEx.Paths.ConfigPath, "DiagnoseServerLag");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, $"lag-{DateTime.Now:yyyyMMdd-HHmmss}.csv");

                var sb = new StringBuilder();
                sb.AppendLine("second,frames,frame_avg_ms,frame_max_ms,stalls,ping_ms,ping_measured,quality_local,quality_remote," +
                              "in_bytes_sec,out_bytes_sec,send_queue_bytes,send_rate_bytes_sec,zdos,instances,zdos_sent_sec,zdos_recv_sec,change_queue,peers");

                var c = CultureInfo.InvariantCulture;
                for (int i = 0; i < Sampler.History.Count; i++)
                {
                    var s = Sampler.History[i];
                    sb.AppendLine(string.Join(",", new[]
                    {
                        s.At.ToString("0.0", c), s.Frames.ToString(c),
                        s.FrameMsAvg.ToString("0.00", c), s.FrameMsMax.ToString("0.00", c), s.Stalls.ToString(c),
                        s.Ping.ToString(c), s.HasPing ? "1" : "0",
                        s.LocalQuality.ToString("0.0000", c), s.RemoteQuality.ToString("0.0000", c),
                        s.InByteSec.ToString("0", c), s.OutByteSec.ToString("0", c),
                        s.SendQueue.ToString(c), s.SendRate.ToString(c),
                        s.Zdos.ToString(c), s.Instances.ToString(c),
                        s.ZdosSent.ToString(c), s.ZdosRecv.ToString(c), s.ChangeQueue.ToString(c),
                        s.Peers.ToString(c),
                    }));
                }

                File.WriteAllText(path, sb.ToString());
                DiagnoseServerLagMod.Log.LogInfo($"[DiagnoseServerLag] Wrote {Sampler.History.Count} seconds to {path}");
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
