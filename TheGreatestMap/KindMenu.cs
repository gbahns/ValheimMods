using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace TheGreatestMap
{
    /// <summary>
    /// The list behind a left click on the marker button, in two columns. On the left, one row per
    /// kind that has markers on your map, showing its icon. On the right, the markers that have been
    /// crossed off: used-up deposits as one switch, and searched places by type, so burial chambers
    /// you are done with can go while the sunken crypts stay. Clicking a row changes that one thing
    /// and leaves the list open, so several can be changed at once.
    /// </summary>
    internal static class KindMenu
    {
        private const float ColumnWidth = 250f;
        private const float Gap = 8f;
        private const float Width = ColumnWidth * 2f + Gap;
        private const float RowHeight = 26f;
        private const float HeaderHeight = 26f;
        private const float Pad = 4f;
        private const float IconSize = 20f;
        private const float Indent = 16f;

        /// <summary>
        /// A switch row is an icon and a name, drawn the way the map draws what it covers: full
        /// strength when shown, dimmed when hidden. An action row is gold text saying what a click
        /// will do, as the master row does.
        /// </summary>
        private sealed class Entry
        {
            public Func<bool> Shown;
            public Func<string> Text;
            public Func<bool> Quiet;
            public TextMeshProUGUI Label;
            public Image Icon;
        }

        private static readonly List<Entry> _entries = new List<Entry>();
        private static GameObject _root;
        private static RectTransform _rect;
        private static Camera _camera;
        private static int _openedFrame = -10;
        private static int _closedFrame = -10;

        internal static bool IsOpen => _root != null;

        private static bool AllShown => TgmConfig.ShowAllMarkers == null || TgmConfig.ShowAllMarkers.Value;

        internal static void Open(Vector2 screenPos)
        {
            // A click on the button while the list is open closes it. Whether that click reaches
            // the button or the "clicked away" check first depends on script order, so a list
            // closed this frame cannot be reopened by the same click.
            if (Time.frameCount == _closedFrame) return;
            Close();
            var map = Minimap.instance;
            if (map == null || map.m_largeRoot == null) return;
            var parent = map.m_largeRoot.transform as RectTransform;
            if (parent == null) return;
            if (ClientPins.Count == 0)
            {
                TheGreatestMapMod.Message("No markers on your map yet.");
                return;
            }

            var seen = new HashSet<Category>();
            int cleared = 0;
            var searched = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var groupIcon = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pin in ClientPins.All)
            {
                var kind = ClientPins.KindOf(pin);
                if (kind.HasValue) seen.Add(kind.Value);
                if (!pin.Checked) continue;
                if (CrossedOff.IsCleared(pin)) { cleared++; continue; }
                string group = CrossedOff.GroupOf(pin);
                searched.TryGetValue(group, out int n);
                searched[group] = n + 1;
                if (!groupIcon.ContainsKey(group)) groupIcon[group] = pin.Icon;
            }

            var panel = MenuKit.Panel(parent, "TGM_KindMenu");
            _root = panel.gameObject;
            _rect = panel.rectTransform;
            _rect.anchorMin = _rect.anchorMax = new Vector2(0f, 1f);
            _rect.pivot = new Vector2(0f, 1f);
            _entries.Clear();

            float left = BuildKinds(map, seen);
            float right = BuildCrossedOff(map, cleared, searched, groupIcon);
            float y = Mathf.Min(left, right);

            var divider = new GameObject("Divider", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            divider.transform.SetParent(_root.transform, false);
            var dimg = divider.GetComponent<Image>();
            dimg.color = new Color(MenuKit.OutlineColor.r, MenuKit.OutlineColor.g, MenuKit.OutlineColor.b, 0.35f);
            dimg.raycastTarget = false;
            Place((RectTransform)divider.transform, ColumnWidth + Gap * 0.5f - 0.5f, -Pad, 1f, -y - Pad);

            // Everything back, both columns: the kinds, the master and every crossed-off switch.
            var showAll = MenuKit.Row(_root.transform, "Show all", true, () =>
            {
                foreach (var k in Categories.All)
                    if (TgmConfig.ShowKind.TryGetValue(k, out var e) && !e.Value) e.Value = true;
                if (TgmConfig.ShowAllMarkers != null) TgmConfig.ShowAllMarkers.Value = true;
                CrossedOff.ShowAll();
                ClientPins.Restyle();
                Refresh();
            });
            Place(showAll, 0f, y, Width, RowHeight);
            y -= RowHeight;

            float height = -y + Pad;
            _rect.sizeDelta = new Vector2(Width, height);

            var canvas = parent.GetComponentInParent<Canvas>();
            _camera = canvas != null && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.rootCanvas.worldCamera : null;
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

        /// <summary>The left column. Returns the y below its last row.</summary>
        private static float BuildKinds(Minimap map, HashSet<Category> seen)
        {
            const float x = 0f;
            float y = -Pad;
            Header("Marker kinds", x, y);
            y -= HeaderHeight;

            // The master, so it is reachable from this list too, not only from a right click on the button.
            ActionRow(x, y, () => { MarkerToggle.ToggleAll(); Refresh(); },
                () => AllShown ? "Hide all markers" : "Show all markers", null);
            y -= RowHeight;

            // Every kind that has markers, vanilla-icon ones included. Structures, portals, camps
            // and boss altars are also hidden and shown by vanilla's own icon buttons, so listing
            // them here is redundant, but leaving them out was worse: a player looking for the
            // switch expects to find it with the rest, and does not care which mod owns the icon.
            // The two are not identical, and the difference is in our favor: vanilla's button hides
            // every pin with that icon, while this one covers only the markers this mod recorded.
            foreach (var kind in Categories.All)
            {
                if (!seen.Contains(kind)) continue;
                var k = kind;
                SwitchRow(map, x, y, 0f, IconKeyFor(k), Categories.Label(k),
                    () => { if (TgmConfig.ShowKind.TryGetValue(k, out var e)) e.Value = !e.Value; },
                    () => AllShown && IsShown(k));
                y -= RowHeight;
            }
            return y;
        }

        /// <summary>
        /// The right column. Used-up deposits first, then searched places: structures on their own,
        /// then dungeons with one switch for all of them and one per type, then anything else that
        /// has been crossed off. A searched group appears once something in it has been crossed off;
        /// before that, the marker's own right-click menu is where the choice comes up.
        /// </summary>
        private static float BuildCrossedOff(Minimap map, int cleared, Dictionary<string, int> searched, Dictionary<string, string> groupIcon)
        {
            float x = ColumnWidth + Gap;
            float y = -Pad;
            Header("Crossed off", x, y);
            y -= HeaderHeight;

            ActionRow(x, y, () =>
                {
                    if (TgmConfig.ShowClearedDeposits != null) TgmConfig.ShowClearedDeposits.Value = !TgmConfig.ShowClearedDeposits.Value;
                },
                () => $"{(TgmConfig.ShowClearedDeposits == null || TgmConfig.ShowClearedDeposits.Value ? "Hide" : "Show")} cleared deposits ({cleared})",
                () => cleared == 0);
            y -= RowHeight;

            void Group(string group, float indent)
            {
                if (!searched.TryGetValue(group, out int count)) return;
                groupIcon.TryGetValue(group, out var icon);
                var g = group;
                SwitchRow(map, x, y, indent, icon, $"{g} ({count})",
                    () => CrossedOff.SetGroupHidden(g, !CrossedOff.IsGroupHidden(g)),
                    () => AllShown && !CrossedOff.IsGroupHidden(g));
                y -= RowHeight;
            }

            // Abandoned houses and the other buildings are not dungeons and stay out of that switch.
            Group(Categories.Label(Category.Structure), 0f);

            var dungeons = CrossedOff.DungeonGroups();
            int dungeonCount = 0;
            foreach (var group in dungeons)
                if (searched.TryGetValue(group, out int n)) dungeonCount += n;
            ActionRow(x, y, () => CrossedOff.SetDungeonsHidden(AnyDungeonShown()),
                () => $"{(AnyDungeonShown() ? "Hide" : "Show")} searched dungeons ({dungeonCount})",
                () => dungeonCount == 0);
            y -= RowHeight;
            foreach (var group in dungeons) Group(group, Indent);

            foreach (var kind in Categories.All)
            {
                if (Categories.IsResource(kind) || kind == Category.Dungeon || kind == Category.Structure) continue;
                Group(Categories.Label(kind), 0f);
            }
            Group(CrossedOff.Placed, 0f);
            Group(CrossedOff.Unsorted, 0f);
            return y;
        }

        private static bool AnyDungeonShown()
        {
            foreach (var group in CrossedOff.DungeonGroups())
                if (!CrossedOff.IsGroupHidden(group)) return true;
            return false;
        }

        private static void Header(string text, float x, float y)
        {
            var header = MenuKit.Text(_root.transform, "Header", text, 15f, MenuKit.Header);
            Place(header.rectTransform, x + 10f, y, ColumnWidth - 20f, HeaderHeight);
            header.alignment = TextAlignmentOptions.MidlineLeft;
        }

        private static void ActionRow(float x, float y, Action onClick, Func<string> text, Func<bool> quiet)
        {
            var row = MenuKit.Row(_root.transform, "", true, () =>
            {
                onClick();
                ClientPins.Restyle();
                Refresh();
            });
            Place(row, x, y, ColumnWidth, RowHeight);
            var label = row.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null) label.alignment = TextAlignmentOptions.MidlineLeft;
            _entries.Add(new Entry { Text = text, Quiet = quiet ?? (() => false), Label = label });
        }

        private static void SwitchRow(Minimap map, float x, float y, float indent, string iconKey, string text, Action onClick, Func<bool> shown)
        {
            var row = MenuKit.Row(_root.transform, "", true, () =>
            {
                onClick();
                ClientPins.Restyle();
                Refresh();
            });
            Place(row, x, y, ColumnWidth, RowHeight);
            var entry = new Entry { Shown = shown };

            var sprite = (iconKey != null ? IconRegistry.Resolve(map, iconKey) : null) ?? IconRegistry.Resolve(map, IconRegistry.FallbackKey);
            var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            iconGo.transform.SetParent(row, false);
            var irt = (RectTransform)iconGo.transform;
            irt.anchorMin = new Vector2(0f, 0.5f);
            irt.anchorMax = new Vector2(0f, 0.5f);
            irt.pivot = new Vector2(0f, 0.5f);
            irt.anchoredPosition = new Vector2(8f + indent, 0f);
            irt.sizeDelta = new Vector2(IconSize, IconSize);
            entry.Icon = iconGo.GetComponent<Image>();
            entry.Icon.sprite = sprite;
            entry.Icon.raycastTarget = false;
            entry.Icon.preserveAspect = true;

            entry.Label = row.GetComponentInChildren<TextMeshProUGUI>();
            if (entry.Label != null)
            {
                entry.Label.text = text;
                entry.Label.alignment = TextAlignmentOptions.MidlineLeft;
                entry.Label.rectTransform.offsetMin = new Vector2(indent + IconSize + 14f, 0f);
            }
            _entries.Add(entry);
        }

        private static string IconKeyFor(Category kind)
        {
            return TgmConfig.CategoryIcon.TryGetValue(kind, out var entry) ? entry.Value : Categories.DefaultIcon(kind);
        }

        private static bool IsShown(Category kind)
        {
            return !TgmConfig.ShowKind.TryGetValue(kind, out var entry) || entry.Value;
        }

        /// <summary>Top-left anchored, so both columns can share one panel.</summary>
        private static void Place(RectTransform rt, float x, float y, float width, float height)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(width, height);
        }

        /// <summary>
        /// Redraw every row from the current settings. While the master is off every switch row is
        /// dimmed, since nothing is drawn either way.
        /// </summary>
        private static void Refresh()
        {
            foreach (var entry in _entries)
            {
                if (entry.Label == null) continue;
                if (entry.Text != null)
                {
                    entry.Label.text = entry.Text();
                    entry.Label.color = entry.Quiet() ? MenuKit.Dim : MenuKit.Header;
                    continue;
                }
                bool shown = entry.Shown == null || entry.Shown();
                entry.Label.color = shown ? MenuKit.Body : MenuKit.Dim;
                if (entry.Icon != null) entry.Icon.color = shown ? Color.white : new Color(1f, 1f, 1f, 0.3f);
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
            if (map == null || map.m_mode != Minimap.MapMode.Large || ZInput.GetKeyDown(KeyCode.Escape)) { Close(); return; }
            if (Time.frameCount - _openedFrame <= 1) return; // the click that opened it
            if (ZInput.GetMouseButtonDown(0) || ZInput.GetMouseButtonDown(1))
                if (!RectTransformUtility.RectangleContainsScreenPoint(_rect, ZInput.pointerPosition, _camera)) Close();
        }
    }
}
