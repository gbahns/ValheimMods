using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DiagnoseServerLag
{
    /// <summary>
    /// The report: the verdict, what it was decided from, and the live numbers behind it.
    ///
    /// Built to be read while annoyed. The conclusion and what to do about it are at the top in
    /// full sentences, and everything underneath exists to let a skeptical reader check the
    /// accusation rather than to be read in order. That ordering is the entire design: a panel
    /// that opened on a grid of counters would be one more thing to interpret at exactly the
    /// moment nobody wants to interpret anything.
    ///
    /// The frame is the game's own text prompt, cloned and stripped, so it matches Valheim without
    /// shipping any art. Built lazily on first use; the scene unload on logout destroys it and it
    /// is rebuilt on the next open.
    /// </summary>
    internal static class LagPanel
    {
        private const float W = 860f, H = 640f;
        private const float MinW = 560f, MinH = 400f, MaxW = 1800f, MaxH = 1400f;

        // Distances from the panel's top-left corner, matching UiKit.Place's convention: x runs
        // right from the left edge, y runs down from the top edge.
        private const float TitleY = 14f, SubtitleY = 48f;
        private const float VerdictTop = 78f;
        private const float SidePad = 30f;
        private const float ButtonH = 34f;

        /// <summary>UiKit has no color for "this is the problem"; the other panels never needed one.</summary>
        private static readonly Color Bad = new Color(1f, 0.42f, 0.35f);
        private static readonly Color Good = new Color(0.55f, 0.85f, 0.5f);

        // The pause toggle's three states, matching the map and the inventory panel exactly so the
        // mark means the same thing wherever it appears.
        private static readonly Color PauseOff = new Color(0.6f, 0.6f, 0.6f, 0.85f);
        private static readonly Color PausePaused = new Color(1f, 0.63f, 0.24f, 1f);   // Valheim orange
        private static readonly Color PauseRefused = new Color(1f, 0.45f, 0.4f, 1f);

        private static GameObject _root;
        private static RectTransform _list;
        private static ScrollRect _scroll;
        private static TextMeshProUGUI _title, _subtitle, _verdict, _advice;
        private static Button _refreshButton, _dumpButton, _closeButton;
        private static RectTransform _grip, _mover;
        private static GameObject _pauseToggle;
        private static Image _pauseLeft, _pauseRight, _pauseSlash;

        private static float _w = W, _h = H;
        private static bool _openedThisFrame;
        private static bool _buildFailed;
        private static int _closedFrame;
        private static float _nextRefresh;

        internal static bool IsOpen => _root != null && _root.activeSelf;
        internal static bool JustClosed => Time.frameCount - _closedFrame <= 1;

        // ── open / close ────────────────────────────────────────────────────────────

        internal static void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        internal static void Open()
        {
            if (Player.m_localPlayer == null) return;
            if (_root == null && !Build())
            {
                DiagnoseServerLagMod.Message("Report unavailable; try dsl_why in the console.");
                return;
            }
            LoadPlacement();
            _root.SetActive(true);
            _root.transform.SetAsLastSibling();
            _openedThisFrame = true;
            Layout();
            // Ask straight away rather than waiting for the next scheduled round: the panel is
            // usually opened in the middle of the thing it is meant to explain.
            LagNetwork.Ask();
            Populate();
            LagPause.Refresh();
            _nextRefresh = Time.unscaledTime + 1f;
        }

        internal static void Close()
        {
            if (_root != null && _root.activeSelf)
            {
                SavePlacement();
                _root.SetActive(false);
                _closedFrame = Time.frameCount;
            }
            // Let go of the pause on the way out, not on the next frame's Refresh: closing the
            // report and leaving the world frozen for a moment would be its own small bug.
            LagPause.Refresh();
        }

        // ── per-frame ───────────────────────────────────────────────────────────────

        internal static void Update()
        {
            if (!IsOpen)
            {
                if (DslConfig.OpenKey != null && Keys.IsDown(DslConfig.OpenKey.Value) && Keys.CanTakeInput()) Open();
                return;
            }

            if (_openedThisFrame) { _openedThisFrame = false; return; }

            // The console opens over the panel and `dsl` in the console is itself a way to get
            // here, so while the console or chat owns the keyboard the panel must keep its hands
            // off it or every keystroke does two things at once.
            if (Console.IsVisible() || (Chat.instance != null && Chat.instance.HasFocus())) return;

            if (ZInput.GetKeyDown(KeyCode.Escape) || (DslConfig.OpenKey != null && Keys.IsDown(DslConfig.OpenKey.Value)))
            {
                Close();
                return;
            }

            // Once a second, in step with the sampler. Rebuilding faster would show the same
            // numbers and throw away the reader's scroll position while they were reading them.
            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + 1f;
            Populate();
        }

        // ── contents ────────────────────────────────────────────────────────────────

        private static void Populate()
        {
            if (_list == null) return;

            var findings = Verdict.Diagnose();
            var best = findings.Count > 0 ? findings[0] : null;

            _subtitle.text = Verdict.CoverageNote();
            _subtitle.color = LagNetwork.Module == ServerModule.Absent ? UiKit.Header : UiKit.Dim;
            // A frozen measurement that looked live would be the one lie this panel must not tell.
            if (Sampler.Frozen)
            {
                _subtitle.text += "   -   paused, so nothing is being recorded";
                _subtitle.color = PausePaused;
            }

            if (best != null)
            {
                _verdict.text = best.Headline;
                _verdict.color = SeverityColor(best);
                _advice.text = best.Advice;
            }

            // The scroll position is kept: the panel rebuilds every second and a reader halfway
            // down the evidence must not be thrown back to the top each time.
            float scrollPos = _scroll != null ? _scroll.verticalNormalizedPosition : 1f;
            UiKit.ClearChildren(_list);

            AddOtherFindings(findings);
            AddThisMachine();
            AddOwnerLatency();
            AddServerSection();
            AddPeers();

            if (_scroll != null) _scroll.verticalNormalizedPosition = scrollPos;
            PaintPauseToggle();
            Layout();
        }

        /// <summary>
        /// Everything the evidence supports besides the headline.
        ///
        /// Kept rather than discarded because these causes genuinely co-occur: a saturated link and
        /// heavy object churn are usually one event seen from two ends, and showing only the
        /// strongest would hide the half that explains the other.
        /// </summary>
        private static void AddOtherFindings(List<Finding> findings)
        {
            UiKit.SectionHeader(_list, "Why");
            if (findings.Count == 0) return;

            for (int i = 0; i < findings.Count; i++)
            {
                var f = findings[i];
                string prefix = i == 0 ? "verdict" : "also";
                UiKit.Row(_list, $"{prefix}  {f.Headline}", f.Cause == Cause.Measuring || f.Cause == Cause.Healthy ? "" : $"{f.Confidence}%",
                    null, null, 17f, SeverityColor(f));
                foreach (var e in f.Evidence)
                    UiKit.Row(_list, $"      {e}", "", null, null, 15f, UiKit.Dim);
                if (i > 0 && !string.IsNullOrEmpty(f.Advice))
                    Paragraph(_list, f.Advice, 14f, UiKit.Body, indent: 24f);
            }
        }

        private static void AddThisMachine()
        {
            UiKit.SectionHeader(_list, "This machine");
            if (!Sampler.TryNewest(out var s))
            {
                UiKit.Row(_list, "nothing measured yet", "", null, null, 15f, UiKit.Dim);
                return;
            }

            var window = Sampler.History.Recent(DslConfig.WindowSeconds.Value);
            int stalls = 0;
            foreach (var w in window) stalls += w.Stalls;

            StatRow("frame time", $"{s.FrameMsAvg:0.0} ms  ({Verdict.Fps(s.FrameMsAvg)})",
                Rank(s.FrameMsAvg, DslConfig.ClientFrameWarnMs.Value, DslConfig.ClientFrameWarnMs.Value * 2f));
            StatRow("worst frame", $"{Stats.Max(window, x => x.FrameMsMax):0} ms in the last {window.Count}s", 0);
            StatRow("stalls", $"{stalls} over {window.Count}s, counting frames past {DslConfig.StallMs.Value:0} ms",
                stalls > 0 ? 1 : 0);
            // Named for what it is. A socket ping and a request round trip are different numbers -
            // the round trip includes a frame of server processing - and quietly labelling one as
            // the other is the kind of small lie this panel exists not to tell.
            string pingLabel = s.PingFromRoundTrip ? "round trip" : "ping";
            StatRow(pingLabel, s.HasPing
                    ? $"{s.Ping} ms, {Stats.Jitter(window, x => x.Ping):0} ms jitter" +
                      (s.PingFromRoundTrip ? "   (measured by this mod; the socket cannot report one)" : "")
                    : "not measurable on this socket",
                s.HasPing ? Rank(Stats.Jitter(window, x => x.Ping), DslConfig.PingJitterWarnMs.Value, DslConfig.PingJitterWarnMs.Value * 2f) : 0);
            // Quality is a separate question from latency: a round trip tells you nothing about
            // packet loss, so having one must not make this row claim to be measured.
            bool qualityMeasured = s.LocalQuality > 0f || s.RemoteQuality > 0f;
            StatRow("quality", qualityMeasured
                    ? $"{s.LocalQuality * 100f:0.0}% local, {s.RemoteQuality * 100f:0.0}% remote"
                    : "not measurable on this socket",
                qualityMeasured ? RankLow(s.LocalQuality, DslConfig.QualityWarn.Value, DslConfig.QualitySevere.Value) : 0);
            StatRow("upload queue", $"{Stats.Bytes(s.SendQueue)}, {Stats.Bytes(Stats.Slope(window, x => x.SendQueue))}/s trend",
                Rank(s.SendQueue, DslConfig.QueueWarnBytes.Value, DslConfig.QueueSevereBytes.Value));
            StatRow("bandwidth", $"{Stats.Bytes(s.InByteSec)}/s in, {Stats.Bytes(s.OutByteSec)}/s out", 0);
            StatRow("objects", $"{s.Zdos} known, {s.Instances} built nearby", 0);
            // Creature AI runs only on the owner, so this is the share of the group's simulation
            // this machine is carrying - and on a shared zone it is nobody's deliberate choice.
            if (s.NearbyAI > 0)
            {
                // Only a finding when there is somebody to have shared it with; see LagHud.
                bool carrying = Sampler.PlayersOnline > 1
                                && s.NearbyAI >= 5
                                && s.OwnedAI >= s.NearbyAI * 0.8f;
                string note = Sampler.PlayersOnline > 1 && !string.IsNullOrEmpty(Sampler.OtherOwners)
                    ? $"   ({Sampler.OtherOwners})" : "";
                StatRow("simulating", $"{s.OwnedAI} of {s.NearbyAI} creatures loaded nearby{note}", carrying ? 1 : 0);
            }
            StatRow("object traffic", $"{s.ZdosRecv}/s in, {s.ZdosSent}/s out, {s.ChangeQueue} unacknowledged", 0);
            // The machine-level rows. Without these the panel could say "your machine hitched" and
            // show nothing at all about what the machine was doing, which is exactly where a reader
            // was left to guess.
            if (Machine.Readable && s.HasCpu)
            {
                StatRow("game CPU", $"{Machine.CoreShare(s.CpuMsPerSec) * 100f:0}% of one core, " +
                                    $"{Machine.MachineShare(s.CpuMsPerSec) * 100f:0.0}% of {Machine.ProcessorCount} threads",
                    Rank(Machine.CoreShare(s.CpuMsPerSec), 0.7f, 0.95f));
                float perMin = Stats.Mean(window, x => Machine.Collections(x)) * 60f;
                StatRow("collections", Machine.GenerationsDistinct
                        ? $"{Stats.Mean(window, x => x.Gc0) * 60f:0} gen0, {Stats.Mean(window, x => x.Gc1) * 60f:0} gen1, {Stats.Mean(window, x => x.Gc2) * 60f:0.0} gen2 per minute"
                        : $"{perMin:0.0} per minute (this runtime does not separate the generations)",
                    perMin >= 6f ? 1 : 0);
                // A working set of zero is Mono declining to answer, not a process using no memory.
                StatRow("memory", Machine.HasWorkingSet
                        ? $"heap {Stats.Bytes(s.HeapBytes)}, working set {Stats.Bytes(s.WorkingSetBytes)}"
                        : $"heap {Stats.Bytes(s.HeapBytes)}, working set not measurable on this runtime", 0);
            }
            else if (!Machine.Readable)
            {
                StatRow("game CPU", $"not measurable ({Machine.UnreadableReason})", 0);
            }

            StatRow("history", Sampler.Frozen
                    ? $"{Sampler.History.Count}s kept, held still while paused"
                    : $"{Sampler.History.Count}s kept of {Sampler.History.Capacity}s", 0);
        }

        /// <summary>
        /// Latency to the people whose objects are loaded around you.
        ///
        /// Your ping to the server does not decide how an interaction feels. Valheim routes it to
        /// the object's owner, so hitting somebody else's tree travels you -> server -> them ->
        /// server -> you, and their line and their frame rate are in the middle of it. Measured by
        /// echoing each owner over that same path rather than by adding two pings together, so the
        /// server's forwarding and the owner's own frame time are included - which is exactly the
        /// cost when the owner is the one struggling.
        /// </summary>
        private static void AddOwnerLatency()
        {
            var owners = LagNetwork.OwnerLatencies();
            if (owners.Count == 0) return;

            UiKit.SectionHeader(_list, "Objects owned by other players");
            foreach (var o in owners)
            {
                string who = string.IsNullOrEmpty(o.Name) ? "another player" : o.Name;
                string latency = o.Answered
                    ? $"{o.Ms:0} ms round trip through them"
                    : "no reply - they are not running this mod";
                string holds = $"{o.Objects} object{(o.Objects == 1 ? "" : "s")}" +
                               (o.Creatures > 0 ? $" including {o.Creatures} creature{(o.Creatures == 1 ? "" : "s")}" : "");
                StatRow(who, $"{holds}, {latency}",
                    o.Answered ? Rank(o.Ms, 200f, 400f) : 0);
            }
            Paragraph(_list,
                "Interacting with one of these goes to its owner and back before anything happens, so this is " +
                "the delay you feel hitting their tree - not your ping to the server. Ownership went to whoever " +
                "was in range first and is never rebalanced; moving apart is what changes it.",
                14f, UiKit.Dim);
        }

        private static void AddServerSection()
        {
            UiKit.SectionHeader(_list, "The server");
            var r = LagNetwork.Latest;
            if (r == null)
            {
                // The single most useful thing the panel can say when half the diagnosis is
                // missing: say which half, and why it is missing.
                Paragraph(_list,
                    LagNetwork.Module == ServerModule.Absent
                        ? "The server is not running this mod, so its tick times cannot be measured. That is the one " +
                          "measurement a client cannot make for itself, and without it the mod can tell you your own " +
                          "end is healthy but never that the server's is. Installing the same DLL server-side is what " +
                          "closes the gap."
                        : "Waiting for the server's first answer.",
                    15f, UiKit.Dim);
                return;
            }

            float tick = Mathf.Max(r.TickMsAvg, r.BaselineTickMs);
            // A server sitting on its frame cap is not slow, and the row must not be painted as if
            // it were: the number is identical either way, and only the spread tells them apart.
            bool paced = Verdict.ServerPaced(r);
            StatRow("tick time", $"{r.TickMsAvg:0.0} ms now, {r.BaselineTickMs:0.0} ms median  ({Verdict.Fps(tick)})",
                paced ? 0 : Rank(tick, DslConfig.ServerTickWarnMs.Value, DslConfig.ServerTickSevereMs.Value));
            StatRow("steadiness", paced
                    ? $"worst {r.WorstTickMs:0} ms against a {r.BaselineTickMs:0.0} ms median - a frame cap, not a struggle"
                    : $"worst {r.WorstTickMs:0} ms over {r.WindowSeconds}s, {r.StallsInWindow} stalls",
                paced ? 0 : (r.StallsInWindow > 0 ? 1 : 0));
            StatRow("world", $"{r.Zdos} networked objects", 0);
            StatRow("object traffic", $"{r.ZdosSent}/s out, {r.ZdosRecv}/s in", 0);
            StatRow("players", $"{r.PeerCount} connected", 0);
            StatRow("worst queue", $"{Stats.Bytes(r.WorstSendQueue)} to one player, {Stats.Bytes(r.TotalSendRate)}/s sent in total",
                Rank(r.WorstSendQueue, DslConfig.QueueWarnBytes.Value, DslConfig.QueueSevereBytes.Value));
            StatRow("kind", r.Dedicated ? "dedicated server" : "a player's game, also drawing their screen", 0);
            // The server's own CPU is not in the report yet - dsl_bench_server is where it lives -
            // so say where to get it rather than leaving a gap the reader reads as zero.
            StatRow("server CPU", "run dsl_bench_server for the server's CPU and headroom", 0);
            StatRow("report age", $"{LagNetwork.ReportAge:0.0}s", LagNetwork.ReportAge > 10f ? 1 : 0);
        }

        private static void AddPeers()
        {
            var r = LagNetwork.Latest;
            if (r == null) return;

            UiKit.SectionHeader(_list, "Players");
            if (r.PeerDetailWithheld)
            {
                Paragraph(_list,
                    "The server is set to share the per-player table with admins only. The numbers above still " +
                    "diagnose the server itself; this section is the part that would name which player is on a " +
                    "bad line.", 15f, UiKit.Dim);
                return;
            }
            if (r.Peers.Count == 0)
            {
                UiKit.Row(_list, "nobody connected", "", null, null, 15f, UiKit.Dim);
                return;
            }

            long me = ZDOMan.GetSessionID();
            foreach (var p in r.Peers)
            {
                bool isMe = p.Uid == me;
                int rank = Mathf.Max(
                    Rank(p.SendQueue, DslConfig.QueueWarnBytes.Value, DslConfig.QueueSevereBytes.Value),
                    RankLow(p.Quality, DslConfig.QualityWarn.Value, DslConfig.QualitySevere.Value));
                UiKit.Row(_list,
                    isMe ? $"{p.Name}  (you)" : p.Name,
                    $"{Stats.Bytes(p.SendQueue)} queued",
                    null, null, 16f, rank >= 2 ? Bad : rank == 1 ? UiKit.Header : (isMe ? UiKit.Gold : UiKit.Body),
                    null,
                    $"{(p.HasPing ? p.Ping + (r.PeerPingIsRoundTrip ? " ms rt" : " ms") : "? ms")}" +
                    $"   {(p.Quality > 0f ? $"q {p.Quality * 100f:0}%" : "q -")}   {p.DistanceFromCenter:0} m out");
            }
        }

        /// <summary>
        /// One measurement row: a short label on the left, the value filling everything to its right.
        ///
        /// Built here rather than through UiKit.Row because Row's right-hand column is a fixed
        /// 96-pixel box - the right shape for the short numbers a scoreboard puts in it, and the
        /// wrong one for these. The values here are sentences: "42 ms over 10s", "0 B, 1.2 KB/s
        /// trend". In a 96-pixel box every one of them ellipsized down to a few characters, so the
        /// panel showed "9.7 ms (103...", "-21294 B qu..." and so on - a report built to be read at
        /// a glance, in which nothing could be read at all.
        ///
        /// The label keeps a fixed narrow column so the values line up with each other, and the
        /// value takes the rest of the width, which is what grows when the panel is dragged wider.
        /// </summary>
        private static void StatRow(string label, string value, int rank)
        {
            const float LabelWidth = 180f;
            const float Gap = 8f;

            var go = new GameObject("Stat", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(LayoutElement));
            go.transform.SetParent(_list, false);
            go.GetComponent<Image>().color = UiKit.RowColor;
            var le = go.GetComponent<LayoutElement>();
            le.preferredHeight = UiKit.RowHeight;
            le.minHeight = UiKit.RowHeight;

            var lbl = UiKit.Text(go.transform, "Label", label, 16f, TextAlignmentOptions.Left, UiKit.Dim);
            var lrt = lbl.rectTransform;
            lrt.anchorMin = new Vector2(0f, 0f);
            lrt.anchorMax = new Vector2(0f, 1f);
            lrt.offsetMin = new Vector2(10f, 0f);
            lrt.offsetMax = new Vector2(10f + LabelWidth, 0f);

            // The value carries the color: it is the part that says whether anything is wrong.
            Color c = rank >= 2 ? Bad : rank == 1 ? UiKit.Header : UiKit.Body;
            var val = UiKit.Text(go.transform, "Value", value, 16f, TextAlignmentOptions.Left, c);
            var vrt = val.rectTransform;
            vrt.anchorMin = new Vector2(0f, 0f);
            vrt.anchorMax = new Vector2(1f, 1f);
            vrt.offsetMin = new Vector2(10f + LabelWidth + Gap, 0f);
            vrt.offsetMax = new Vector2(-10f, 0f);
        }

        private static int Rank(float value, float warn, float severe) =>
            value >= severe ? 2 : value >= warn ? 1 : 0;

        /// <summary>The same, for measurements where smaller is worse. Zero means "not measurable", not "broken".</summary>
        private static int RankLow(float value, float warn, float severe)
        {
            if (value <= 0f) return 0;
            return value <= severe ? 2 : value <= warn ? 1 : 0;
        }

        private static Color SeverityColor(Finding f)
        {
            if (f.Cause == Cause.Healthy) return Good;
            if (f.Cause == Cause.Measuring) return UiKit.Dim;
            if (f.Confidence >= 80) return Bad;
            if (f.Confidence >= 60) return UiKit.Header;
            return UiKit.Dim;
        }

        /// <summary>
        /// A wrapped paragraph inside the scroll list.
        ///
        /// The list's layout group controls child heights from their preferred height, and TMP
        /// reports one for the width it is given, so a paragraph sizes itself to however many lines
        /// it needs without anyone counting them.
        /// </summary>
        private static void Paragraph(Transform parent, string text, float size, Color color, float indent = 0f)
        {
            // Built through UiKit.Text rather than by hand. Adding a TextMeshProUGUI to a live
            // GameObject makes TMP look up Unity's default font in Awake, which Valheim does not
            // ship: the component ends up with no font asset at all, and the next layout pass
            // throws NullReferenceException out of TMP_Text.GetPreferredWidth - once per frame,
            // because this sits inside a layout group. UiKit.Text adds the component while the
            // object is inactive and assigns the game's font first, which is the whole reason that
            // dance is in there. Shipped broken in 0.1.0 and 0.1.1, where it fired whenever a
            // paragraph was shown - including the "the server is not running this mod" notice,
            // so almost immediately for anyone on an unmodded server.
            var t = UiKit.Text(parent, "Paragraph", text, size, TextAlignmentOptions.TopLeft, color);
            Wrap(t);
            t.margin = new Vector4(indent + 6f, 4f, 6f, 6f);
        }

        // ── construction ────────────────────────────────────────────────────────────

        private static bool Build()
        {
            if (_buildFailed) return false;
            try
            {
                if (BuildInner()) return true;
                // The game's text prompt was not there to clone. That happens during a scene
                // change and fixes itself, so this is not latched: the next keypress tries again.
            }
            catch (Exception e)
            {
                // A real fault. Latch it, or every keypress repeats the same exception.
                DiagnoseServerLagMod.Log.LogError($"[DiagnoseServerLag] Building the report failed: {e}");
                _buildFailed = true;
            }
            if (_root != null) UnityEngine.Object.Destroy(_root);
            _root = null;
            return false;
        }

        private static bool BuildInner()
        {
            var src = TextInput.instance;
            if (src == null || src.m_panel == null)
            {
                DiagnoseServerLagMod.Log.LogWarning("[DiagnoseServerLag] The game's text prompt is not available; cannot build the report.");
                return false;
            }
            UiKit.EnsureFont();

            _root = UnityEngine.Object.Instantiate(src.m_panel, src.m_panel.transform.parent);
            _root.name = "DSL_Report";
            _root.SetActive(false);

            foreach (var c in _root.GetComponents<LayoutGroup>()) UnityEngine.Object.Destroy(c);
            foreach (var c in _root.GetComponents<ContentSizeFitter>()) UnityEngine.Object.Destroy(c);

            Button template = null;
            foreach (var b in _root.GetComponentsInChildren<Button>(true)) { template = b; break; }

            var content = new GameObject("DSL_Content", typeof(RectTransform));
            content.transform.SetParent(_root.transform, false);
            UiKit.Stretch(content.GetComponent<RectTransform>());

            if (template != null)
            {
                template = UnityEngine.Object.Instantiate(template, content.transform);
                template.gameObject.SetActive(false);
                template.name = "ButtonTemplate";
            }

            // Of what the prompt came with, keep only flat background art; everything else goes.
            for (int i = _root.transform.childCount - 1; i >= 0; i--)
            {
                var child = _root.transform.GetChild(i);
                if (child == content.transform) continue;
                if (IsDecoration(child))
                {
                    child.SetParent(content.transform, false);
                    child.SetAsFirstSibling();
                    UiKit.Stretch(child.GetComponent<RectTransform>());
                }
                else UnityEngine.Object.Destroy(child.gameObject);
            }

            var rt = _root.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(W, H);

            _title = UiKit.Text(content.transform, "Title", "Diagnose Server Lag", 26f, TextAlignmentOptions.Center, UiKit.Gold);
            _subtitle = UiKit.Text(content.transform, "Subtitle", "", 15f, TextAlignmentOptions.Center, UiKit.Dim);
            // UiKit.Text builds labels that never wrap and ellipsize, which is right for table rows
            // and wrong for both of these: a verdict is a sentence and must be readable in full.
            _verdict = UiKit.Text(content.transform, "Verdict", "", 20f, TextAlignmentOptions.TopLeft, UiKit.Header);
            Wrap(_verdict);
            _advice = UiKit.Text(content.transform, "Advice", "", 15f, TextAlignmentOptions.TopLeft, UiKit.Body);
            Wrap(_advice);

            _list = UiKit.ScrollList(content.transform, "List", out _scroll);

            _refreshButton = MakeButton(template, content.transform, "Refresh", "Refresh", () => { LagNetwork.Ask(); Populate(); });
            _dumpButton = MakeButton(template, content.transform, "Dump", "Write CSV", () =>
            {
                string path = Commands.Dump();
                DiagnoseServerLagMod.Message(path == null ? "Could not write the CSV" : "Wrote " + System.IO.Path.GetFileName(path));
            });
            _closeButton = MakeButton(template, content.transform, "Close", "Close", Close);

            BuildPauseToggle(content.transform);

            // The prompt's own labels come with a Localize component that rewrites their text from
            // the language file on enable. Left in place it would overwrite every label here the
            // first time the panel is shown.
            foreach (var l in _root.GetComponentsInChildren<Localize>(true)) UnityEngine.Object.Destroy(l);

            // Resize grip in the bottom-right corner; the size is remembered in the config.
            var gripGo = new GameObject("Grip", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(UiKit.DragHandle));
            gripGo.transform.SetParent(content.transform, false);
            _grip = gripGo.GetComponent<RectTransform>();
            var gripImg = gripGo.GetComponent<Image>();
            gripImg.sprite = UiKit.Grip();
            gripImg.color = new Color(1f, 0.85f, 0.45f, 0.75f);
            gripImg.raycastTarget = true;
            var gripHandle = gripGo.GetComponent<UiKit.DragHandle>();
            gripHandle.OnDrag = OnGripDrag;
            gripHandle.OnEnd = SavePlacement;

            // The title strip drags the whole panel; the position is remembered too. This is an
            // invisible image rather than the title text itself, because the panel's background art
            // is a plain graphic that would swallow the pointer without handling the drag, and only
            // something above it can catch one. Left as the last sibling for the same reason.
            var moveGo = new GameObject("Mover", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(UiKit.DragHandle));
            moveGo.transform.SetParent(content.transform, false);
            _mover = moveGo.GetComponent<RectTransform>();
            var moveImg = moveGo.GetComponent<Image>();
            moveImg.color = new Color(0f, 0f, 0f, 0f);
            moveImg.raycastTarget = true;
            var mover = moveGo.GetComponent<UiKit.DragHandle>();
            mover.OnDrag = OnMoveDrag;
            mover.OnEnd = SavePlacement;

            // The toggle has to sit above the drag strip, which covers the whole title area and
            // therefore the corner the toggle lives in. Sibling order is what decides that, so the
            // toggle goes last however early it was built - otherwise every click on it would be
            // caught by the mover and turn into a one-pixel drag of the panel.
            _pauseToggle.transform.SetAsLastSibling();

            LoadPlacement();
            Layout();
            return true;
        }

        /// <summary>
        /// The pause toggle in the top-right corner, built the same way as the ones on the large map
        /// and GrabMaterials' inventory panel: two bars drawn from plain rectangles, because the
        /// game's font has no media-control glyph, plus a diagonal slash when a pause was asked for
        /// and refused. The mark never claims a pause that is not happening - the request can be
        /// turned down by a server with other players on it, or one without Pause My Server at all.
        /// </summary>
        private static void BuildPauseToggle(Transform parent)
        {
            _pauseToggle = new GameObject("DSL_PauseToggle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            _pauseToggle.transform.SetParent(parent, false);
            var rect = (RectTransform)_pauseToggle.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-14f, -12f);
            rect.sizeDelta = new Vector2(28f, 28f);

            var hit = _pauseToggle.GetComponent<Image>();
            hit.color = new Color(1f, 1f, 1f, 0f);   // invisible, but it is what catches the click
            hit.raycastTarget = true;

            _pauseLeft = PauseBar(_pauseToggle.transform, new Vector2(6f, 18f), new Vector2(-5f, 0f));
            _pauseRight = PauseBar(_pauseToggle.transform, new Vector2(6f, 18f), new Vector2(5f, 0f));
            _pauseSlash = PauseBar(_pauseToggle.transform, new Vector2(30f, 3f), Vector2.zero, 45f);
            _pauseSlash.gameObject.SetActive(false);

            var button = _pauseToggle.GetComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = hit;
            button.onClick.AddListener(() =>
            {
                if (DslConfig.PauseWhileOpen == null) return;
                DslConfig.PauseWhileOpen.Value = !DslConfig.PauseWhileOpen.Value;
                LagPause.Refresh();
                PaintPauseToggle();
            });
        }

        private static Image PauseBar(Transform parent, Vector2 size, Vector2 pos, float rotation = 0f)
        {
            var go = new GameObject("Bar", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            if (rotation != 0f) rt.localRotation = Quaternion.Euler(0f, 0f, rotation);
            var img = go.GetComponent<Image>();
            img.raycastTarget = false;
            return img;
        }

        /// <summary>Gray when switched off, orange while the game really is paused, red with a slash when the pause was refused.</summary>
        private static void PaintPauseToggle()
        {
            if (_pauseToggle == null) return;
            bool show = DslConfig.ShowPauseButton == null || DslConfig.ShowPauseButton.Value;
            if (_pauseToggle.activeSelf != show) _pauseToggle.SetActive(show);
            if (!show || _pauseLeft == null) return;

            bool on = DslConfig.PauseWhileOpen != null && DslConfig.PauseWhileOpen.Value;
            bool refused = false;
            Color color;
            if (!on) color = PauseOff;
            else if (Game.IsPaused()) color = PausePaused;
            else { color = PauseRefused; refused = true; }
            _pauseLeft.color = color;
            _pauseRight.color = color;
            _pauseSlash.color = color;
            if (_pauseSlash.gameObject.activeSelf != refused) _pauseSlash.gameObject.SetActive(refused);
        }

        private static bool IsDecoration(Transform child)
        {
            if (child.GetComponentInChildren<TMP_Text>(true) != null) return false;
            if (child.GetComponentInChildren<Selectable>(true) != null) return false;
            return child.GetComponentInChildren<Graphic>(true) != null && child.GetComponent<RectTransform>() != null;
        }

        private static Button MakeButton(Button template, Transform parent, string name, string label, Action onClick)
        {
            return template != null
                ? UiKit.CloneButton(template, parent, name, label, onClick)
                : UiKit.SimpleButton(parent, name, label, onClick);
        }

        // ── layout ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// Places everything for the current panel size.
        ///
        /// The verdict block is measured rather than given a fixed height: its headline can be one
        /// line or three depending on the cause, and a fixed box would either clip the long ones or
        /// leave a hole under the short ones.
        /// </summary>
        private static void Layout()
        {
            if (_root == null) return;
            _root.GetComponent<RectTransform>().sizeDelta = new Vector2(_w, _h);

            float inner = _w - SidePad * 2f;
            UiKit.Place(_title.rectTransform, 0f, TitleY, _w, 34f);
            UiKit.Place(_subtitle.rectTransform, 0f, SubtitleY, _w, 22f);

            // Measured rather than given a fixed height: a headline can be one line or three
            // depending on the cause, and a fixed box would clip the long ones or leave a hole
            // under the short ones. GetPreferredValues asks for the height at a stated width, so
            // this does not depend on Unity having already laid the label out at the new size.
            float verdictH = Mathf.Max(26f, _verdict.GetPreferredValues(_verdict.text, inner, 0f).y);
            UiKit.Place(_verdict.rectTransform, SidePad, VerdictTop, inner, verdictH);

            float adviceY = VerdictTop + verdictH + 6f;
            float adviceH = string.IsNullOrEmpty(_advice.text)
                ? 0f
                : _advice.GetPreferredValues(_advice.text, inner, 0f).y;
            UiKit.Place(_advice.rectTransform, SidePad, adviceY, inner, adviceH);

            float listY = adviceY + adviceH + 12f;
            float buttonY = _h - ButtonH - 14f;
            float listH = Mathf.Max(80f, buttonY - listY - 12f);
            UiKit.Place(_scroll.GetComponent<RectTransform>(), SidePad, listY, inner, listH);

            UiKit.Place(_refreshButton.GetComponent<RectTransform>(), SidePad, buttonY, 130f, ButtonH);
            UiKit.Place(_dumpButton.GetComponent<RectTransform>(), SidePad + 138f, buttonY, 130f, ButtonH);
            UiKit.Place(_closeButton.GetComponent<RectTransform>(), _w - SidePad - 130f, buttonY, 130f, ButtonH);

            // The drag strip covers the title and subtitle and nothing else, so it can never sit
            // over a control and eat its clicks.
            UiKit.Place(_mover, 0f, 0f, _w, 70f);

            _grip.anchorMin = _grip.anchorMax = _grip.pivot = new Vector2(1f, 0f);
            _grip.anchoredPosition = new Vector2(-5f, 5f);
            _grip.sizeDelta = new Vector2(18f, 18f);
        }

        /// <summary>
        /// Makes a UiKit label wrap instead of ellipsizing.
        ///
        /// UiKit builds labels for table rows, where a long value should be cut off rather than
        /// push the row's neighbors around. The verdict and its advice are prose and need the
        /// opposite, so they are switched over one at a time rather than by changing UiKit for
        /// every panel that shares it.
        /// </summary>
        private static void Wrap(TMP_Text t)
        {
            t.textWrappingMode = TextWrappingModes.Normal;
            t.overflowMode = TextOverflowModes.Overflow;
        }

        /// <summary>The grip follows the pointer: the bottom-right corner moves, the top-left corner stays.</summary>
        private static void OnGripDrag(Vector2 screenDelta)
        {
            if (_root == null) return;
            float scale = CanvasScale();
            float nw = Mathf.Clamp(_w + screenDelta.x / scale, MinW, MaxW);
            float nh = Mathf.Clamp(_h - screenDelta.y / scale, MinH, MaxH);
            var rt = _root.GetComponent<RectTransform>();
            rt.anchoredPosition += new Vector2((nw - _w) / 2f, -(nh - _h) / 2f);
            _w = nw;
            _h = nh;
            Layout();
        }

        private static void OnMoveDrag(Vector2 screenDelta)
        {
            if (_root == null) return;
            var rt = _root.GetComponent<RectTransform>();
            rt.anchoredPosition += screenDelta / CanvasScale();
            ClampToScreen(rt);
        }

        private static float CanvasScale()
        {
            var canvas = _root != null ? _root.GetComponentInParent<Canvas>() : null;
            float scale = canvas != null ? canvas.rootCanvas.scaleFactor : 1f;
            return scale <= 0f ? 1f : scale;
        }

        /// <summary>Keeps at least a corner of the panel on screen, so it can always be dragged back.</summary>
        private static void ClampToScreen(RectTransform rt)
        {
            var parent = rt.parent as RectTransform;
            if (parent == null) return;
            float halfW = parent.rect.width / 2f, halfH = parent.rect.height / 2f;
            var p = rt.anchoredPosition;
            p.x = Mathf.Clamp(p.x, -halfW, halfW);
            p.y = Mathf.Clamp(p.y, -halfH, halfH);
            rt.anchoredPosition = p;
        }

        private static void LoadPlacement()
        {
            _w = W;
            _h = H;
            if (TryPair(DslConfig.PanelSize != null ? DslConfig.PanelSize.Value : "", out float w, out float h))
            {
                _w = Mathf.Clamp(w, MinW, MaxW);
                _h = Mathf.Clamp(h, MinH, MaxH);
            }
            if (_root != null)
            {
                var rt = _root.GetComponent<RectTransform>();
                rt.anchoredPosition = TryPair(DslConfig.PanelPosition != null ? DslConfig.PanelPosition.Value : "", out float x, out float y)
                    ? new Vector2(x, y) : Vector2.zero;
                ClampToScreen(rt);
            }
        }

        private static void SavePlacement()
        {
            if (DslConfig.PanelSize != null) DslConfig.PanelSize.Value = $"{Mathf.RoundToInt(_w)},{Mathf.RoundToInt(_h)}";
            if (DslConfig.PanelPosition != null && _root != null)
            {
                var p = _root.GetComponent<RectTransform>().anchoredPosition;
                DslConfig.PanelPosition.Value = $"{Mathf.RoundToInt(p.x)},{Mathf.RoundToInt(p.y)}";
            }
        }

        private static bool TryPair(string text, out float a, out float b)
        {
            a = 0f;
            b = 0f;
            if (string.IsNullOrEmpty(text)) return false;
            var parts = text.Split(',');
            return parts.Length == 2
                && float.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out a)
                && float.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out b);
        }
    }
}
