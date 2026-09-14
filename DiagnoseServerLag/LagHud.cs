using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DiagnoseServerLag
{
    /// <summary>
    /// The optional always-on corner readout: four lines, updated once a second.
    ///
    /// Off by default, and deliberately small. The mod's whole argument is that watching numbers
    /// is what fails people - a frame counter says the machine is fine while the server burns, and
    /// its owner concludes the game is fine - so this is here for the case the panel cannot serve,
    /// which is wanting to notice the moment something turns while you are playing rather than
    /// reading a diagnosis afterwards. The line that is furthest outside its threshold is colored,
    /// so a glance is enough and reading is not required.
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

        private static void Refresh()
        {
            if (!Sampler.TryNewest(out var s)) return;
            var report = LagNetwork.Latest;

            string frames = Line("frames", $"{s.FrameMsAvg:0} ms  {1000f / Mathf.Max(0.01f, s.FrameMsAvg):0}/s",
                Rank(s.FrameMsAvg, DslConfig.ClientFrameWarnMs.Value, DslConfig.ClientFrameWarnMs.Value * 2f));

            string link = s.HasPing
                ? Line("link", $"{s.Ping} ms  q {s.LocalQuality * 100f:0}%",
                    Mathf.Max(Rank(s.Ping, 120f, 250f), RankLow(s.LocalQuality, DslConfig.QualityWarn.Value, DslConfig.QualitySevere.Value)))
                : Line("link", "not measurable", 0);

            string queue = Line("queue", Stats.Bytes(s.SendQueue),
                Rank(s.SendQueue, DslConfig.QueueWarnBytes.Value, DslConfig.QueueSevereBytes.Value));

            string server;
            if (report == null)
                server = Line("server", LagNetwork.Module == ServerModule.Absent ? "no mod" : "asking...", 0);
            else
            {
                float tick = Mathf.Max(report.TickMsAvg, report.BaselineTickMs);
                server = Line("server", $"{tick:0.0} ms  {report.Zdos} obj",
                    Rank(tick, DslConfig.ServerTickWarnMs.Value, DslConfig.ServerTickSevereMs.Value));
            }

            _label.text = string.Join("\n", new[] { frames, link, queue, server });
        }

        /// <summary>0 normal, 1 past the warning threshold, 2 past the severe one.</summary>
        private static int Rank(float value, float warn, float severe) =>
            value >= severe ? 2 : value >= warn ? 1 : 0;

        /// <summary>The same, for measurements where smaller is worse.</summary>
        private static int RankLow(float value, float warn, float severe)
        {
            // Zero means the socket could not tell us. Coloring that red would report a broken
            // link every time the plain TCP path is in use, which is not a fault, only a blind spot.
            if (value <= 0f) return 0;
            return value <= severe ? 2 : value <= warn ? 1 : 0;
        }

        private static string Line(string label, string value, int rank)
        {
            Color c = rank >= 2 ? Bad : rank == 1 ? Warn : Good;
            string hex = ColorUtility.ToHtmlStringRGB(c);
            return $"<color=#{hex}><mspace=0.55em>{label,-7}</mspace>{value}</color>";
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
            rt.sizeDelta = new Vector2(260f, 96f);
            rt.anchoredPosition = new Vector2(-18f, -150f);

            go.SetActive(true);
            _label = text;
            return true;
        }
    }
}
