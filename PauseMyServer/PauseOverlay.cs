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
    ///    other players are online. Without it the menu looks exactly like a pause.
    /// </summary>
    internal static class PauseOverlay
    {
        internal enum Position { Top, Bottom }

        private enum Mode { Hidden, Paused, Unpaused }

        private static GameObject _root;
        private static TMP_Text _label;
        private static Color _baseColor = Color.white;
        private static Mode _mode = Mode.Hidden;

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

            string text = mode == Mode.Paused ? PausedText() : PauseMyServerMod.UnpausedText.Value;
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
        }

        /// <summary>The server's player list (sent every two seconds) counts us too, so more than one means company.</summary>
        private static bool OthersOnline()
        {
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

        /// <summary>Re-reads the layout config every time the label appears, so edits apply without a restart.</summary>
        private static void ApplyLayout()
        {
            _label.fontSize = PauseMyServerMod.PauseMessageSize.Value;
            bool top = PauseMyServerMod.PauseMessagePosition.Value == Position.Top;
            var rt = _label.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, top ? 1f : 0f);
            rt.pivot = new Vector2(0.5f, top ? 1f : 0f);
            rt.anchoredPosition = new Vector2(0f, top ? -60f : 90f);
        }

        private static bool Create()
        {
            var hud = MessageHud.instance;
            if (hud == null || hud.m_messageCenterText == null) return false;
            var source = hud.m_messageCenterText;

            _root = new GameObject("PauseMyServer_Overlay");
            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;
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
            text.rectTransform.sizeDelta = new Vector2(1600f, 100f);
            _label = text;

            _root.SetActive(false);
            return true;
        }
    }
}
