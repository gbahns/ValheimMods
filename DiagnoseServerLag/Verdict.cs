using System.Collections.Generic;
using UnityEngine;

namespace DiagnoseServerLag
{
    /// <summary>What is actually wrong. The whole point of the mod is to pick the right one of these.</summary>
    internal enum Cause
    {
        Healthy,
        Measuring,
        /// <summary>The server could not keep up. Felt by everyone at once, and nothing a client can fix.</summary>
        ServerStarved,
        /// <summary>The link to this player is carrying more than it can drain.</summary>
        LinkSaturated,
        /// <summary>Packets are being lost or the latency is swinging. A path problem, not a load problem.</summary>
        ConnectionQuality,
        /// <summary>Far more objects are changing than usual, so the link is full of legitimate traffic.</summary>
        ObjectChurn,
        /// <summary>The client is building a piece of the world. Transient by nature.</summary>
        SceneLoading,
        /// <summary>This computer could not draw fast enough, with everything else healthy.</summary>
        ThisMachine,
        /// <summary>Something stalled and none of the rules claimed it.</summary>
        Unattributed,
    }

    /// <summary>One conclusion, with the numbers that led to it.</summary>
    internal sealed class Finding
    {
        internal Cause Cause;
        /// <summary>0-100. How well the evidence separates this cause from the others.</summary>
        internal int Confidence;
        internal string Headline;
        internal string Advice;
        internal readonly List<string> Evidence = new List<string>();

        internal Finding(Cause cause, int confidence, string headline, string advice)
        {
            Cause = cause;
            Confidence = confidence;
            Headline = headline;
            Advice = advice;
        }

        internal Finding With(string evidence) { Evidence.Add(evidence); return this; }
    }

    /// <summary>
    /// Turns the measurements into an accusation.
    ///
    /// The reason this mod exists is that the six things below all feel identical while playing -
    /// the game goes sticky - and the fix for each is unrelated to the fix for the others. Upgrading
    /// a graphics card does nothing for a starved server; restarting the server does nothing for a
    /// lossy home connection; and pruning a base does nothing for either. So the output is a verdict
    /// with its evidence attached, not a wall of numbers for someone to interpret under pressure.
    ///
    /// Two kinds of test are used together, deliberately.
    ///
    /// Absolute thresholds carry the tests that have a physical meaning: a connection losing packets
    /// is losing packets whatever it did a minute ago, and a server taking 80 ms to tick is too slow
    /// to run Valheim's send loop properly no matter what it is used to.
    ///
    /// Baseline-relative tests carry everything whose normal value depends on the world, the base
    /// and the hardware. There is no universal right number for how many objects a second should be
    /// changing near a large base, so those rules ask whether now is much worse than the last minute
    /// on this very server, which needs no universal number to be true.
    /// </summary>
    internal static class Verdict
    {
        /// <summary>Enough seconds to say anything at all.</summary>
        private const int MinimumSamples = 5;

        /// <summary>
        /// Every conclusion the current measurements support, best first.
        ///
        /// More than one can be true at once and often is - a saturated link and heavy object churn
        /// are usually the same event seen from two sides - so the list is ranked rather than
        /// reduced to a single answer, and the panel shows the rest underneath the headline.
        /// </summary>
        internal static List<Finding> Diagnose()
        {
            var findings = new List<Finding>();

            var window = Sampler.History.Recent(DslConfig.WindowSeconds.Value);
            if (window.Count < MinimumSamples)
            {
                findings.Add(new Finding(Cause.Measuring, 0,
                    "Still measuring.",
                    $"Give it {MinimumSamples} seconds in the world. Nothing is wrong; there is just not enough history yet."));
                return findings;
            }

            var baseline = Sampler.BaselineWindow();
            var report = LagNetwork.Latest;

            int stalls = 0;
            foreach (var s in window) stalls += s.Stalls;

            bool serverKnown = report != null && LagNetwork.Module != ServerModule.Absent;
            bool serverHealthy = serverKnown && ServerTickMs(report) < DslConfig.ServerTickWarnMs.Value;

            AddServerStarved(findings, report, serverKnown);
            AddLinkSaturated(findings, window, report);
            AddConnectionQuality(findings, window);
            AddObjectChurn(findings, window, baseline, report);
            AddSceneLoading(findings, window, stalls);
            AddThisMachine(findings, window, stalls, serverKnown, serverHealthy);

            if (findings.Count == 0)
            {
                if (stalls > 0)
                {
                    findings.Add(new Finding(Cause.Unattributed, 30,
                        $"{stalls} stall{(stalls == 1 ? "" : "s")} in the last {window.Count}s, with every measurement normal.",
                        "Nothing this mod watches explains it. The usual causes left are outside the game: another " +
                        "program taking the CPU or the disk, a driver hitch, or shader compilation on a machine that " +
                        "has not seen this area before. dsl_dump writes the raw seconds out if you want to look.")
                        .With($"frame times stayed near {Stats.Median(window, x => x.FrameMsAvg):0} ms and no network measurement moved"));
                }
                else
                {
                    var healthy = new Finding(Cause.Healthy, 90,
                        "Nothing wrong right now.",
                        "Leave this open and come back to it after the next bad patch; the last " +
                        $"{Sampler.History.Count}s are kept and the verdict is recalculated every second.");
                    healthy.With($"frames {Stats.Median(window, x => x.FrameMsAvg):0} ms ({Fps(Stats.Median(window, x => x.FrameMsAvg))}), no stalls");
                    if (serverKnown) healthy.With($"server tick {ServerTickMs(report):0.0} ms, {report.Zdos} objects in the world");
                    findings.Add(healthy);
                }
            }

            findings.Sort((a, b) => b.Confidence.CompareTo(a.Confidence));
            return findings;
        }

