using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace TheGreatestMap
{
    /// <summary>
    /// The small menu behind the gear beside the marker button: the settings about how the map is
    /// drawn, as opposed to which markers are on it.
    ///
    /// <para>Those two questions were in one panel and did not belong there. The marker list is a
    /// list of markers -- kinds, types, what you are finished with -- and at the foot of it sat two
    /// buttons about whether any marker's name is written beside it, which is a thing about the
    /// whole map and about no marker in particular. A reader looking for "turn the labels off" had
    /// to go through the list of kinds to find it, and a reader going through the list of kinds had
    /// to step over the labels.</para>
    ///
    /// <para>Deliberately small. It is here to hold the handful of switches about drawing that have
    /// nowhere better to live, and it is worth keeping it readable at a glance rather than letting
    /// it grow into the thing the marker list just stopped being.</para>
    /// </summary>
    internal static class GearMenu
    {
        private const float Width = 230f;
        private const float RowHeight = 26f;
        private const float HeaderHeight = 26f;
        private const float Pad = 4f;

        /// <summary>A row whose words say what pressing it will do, read again after every press.</summary>
        private sealed class Entry
        {
            public Func<string> Text;
            public TextMeshProUGUI Label;
        }

        private static readonly List<Entry> _entries = new List<Entry>();
        private static GameObject _root;
        private static RectTransform _rect;
        private static Camera _camera;
        private static int _openedFrame = -10;
        private static int _closedFrame = -10;

        internal static bool IsOpen => _root != null;

        /// <summary>Is the pointer over the menu itself, rather than merely is it open?</summary>
        internal static bool Hovered =>
            _root != null && _rect != null
            && RectTransformUtility.RectangleContainsScreenPoint(_rect, ZInput.pointerPosition, _camera);

        internal static void Toggle(Vector2 screenPos)
        {
            if (_root != null) Close();
            else Open(screenPos);
        }

        internal static void Open(Vector2 screenPos)
        {
            // A click on the gear while the menu is open closes it. Whether that click reaches the
            // gear or the "clicked away" check first depends on script order, so a menu closed
            // this frame cannot be reopened by the same click.
            if (Time.frameCount == _closedFrame) return;
            Close();
            var map = Minimap.instance;
            if (map == null || map.m_largeRoot == null) return;
            var parent = map.m_largeRoot.transform as RectTransform;
            if (parent == null) return;

            var panel = MenuKit.Panel(parent, "TGM_GearMenu");
            _root = panel.gameObject;
            _rect = panel.rectTransform;
            _rect.anchorMin = _rect.anchorMax = new Vector2(0f, 1f);
            _rect.pivot = new Vector2(0f, 1f);
            _entries.Clear();

            float y = -Pad;
            var header = MenuKit.Text(_root.transform, "Header", "How the map is drawn", 15f, MenuKit.Header);
            Place(header.rectTransform, 10f, y, Width - 20f, HeaderHeight);
            header.alignment = TextAlignmentOptions.MidlineLeft;
            y -= HeaderHeight;

            Row(y, () => TgmConfig.ShowMarkerLabels,
                () => On(TgmConfig.ShowMarkerLabels) ? "Hide Marker Labels" : "Show Marker Labels",
                "Marker Labels", "Write a marker's name beside it, where it has one");
            y -= RowHeight;

            Row(y, () => TgmConfig.ShowPortalLabels,
                () => On(TgmConfig.ShowPortalLabels) ? "Hide Portal Labels" : "Show Portal Labels",
                "Portal Labels", "Write a portal's tag beside it on the map");
            y -= RowHeight;

            float height = -y + Pad;
            _rect.sizeDelta = new Vector2(Width, height);

            var canvas = parent.GetComponentInParent<Canvas>();
            _camera = canvas != null && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.rootCanvas.worldCamera : null;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screenPos, _camera, out var local);
            var pr = parent.rect;
            float x = local.x - pr.xMin;
            float top = pr.yMax - local.y;
            if (x + Width > pr.width) x = Mathf.Max(0f, pr.width - Width);
            if (top + height > pr.height) top = Mathf.Max(0f, pr.height - height);
            _rect.anchoredPosition = new Vector2(x, -top);
            _root.transform.SetAsLastSibling();
            _openedFrame = Time.frameCount;
            Refresh();
        }

        private static bool On(ConfigEntry<bool> entry) => entry != null && entry.Value;

        /// <summary>
        /// A row captioned with what pressing it will do rather than with the state it is in.
        /// "Hide Marker Labels" is an instruction; "Marker Labels: on" is a report that leaves the
        /// reader to work out what a press does.
        /// </summary>
        private static void Row(float y, Func<ConfigEntry<bool>> entry, Func<string> text, string topic, string tip)
        {
            var row = MenuKit.Row(_root.transform, "", true, () =>
            {
                var e = entry();
                if (e != null) e.Value = !e.Value;
                Refresh();
            });
            Place(row, 0f, y, Width, RowHeight);
            MenuKit.Tip(row.gameObject, topic, tip);
            var label = row.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null) label.alignment = TextAlignmentOptions.MidlineLeft;
            _entries.Add(new Entry { Text = text, Label = label });
        }

        private static void Place(RectTransform rt, float x, float y, float width, float height)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(width, height);
        }

        private static void Refresh()
        {
            foreach (var entry in _entries)
            {
                if (entry.Label == null || entry.Text == null) continue;
                entry.Label.text = entry.Text();
                entry.Label.color = MenuKit.Header;
            }
        }

        internal static void Close()
        {
            if (_root != null) Object.Destroy(_root);
            _root = null;
            _rect = null;
            _entries.Clear();
            _closedFrame = Time.frameCount;
        }

        internal static void Update()
        {
            if (_root == null) return;
            var map = Minimap.instance;
            if (map == null || map.m_mode != Minimap.MapMode.Large) { Close(); return; }
            if (ZInput.GetKeyDown(KeyCode.Escape)) { Close(); return; }
            if (Time.frameCount - _openedFrame <= 1) return;   // the click that opened it
            if (ZInput.GetMouseButtonDown(0) || ZInput.GetMouseButtonDown(1))
                if (!RectTransformUtility.RectangleContainsScreenPoint(_rect, ZInput.pointerPosition, _camera)) Close();
        }
    }
}
