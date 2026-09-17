using System.Text;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.UI;

namespace CaptainsLog
{
    public enum HudPosition
    {
        BelowHotbar,
        Centered,
        TopLeft,
    }

    public enum SpeedUnits
    {
        Knots,
        MetersPerSecond,
        KnotsWithMetersPerSecond,
    }

    // Always-on-while-sailing HUD widget showing live speed/wind telemetry. Visibility is
    // driven by how fresh the last logged sample is (see ShipTelemetry.SampleLocal), rather
    // than re-deriving "am I on a ship" here, so the HUD and the CSV log never disagree.
    //
    // It lives under Hud's hudroot, the same canvas as the hotbars, so it can be placed
    // against them in one coordinate space and hides with the rest of the HUD (Ctrl+F3).
    internal static class ShipHud
    {
        private const float UpdateInterval = 0.2f;
        private const float StaleAfter = 0.75f;
        private const float BarRescanInterval = 2f;
        private const float GapBelowHotbar = 6f;
        private static readonly Vector2 TopLeftBase = new Vector2(10f, 40f);
        // Right of the ship's rudder icon, about where Azumatt's Ship Stats sits.
        private static readonly Vector2 CenteredBase = new Vector2(75f, -27f);

        private static GameObject _go;
        private static RectTransform _rect;
        private static RectTransform _parent;
        private static Image _background;
        private static Text _text;
        private static float _lastUpdate;
        private static float _lastBarScan = -999f;
        private static HotkeyBar[] _bars = new HotkeyBar[0];
        private static readonly Vector3[] Corners = new Vector3[4];

        private static CaptainsLogMod Mod => CaptainsLogMod.Instance;

        public static void Tick()
        {
            EnsureCreated();
            if (_go == null) return;

            var onShip = Time.time - ShipTelemetry.LastSample.SampledAtTime < StaleAfter;
            var shouldShow = Mod.ShowHud.Value && onShip;
            if (_go.activeSelf != shouldShow) _go.SetActive(shouldShow);
            if (!shouldShow) return;

            // Every frame, since Centered tracks a moving ship on screen.
            Place();

            var now = Time.time;
            if (now - _lastUpdate < UpdateInterval) return;
            _lastUpdate = now;

            _background.color = new Color(0f, 0f, 0f, Mod.HudBackgroundOpacity.Value);
            _text.text = Compose(ShipTelemetry.LastSample);
        }

        private static string Compose(ShipSample s)
        {
            var sb = new StringBuilder();
            if (Mod.ShowShipName.Value)
            {
                var name = ShipName(ShipTelemetry.LastShip);
                if (name.Length > 0) sb.Append(name).Append('\n');
            }
            sb.Append("Speed: ").Append(FormatSpeed(s.SpeedMs));

            // Wind as a strength, not a speed: Valheim wind has no speed, and a knots figure
            // invites comparing it with the ship's, which the game's physics never does.
            sb.Append($"\nWind: {s.WindIntensity * 100f:F0}%");
            if (Mod.ShowWindAngle.Value) sb.Append(" from ").Append(WindBearing(s.WindAngleDeg));

            switch (s.SpeedSetting)
            {
                case Ship.Speed.Half:
                case Ship.Speed.Full:
                    var sail = s.SpeedSetting == Ship.Speed.Full ? "Full" : "Half";
                    sb.Append($"\nSail: {sail}, {s.WindAngleFactor * 100f:F0}% efficient");
                    break;
                case Ship.Speed.Slow:
                    sb.Append("\nOars: Forward");
                    break;
                case Ship.Speed.Back:
                    sb.Append("\nOars: Reverse");
                    break;
            }

            // Same threshold vanilla uses to hide its rudder indicator.
            if (Mathf.Abs(s.Rudder) >= 0.02f)
                sb.Append($"\nRudder: {Mathf.Abs(s.Rudder) * 100f:F0}% {(s.Rudder > 0f ? "right" : "left")}");

            return sb.ToString();
        }

        // Ships have no player-given names; this is the ship type's display name (the Piece's
        // $ship_* token, localized), which also covers modded ships like TheGreatestShips'.
        private static Ship _namedShip;
        private static string _shipName = string.Empty;

        private static string ShipName(Ship ship)
        {
            if (ship == null) return string.Empty;
            if (ship == _namedShip) return _shipName;
            _namedShip = ship;
            var token = ship.GetComponent<Piece>()?.m_name;
            _shipName = string.IsNullOrEmpty(token)
                ? ship.gameObject.name.Replace("(Clone)", string.Empty).Trim()
                : Localization.instance.Localize(token);
            return _shipName;
        }

        // Where the wind comes from relative to the hull. WindAngleDeg is 0 with the wind
        // dead astern, +/-180 dead ahead, positive from starboard and negative from port
        // (Ship.GetWindAngle is the negated yaw of the wind's heading in the ship's frame).
        private static string WindBearing(float angle)
        {
            var off = Mathf.Abs(angle);
            if (off < 5f) return "astern";
            if (off > 175f) return "ahead";
            var side = angle > 0f ? "starboard" : "port";
            var part = off < 67.5f ? "quarter" : off <= 112.5f ? "beam" : "bow";
            return $"{side} {part}";
        }