        /// <summary>The headline conclusion: the strongest finding.</summary>
        internal static Finding Best()
        {
            var all = Diagnose();
            return all.Count > 0 ? all[0] : new Finding(Cause.Measuring, 0, "Still measuring.", "");
        }

        // ── the rules ───────────────────────────────────────────────────────────────

        /// <summary>
        /// The server could not keep up.
        ///
        /// This rule is checked first and outranks the others when it fires, because it is the one
        /// conclusion that makes every other measurement on the client untrustworthy: a server that
        /// is not ticking cannot send position updates on time, so the client sees entities jump,
        /// its own hits fail to register, and its frame rate stay perfectly fine throughout. A
        /// player in that situation who is looking at their own frame counter will conclude their
        /// computer is fine and be completely right, and still be unable to play.
        ///
        /// A dedicated Valheim server ticks in single-digit milliseconds when it is healthy - it
        /// renders nothing and its loop is uncapped - so the thresholds here are deliberately
        /// generous. Anything near them is already far outside normal.
        /// </summary>
        private static void AddServerStarved(List<Finding> findings, ServerReport report, bool serverKnown)
        {
            if (!serverKnown) return;

            float now = report.TickMsAvg;
            float sustained = report.BaselineTickMs;
            float warn = DslConfig.ServerTickWarnMs.Value;
            float severe = DslConfig.ServerTickSevereMs.Value;

            int confidence;
            string headline;
            if (sustained >= severe)
            {
                confidence = 95;
                headline = $"The server is badly starved: {sustained:0} ms per tick sustained ({Fps(sustained)}).";
            }
            else if (now >= severe)
            {
                confidence = 85;
                headline = $"The server is stalling right now: {now:0} ms in the last tick measured ({Fps(now)}).";
            }
            else if (sustained >= warn)
            {
                confidence = 80;
                headline = $"The server is not keeping up: {sustained:0} ms per tick sustained ({Fps(sustained)}).";
            }
            else if (now >= warn)
            {
                confidence = 60;
                headline = $"The server dipped: {now:0} ms in the last tick measured ({Fps(now)}).";
            }
            else return;

            var f = new Finding(Cause.ServerStarved, confidence, headline,
                "Nothing on your machine will change this, and it is happening to everyone online at the same " +
                "time. On a rented server the causes worth checking in order are: too many ticking objects in " +
                "the world, another mod doing work every tick, and the host machine being oversubscribed. If " +
                "the object count below is normal and the tick time is still bad, it is the host.");

            f.With($"tick {now:0.0} ms now, {sustained:0.0} ms median over the server's last {report.WindowSeconds}s");
            if (report.WorstTickMs > 0f) f.With($"worst single tick in that window {report.WorstTickMs:0} ms");
            if (report.StallsInWindow > 0) f.With($"{report.StallsInWindow} server stalls over the window");
            f.With($"{report.Zdos} networked objects in the world, {report.ZdosSent}/s sent to {report.PeerCount} player{(report.PeerCount == 1 ? "" : "s")}");
            if (!report.Dedicated) f.With("the server is a player's game, not a dedicated one, so it is also drawing the host's screen");
            findings.Add(f);
        }

