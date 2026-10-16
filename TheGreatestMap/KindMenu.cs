using System;
using System.Collections.Generic;
using HarmonyLib;
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
        private static TMP_InputField _find;
        private static Vector2 _screenPos;
        private static List<Focus.TypeCount> _results;
        private static string _pendingText;
        private static bool _rebuilding;
        private static bool _rebuildWanted;

        internal static bool IsOpen => _root != null;

        /// <summary>
        /// True while the find box has the keyboard. Every hotkey in this mod is gated on it, and
        /// the game's own are too, by way of the TextInput patch below -- otherwise searching for
        /// a frost cave takes the map out, opens the inventory and lights a torch on the way.
        /// </summary>
        internal static bool Typing => _find != null && _find.isFocused;

        private static bool AllShown => TgmConfig.ShowAllMarkers == null || TgmConfig.ShowAllMarkers.Value;

        internal static void Open(Vector2 screenPos)
        {
            // A click on the button while the list is open closes it. Whether that click reaches
            // the button or the "clicked away" check first depends on script order, so a list
            // closed this frame cannot be reopened by the same click.
            if (Time.frameCount == _closedFrame) return;
            Close();
            _screenPos = screenPos;
            if (!_rebuilding) _results = null;
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
                Focus.Clear();
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
            if (_pendingText != null && _find != null)
            {
                _find.SetTextWithoutNotify(_pendingText);
                _find.ActivateInputField();
            }
            _pendingText = null;
            Refresh();
        }

        /// <summary>
        /// Draw the list again in place, keeping what was typed. Used when the rows themselves have
        /// to change rather than only their colors -- showing the types that matched instead of the
        /// kinds. The close is ours rather than a click elsewhere, so it must not count as the
        /// click-away that stops the list reopening in the same frame.
        /// </summary>
        private static void Rebuild()
        {
            if (_root == null) return;
            _rebuildWanted = false;
            _pendingText = _find != null ? _find.text : null;
            var pos = _screenPos;
            _rebuilding = true;
            Close();
            _closedFrame = -10;
            try { Open(pos); }
            finally { _rebuilding = false; }
        }

        /// <summary>The left column. Returns the y below its last row.</summary>
        private static float BuildKinds(Minimap map, HashSet<Category> seen)
        {
            const float x = 0f;
            float y = -Pad;
            Header(_results != null
                ? "Matches   click one to see only it"
                : "Marker kinds   +  places one by hand", x, y);
            y -= HeaderHeight;

            FindBox(x, y);
            y -= RowHeight;

            // What the map is showing, which is also how to stop: the row is the way back to
            // everything. It is built whether or not anything is focused, because focusing happens
            // by typing in the box above it and the row has to be there to say so.
            ActionRow(x, y, Focus.Clear, () =>
                {
                    Focus.Tally(out int shown, out int all);
                    return Focus.On
                        ? $"Only {Focus.What()} — {shown} of {all}, click for all"
                        : $"Showing all {all} markers";
                },
                () => !Focus.On);
            y -= RowHeight;

            // The master, so it is reachable from this list too, not only from a right click on the button.
            ActionRow(x, y, () => { MarkerToggle.ToggleAll(); Refresh(); },
                () => AllShown ? "Hide all markers" : "Show all markers", null);
            y -= RowHeight;

            if (_results != null)
            {
                ActionRow(x, y, () =>
                    {
                        // Empty the box as well, or the kinds come back with a search still
                        // written above them and nothing being searched for.
                        if (_find != null) _find.SetTextWithoutNotify("");
                        Focus.Clear();
                        _results = null;
                        _rebuildWanted = true;
                    },
                    () => "Back to the kinds", null);
                y -= RowHeight;
                if (_results.Count == 0)
                {
                    var none = MenuKit.Text(_root.transform, "None", "Nothing on the map matches.", 13f, MenuKit.Dim);
                    Place(none.rectTransform, x + 10f, y, ColumnWidth - 20f, RowHeight);
                    none.alignment = TextAlignmentOptions.MidlineLeft;
                    y -= RowHeight;
                }
                foreach (var match in _results)
                {
                    var m = match;
                    SwitchRow(map, x, y, 0f, m.Icon, $"{m.Label} ({m.Count})",
                        () => Focus.SetIcon(m.Icon),
                        () => Focus.Icon != null && IconRegistry.SameKey(Focus.Icon, m.Icon));
                    y -= RowHeight;
                }
                return y;
            }

            // Every kind that has markers, vanilla-icon ones included. Structures, portals, camps
            // and boss altars are also hidden and shown by vanilla's own icon buttons, so listing
            // them here is redundant, but leaving them out was worse: a player looking for the
            // switch expects to find it with the rest, and does not care which mod owns the icon.
            // The two are not identical, and the difference is in our favor: vanilla's button hides
            // every pin with that icon, while this one covers only the markers this mod recorded.
            foreach (var kind in Categories.All)
            {
                // A kind that is only ever placed by hand has no markers until one is placed, so
                // it must be listed anyway -- it is the list that places it.
                if (!seen.Contains(kind) && !Categories.IsPlacedOnly(kind)) continue;
                var k = kind;
                SwitchRow(map, x, y, 0f, IconKeyFor(k), Categories.Label(k),
                    () => { if (TgmConfig.ShowKind.TryGetValue(k, out var e)) e.Value = !e.Value; },
                    () => AllShown && IsShown(k),
                    // The small pin at the row's end arms that kind for placing by hand. It is a
                    // button of its own rather than another meaning for the row, which already
                    // means "show or hide these".
                    () => { if (ManualPlace.Arm(map, k)) Close(); },
                    // And "only": the same row's switch would need twenty others turned off to say
                    // this, so it says it in one press, and says it again to take it back.
                    () => Focus.SetKind(k));
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

            // Dungeons and structures are each several things wearing one kind, so each gets a
            // switch for the lot and a row per type under it.
            foreach (var kind in Categories.All)
            {
                if (!CrossedOff.Splits(kind)) { Group(Categories.Label(kind), 0f); continue; }
                var groups = CrossedOff.GroupsOf(kind);
                int total = 0;
                foreach (var group in groups)
                    if (searched.TryGetValue(group, out int n)) total += n;
                var owner = kind;
                string label = Categories.Label(kind).ToLowerInvariant();
                ActionRow(x, y, () => CrossedOff.SetTypesHidden(owner, AnyShown(owner)),
                    () => $"{(AnyShown(owner) ? "Hide" : "Show")} searched {label} ({total})",
                    () => total == 0);
                y -= RowHeight;
                foreach (var group in groups) Group(group, Indent);
            }

            foreach (var kind in Categories.All)
            {
                if (Categories.IsResource(kind) || CrossedOff.HasTypes(kind)) continue;   // done above
                Group(Categories.Label(kind), 0f);
            }
            Group(CrossedOff.Placed, 0f);
            Group(CrossedOff.Unsorted, 0f);
            return y;
        }

        private static bool AnyShown(Category cat)
        {
            foreach (var group in CrossedOff.GroupsOf(cat))
                if (!CrossedOff.IsGroupHidden(group)) return true;
            return false;
        }

        /// <summary>
        /// A button of its own at the end of a row, so the row keeps meaning what it meant: a small
        /// pin that arms the kind for placing by hand, and "only", which narrows the map to it.
        /// Written rather than drawn as a glyph, because the game's font is a display face and what
        /// it has beyond the Latin alphabet cannot be relied on.
        /// </summary>
        private static void RowButton(RectTransform row, string glyph, float size, float right, float width, Action onClick)
        {
            var go = new GameObject("RowButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            go.transform.SetParent(row, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = new Vector2(-right, 0f);
            rt.sizeDelta = new Vector2(width, RowHeight - 8f);
            var img = go.GetComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0.07f);
            img.raycastTarget = true;
            var button = go.GetComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = img;
            button.onClick.AddListener(() => onClick());

            var text = MenuKit.Text(go.transform, "Glyph", glyph, size, new Color(1f, 0.85f, 0.45f, 0.9f));
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            var grt = text.rectTransform;
            grt.anchorMin = Vector2.zero;
            grt.anchorMax = Vector2.one;
            grt.offsetMin = Vector2.zero;
            grt.offsetMax = Vector2.zero;
        }

        /// <summary>
        /// The find box. Typing narrows the map as the letters go in, so the map answers before the
        /// word is finished; Enter turns what matched into rows, one per type, to pick exactly one
        /// of them. Emptying the box shows everything again, which is why there is no clear button.
        /// </summary>
        private static void FindBox(float x, float y)
        {
            var go = new GameObject("Find", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(TMP_InputField));
            go.transform.SetParent(_root.transform, false);
            Place((RectTransform)go.transform, x + 8f, y - 2f, ColumnWidth - 16f, RowHeight - 6f);
            go.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.14f);

            var text = MenuKit.Text(go.transform, "Text", "", 13f, MenuKit.Body);
            text.alignment = TextAlignmentOptions.MidlineLeft;
            var trt = text.rectTransform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(6f, 0f);
            trt.offsetMax = new Vector2(-6f, 0f);

            var hint = MenuKit.Text(go.transform, "Hint", "find a marker...", 13f, MenuKit.Dim);
            hint.alignment = TextAlignmentOptions.MidlineLeft;
            var hrt = hint.rectTransform;
            hrt.anchorMin = Vector2.zero;
            hrt.anchorMax = Vector2.one;
            hrt.offsetMin = new Vector2(6f, 0f);
            hrt.offsetMax = new Vector2(-6f, 0f);

            _find = go.GetComponent<TMP_InputField>();
            _find.textViewport = (RectTransform)go.transform;
            _find.textComponent = text;
            _find.placeholder = hint;
            _find.lineType = TMP_InputField.LineType.SingleLine;
            // TextMeshPro draws its caret in its own dark default and ignores caretColor until it
            // is told to use a custom one, which on a dark panel leaves you typing blind.
            _find.customCaretColor = true;
            _find.caretColor = new Color(1f, 0.85f, 0.45f, 1f);
            _find.caretWidth = 2;
            _find.caretBlinkRate = 0.8f;
            _find.selectionColor = new Color(1f, 0.85f, 0.45f, 0.45f);
            _find.onValueChanged.AddListener(typed => { Focus.Search(typed); Refresh(); });
            _find.onSubmit.AddListener(typed =>
            {
                Focus.Search(typed);
                _results = Focus.Types(typed);
                _rebuildWanted = true;
            });
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

        private static void SwitchRow(Minimap map, float x, float y, float indent, string iconKey, string text, Action onClick, Func<bool> shown, Action onPlace = null, Action onOnly = null)
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

            if (onPlace != null) RowButton(row, "+", 15f, 6f, RowHeight - 8f, onPlace);
            if (onOnly != null)
                RowButton(row, "only", 11f, onPlace != null ? RowHeight + 2f : 6f, 34f,
                    () => { onOnly(); Refresh(); });

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
            _find = null;
            _closedFrame = Time.frameCount;
        }

        internal static void Update()
        {
            if (_root == null) return;
            var map = Minimap.instance;
            if (map == null || map.m_mode != Minimap.MapMode.Large) { Close(); return; }
            // Asked for between frames rather than from the find box's own callback, which would
            // destroy the box while it was still handling the Enter that asked for this.
            if (_rebuildWanted) { Rebuild(); return; }
            // Escape leaves the find box first, as it does in every other text field, so a search
            // can be abandoned without the whole list going with it.
            if (ZInput.GetKeyDown(KeyCode.Escape) && !Typing) { Close(); return; }
            // A click lands in the box as a click in the panel, so the list stays open and the
            // click-away test below is never reached while typing.
            if (Time.frameCount - _openedFrame <= 1) return; // the click that opened it
            if (ZInput.GetMouseButtonDown(0) || ZInput.GetMouseButtonDown(1))
                if (!RectTransformUtility.RectangleContainsScreenPoint(_rect, ZInput.pointerPosition, _camera)) Close();
        }
    }

    /// <summary>
    /// While the find box has the keyboard, the game is told a text input is up, which is how it
    /// already knows to leave its own keys alone. Only while typing: the list is full of clickable
    /// rows the rest of the time, and claiming a text input throughout would take keys the player
    /// still wants. The same trick the legend uses, for the same reason.
    /// </summary>
    [HarmonyPatch(typeof(TextInput), nameof(TextInput.IsVisible))]
    internal static class TextInput_IsVisible_KindMenu_Patch
    {
        private static void Postfix(ref bool __result)
        {
            if (KindMenu.Typing) __result = true;
        }
    }
}
