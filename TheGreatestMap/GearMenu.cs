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

        /// <summary>
        /// A row with a checkbox: the words name the setting and stay put, and the box says
        /// whether it is on.
        ///
        /// <para>These rows used to be captioned with what pressing them would do -- "Hide Marker
        /// Labels" when the labels were on -- which is right for a row that performs an action and
        /// wrong for one that holds a setting. The words changing under the pointer is also what
        /// makes a settings panel hard to read at a glance: you cannot see what is on without
        /// reading each line and working out the inverse.</para>
        /// </summary>
        private sealed class Entry
        {
            public Func<bool> On;
            public TextMeshProUGUI Label;
            public Image Box;
            public Image Tick;
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

            Row(y, () => TgmConfig.ShowMarkerLabels, "Show Marker Labels",
                "Marker Labels", "Write a marker's name beside it, where it has one");
            y -= RowHeight;

            Row(y, () => TgmConfig.ShowPortalLabels, "Show Portal Labels",
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
        /// One setting: a checkbox, then the name of the thing it draws. The whole row is the
        /// target, box included, so there is no small square to hit.
        /// </summary>
        private static void Row(float y, Func<ConfigEntry<bool>> entry, string text, string topic, string tip)
        {
            var row = MenuKit.Row(_root.transform, "", true, () =>
            {
                var e = entry();
                if (e != null) e.Value = !e.Value;
                Refresh();
            });
            Place(row, 0f, y, Width, RowHeight);
            MenuKit.Tip(row.gameObject, topic, tip);

            var box = Picture(row, MenuKit.Box(), 16f);
            var tick = Picture(row, MenuKit.Check(), 12f);

            var label = row.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null)
            {
                label.text = text;
                label.alignment = TextAlignmentOptions.MidlineLeft;
                label.rectTransform.offsetMin = new Vector2(34f, 0f);
            }
            _entries.Add(new Entry { On = () => On(entry()), Label = label, Box = box, Tick = tick });
        }

        /// <summary>One of the drawn marks, at the head of a row where the checkbox goes.</summary>
        private static Image Picture(RectTransform row, Sprite sprite, float size)
        {
            var go = new GameObject("Mark", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(row, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(10f + (16f - size) * 0.5f, 0f);
            rt.sizeDelta = new Vector2(size, size);
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.preserveAspect = true;
            img.raycastTarget = false;
            return img;
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
                bool on = entry.On == null || entry.On();
                // Dimmed while it is off, the way the marker list dims a kind it is not drawing:
                // across this mod, faint means "not on the map".
                if (entry.Label != null) entry.Label.color = on ? MenuKit.Body : MenuKit.Dim;
                if (entry.Tick != null) entry.Tick.enabled = on;
                if (entry.Box != null)
                    entry.Box.color = on ? MenuKit.Header : new Color(1f, 0.85f, 0.45f, 0.4f);
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
