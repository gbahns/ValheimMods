using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DudeWhatAreMyStats
{
    internal enum HudCorner { TopLeft, TopRight, BottomLeft, BottomRight }

    /// <summary>
    /// An always-visible list of players and how many times each has died, for anyone who wants
    /// the scoreboard's most argued-over number on screen without opening anything.
    ///
    /// Off by default: an overlay that turns up unasked after an update is one people switch off
    /// in annoyance. When on, it is there for the whole of play, over the map and the inventory
    /// included. It steps aside only where it would be wrong to draw: when the game hides its own
    /// HUD (Ctrl+F3) or plays a cutscene, which are the two conditions vanilla's HUD uses, and
    /// behind the pause menu, the death and teleport fade, and this mod's own stats panel, all of
    /// which it would otherwise sit on top of, since its canvas sorts above them.
    ///
    /// It never takes the pointer. Its canvas has no raycaster and nothing in it is a raycast
    /// target, so it cannot swallow a click meant for the map or an inventory slot underneath.
    /// The scene change on logout destroys it; it is rebuilt the next time it is wanted.
    /// </summary>
    internal static class PlayerListHud
    {
        // Sizes at the default font size of 16; everything scales with the configured size, so a
        // larger font gets a wider box rather than names running under the counts.
        private const float BaseFont = 16f;
        private const float BaseWidth = 230f;
        private const float BaseCountColumn = 56f;
        private const float BasePad = 8f;
        private const float LineEm = 1.25f;
        private const int MaxNameChars = 16;

        // Every line in both columns is spaced by this fixed amount rather than by whatever font a
        // character happens to come from. Without it, a name drawn partly from a fallback font (a
        // Cyrillic or CJK name) gets a taller line than its count beside it, and every row below
        // drifts out of step. It also makes the box height exact.
        private static readonly string LineHeight = "<line-height=" + LineEm.ToString(System.Globalization.CultureInfo.InvariantCulture) + "em>";

        // What the counts column writes on a line that has no count (the header, "+N more"): an
        // invisible zero. A real glyph from the same font, unlike an empty line or a no-break
        // space, so the first line's height matches the names column exactly and no font can lack it.
        private const string Placeholder = "<alpha=#00>0</alpha>";

        private static GameObject _root;
        private static RectTransform _box;
        private static TextMeshProUGUI _names;
        private static TextMeshProUGUI _counts;
        private static float _nextRedraw;
        private static float _nextRequest;
        private static string _layoutKey;
        private static float _scale = 1f;

        internal static void Update()
        {
            // The key first: it is a cheap no-op when unbound, which it is by default.
            if (DwamsConfig.PlayerListKey != null && Keys.IsDown(DwamsConfig.PlayerListKey.Value) && Keys.CanTakeInput())
                Toggle();

            if (!Wanted())
            {
                if (_root != null) _root.SetActive(false);
                return;
            }

            if (_root == null && !Create()) return;
            if (!_root.activeSelf)
            {
                _root.SetActive(true);
                _nextRedraw = 0f;
            }

            KeepFresh();

            // Once a second: deaths change rarely, and a label rebuilt every frame would cost more
            // than the number is worth.
            if (Time.unscaledTime < _nextRedraw) return;
            _nextRedraw = Time.unscaledTime + 1f;
            Redraw();
        }

        internal static void Toggle()
        {
            if (DwamsConfig.ShowPlayerList == null) return;
            DwamsConfig.ShowPlayerList.Value = !DwamsConfig.ShowPlayerList.Value;
            DudeWhatAreMyStatsMod.Message(DwamsConfig.ShowPlayerList.Value ? "Player list on" : "Player list off");
            _nextRedraw = 0f;
            _nextRequest = 0f;
        }

        internal static void Hide()
        {
            if (_root != null) _root.SetActive(false);
        }

        private static bool Wanted()
        {
            if (DwamsConfig.ShowPlayerList == null || !DwamsConfig.ShowPlayerList.Value) return false;
            var player = Player.m_localPlayer;
            var hud = Hud.instance;
            if (player == null || hud == null) return false;
            // The two conditions vanilla's own HUD uses.
            if (hud.m_userHidden || player.InCutscene()) return false;
            // Places this canvas would otherwise be drawn on top of.
            if (Menu.IsVisible() || StatsPanel.IsOpen || LoadingScreenShowing(hud)) return false;
            return true;
        }

        /// <summary>The black fade the game puts up on death and on teleporting.</summary>
        private static bool LoadingScreenShowing(Hud hud)
        {
            var fade = hud.m_loadingScreen;
            return fade != null && fade.gameObject.activeInHierarchy && fade.alpha > 0.01f;
        }

        /// <summary>
        /// Asks the other players again now and then, since the list is on screen whether or not the
        /// panel is. While the panel is open it asks for itself, and more often, so this clock is
        /// moved on rather than left to fire the moment the panel closes, which would send a second
        /// request a few seconds after the panel's last one.
        /// </summary>
        private static void KeepFresh()
        {
            float every = Mathf.Max(5f, DwamsConfig.PlayerListRefreshSeconds != null ? DwamsConfig.PlayerListRefreshSeconds.Value : 30f);
            if (StatsPanel.IsOpen)
            {
                _nextRequest = Time.unscaledTime + every;
                return;
            }
            if (Time.unscaledTime < _nextRequest) return;
            _nextRequest = Time.unscaledTime + every;
            // The stored roster only matters if offline players are on the list; otherwise leave
            // the server's largest message out of a request that repeats all session.
            bool includeOffline = DwamsConfig.PlayerListIncludeOffline != null && DwamsConfig.PlayerListIncludeOffline.Value;
            StatsNetwork.Request(includeServer: includeOffline);
        }

        // ── contents ────────────────────────────────────────────────────────────────

        private static void Redraw()
        {
            ApplyLayout();

            bool includeOffline = DwamsConfig.PlayerListIncludeOffline != null && DwamsConfig.PlayerListIncludeOffline.Value;
            int max = DwamsConfig.PlayerListMaxRows != null ? DwamsConfig.PlayerListMaxRows.Value : 10;

            var rows = new List<Snapshot>();
            foreach (var s in StatsNetwork.Roster())
                if (s.IsLocal || s.Online || includeOffline) rows.Add(s);

            // Most deaths first, which is the order people want to argue about.
            rows.Sort((a, b) =>
            {
                int c = b.Deaths.CompareTo(a.Deaths);
                return c != 0 ? c : string.Compare(a.Name ?? "", b.Name ?? "", StringComparison.OrdinalIgnoreCase);
            });

            // Two labels, one line for one line: names on the left, counts on the right. Every row
            // writes a line to both, so the columns cannot drift apart.
            var names = new StringBuilder(LineHeight).Append(Colored("Deaths", UiKit.Header));
            var counts = new StringBuilder(LineHeight).Append(Placeholder);

            int shown = 0;
            foreach (var s in rows)
            {
                if (shown >= max) break;
                shown++;
                Color color = s.IsLocal ? UiKit.Gold : s.Online ? UiKit.Body : UiKit.Dim;
                names.Append('\n').Append(Colored(Clean(s.Name), color));
                counts.Append('\n').Append(Colored(StatGroups.Count(s.Deaths), color));
            }

            int hidden = rows.Count - shown;
            if (hidden > 0)
            {
                names.Append('\n').Append(Colored("+" + hidden + " more", UiKit.Dim));
                counts.Append('\n').Append(Placeholder);
            }

            _names.text = names.ToString();
            _counts.text = counts.ToString();

            // Exact, because every line is the fixed height set above.
            int lines = 1 + shown + (hidden > 0 ? 1 : 0);
            float size = FontSize();
            float pad = BasePad * _scale;
            _box.sizeDelta = new Vector2(BaseWidth * _scale, lines * size * LineEm + pad * 2f);
        }

        /// <summary>
        /// A name as it can safely be shown on one line of a rich-text label.
        ///
        /// A player name is text the game lets anyone type, and a modded client or an edited save can
        /// put in more than vanilla allows. So: no "&lt;", which would open a tag; no backslash, which
        /// the label turns into a line break when followed by n; no control or line-separator
        /// characters, any of which would add a line to the names column only and throw the two
        /// columns out of step. Then cut to fit before the count column. Plain ASCII throughout, so
        /// no character here can be missing from the game's font.
        /// </summary>
        private static string Clean(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Viking";
            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                if (c == '<' || c == '\\') continue;
                if (char.IsControl(c)) continue;
                if (c == (char)0x2028 || c == (char)0x2029) continue;   // line and paragraph separators
                sb.Append(c);
            }
            string s = sb.ToString().Trim();
            if (s.Length == 0) return "Viking";
            return s.Length <= MaxNameChars ? s : s.Substring(0, MaxNameChars - 3) + "...";
        }

        private static string Colored(string text, Color color)
        {
            return "<color=#" + ColorUtility.ToHtmlStringRGB(color) + ">" + text + "</color>";
        }

        private static float FontSize()
        {
            return DwamsConfig.PlayerListFontSize != null ? DwamsConfig.PlayerListFontSize.Value : BaseFont;
        }

        // ── building and placing it ─────────────────────────────────────────────────

        private static bool Create()
        {
            if (MessageHud.instance == null && TextInput.instance == null) return false;   // no font to borrow yet
            UiKit.EnsureFont();

            _root = new GameObject("DWAMS_PlayerList");
            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 899;   // just under DiagnoseServerLag's readout, should both be on
            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            // No GraphicRaycaster: nothing here should ever be hit by the pointer.

            var bg = UiKit.Panel(_root.transform, "Box", new Color(0f, 0f, 0f, 0.38f));
            bg.raycastTarget = false;
            _box = bg.rectTransform;

            _names = MakeLabel("Names", TextAlignmentOptions.TopLeft);
            _counts = MakeLabel("Counts", TextAlignmentOptions.TopRight);

            // A rebuild after a relog starts fresh: timers left over from the last session could
            // otherwise leave an empty box on screen, or hold the first request back.
            _layoutKey = null;
            _nextRedraw = 0f;
            _nextRequest = 0f;
            ApplyLayout();
            return true;
        }

        private static TextMeshProUGUI MakeLabel(string name, TextAlignmentOptions align)
        {
            var label = UiKit.Text(_box, name, "", FontSize(), align, UiKit.Body);
            label.overflowMode = TextOverflowModes.Overflow;   // names are cut to length in code
            label.richText = true;
            // Our own line breaks are real newline characters. This stops the label also reading the
            // two typed characters backslash-n as one, which a name could otherwise smuggle in.
            label.parseCtrlCharacters = false;
            var rt = label.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            return label;
        }

        /// <summary>Places and sizes everything, only when the corner, offset or font size has changed.</summary>
        private static void ApplyLayout()
        {
            if (_box == null) return;
            var corner = DwamsConfig.PlayerListCorner != null ? DwamsConfig.PlayerListCorner.Value : HudCorner.TopLeft;
            string offset = DwamsConfig.PlayerListOffset != null ? DwamsConfig.PlayerListOffset.Value : "16,300";
            float size = FontSize();
            string key = corner + "|" + offset + "|" + size.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (key == _layoutKey) return;
            _layoutKey = key;

            _scale = size / BaseFont;
            float pad = BasePad * _scale;
            float countColumn = BaseCountColumn * _scale;
            if (_names != null)
            {
                _names.fontSize = size;
                _names.rectTransform.offsetMin = new Vector2(pad, pad);
                _names.rectTransform.offsetMax = new Vector2(-(pad + countColumn), -pad);
            }
            if (_counts != null)
            {
                _counts.fontSize = size;
                _counts.rectTransform.offsetMin = new Vector2(pad, pad);
                _counts.rectTransform.offsetMax = new Vector2(-pad, -pad);
            }

            Vector2 anchor;
            switch (corner)
            {
                case HudCorner.TopRight: anchor = new Vector2(1f, 1f); break;
                case HudCorner.BottomLeft: anchor = new Vector2(0f, 0f); break;
                case HudCorner.BottomRight: anchor = new Vector2(1f, 0f); break;
                default: anchor = new Vector2(0f, 1f); break;
            }
            _box.anchorMin = anchor;
            _box.anchorMax = anchor;
            _box.pivot = anchor;

            // The offset is how far in from the chosen corner, both numbers positive, whichever
            // corner that is. The signs follow the corner.
            TryPair(offset, out float x, out float y);
            float sx = anchor.x > 0.5f ? -1f : 1f;
            float sy = anchor.y > 0.5f ? -1f : 1f;
            _box.anchoredPosition = new Vector2(sx * Mathf.Abs(x), sy * Mathf.Abs(y));
        }

        private static void TryPair(string text, out float a, out float b)
        {
            a = 16f;
            b = 300f;
            if (string.IsNullOrEmpty(text)) return;
            var parts = text.Split(',');
            if (parts.Length != 2) return;
            var style = System.Globalization.NumberStyles.Float;
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            if (float.TryParse(parts[0].Trim(), style, culture, out float pa)) a = pa;
            if (float.TryParse(parts[1].Trim(), style, culture, out float pb)) b = pb;
        }
    }
}
