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
        /// <summary>The stalls are collection pauses. Memory, not speed.</summary>
        GarbageCollection,
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
            // Healthy means the server rule did not fire, which now includes a server sitting
            // steadily on its frame cap. Comparing the raw tick time against the warning threshold
            // here would have gone on calling a capped server unhealthy after the rule itself
            // stopped, and quietly held down the confidence of every client-side finding.
            bool serverHealthy = serverKnown
                && (ServerPaced(report) || ServerTickMs(report) < DslConfig.ServerTickWarnMs.Value)
                && ServerTickMs(report) < DslConfig.ServerTickSevereMs.Value;

            AddServerStarved(findings, report, serverKnown);
            AddLinkSaturated(findings, window, report);
            AddConnectionQuality(findings, window);
            AddObjectChurn(findings, window, baseline, report);
            AddSceneLoading(findings, window, stalls);
            AddGarbageCollection(findings, window, stalls);
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
                    if (serverKnown)
                        healthy.With(ServerPaced(report)
                            ? $"server ticking steadily at {ServerTickMs(report):0.0} ms ({Fps(ServerTickMs(report))}), which is its cap rather than a limit it is straining against; {report.Zdos} objects in the world"
                            : $"server tick {ServerTickMs(report):0.0} ms, {report.Zdos} objects in the world");
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

            // A steady tick is a frame cap, not a struggle - checked before the thresholds, and
            // the reason this rule stopped accusing a perfectly healthy server. See ServerPaced.
            // Above the severe threshold it no longer earns the benefit of the doubt: a server
            // holding a metronomic 200 ms is still far too slow to run the game, however even it is.
            if (sustained < severe && ServerPaced(report)) return;

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
            if (report.WorstTickMs > 0f) f.With($"worst single tick in that window {report.WorstTickMs:0} ms, against a {sustained:0.0} ms median - the spread is what says this is not simply a frame cap");
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
        /// The stalls are garbage collections.
        ///
        /// This is the rule that stops "your machine hitched" being the end of the conversation. A
        /// collection pause is invisible to every other measurement here: the network is fine, the
        /// server is fine, the frame average barely moves, and one frame in a hundred takes a
        /// quarter of a second. That is exactly the shape a player reports as random stuttering.
        ///
        /// Coincidence alone would not be evidence. A big heap collects constantly, so "there was a
        /// collection during the stall" is nearly always true and proves nothing. What counts is
        /// whether collections are *disproportionately* concentrated in the seconds that stalled
        /// compared with the seconds that did not - the same base-rate comparison the churn rule
        /// uses, for the same reason.
        ///
        /// Which collections count is left to Machine, because it depends on the runtime. Where the
        /// generations are real, only gen1 and gen2 are considered: gen0 is cheap and constant and
        /// would make this fire everywhere. Unity's Mono reports one number three times, so there
        /// the test is simply whether a collection happened at all - which is why the base-rate
        /// comparison carries the rule rather than the generation filter.
        /// </summary>
        private static void AddGarbageCollection(List<Finding> findings, List<Sample> window, int stalls)
        {
            if (stalls <= 0) return;

            int stallSeconds = 0, quietSeconds = 0, gcInStall = 0, gcInQuiet = 0;
            int gen2 = 0, gen1 = 0, total = 0;
            foreach (var s in window)
            {
                bool collected = Machine.Collected(s);
                gen2 += s.Gc2;
                gen1 += s.Gc1;
                total += Machine.Collections(s);
                if (s.Stalls > 0) { stallSeconds++; if (collected) gcInStall++; }
                else { quietSeconds++; if (collected) gcInQuiet++; }
            }
            if (stallSeconds < 2 || gcInStall == 0) return;

            float duringStalls = (float)gcInStall / stallSeconds;
            float duringQuiet = quietSeconds > 0 ? (float)gcInQuiet / quietSeconds : 0f;
            if (duringStalls < DslConfig.GcCoincidence.Value) return;
            // Twice the background rate, or a background rate of essentially zero. Without this a
            // machine collecting every second would have every stall blamed on the collector.
            if (duringQuiet > 0.01f && duringStalls < duringQuiet * 2f) return;

            int confidence = duringQuiet <= 0.01f ? 85 : 70;
            var f = new Finding(Cause.GarbageCollection, confidence,
                $"The hitches are garbage collections: {gcInStall} of {stallSeconds} stalled second{(stallSeconds == 1 ? "" : "s")} had one.",
                "This is memory, not speed. The game pauses to collect, and the pause gets longer the more memory is " +
                "under management - so the machine-wide picture matters as much as the game's own heap. Closing what " +
                "else is running, and anything holding a large working set, does more here than any graphics setting. " +
                "If Windows is compressing memory to keep up, that is the same problem seen from the other side.");

            f.With($"collections in {duringStalls * 100f:0}% of stalled seconds against {duringQuiet * 100f:0}% of quiet ones");
            f.With(Machine.GenerationsDistinct
                ? $"{gen1} gen1 and {gen2} gen2 collections over {window.Count}s"
                : $"{total} collections over {window.Count}s (this runtime does not separate the generations)");
            f.With($"worst frame {Stats.Max(window, x => x.FrameMsMax):0} ms, median {Stats.Median(window, x => x.FrameMsAvg):0} ms");
            Sampler.TryNewest(out var now);
            if (now.HeapBytes > 0)
                f.With($"game heap {Stats.Bytes(now.HeapBytes)}, working set {Stats.Bytes(now.WorkingSetBytes)}");
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
                if (f.Cause == Cause.SceneLoading || f.Cause == Cause.ServerStarved
                    || f.Cause == Cause.GarbageCollection) return;

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
            // Without this the finding names the machine and says nothing about what it was doing,
            // which is where the reader was left to guess.
            bool haveCpu = false;
            foreach (var w in window) if (w.HasCpu) { haveCpu = true; break; }
            if (haveCpu)
            {
                float cpu = Stats.Median(window, x => x.CpuMsPerSec);
                finding.With($"this game used {Machine.CoreShare(cpu) * 100f:0}% of one core, {Machine.MachineShare(cpu) * 100f:0.0}% of {Machine.ProcessorCount} threads");
                finding.With($"heap {Stats.Bytes(Stats.Median(window, x => x.HeapBytes))}, working set {Stats.Bytes(Stats.Median(window, x => x.WorkingSetBytes))}, " +
                             $"{Stats.Mean(window, x => x.Gc1) * 60f:0} gen1 and {Stats.Mean(window, x => x.Gc2) * 60f:0.0} gen2 collections a minute");
            }
            findings.Add(finding);
        }

        // ── helpers ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Whether the server is holding a deliberate, steady tick rate rather than failing to keep up.
        ///
        /// This rule exists because the first version of the mod accused a healthy server on sight.
        /// bahnsheim reported 33.3 ms now, 33.3 ms median and a 34 ms worst tick, with no stalls -
        /// which is not a server in trouble, it is a server pinned to exactly 30 ticks a second by a
        /// frame limiter. The old 33 ms warning threshold sat precisely on that cap, so the verdict
        /// read "the server is not keeping up" permanently, about a server that was keeping up
        /// perfectly.
        ///
        /// The discriminator is spread, not level. A frame limiter holds every tick to nearly the
        /// same length; a machine that genuinely cannot keep up produces variance, because the work
        /// that overruns is not the same work every tick. So a worst tick barely above the median,
        /// with no stalls at all, means the number you are looking at is a cap - and a cap tells you
        /// nothing except which rate the server was configured for.
        ///
        /// Deliberately not a fixed list of known cap values. 30 Hz is what this server runs, but
        /// another host may cap at 20 or 60, and the shape of the evidence identifies all of them
        /// without anyone having to enumerate them.
        /// </summary>
        internal static bool ServerPaced(ServerReport report)
        {
            if (report == null || report.BaselineTickMs <= 0f || report.WorstTickMs <= 0f) return false;
            if (report.StallsInWindow > 0) return false;
            // The small constant keeps a very fast server - where a fraction of a millisecond of
            // jitter is a large ratio - from failing the test for no reason.
            return report.WorstTickMs <= report.BaselineTickMs * DslConfig.SteadyTickRatio.Value + 2f;
        }

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
