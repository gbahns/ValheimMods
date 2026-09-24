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
                lines.Add(Line("cpu", $"{Machine.CoreShare(s.CpuMsPerSec) * 100f:0}% of a core",
                    Rank(Machine.CoreShare(s.CpuMsPerSec), 0.8f, 1.2f)));

            // A round trip and a socket ping are different numbers; the label says which this is.
            if (s.HasPing)
                lines.Add(Line(s.PingFromRoundTrip ? "round trip" : "ping", $"{s.Ping} ms",
                    Rank(s.Ping, 120f, 250f)));
            else
                lines.Add(Line("link", "not measurable", 0));

            if (s.NearbyAI > 0)
            {
                // Coloured on share, not on count - and only when somebody else could be sharing
                // it. Alone in a zone you own everything near you by definition; that is the design
                // working, not a warning, and colouring it would cry wolf in single player and on
                // every solo evening.
                bool carrying = Sampler.PlayersOnline > 1
                                && s.NearbyAI >= 5
                                && s.OwnedAI >= s.NearbyAI * 0.8f;
                string others = string.IsNullOrEmpty(Sampler.OtherOwners) ? "" : $"  ({Sampler.OtherOwners})";
                lines.Add(Line("simulating", $"{s.OwnedAI}/{s.NearbyAI}{others}", carrying ? 1 : 0));
            }

            if (report == null)
                lines.Add(Line("server", LagNetwork.Module == ServerModule.Absent ? "no mod" : "asking...", 0));
            else
            {
                float tick = Mathf.Max(report.TickMsAvg, report.BaselineTickMs);
                lines.Add(Line("server", $"{tick:0.0} ms  {report.Zdos} obj",
                    Verdict.ServerPaced(report) ? 0
                        : Rank(tick, DslConfig.ServerTickWarnMs.Value, DslConfig.ServerTickSevereMs.Value)));
            }

            _label.text = string.Join("\n", lines.ToArray());
        }

        /// <summary>0 normal, 1 past the warning threshold, 2 past the severe one.</summary>
        private static int Rank(float value, float warn, float severe) =>
            value >= severe ? 2 : value >= warn ? 1 : 0;

        private static string Line(string label, string value, int rank)
        {
            Color c = rank >= 2 ? Bad : rank == 1 ? Warn : Good;
            string hex = ColorUtility.ToHtmlStringRGB(c);
            return $"<color=#{hex}><mspace=0.55em>{label,-11}</mspace>{value}</color>";
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

            // Top-right, below the clock and the biome name rather than over them.
            var rt = text.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.sizeDelta = new Vector2(420f, 150f);
            rt.anchoredPosition = new Vector2(-18f, -150f);

            go.SetActive(true);
            _label = text;
            return true;
        }
    }
}