        /// <summary>
        /// The link cannot drain what is being put on it.
        ///
        /// The send queue is what separates this from every other cause. Bandwidth being busy is
        /// normal and harmless; bandwidth being over-committed shows up as bytes that have been
        /// handed to the socket and have not left, and the direction of that number matters far
        /// more than its size. A queue sitting at a steady 40 KB is a link running full and keeping
        /// up. The same 40 KB while climbing is a link that has already lost, and the backlog it is
        /// building now is what will be felt as rubber-banding several seconds from now.
        /// </summary>
        private static void AddLinkSaturated(List<Finding> findings, List<Sample> window, ServerReport report)
        {
            // Two vantage points on the same link. The client's own queue is what it has failed to
            // upload; its row in the server's peer table is what the server has failed to send it,
            // which is the direction that actually carries the world.
            float clientQueue = Stats.Median(window, x => x.SendQueue);
            float clientSlope = Stats.Slope(window, x => x.SendQueue);

            int downQueue = 0;
            bool haveDown = false;
            if (report != null && report.Peers.Count > 0)
            {
                long me = ZDOMan.GetSessionID();
                foreach (var p in report.Peers)
                {
                    if (p.Uid != me) continue;
                    downQueue = p.SendQueue;
                    haveDown = true;
                    break;
                }
            }

            float worst = Mathf.Max(clientQueue, downQueue);
            float warn = DslConfig.QueueWarnBytes.Value;
            float severe = DslConfig.QueueSevereBytes.Value;
            bool climbing = clientSlope > warn * 0.1f;

            int confidence;
            string headline;
            if (worst >= severe)
            {
                confidence = 90;
                headline = $"The link to the server is saturated: {Stats.Bytes(worst)} queued and not sent.";
            }
            else if (worst >= warn && climbing)
            {
                confidence = 80;
                headline = $"The link is falling behind: {Stats.Bytes(worst)} queued and growing.";
            }
            else if (worst >= warn)
            {
                confidence = 55;
                headline = $"The link is running full: {Stats.Bytes(worst)} queued but holding steady.";
            }
            else return;

            var f = new Finding(Cause.LinkSaturated, confidence, headline,
                "This is bandwidth, not computation. If it happens near one particular base, that base is " +
                "sending more than the connection can carry and thinning it out is the fix. If it happens " +
                "everywhere, it is the connection itself - and a mod that compresses the protocol, such as " +
                "BetterNetworking, buys real headroom here.");

            f.With($"your upload queue {Stats.Bytes(clientQueue)} median over {window.Count}s, {(clientSlope >= 0f ? "+" : "")}{Stats.Bytes(clientSlope)}/s trend");
            if (haveDown) f.With($"the server's queue toward you {Stats.Bytes(downQueue)}");
            else if (report != null) f.With("the server did not share the per-player table, so only your upload side is measured");
            f.With($"{Stats.Bytes(Stats.Median(window, x => x.InByteSec))}/s in, {Stats.Bytes(Stats.Median(window, x => x.OutByteSec))}/s out");
            findings.Add(f);
        }

