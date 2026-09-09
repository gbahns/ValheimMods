using Jotunn.Managers;
using UnityEngine;
using UnityEngine.UI;

namespace CaptainsLog
{
    // Always-on-while-sailing HUD widget showing live speed/wind telemetry. Visibility is
    // driven by how fresh the last logged sample is (see ShipTelemetry.Sample), rather than
    // re-deriving "am I on a ship" here, so the HUD and the CSV log never disagree.
    internal static class ShipHud
    {
        private const float UpdateInterval = 0.2f;
        private const float StaleAfter = 0.75f;
        private const float Width = 280f;
        private const float Height = 70f;

        private static GameObject _go;
        private static Text _text;
        private static RectTransform _rect;
        private static float _lastUpdate;

        private static bool Enabled => CaptainsLogMod.Instance?.ShowHud?.Value ?? true;
        private static float OffsetX => CaptainsLogMod.Instance?.HudOffsetX?.Value ?? 10f;
        private static float OffsetY => CaptainsLogMod.Instance?.HudOffsetY?.Value ?? 40f;

        public static void Tick()
        {
            EnsureCreated();
            if (_text == null) return;

            var onShip = Time.time - ShipTelemetry.LastSample.SampledAtTime < StaleAfter;
            var shouldShow = Enabled && onShip;
            if (_go.activeSelf != shouldShow) _go.SetActive(shouldShow);
            if (!shouldShow) return;

            _rect.anchoredPosition = new Vector2(OffsetX, -OffsetY);

            var now = Time.time;
            if (now - _lastUpdate < UpdateInterval) return;
            _lastUpdate = now;

            var s = ShipTelemetry.LastSample;
            _text.text =
                $"Speed: {s.SpeedMs:F1} m/s ({s.SpeedKnots:F1} kn)  {s.Propulsion}\n" +
                $"Wind: {s.WindIntensity * 100f:F1}%  @ {s.WindAngleDeg:F0}°\n" +
                $"Sail: {s.WindAngleFactor * 100f:F0}%  Rudder: {s.Rudder:F1}";
        }

        private static void EnsureCreated()
        {
            if (_go != null) return;
            if (GUIManager.Instance == null || GUIManager.CustomGUIFront == null) return;

            _go = GUIManager.Instance.CreateText(
                text: string.Empty,
                parent: GUIManager.CustomGUIFront.transform,
                anchorMin: new Vector2(0f, 1f),
                anchorMax: new Vector2(0f, 1f),
                position: new Vector2(OffsetX, -OffsetY),
                font: GUIManager.Instance.AveriaSerifBold,
                fontSize: 16,
                color: GUIManager.Instance.ValheimOrange,
                outline: true,
                outlineColor: Color.black,
                width: Width,
                height: Height,
                addContentSizeFitter: false);
            _go.name = "CaptainsLog_ShipHud";
            _rect = _go.GetComponent<RectTransform>();
            _rect.pivot = new Vector2(0f, 1f);
            _rect.anchoredPosition = new Vector2(OffsetX, -OffsetY);
            _text = _go.GetComponent<Text>();
            _text.alignment = TextAnchor.UpperLeft;
            _text.raycastTarget = false;
        }
    }
}
