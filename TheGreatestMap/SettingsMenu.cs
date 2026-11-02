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
    /// The marker list's settings, behind the gear in its title strip: how the list is ordered,
    /// and what the map writes beside a marker.
    ///
    /// <para>In front of the list rather than in the place of it. These were a view of the list
    /// for an afternoon -- press the gear and the markers were replaced -- and that reads as
    /// having gone somewhere else, when the whole point of putting them here was that they are
    /// about what is on the screen behind them. A player changing the order wants to watch the
    /// order change.</para>
    ///
    /// <para>It belongs to the list and goes when the list goes. A click inside it is a click in
    /// the list as far as the list is concerned, or the list would close under its own settings
    /// the first time anybody pressed one.</para>
    /// </summary>
    internal static class SettingsMenu
    {
        private const float Width = 260f;
        private const float RowHeight = 26f;
        private const float HeadHeight = 22f;
        private const float Pad = 4f;

        /// <summary>A row with a box: what it says is fixed, and the box says whether it is on.</summary>
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

        internal static bool IsOpen => _root != null;

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
            Close();
            var map = Minimap.instance;
            if (map == null || map.m_largeRoot == null) return;
            var parent = map.m_largeRoot.transform as RectTransform;
            if (parent == null) return;

            var panel = MenuKit.Panel(parent, "TGM_SettingsMenu");
            _root = panel.gameObject;
            _rect = panel.rectTransform;
            _rect.anchorMin = _rect.anchorMax = new Vector2(0f, 1f);
            _rect.pivot = new Vector2(0f, 1f);
            _entries.Clear();

            float y = -Pad;
            y = Heading("Counts", y);
            y = Switch(y, () => TgmConfig.ShowCounts, "Show counts",
                "Write how many each heading, kind and type has after its name, and how many of those are drawn " +
                "where the two differ. The count of what you are finished with keeps its button either way, that " +
                "one being the switch for them as well as the number.");

            y = Heading("Grouping", y);
            y = Switch(y, () => TgmConfig.SplitPlantsByFarming, "Split plants by farming",
                "Stand two rows in place of the five plant kinds -- Farm and Gather. What you can plant you only " +
                "have to find once, so those are worth folding away; what you cannot plant you have to go out and " +
                "gather again, which is what a map is for. Ore is a row of its own either way.");

            y = Heading("The Map's Own Buttons", y);
            y = Switch(y, () => TgmConfig.HideVanillaControls, "Hide vanilla controls",
                "Take the game's row of pin buttons off the large map. This list covers every one of them now: a " +
                "row for each icon pressing the same switch its button does, a place-by-hand button on the five " +
                "you can drop, and a line at the foot of this list for pinging out loud. Nothing else of the map's " +
                "is touched, and nothing is hidden while the marker button is off, that being the way in here.");

            y = Heading("Marker Order", y);
            y = Order(y, TypeOrder.Progression, "Progression",
                "The order the catalog lists them in, which is the order you meet them: a dandelion before a " +
                "thistle, copper before silver. Reorder a Catalog line and these follow it.");
            y = Order(y, TypeOrder.MostFirst, "Most first",
                "The commonest at the top, which is what this list did before there was a choice");
            y = Order(y, TypeOrder.AToZ, "A to Z", "By name");

            y = Heading("Labels", y);
            y = Switch(y, () => TgmConfig.ShowMarkerLabels, "Marker labels",
                "Write a marker's label beside it, where it has one");
            y = Switch(y, () => TgmConfig.ShowPortalLabels, "Portal names",
                "Write a portal's tag beside it on the map");
            y = Switch(y, () => TgmConfig.ShowBossLabels, "Boss names",
                "Write a boss altar's name beside it, as the game does when you find one");

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
            // In front of the list, which was itself brought to the front when it opened.
            _root.transform.SetAsLastSibling();
            _openedFrame = Time.frameCount;
            Refresh();
        }

        private static float Heading(string text, float y)
        {
            var head = MenuKit.Text(_root.transform, "Heading", text, 13f, MenuKit.Header);
            Place(head.rectTransform, 10f, y, Width - 20f, HeadHeight);
            head.alignment = TextAlignmentOptions.MidlineLeft;
            return y - HeadHeight;
        }

        /// <summary>One of the orders the types can come in, ticked while it is the one in use.</summary>
        private static float Order(float y, TypeOrder order, string text, string tip)
        {
            Row(y, text, tip,
                () => TgmConfig.TypeRowOrder != null && TgmConfig.TypeRowOrder.Value == order,
                () =>
                {
                    if (TgmConfig.TypeRowOrder != null) TgmConfig.TypeRowOrder.Value = order;
                    // The list behind this one is what changed, so it is the one to redraw.
                    KindMenu.Relayout();
                });
            return y - RowHeight;
        }

        private static float Switch(float y, Func<ConfigEntry<bool>> entry, string text, string tip)
        {
            Row(y, text, tip,
                () => { var e = entry(); return e == null || e.Value; },
                () => { var e = entry(); if (e != null) e.Value = !e.Value; });
            return y - RowHeight;
        }

        /// <summary>
        /// A box to check and the name of what it does. The whole row is the target, box
        /// included, so there is no small square to hit.
        /// </summary>
        private static void Row(float y, string text, string tip, Func<bool> on, Action press)
        {
            var row = MenuKit.Row(_root.transform, "", true, () => { press(); ClientPins.Restyle(); Refresh(); });
            Place(row, 0f, y, Width, RowHeight);
            MenuKit.Tip(row.gameObject, null, tip);

            var box = Mark(row, MenuKit.Box(), 14f, 14f);
            var tick = Mark(row, MenuKit.Check(), 11f, 15.5f);
            var label = row.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null)
            {
                label.text = text;
                label.alignment = TextAlignmentOptions.MidlineLeft;
                label.rectTransform.offsetMin = new Vector2(38f, 0f);
            }
            _entries.Add(new Entry { On = on, Label = label, Box = box, Tick = tick });
        }

        private static Image Mark(RectTransform row, Sprite sprite, float size, float left)
        {
            var go = new GameObject("Mark", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(row, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(left, 0f);
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
                if (entry.Tick != null) entry.Tick.enabled = on;
                if (entry.Box != null) entry.Box.color = on ? MenuKit.Header : new Color(1f, 0.85f, 0.45f, 0.4f);
                // Dimmed while it is off, the way the marker list dims a kind it is not drawing:
                // across this mod, faint means "not on the map".
                if (entry.Label != null) entry.Label.color = on ? MenuKit.Body : MenuKit.Dim;
            }
        }

        internal static void Close()
        {
            if (_root != null) Object.Destroy(_root);
            _root = null;
            _rect = null;
            _entries.Clear();
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