        private static string FormatSpeed(float ms)
        {
            var knots = ms * ShipTelemetry.MetersPerSecondToKnots;
            switch (Mod.SpeedUnits.Value)
            {
                case SpeedUnits.MetersPerSecond: return $"{ms:F1} m/s";
                case SpeedUnits.KnotsWithMetersPerSecond: return $"{knots:F1} kn ({ms:F1} m/s)";
                default: return $"{knots:F1} kn";
            }
        }

        private static void Place()
        {
            var nudge = new Vector2(Mod.HudNudgeX.Value, -Mod.HudNudgeY.Value);
            var mode = Mod.HudPosition.Value;
            if (mode == HudPosition.Centered)
            {
                var ship = ShipTelemetry.LastShip;
                var camera = Utils.GetMainCamera();
                if (ship != null && camera != null)
                {
                    _rect.pivot = new Vector2(0f, 0.5f);
                    var screen = camera.WorldToScreenPointScaled(ship.m_controlGuiPos.position);
                    SetLocal((Vector2)_parent.InverseTransformPoint(screen) + CenteredBase + nudge);
                    return;
                }
            }

            _rect.pivot = new Vector2(0f, 1f);
            if (mode == HudPosition.BelowHotbar && TryGetHotbarBottomLeft(out var bottomLeft))
            {
                SetLocal(bottomLeft + new Vector2(0f, -GapBelowHotbar) + nudge);
                return;
            }

            var r = _parent.rect;
            SetLocal(new Vector2(r.xMin + TopLeftBase.x, r.yMax - TopLeftBase.y) + nudge);
        }

        private static void SetLocal(Vector2 p) => _rect.localPosition = new Vector3(p.x, p.y, 0f);

        // The lowest edge of every visible hotbar in the upper part of the screen - the
        // vanilla one plus any a mod adds, like AzuExtendedPlayerInventory's quick slot row
        // (a second HotkeyBar named AzuEPI_QuickAccessBar, placed wherever its config says).
        // Measured from the slot elements, since a HotkeyBar's own rect has no size.
        private static bool TryGetHotbarBottomLeft(out Vector2 bottomLeft)
        {
            if (Time.time - _lastBarScan > BarRescanInterval)
            {
                _lastBarScan = Time.time;
                _bars = _parent.GetComponentsInChildren<HotkeyBar>(includeInactive: false);
            }

            var found = false;
            var minX = float.MaxValue;
            var minY = float.MaxValue;
            var midY = _parent.rect.center.y;
            foreach (var bar in _bars)
            {
                if (bar == null || !bar.isActiveAndEnabled) continue;
                foreach (Transform child in bar.transform)
                {
                    if (!child.gameObject.activeSelf || !(child is RectTransform rt)) continue;
                    rt.GetWorldCorners(Corners);
                    var lo = _parent.InverseTransformPoint(Corners[0]); // bottom-left
                    var hi = _parent.InverseTransformPoint(Corners[2]); // top-right
                    if (hi.y < midY) continue; // a bar parked at the bottom of the screen
                    minX = Mathf.Min(minX, lo.x);
                    minY = Mathf.Min(minY, lo.y);
                    found = true;
                }
            }

            bottomLeft = new Vector2(minX, minY);
            return found;
        }

        private static void EnsureCreated()
        {
            if (_go != null) return;
            if (Hud.instance == null || Hud.instance.m_rootObject == null) return;
            if (GUIManager.Instance == null) return;

            _parent = Hud.instance.m_rootObject.GetComponent<RectTransform>();
            if (_parent == null) return;

            _go = new GameObject("CaptainsLog_ShipHud", typeof(RectTransform));
            _go.transform.SetParent(_parent, worldPositionStays: false);
            _rect = _go.GetComponent<RectTransform>();
            _rect.anchorMin = _rect.anchorMax = new Vector2(0.5f, 0.5f);
            _rect.pivot = new Vector2(0f, 1f);

            _background = _go.AddComponent<Image>();
            _background.raycastTarget = false;

            var layout = _go.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(8, 8, 5, 5);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var fitter = _go.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(_go.transform, worldPositionStays: false);
            _text = textGo.AddComponent<Text>();
            _text.font = GUIManager.Instance.AveriaSerifBold;
            _text.fontSize = 16;
            _text.color = GUIManager.Instance.ValheimOrange;
            _text.horizontalOverflow = HorizontalWrapMode.Overflow;
            _text.verticalOverflow = VerticalWrapMode.Overflow;
            _text.alignment = TextAnchor.UpperLeft;
            _text.raycastTarget = false;
            var outline = textGo.AddComponent<Outline>();
            outline.effectColor = Color.black;

            _go.SetActive(false);
        }
    }
}
