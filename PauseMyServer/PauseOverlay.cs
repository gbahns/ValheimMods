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
    ///  * Unpaused warning, in bright red: the ESC menu is up but the game keeps running because
    ///    other players are online and not all of them are in their menu. Shows how many are,
    ///    when the server reports it. Without it the menu looks exactly like a pause.
    ///
    /// While the large map is open (possible during an admin pause) the label moves into the
    /// strip between the map's edge and the screen edge instead of sitting on top of the map.
    /// </summary>
    internal static class PauseOverlay
    {
        internal enum Position { Top, Bottom }

        private enum Mode { Hidden, Paused, Unpaused }

        private const float DefaultTopOffset    = 60f;
        private const float DefaultBottomOffset = 90f;
        private const float MinEdgeOffset       = 4f;

        private static GameObject _root;
        private static Canvas _canvas;
        private static TMP_Text _label;
        private static Color _baseColor = Color.white;
        private static Mode _mode = Mode.Hidden;
        private static float _lastY = float.NaN;
        private static readonly Vector3[] _corners = new Vector3[4];

        internal static void Update()
        {
            Mode mode = Mode.Hidden;
            if (Player.m_localPlayer != null)
            {
                if (Game.IsPaused())
                {
                    if (PauseMyServerMod.ShowPauseMessage.Value) mode = Mode.Paused;
                }
                else if (PauseMyServerMod.ShowUnpausedWarning.Value && Menu.IsVisible() && OthersOnline())
                {
                    mode = Mode.Unpaused;
                }
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
                _label.text = text;
                _label.color = mode == Mode.Paused ? _baseColor : Color.red;
            }
            _mode = mode;
            UpdatePosition();
        }

        /// <summary>The server's count when it has reported one; else the player list (sent every two seconds), which counts us too.</summary>
        private static bool OthersOnline()
        {
            if (PauseSync.PlayerCount > 0) return PauseSync.PlayerCount > 1;
            var znet = ZNet.instance;
            return znet != null && znet.GetNrOfPlayers() > 1;
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

        private static string UnpausedText()
        {
            if (PauseSync.PlayerCount > 0)
            {
                try { return string.Format(PauseMyServerMod.UnpausedCountText.Value, PauseSync.WantCount, PauseSync.PlayerCount); }
                catch (FormatException) { }
            }
            return PauseMyServerMod.UnpausedText.Value;
        }

        /// <summary>Re-reads the layout config every time the label appears, so edits apply without a restart.</summary>
        private static void ApplyLayout()
        {
            _label.fontSize = PauseMyServerMod.PauseMessageSize.Value;
            bool top = PauseMyServerMod.PauseMessagePosition.Value == Position.Top;
            var rt = _label.rectTransform;
            rt.sizeDelta = new Vector2(1600f, PauseMyServerMod.PauseMessageSize.Value * 1.25f);
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
            text.raycastTarget = false;
            text.rectTransform.sizeDelta = new Vector2(1600f, 50f);
            _label = text;

            _root.SetActive(false);
            return true;
        }
    }
}
