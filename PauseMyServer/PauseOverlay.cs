using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PauseMyServer
{
    /// <summary>
    /// The persistent on-screen label. It lives on its own screen-space overlay canvas, so it
    /// sits above the ESC menu and ignores the HUD-hidden setting, and it borrows the font and
    /// material of the HUD's centre message so it reads as the game's own text. Created lazily the
    /// first time it is needed; the scene unload on logout destroys it and it is rebuilt on the
    /// next use.
    ///
    /// Two modes:
    ///  * Paused: whenever the game reports itself paused (a solo host too). During an admin
    ///    pause the text names the admin.
    ///  * Unpaused warning, in bright red: a pause was asked for and the game is still running.
    ///    The trigger is the request, not the ESC menu, so it covers any mod that calls
    ///    Game.Pause() (an open map, an open inventory panel) as well as the menu. Shows how
    ///    many players are in, when the server reports it.
    ///
    /// While the large map is open the label moves into the strip between the map's edge and the
    /// screen edge instead of sitting on top of the map.
    /// </summary>
    internal static class PauseOverlay
    {
        internal enum Position { Top, Bottom }

        private enum Mode { Hidden, Paused, Unpaused }

        private const float DefaultTopOffset    = 60f;
        private const float DefaultBottomOffset = 90f;
        private const float MinEdgeOffset       = 4f;

        // The warning's second line ("2/3 want to pause") is drawn at this fraction of the main size.
        private const float DetailScale = 0.55f;

        // A pause request travels to the server and back before it can be granted. Only call it
        // refused once it has gone unanswered for this long, so the round trip never flashes red.
        private const float RefusedGraceSeconds = 0.5f;

        private static GameObject _root;
        private static Canvas _canvas;
        private static TMP_Text _label;
        private static Color _baseColor = Color.white;
        private static Mode _mode = Mode.Hidden;
        private static float _lastY = float.NaN;
        private static float _wantSince = -1f;
        private static readonly Vector3[] _corners = new Vector3[4];

        internal static void Update()
        {
            Mode mode = Mode.Hidden;
            if (Player.m_localPlayer == null)
            {
                _wantSince = -1f;
            }
            else if (Game.IsPaused())
            {
                _wantSince = -1f;
                if (PauseMyServerMod.ShowPauseMessage.Value) mode = Mode.Paused;
            }
            else if (PauseSync.WantPause && !InCutscene())
            {
                // Somebody asked for a pause and the game is still running.
                if (_wantSince < 0f) _wantSince = Time.unscaledTime;
                if (PauseMyServerMod.ShowUnpausedWarning.Value
                    && Time.unscaledTime - _wantSince >= RefusedGraceSeconds)
                    mode = Mode.Unpaused;
            }
            else
            {
                _wantSince = -1f;
            }

            if (mode == Mode.Hidden)
            {
                if (_mode == Mode.Hidden) return;
                if (_root != null) _root.SetActive(false);
                _mode = Mode.Hidden;
                return;
            }

            if (_root == null)
            {
                _mode = Mode.Hidden;
                if (!Create()) return;
            }

            string text = mode == Mode.Paused ? PausedText() : UnpausedText();
            if (_mode == Mode.Hidden)
            {
                ApplyLayout();
                _root.SetActive(true);
            }
            if (_mode != mode || _label.text != text)
            {
                SetText(text);
                _label.color = mode == Mode.Paused ? _baseColor : Color.red;
            }
            _mode = mode;
            UpdatePosition();
        }

        /// <summary>The intro and the cinematic player pause of their own accord; that is not a refused request.</summary>
        private static bool InCutscene()
        {
            if (CinematicsManager.IsPlaying()) return true;
            var game = Game.instance;
            return game != null && game.InIntro(includeQueued: true);
        }

        private static string PausedText()
        {
            if (PauseSync.ClientForced && !string.IsNullOrEmpty(PauseSync.PausedBy))
            {
                try { return string.Format(PauseMyServerMod.AdminText.Value, PauseSync.PausedBy); }
                catch (FormatException) { return PauseMyServerMod.AdminText.Value; }
            }
            return PauseMyServerMod.PauseMessage.Value;
        }

        /// <summary>"Unpaused", with the count on a second line in a smaller font once the server reports it.</summary>
        private static string UnpausedText()
        {
            string head = PauseMyServerMod.UnpausedText.Value;
            if (PauseSync.PlayerCount <= 0) return head;

            string detail;
            try { detail = string.Format(PauseMyServerMod.UnpausedCountText.Value, PauseSync.WantCount, PauseSync.PlayerCount); }
            catch (FormatException) { return head; }
            if (string.IsNullOrEmpty(detail)) return head;

            return head + "\n<size=" + Mathf.RoundToInt(DetailScale * 100f) + "%>" + detail + "</size>";
        }

        /// <summary>Sets the text and sizes the box to the lines it needs, so the map-gap fit stays honest.</summary>
        private static void SetText(string text)
        {
            _label.text = text;
            float size = PauseMyServerMod.PauseMessageSize.Value;
            float height = size * 1.25f;
            if (text.IndexOf('\n') >= 0) height += size * DetailScale * 1.25f;
            _label.rectTransform.sizeDelta = new Vector2(1600f, height);
            _lastY = float.NaN;   // the box changed height; let UpdatePosition place it again
        }

        /// <summary>Re-reads the layout config every time the label appears, so edits apply without a restart.</summary>
        private static void ApplyLayout()
        {
            _label.fontSize = PauseMyServerMod.PauseMessageSize.Value;
            bool top = PauseMyServerMod.PauseMessagePosition.Value == Position.Top;
            var rt = _label.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, top ? 1f : 0f);
            rt.pivot = new Vector2(0.5f, top ? 1f : 0f);
            _lastY = float.NaN;
        }

        /// <summary>
        /// Distance from the chosen screen edge to the label box. Normally the default offset;
        /// with the large map open, the box is centred in the strip between the map's edge and
        /// the screen edge (never off-screen, never further in than the default).
        /// </summary>
        private static void UpdatePosition()
        {
            bool top = PauseMyServerMod.PauseMessagePosition.Value == Position.Top;
            float defaultOffset = top ? DefaultTopOffset : DefaultBottomOffset;
            float offset = defaultOffset;

            float strip = MapStrip(top);
            if (strip >= 0f)
            {
                float free = Mathf.Max(0f, strip - _label.rectTransform.sizeDelta.y);
                offset = Mathf.Clamp(free / 2f, MinEdgeOffset, defaultOffset);
            }

            float y = top ? -offset : offset;
            if (!float.IsNaN(_lastY) && Mathf.Abs(y - _lastY) < 0.5f) return;
            _label.rectTransform.anchoredPosition = new Vector2(0f, y);
            _lastY = y;
        }

        /// <summary>Height, in our canvas units, of the gap between the screen edge and the open large map's edge; -1 when the map is closed.</summary>
        private static float MapStrip(bool top)
        {
            if (_canvas == null || !Minimap.IsOpen()) return -1f;
            var map = Minimap.instance;
            if (map == null || map.m_mapImageLarge == null) return -1f;

            var mapRt = map.m_mapImageLarge.rectTransform;
            var mapCanvas = mapRt.GetComponentInParent<Canvas>();
            if (mapCanvas != null) mapCanvas = mapCanvas.rootCanvas;
            Camera cam = mapCanvas != null && mapCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? mapCanvas.worldCamera : null;

            mapRt.GetWorldCorners(_corners);   // bottom-left, top-left, top-right, bottom-right
            float scale = _canvas.scaleFactor > 0f ? _canvas.scaleFactor : 1f;
            if (top)
            {
                float mapTop = RectTransformUtility.WorldToScreenPoint(cam, _corners[1]).y;
                return Mathf.Max(0f, Screen.height - mapTop) / scale;
            }
            float mapBottom = RectTransformUtility.WorldToScreenPoint(cam, _corners[0]).y;
            return Mathf.Max(0f, mapBottom) / scale;
        }

        private static bool Create()
        {
            var hud = MessageHud.instance;
            if (hud == null || hud.m_messageCenterText == null) return false;
            var source = hud.m_messageCenterText;

            _root = new GameObject("PauseMyServer_Overlay");
            _canvas = _root.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 1000;
            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var go = new GameObject("PauseMyServer_Label", typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(_root.transform, false);
            var text = go.AddComponent<TextMeshProUGUI>();
            text.font = source.font;
            text.fontSharedMaterial = source.fontSharedMaterial;
            _baseColor = source.color;
            text.color = _baseColor;
            text.alignment = TextAlignmentOptions.Center;
            text.richText = true;   // the warning's second line is sized with a <size> tag
            text.raycastTarget = false;
            text.rectTransform.sizeDelta = new Vector2(1600f, 50f);
            _label = text;

            _root.SetActive(false);
            return true;
        }
    }
}
