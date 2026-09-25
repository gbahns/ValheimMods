using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DiagnoseServerLag
{
    /// <summary>
    /// The corner readout: six lines, updated once a second.
    ///
    /// The mod's argument is that watching numbers is what fails people - a frame counter says the
    /// machine is fine while the server burns, and its owner concludes the game is fine. This
    /// earns its place by showing the numbers a player can act on rather than the ones that are
    /// easiest to measure, and by colouring whichever is furthest outside its threshold, so a
    /// glance is enough and reading is not required.
    ///
    /// Creatures simulated is the line that justifies the whole thing. Valheim runs a creature's
    /// AI only on the machine that owns it, ownership falls to whoever was in range first and is
    /// never rebalanced, and nothing else in the game will ever tell you that you are carrying a
    /// zone for four other people.
    ///
    /// It lives on its own screen-space overlay canvas above the HUD, and borrows the font and
    /// material of the HUD's center message so it reads as the game's own text. Created lazily; the
    /// scene unload on logout destroys it and it is rebuilt on next use.
    /// </summary>
    internal static class LagHud
    {
        /// <summary>Where the readout sits. Custom is placed by the two percentage settings.</summary>
        internal enum Corner { TopLeft, TopRight, BottomLeft, BottomRight, Custom }

        /// <summary>How far in from the screen edge a cornered readout sits, in reference pixels.</summary>
        private const float Margin = 18f;

        /// <summary>Where the value column starts, measured from the start of the label.</summary>
        private const float ValueColumn = 104f;

        /// <summary>
        /// Width of the readout block, sized for the widest line the mod produces - currently the
        /// cpu line. It has to be a fixed width rather than fitted to the content, because a block
        /// that resized itself every second would jitter sideways as the numbers changed.
        /// </summary>
        private const float BlockWidth = 360f;

        /// <summary>
        /// How many other owners get a row before the rest are summarised. Four keeps the block a
        /// readable height on a busy server; past that the count matters more than the names.
        /// </summary>
        private const int MaxOwnersShown = 4;

        /// <summary>Gap left between the readout and the key hints it is sitting above.</summary>
        private const float HintGap = 12f;

        /// <summary>
        /// Used when the key hints cannot be measured but are presumably there. Deliberately
        /// generous: overlapping the game's own UI is worse than floating a little high.
        /// </summary>
        private const float HintFallback = 110f;

        private static readonly Vector3[] _corners = new Vector3[4];

        private static GameObject _root;
        private static TMP_Text _label;
        private static float _nextRefresh;

        private static readonly Color Good = new Color(0.78f, 0.75f, 0.7f);
        private static readonly Color Warn = new Color(1f, 0.72f, 0.32f);
        private static readonly Color Bad = new Color(1f, 0.42f, 0.35f);

        internal static void Update()
        {
            if (DslConfig.HudKey != null && Keys.CanTakeInput() && Keys.IsDown(DslConfig.HudKey.Value))
            {
                DslConfig.ShowHud.Value = !DslConfig.ShowHud.Value;
                DiagnoseServerLagMod.Message(DslConfig.ShowHud.Value ? "Lag readout on" : "Lag readout off");
            }

            // Handled here beside the readout's own key, but deliberately before the early
            // return below: the owner labels are independent of whether the readout is showing.
            if (DslConfig.OwnerNamesKey != null && Keys.CanTakeInput() && Keys.IsDown(DslConfig.OwnerNamesKey.Value))
                OwnerNames.Toggle();

            bool wanted = DslConfig.ShowHud.Value && Sampler.History.Count > 0;
            if (!wanted)
            {
                if (_root != null) _root.SetActive(false);
                return;
            }

            if (_root == null && !Create()) return;
            _root.SetActive(true);

            // Once a second, in step with the sampler: a readout that flickered every frame would
            // be unreadable and would cost more than the thing it is measuring.
            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + 1f;
            Refresh();
        }

        internal static void Hide()
        {
            if (_root != null) _root.SetActive(false);
        }

        /// <summary>
        /// The six numbers worth a permanent place on screen.
        ///
        /// Chosen for what a player can act on rather than for what is measurable. Creatures
        /// simulated is the one that earns its slot outright: Valheim runs a creature's AI only on
        /// the machine that owns it, ownership goes to whoever was in range first and is never
        /// rebalanced, and nothing else in the game tells you that you are the one carrying a zone
        /// for everybody else. Knowing it is the difference between enduring a bad fight and
        /// spreading out.
        ///
        /// Everything else here answers "is it me": frames and stalls for this machine, round trip
        /// for the line, server tick for the other end. Quality, queue bytes, heap and collections
        /// are deliberately absent - they matter when a rule fires, and the rule is one key away.
        /// </summary>
        private static void Refresh()
        {
            if (!Sampler.TryNewest(out var s)) return;
            var report = LagNetwork.Latest;
            var window = Sampler.History.Recent(DslConfig.WindowSeconds.Value);
            int stalls = 0;
            foreach (var w in window) stalls += w.Stalls;

            var lines = new List<string>();

            lines.Add(Line("frames", $"{s.FrameMsAvg:0} ms  {1000f / Mathf.Max(0.01f, s.FrameMsAvg):0}/s",
                Rank(s.FrameMsAvg, DslConfig.ClientFrameWarnMs.Value, DslConfig.ClientFrameWarnMs.Value * 2f)));

            lines.Add(Line("stalls", $"{stalls} in {window.Count}s", stalls > 0 ? (stalls > 2 ? 2 : 1) : 0));

            if (Machine.Readable && s.HasCpu)
            {
                // Shown against one core because that is the number people recognise, but judged
                // against the whole machine, because a game client is heavily multi-threaded and a
                // server's simulation is not. Valheim's client measured 331% of one core while
                // running perfectly well; colouring that red - which the first version did - is the
                // same error as reading a server's frame cap as a struggle.
                float machine = Machine.MachineShare(s.CpuMsPerSec);
                lines.Add(Line("cpu", $"{Machine.CoreShare(s.CpuMsPerSec) * 100f:0}% of a core" +
                                      $"  ({machine * 100f:0}% of {Machine.ProcessorCount})",
                    Rank(machine, 0.5f, 0.8f)));
            }

            // A round trip and a socket ping are different numbers; the label says which this is.
            // A socket reporting exactly zero is reporting nothing - the same trap as quality and
            // working set - so the mod's own round trip is preferred whenever it has one.
            if (s.HasPing && s.Ping > 0)
                lines.Add(Line(s.PingFromRoundTrip ? "server trip" : "ping", $"{s.Ping} ms",
                    Rank(s.Ping, 120f, 250f)));
            else if (LagNetwork.RoundTripMs > 0f)
                // Named for its far end, because "round trip" alone does not say to what - and the
                // per-player rows below are round trips too, to somewhere else entirely.
                lines.Add(Line("server trip", $"{LagNetwork.RoundTripMs:0} ms",
                    Rank(LagNetwork.RoundTripMs, 120f, 250f)));
            else
                lines.Add(Line("link", s.HasPing ? "under 1 ms" : "not measurable", 0));

            if (s.NearbyObjects > 0 || s.NearbyAI > 0)
            {
                // The ownership table: a row per owner, you among them under your own name rather
                // than as a separate "simulating" line above the others. Reading your share off the
                // same row shape as everybody else's is what makes the comparison immediate - the
                // question is never "how much do I have" but "how does my share compare to theirs",
                // and two different line formats made that a calculation instead of a glance.
                //
                // Coloured on share, and only when somebody else could be taking some. Alone in a
                // zone you own everything near you by definition; that is the design working, not a
                // warning, and colouring it would cry wolf on every solo evening.
                bool carrying = Sampler.PlayersOnline > 1
                                && s.NearbyAI >= 5
                                && s.OwnedAI >= s.NearbyAI * 0.8f;
                lines.Add(Line(LocalName(), Holding(s.OwnedObjects, s.OwnedAI, null), carrying ? 1 : 0));

                var owners = LagNetwork.OwnerLatencies();
                int shown = 0;
                foreach (var o in owners)
                {
                    if (shown >= MaxOwnersShown) break;
                    shown++;
                    string who = string.IsNullOrEmpty(o.Name) ? "another player" : o.Name;
                    // No reply means they are not running this mod, so the cost is unmeasurable
                    // rather than zero. Saying so is better than an empty column that reads as fast.
                    string cost = o.Answered ? $"{o.Ms:0} ms" : "no mod";
                    lines.Add(Line(who, Holding(o.Objects, o.Creatures, cost),
                        o.Answered ? Rank(o.Ms, 200f, 400f) : 0));
                }
                if (owners.Count > shown)
                    lines.Add(Line("", $"+{owners.Count - shown} more", 0));

                // Not an owner, but it belongs in the table or the arithmetic does not close: these
                // are loaded and nobody is simulating them at all.
                if (s.UnownedObjects > 0 || s.UnownedAI > 0)
                    lines.Add(Line("unowned", Holding(s.UnownedObjects, s.UnownedAI, null), 0));

                lines.Add(Line("total", Holding(s.NearbyObjects, s.NearbyAI, null), 0));
            }

            // How often the server actually reaches this client. Shown next to what the send
            // cycle predicts, because the number alone means nothing - 200 ms is fine at eight
            // players and a fault at two. A server can hold a perfect tick on 14% of a core and
            // still only reach you five times a second; this is the only line that would show it.
            float feed = Feed.IntervalMs;
            if (feed > 0f)
            {
                float expect = Feed.PredictedMs(report);
                string note = expect > 0f ? $"  (expect {expect:0})" : "";
                // Judged against the prediction, not against a constant: being served slower than
                // the cycle explains is the finding, and the cycle legitimately grows with the
                // number of people playing.
                int rank = expect <= 0f ? 0 : Rank(feed, expect * 1.5f, expect * 2.5f);
                lines.Add(Line("server feed", $"{feed:0} ms{note}", rank));
            }

            if (report == null)
                lines.Add(Line("server tick", LagNetwork.Module == ServerModule.Absent ? "no mod" : "asking...", 0));
            else
            {
                float tick = Mathf.Max(report.TickMsAvg, report.BaselineTickMs);
                // "server tick", not "server": the table above can now carry a row named "server"
                // for the objects the server itself owns, and two different lines under one label
                // would be read as one thing.
                lines.Add(Line("server tick", $"{tick:0.0} ms  {report.Zdos} obj",
                    Verdict.ServerPaced(report) ? 0
                        : Rank(tick, DslConfig.ServerTickWarnMs.Value, DslConfig.ServerTickSevereMs.Value)));
            }

            ApplyPosition();
            _label.text = string.Join("\n", lines.ToArray());
        }

        /// <summary>One holding, in the shared column shape every row of the table uses.</summary>
        private static string Holding(int objects, int creatures, string cost) =>
            $"{objects,5} obj {creatures,3} mob" + (cost == null ? "" : $"   {cost}");

        /// <summary>Your own name, so your row reads like everybody else's rather than "you".</summary>
        private static string LocalName()
        {
            try
            {
                var me = Player.m_localPlayer;
                if (me != null)
                {
                    string name = me.GetPlayerName();
                    if (!string.IsNullOrEmpty(name)) return name;
                }
            }
            catch { }
            return "you";
        }

        /// <summary>0 normal, 1 past the warning threshold, 2 past the severe one.</summary>
        private static int Rank(float value, float warn, float severe) =>
            value >= severe ? 2 : value >= warn ? 1 : 0;

        /// <summary>
        /// One label-and-value row.
        ///
        /// The value is placed with an explicit column stop rather than by padding the label out to
        /// a fixed character count. Padding only lines up in a monospaced run, and forcing this font
        /// to monospace - which the first version did, with mspace - sets every glyph on the same
        /// advance whether it needs it or not, so narrow letters drift apart and "frames" reads as
        /// "f rames". A column stop leaves the letterforms alone and still aligns the values.
        /// </summary>
        private static string Line(string label, string value, int rank)
        {
            Color c = rank >= 2 ? Bad : rank == 1 ? Warn : Good;
            string hex = ColorUtility.ToHtmlStringRGB(c);
            return $"<color=#{hex}>{label}<pos={ValueColumn}px>{value}</color>";
        }


        /// <summary>
        /// Places the readout, re-read every refresh so a config change lands while you watch it.
        ///
        /// The pivot matters as much as the anchor: pinned at a bottom corner the block has to grow
        /// upward, or adding a line would push it off the screen. Anchoring both to the same point
        /// is what makes the readout hug its corner at any resolution or UI scale.
        /// </summary>
        private static void ApplyPosition()
        {
            if (_label == null) return;
            var rt = _label.rectTransform;
            var corner = DslConfig.HudPosition != null ? DslConfig.HudPosition.Value : Corner.BottomRight;

            float bottom = Margin + BottomClearance();

            Vector2 anchor, offset;
            switch (corner)
            {
                case Corner.TopLeft:
                    anchor = new Vector2(0f, 1f); offset = new Vector2(Margin, -Margin); break;
                case Corner.TopRight:
                    anchor = new Vector2(1f, 1f); offset = new Vector2(-Margin, -Margin); break;
                case Corner.BottomLeft:
                    anchor = new Vector2(0f, 0f); offset = new Vector2(Margin, bottom); break;
                case Corner.Custom:
                    float x = (DslConfig.HudX != null ? DslConfig.HudX.Value : 98f) / 100f;
                    float y = (DslConfig.HudY != null ? DslConfig.HudY.Value : 4f) / 100f;
                    anchor = new Vector2(Mathf.Clamp01(x), Mathf.Clamp01(y));
                    offset = Vector2.zero;
                    break;
                default:
                    anchor = new Vector2(1f, 0f); offset = new Vector2(-Margin, bottom); break;
            }

            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.anchoredPosition = offset;
            // Always left-aligned, whichever corner it is pinned to. Right-aligning a right-hand
            // readout lines up the ends of the values instead of the starts of the labels, which
            // leaves the label column ragged - the values are all different widths. The block hugs
            // its corner because the rect does; the text inside it stays in two straight columns.
            _label.alignment = anchor.y > 0.5f
                ? TextAlignmentOptions.TopLeft
                : TextAlignmentOptions.BottomLeft;
        }

        /// <summary>
        /// How much room the game's key hints need along the bottom of the screen.
        ///
        /// Measured rather than assumed, because the hints are contextual - building, fighting and
        /// fishing each show a different block, at a different height - so any constant would be
        /// wrong most of the time. The measurement goes through screen space rather than comparing
        /// rect sizes directly: the hints live on the game's canvas and the readout on its own, and
        /// the two need not share a scale factor.
        ///
        /// A player who has turned the hints off in Settings -> Gameplay gets the space back,
        /// because this asks whether they are actually on screen rather than whether they exist.
        /// </summary>
        private static float BottomClearance()
        {
            try
            {
                var hints = KeyHints.instance;
                if (hints == null || !hints.gameObject.activeInHierarchy) return 0f;
                var rt = hints.GetComponent<RectTransform>();
                if (rt == null) return HintFallback;

                var canvas = _root != null ? _root.GetComponent<Canvas>() : null;
                float scale = canvas != null && canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
                float limit = Screen.height / scale * 0.4f;

                // The hint blocks rather than their container. KeyHints holds one child per context
                // - building, combat, inventory - and shows whichever applies, so the container is
                // free to be a stretched full-screen rect whose top edge says nothing about where
                // the visible hints end. The active children are the thing actually on screen.
                float clearance = 0f;
                for (int i = 0; i < rt.childCount; i++)
                {
                    var child = rt.GetChild(i) as RectTransform;
                    if (child == null || !child.gameObject.activeInHierarchy) continue;
                    float top = TopOf(child, scale);
                    if (top > clearance && top <= limit) clearance = top;
                }

                // No usable child: fall back to the container, which is right when KeyHints is a
                // plain bottom-anchored panel and caught by the same sanity limit when it is not.
                if (clearance <= 0f)
                {
                    float top = TopOf(rt, scale);
                    if (top > 0f && top <= limit) clearance = top;
                }

                return clearance > 0f ? clearance + HintGap : HintFallback;
            }
            catch
            {
                return HintFallback;
            }
        }

        /// <summary>Height of a rect's top edge above the bottom of the screen, in canvas units.</summary>
        private static float TopOf(RectTransform rt, float scale)
        {
            rt.GetWorldCorners(_corners);              // bottom-left, top-left, top-right, bottom-right
            return RectTransformUtility.WorldToScreenPoint(null, _corners[1]).y / scale;
        }

        private static bool Create()
        {
            var hud = MessageHud.instance;
            if (hud == null || hud.m_messageCenterText == null) return false;
            var source = hud.m_messageCenterText;

            _root = new GameObject("DiagnoseServerLag_Hud");
            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 900;
            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var go = new GameObject("DiagnoseServerLag_HudLabel", typeof(RectTransform), typeof(CanvasRenderer));
            // Inactive while the component is added, for the same reason UiKit.Text does it: TMP's
            // Awake looks up Unity's default font, which the game does not ship, unless a font is
            // already there. Assigning one afterwards leaves the warning behind.
            go.SetActive(false);
            go.transform.SetParent(_root.transform, false);
            var text = go.AddComponent<TextMeshProUGUI>();
            text.font = source.font;
            text.fontSharedMaterial = source.fontSharedMaterial;
            text.fontSize = 17f;
            text.alignment = TextAlignmentOptions.TopLeft;
            text.richText = true;
            text.raycastTarget = false;
            // Never wrap. A readout line that folded onto a second line would be worse than one
            // that runs a little past its box, and the box is sized so that does not happen.
            text.textWrappingMode = TextWrappingModes.NoWrap;

            text.rectTransform.sizeDelta = new Vector2(BlockWidth, 150f);

            go.SetActive(true);
            _label = text;
            ApplyPosition();
            return true;
        }
    }
}