        /// <summary>
        /// The path between the two machines is unreliable.
        ///
        /// Steam reports a connection quality as a fraction of packets getting through, so anything
        /// below 1.0 is measured loss rather than an estimate. Jitter is checked separately from
        /// latency because the two feel completely different and get confused constantly: a steady
        /// 140 ms to a distant server is playable and consistent, while a connection swinging
        /// between 20 and 220 ms averages better and is far worse to play on, because every
        /// prediction the client makes is wrong by a different amount each time.
        /// </summary>
        private static void AddConnectionQuality(List<Finding> findings, List<Sample> window)
        {
            // A zero here means the socket could not tell us, not that the link is perfect: the
            // plain TCP path hardcodes ping and quality to zero. Judging those samples would hand
            // out a clean bill of health precisely when there is no evidence for one.
            var measured = new List<Sample>();
            foreach (var s in window) if (s.HasPing) measured.Add(s);
            if (measured.Count < 3) return;

            float quality = Mathf.Min(Stats.Median(measured, x => x.LocalQuality), Stats.Median(measured, x => x.RemoteQuality));
            float ping = Stats.Median(measured, x => x.Ping);
            float jitter = Stats.Jitter(measured, x => x.Ping);

            bool lossy = quality > 0f && quality < DslConfig.QualityWarn.Value;
            bool jittery = jitter >= DslConfig.PingJitterWarnMs.Value;
            if (!lossy && !jittery) return;

            int confidence = 0;
            string headline;
            if (quality > 0f && quality < DslConfig.QualitySevere.Value)
            {
                confidence = 90;
                headline = $"Packets are being lost: connection quality {quality * 100f:0.0}%.";
            }
            else if (lossy && jittery)
            {
                confidence = 85;
                headline = $"The connection is unstable: {quality * 100f:0.0}% quality and {jitter:0} ms of jitter.";
            }
            else if (lossy)
            {
                confidence = 70;
                headline = $"Some packet loss: connection quality {quality * 100f:0.0}%.";
            }
            else
            {
                confidence = 65;
                headline = $"The latency is swinging: {jitter:0} ms of jitter around a {ping:0} ms ping.";
            }

            var f = new Finding(Cause.ConnectionQuality, confidence, headline,
                "This is the network path, not the server and not your computer, so neither restarting the " +
                "server nor turning settings down will touch it. Wi-Fi is the usual answer and a cable is the " +
                "usual fix; after that it is the route, which a VPN sometimes improves and sometimes ruins.");

            f.With($"ping {ping:0} ms median, {jitter:0} ms average change per second");
            if (quality > 0f) f.With($"quality {quality * 100f:0.0}% (100% is no measured loss)");
            f.With($"measured on {measured.Count} of the last {window.Count} seconds");
            findings.Add(f);
        }

        /// <summary>
        /// Far more of the world is changing than usual.
        ///
        /// This one has no defensible absolute threshold - a quiet forest and a large base with
        /// fifty smelters running legitimately differ by an order of magnitude - so it is judged
        /// entirely against what this server was doing a minute ago. Both a floor and a multiplier
        /// are required, because tripling a very small number is not news.
        /// </summary>
        private static void AddObjectChurn(List<Finding> findings, List<Sample> window, List<Sample> baseline, ServerReport report)
        {
            float now = Stats.Median(window, x => x.ZdosRecv);
            float normal = Stats.Median(baseline, x => x.ZdosRecv);
            float floor = DslConfig.ChurnFloor.Value;
            float factor = DslConfig.ChurnFactor.Value;

            if (now < floor) return;
            if (normal <= 0f || now < normal * factor) return;

            int confidence = now >= normal * factor * 2f ? 75 : 60;
            var f = new Finding(Cause.ObjectChurn, confidence,
                $"The world is churning: {now:0} object updates a second, {now / normal:0.0}x the usual {normal:0}.",
                "Nothing is broken; the connection is full of traffic the game genuinely needs to send. It is " +
                "worth knowing what produced it - a big base coming into range, a raid, a herd of tamed animals " +
                "breeding, a field of crops, or a pile of dropped items nobody collected. Thinning whichever it " +
                "is does more for everyone than any setting.");

            f.With($"{now:0}/s received now against a {baseline.Count}s median of {normal:0}/s");
            if (Sampler.TryNewest(out var newest)) f.With($"{newest.Instances} objects instantiated around you");
            if (report != null) f.With($"the server is sending {report.ZdosSent}/s across {report.PeerCount} player{(report.PeerCount == 1 ? "" : "s")} and holds {report.Zdos} objects");
            findings.Add(f);
        }

        /// <summary>
        /// The client is building part of the world.
        ///
        /// Worth naming separately from every other client-side stall because it is the one that is
        /// supposed to happen and will stop on its own. Someone who rides into a large base, hitches
        /// for four seconds and then plays perfectly has not got a problem to solve, and telling
        /// them their machine is too slow would send them off to change settings that were never
        /// involved.
        /// </summary>
        private static void AddSceneLoading(List<Finding> findings, List<Sample> window, int stalls)
        {
            if (stalls <= 0) return;
            float rise = Stats.Slope(window, x => x.Instances);
            if (rise < DslConfig.InstanceRiseWarn.Value) return;

            var f = new Finding(Cause.SceneLoading, 70,
                $"Your game is building the world around you: {rise:0} new objects a second while it stalled.",
                "Expected, and it stops once the area is built. It only deserves attention if it lasts more " +
                "than a few seconds every time you reach the same place, which means that place holds more " +
                "pieces than your machine can instantiate smoothly.");

            Sampler.TryNewest(out var newest);
            f.With($"{stalls} stall{(stalls == 1 ? "" : "s")} while the instance count climbed to {newest.Instances}");
            f.With($"worst frame {Stats.Max(window, x => x.FrameMsMax):0} ms");
            findings.Add(f);
        }

