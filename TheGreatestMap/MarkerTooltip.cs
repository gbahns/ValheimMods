using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TheGreatestMap
{
    /// <summary>
    /// What the marker under the pointer actually is. This earns its place because the map is
    /// deliberately short on text: dungeons, camps, structures and plants carry no label by
    /// default, which is what keeps a map with hundreds of markers readable. The information is
    /// still worth having, just not painted over the world, so hovering is where it lives.
    /// </summary>
    internal static class MarkerTooltip
    {
        private const float Width = 230f;
        private const float LineHeight = 17f;
        private const float TitleHeight = 20f;
        private const float Pad = 6f;
        private const float Interval = 0.05f;

        private static GameObject _root;
        private static RectTransform _rect;
        private static TextMeshProUGUI _title;
        private static readonly List<TextMeshProUGUI> _lines = new List<TextMeshProUGUI>();
        private static Camera _camera;
        private static string _shownId;
        private static float _next;

        internal static void Reset()
        {
            if (_root != null) UnityEngine.Object.Destroy(_root);
            _root = null;
            _rect = null;
            _title = null;
            _lines.Clear();
            _shownId = null;
        }

        internal static void Update()
        {
            var map = Minimap.instance;
            if (map == null || map.m_mode != Minimap.MapMode.Large
                || TgmConfig.MarkerTooltips == null || !TgmConfig.MarkerTooltips.Value
                || MarkerMenu.IsOpen || KindMenu.IsOpen)
            {
                Hide();
                return;
            }
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + Interval;

            var world = Access.ScreenToWorldPoint(map, ZInput.pointerPosition);
            var pin = ClientPins.ClosestPin(map, world, Access.PinInteractRadius(map));
            var shared = ClientPins.SharedFor(pin);
            if (shared == null) { Hide(); return; }
            Show(map, shared);
        }

        private static void Hide()
        {
            if (_root != null && _root.activeSelf) _root.SetActive(false);
            _shownId = null;
        }

        private static void Show(Minimap map, SharedPin shared)
        {
            var parent = map.m_largeRoot != null ? map.m_largeRoot.transform as RectTransform : null;
            if (parent == null) return;
            if (_root == null) Build(parent);
            if (_root == null) return;

            if (shared.Id != _shownId)
            {
                _shownId = shared.Id;
                Fill(shared);
            }
            if (!_root.activeSelf) _root.SetActive(true);
            Place(parent);
        }

        /// <summary>Title plus up to three quiet lines; unused ones are switched off rather than rebuilt.</summary>
        private static void Fill(SharedPin shared)
        {
            string title = !string.IsNullOrEmpty(shared.Name) ? shared.Name : null;
            var kind = ClientPins.KindOf(shared);
            if (title == null) title = kind.HasValue ? Categories.Label(kind.Value) : IconRegistry.DisplayName(shared.Icon);
            _title.text = title;

            var lines = new List<string>();
            string what = kind.HasValue ? Categories.Label(kind.Value) : "Marker";
            // The icon is worth naming when it is the only thing telling the kinds apart, which is
            // exactly the case the map's missing labels create.
            string iconName = IconRegistry.DisplayName(shared.Icon);
            if (!string.Equals(iconName, title, StringComparison.OrdinalIgnoreCase))
                lines.Add(shared.Auto ? $"{what} · {iconName}" : $"Placed · {iconName}");
            else
                lines.Add(shared.Auto ? what : "Placed marker");

            if (shared.Checked)
                lines.Add(kind.HasValue && Categories.IsResource(kind.Value) ? "Cleared" : "Searched");

            // The raw icon key, which is the field that goes wrong: a marker showing a plain dot
            // is usually one whose key names an item this game does not have, and nothing else on
            // screen says so. The prefab it came from is not stored on a marker, so that level of
            // detail lives in the legend rather than here.
            lines.Add(shared.Icon);

            string who = !string.IsNullOrEmpty(shared.Author) ? shared.Author : null;
            string when = When(shared.Created);
            if (who != null && when != null) lines.Add($"{who}, {when}");
            else if (who != null) lines.Add(who);
            else if (when != null) lines.Add(when);

            for (int i = 0; i < _lines.Count; i++)
            {
                bool used = i < lines.Count;
                if (_lines[i].gameObject.activeSelf != used) _lines[i].gameObject.SetActive(used);
                if (used) _lines[i].text = lines[i];
            }
            _rect.sizeDelta = new Vector2(Width, Pad + TitleHeight + Mathf.Min(lines.Count, _lines.Count) * LineHeight + Pad);
        }

        private static string When(long ticks)
        {
            if (ticks <= 0L) return null;
            try
            {
                var then = new DateTime(ticks, DateTimeKind.Utc).ToLocalTime();
                var age = DateTime.Now - then;
                if (age.TotalMinutes < 1) return "just now";
                if (age.TotalHours < 1) return $"{(int)age.TotalMinutes} min ago";
                if (age.TotalDays < 1) return $"{(int)age.TotalHours} h ago";
                if (age.TotalDays < 7) return $"{(int)age.TotalDays} d ago";
                return then.ToString("d MMM");
            }
            catch (Exception) { return null; }
        }

        private static void Build(RectTransform parent)
        {
            var panel = MenuKit.Panel(parent, "TGM_MarkerTooltip");
            _root = panel.gameObject;
            _rect = panel.rectTransform;
            _rect.anchorMin = _rect.anchorMax = new Vector2(0f, 1f);
            _rect.pivot = new Vector2(0f, 1f);
            _rect.sizeDelta = new Vector2(Width, 80f);
            panel.raycastTarget = false; // never eat a click meant for the map

            _title = MenuKit.Text(_root.transform, "Title", "", 15f, MenuKit.Header);
            Stretch(_title.rectTransform, -Pad, TitleHeight);
            _title.alignment = TextAlignmentOptions.MidlineLeft;

            for (int i = 0; i < 4; i++)
            {
                var line = MenuKit.Text(_root.transform, "Line" + i, "", 13f, MenuKit.Dim);
                Stretch(line.rectTransform, -(Pad + TitleHeight + i * LineHeight), LineHeight);
                line.alignment = TextAlignmentOptions.MidlineLeft;
                _lines.Add(line);
            }
        }

        private static void Stretch(RectTransform rt, float y, float height)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(10f, y);
            rt.sizeDelta = new Vector2(-20f, height);
        }

        /// <summary>Beside the pointer, flipping to the other side rather than running off the map.</summary>
        private static void Place(RectTransform parent)
        {
            var canvas = parent.GetComponentInParent<Canvas>();
            _camera = canvas != null && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.rootCanvas.worldCamera : null;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, ZInput.pointerPosition, _camera, out var local);
            var pr = parent.rect;
            float height = _rect.sizeDelta.y;
            float x = local.x - pr.xMin + 18f;
            float top = pr.yMax - local.y + 18f;
            if (x + Width > pr.width) x = Mathf.Max(0f, x - Width - 36f);
            if (top + height > pr.height) top = Mathf.Max(0f, top - height - 36f);
            _rect.anchoredPosition = new Vector2(x, -top);
            _root.transform.SetAsLastSibling();
        }
    }
}