        /// <summary>
        /// This computer, and nothing else.
        ///
        /// Deliberately the last rule and conditional on the others having stayed quiet. It is the
        /// conclusion players reach first and the one they are most often wrong about, so it is
        /// only worth stating when the alternatives have actually been measured and ruled out -
        /// which is the whole reason the server half of this mod exists. With no server report the
        /// confidence drops accordingly, and the finding says so rather than pretending.
        /// </summary>
        private static void AddThisMachine(List<Finding> findings, List<Sample> window, int stalls, bool serverKnown, bool serverHealthy)
        {
            float frame = Stats.Median(window, x => x.FrameMsAvg);
            if (frame < DslConfig.ClientFrameWarnMs.Value && stalls == 0) return;

            // If another rule already explained it, this is a symptom rather than a cause.
            foreach (var f in findings)
                if (f.Cause == Cause.SceneLoading || f.Cause == Cause.ServerStarved) return;

            int confidence;
            string advice;
            if (serverKnown && serverHealthy)
            {
                confidence = 85;
                advice = "The server was measured and it was keeping up, so this really is local. Graphics " +
                         "settings, background programs and driver updates are worth your time here; restarting " +
                         "the server is not.";
            }
            else if (serverKnown)
            {
                confidence = 40;
                advice = "The server was also struggling, so this may be a symptom rather than the cause. Read " +
                         "the server finding above first.";
            }
            else
            {
                confidence = 45;
                advice = "The server is not running this mod, so it could not be measured and cannot be ruled " +
                         "out. Installing DiagnoseServerLag on the server is what turns this guess into an answer.";
            }

            var finding = new Finding(Cause.ThisMachine, confidence,
                frame >= DslConfig.ClientFrameWarnMs.Value
                    ? $"Your machine is the bottleneck: {frame:0} ms per frame ({Fps(frame)})."
                    : $"Your machine hitched: {stalls} stall{(stalls == 1 ? "" : "s")} in {window.Count}s with the network healthy.",
                advice);

            finding.With($"frames {frame:0} ms median, worst {Stats.Max(window, x => x.FrameMsMax):0} ms");
            if (serverKnown) finding.With($"server tick {ServerTickMs(LagNetwork.Latest):0.0} ms - {(serverHealthy ? "healthy" : "also struggling")}");
            else finding.With("no server measurement available");
            finding.With($"queue {Stats.Bytes(Stats.Median(window, x => x.SendQueue))}, ping {Stats.Median(window, x => x.Ping):0} ms");
            findings.Add(finding);
        }

        // ── helpers ─────────────────────────────────────────────────────────────────

        /// <summary>The server's worst honest tick figure: sustained if it has one, otherwise the latest.</summary>
        private static float ServerTickMs(ServerReport report)
        {
            if (report == null) return 0f;
            return Mathf.Max(report.BaselineTickMs, report.TickMsAvg);
        }

        /// <summary>Frame time read back as a rate, because that is the unit people think in.</summary>
        internal static string Fps(float ms) => ms <= 0.0001f ? "-" : $"{1000f / ms:0} per second";

        /// <summary>What the diagnosis could not see, stated plainly so the verdict is never overtrusted.</summary>
        internal static string CoverageNote()
        {
            switch (LagNetwork.Module)
            {
                case ServerModule.Local:
                    return Sampler.IsDedicatedHere
                        ? "This is the server. The numbers below are its own, measured directly."
                        : "You are the host, so the server numbers are read directly from this process.";
                case ServerModule.Present:
                    float age = LagNetwork.ReportAge;
                    return $"Server reporting in, last heard {age:0.0}s ago.";
                case ServerModule.Absent:
                    return "The server is not running this mod, so it cannot be measured. Every verdict below is " +
                           "made from this machine's side alone, and 'the server was fine' is the one thing it " +
                           "cannot tell you.";
                default:
                    return "Waiting for the server to answer.";
            }
        }
    }
}
