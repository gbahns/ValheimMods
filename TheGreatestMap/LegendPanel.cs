using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TheGreatestMap
{
    /// <summary>
    /// Every marker the mod can draw, with the thing it stands for, in one scrollable list. Built
    /// for reviewing the guesses rather than for play: the icon shown is resolved exactly the way
    /// recording resolves it, each row says where that icon came from, and a row whose icon names
    /// an item this game does not have is called out in red. That last case is how a marker ends
    /// up as a plain dot, and it is invisible until you stand in front of the thing.
    ///
    /// The panel moves by its title and resizes by the corner grip, and both are remembered.
    /// </summary>
    internal static class LegendPanel
    {
        private const float MinW = 380f, MaxW = 1600f;
        private const float MinH = 220f, MaxH = 1400f;
        private const float RowHeight = 26f;
        private const float HeaderHeight = 24f;
        private const float IconSize = 22f;
        private const float DetailWidth = 330f;
        private const float ColumnMin = 470f;   // a name and its detail, below which a column is unreadable
        private const float ColumnGap = 14f;
        private const int MaxColumns = 3;
        private const float Pad = 10f;
        private const float TitleHeight = 28f;
        private const float HintHeight = 16f;
        private const float GripSize = 18f;

        private static GameObject _root;
        private static RectTransform _rect, _viewport, _content, _grip, _mover;
        private static TextMeshProUGUI _title, _hint;
        private static PauseToggle _pause;
        private static float _w = 560f, _h = 620f;
        private static float _contentHeight;
        private static float _scroll;
        private static float _wheel;
        private static Catalog.LegendRow _picked;
        private static TMP_InputField _field;
        private static TextMeshProUGUI _editLabel;
        private const float EditHeight = 24f;
        private static readonly List<KeyValuePair<Catalog.LegendRow, Image>> _rowBacks =
            new List<KeyValuePair<Catalog.LegendRow, Image>>();
        private static bool _rebuildWanted;
        private static int _builtColumns = 1;

        /// <summary>True while the icon box has the keyboard, so our own keys stand down.</summary>
        internal static bool Typing => _field != null && _field.isFocused;

        internal static bool IsOpen => _root != null;

        internal static void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        internal static void Close()
        {
            _pause?.Destroy();
            _pause = null;
            if (_root != null) Object.Destroy(_root);
            _root = null;
            _rect = _viewport = _content = _grip = _mover = null;
            _title = _hint = null;
            _scroll = 0f;
        }

        internal static void Open()
        {
            Close();
            var parent = Hud.instance != null ? Hud.instance.transform as RectTransform : null;
            if (parent == null) { TheGreatestMapMod.Message("No HUD to draw the legend on."); return; }

            var rows = Catalog.Legend();
            LoadPlacement();

            var panel = MenuKit.Panel(parent, "TGM_Legend");
            _root = panel.gameObject;
            _rect = panel.rectTransform;
            _rect.anchorMin = _rect.anchorMax = _rect.pivot = new Vector2(0.5f, 0.5f);
            var saved = TgmConfig.ParseVector(TgmConfig.LegendPosition != null ? TgmConfig.LegendPosition.Value : null, Vector3.zero);
            _rect.anchoredPosition = new Vector2(saved.x, saved.y);

            int missing = 0, fallback = 0, absent = 0;
            foreach (var r in rows)
            {
                if (r.Missing) missing++;
                if (r.NoPrefab) absent++;
                if (r.Source == "the kind's fallback") fallback++;
            }
            _title = MenuKit.Text(_root.transform, "Title",
                $"Map legend — {rows.Count} entries, {fallback} on a fallback icon, {missing} bad icon, {absent} not in this game", 16f, MenuKit.Header);
            _title.alignment = TextAlignmentOptions.MidlineLeft;

            // Anything outside the viewport is clipped, which is what allows scrolling without a
            // full ScrollRect and its scrollbars.
            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewport.transform.SetParent(_root.transform, false);
            _viewport = (RectTransform)viewport.transform;

            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);
            _content = (RectTransform)content.transform;
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0f, 1f);

            _rowBacks.Clear();
            // Wide enough for two or three readable columns? Then use them: this list is long and
            // mostly read by scanning, which a tall thin column makes needlessly slow.
            float inner = _w - 2f * Pad;
            int columns = ColumnsFor(_w);
            _builtColumns = columns;
            float columnWidth = (inner - ColumnGap * (columns - 1)) / columns;

            float total = 0f;
            Category? counting = null;
            foreach (var row in rows)
            {
                if (counting != row.Cat) { counting = row.Cat; total += HeaderHeight; }
                total += RowHeight;
            }
            float target = total / columns;

            float y = 0f, tallest = 0f;
            int column = 0;
            Category? group = null;
            foreach (var row in rows)
            {
                // A new column starts at a row rather than mid-header, and the kind's name is
                // written again at the top of it so a column never opens with unlabelled rows.
                if (column < columns - 1 && y >= target)
                {
                    tallest = Mathf.Max(tallest, y);
                    column++;
                    y = 0f;
                    group = null;
                }
                if (group != row.Cat)
                {
                    group = row.Cat;
                    var header = MenuKit.Text(_content, "Header", Categories.Label(row.Cat), 14f, MenuKit.Header);
                    Stretch(header.rectTransform, ColumnX(column, columnWidth) + 2f, -y, -(inner - columnWidth) - 4f, HeaderHeight);
                    header.alignment = TextAlignmentOptions.MidlineLeft;
                    y += HeaderHeight;
                }
                AddRow(row, ColumnX(column, columnWidth), y, columnWidth, inner);
                y += RowHeight;
            }
            _contentHeight = Mathf.Max(tallest, y);
            _content.sizeDelta = new Vector2(0f, _contentHeight);

            BuildEditor();

            _hint = MenuKit.Text(_root.transform, "Hint", "wheel to scroll · drag the title to move · drag the corner to resize · widen for more columns · Esc to close", 12f, MenuKit.Dim);
            _hint.alignment = TextAlignmentOptions.MidlineLeft;

            // The title strip drags the panel; the corner grip resizes it.
            _mover = Handle("Mover", new Color(0f, 0f, 0f, 0f), null, OnMoveDrag);
            _grip = Handle("Grip", new Color(1f, 0.85f, 0.45f, 0.75f), MenuKit.Grip(), OnGripDrag);
            // The columns are laid out once, so a resize that changes their number or width has to
            // lay them out again; doing it when the grip is let go keeps the drag itself smooth.
            _grip.GetComponent<MenuKit.DragHandle>().OnEnd = () => { SavePlacement(); _rebuildWanted = true; };

            _pause = PauseToggle.Build(_rect, new Vector2(-10f, -8f));
            Layout();
        }

        /// <summary>
        /// A box at the foot of the panel holding the icon of whichever row was last clicked.
        /// Change it and the catalog changes, which rebuilds the list in front of you -- the whole
        /// point, since an icon key can only be judged by looking at the picture it gives.
        /// </summary>
        private static void BuildEditor()
        {
            _editLabel = MenuKit.Text(_root.transform, "EditLabel", "Click a row to change its icon.", 12f, MenuKit.Dim);
            _editLabel.alignment = TextAlignmentOptions.MidlineLeft;

            var go = new GameObject("IconField", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(TMP_InputField));
            go.transform.SetParent(_root.transform, false);
            var back = go.GetComponent<Image>();
            back.color = new Color(1f, 1f, 1f, 0.14f);

            var text = MenuKit.Text(go.transform, "Text", "", 13f, MenuKit.Body);
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.enableWordWrapping = false;
            Stretch(text.rectTransform, 6f, 0f, -12f, EditHeight);

            _field = go.GetComponent<TMP_InputField>();
            _field.textViewport = (RectTransform)go.transform;
            _field.textComponent = text;
            _field.lineType = TMP_InputField.LineType.SingleLine;
            _field.restoreOriginalTextOnEscape = true;
            // TextMeshPro draws its caret in its own dark default and ignores caretColor until it
            // is told to use a custom one, which on a dark panel leaves you typing blind.
            _field.customCaretColor = true;
            _field.caretColor = new Color(1f, 0.85f, 0.45f, 1f);
            _field.caretWidth = 2;
            _field.caretBlinkRate = 0.8f;
            _field.selectionColor = new Color(1f, 0.85f, 0.45f, 0.45f);
            _field.interactable = false;   // nothing picked yet
            _field.onSubmit.AddListener(Apply);
        }

        private static void Pick(Catalog.LegendRow row)
        {
            _picked = row;
            if (_field == null) return;
            _field.interactable = true;
            _field.SetTextWithoutNotify(IconRegistry.Short(row.Icon));
            _editLabel.text = $"{row.Prefab} ({Categories.Label(row.Cat)}) — icon:";
            _editLabel.color = MenuKit.Body;
            _field.ActivateInputField();
            Highlight();
        }

        private static void Highlight()
        {
            foreach (var kv in _rowBacks)
            {
                if (kv.Value == null) continue;
                bool here = _picked != null && kv.Key.Prefab == _picked.Prefab && kv.Key.Cat == _picked.Cat;
                kv.Value.color = here ? new Color(1f, 0.85f, 0.45f, 0.14f) : new Color(0f, 0f, 0f, 0f);
            }
        }

        /// <summary>
        /// Put the typed icon into the player's own catalog line. The setting's own change hook
        /// throws the catalog away and builds it again, so reopening the list here shows the new
        /// picture -- or shows it in red, which is the answer to "is that the right name?".
        /// </summary>
        private static void Apply(string typed)
        {
            if (_picked == null) return;
            var row = _picked;
            if (!Catalog.SetIcon(row.Cat, row.Prefab, row.Name, typed))
            {
                TheGreatestMapMod.Message($"{Categories.Label(row.Cat)} has no catalog line to write to.");
                return;
            }
            ClientPins.Restyle();
            _rebuildWanted = true;
        }

        /// <summary>
        /// Draw the list again, keeping the panel where it is and the scroll where it was. Done
        /// between frames rather than from the icon box's own callback, which would destroy the box
        /// while it was still handling the keystroke that asked for this.
        /// </summary>
        private static void Rebuild()
        {
            if (_root == null) return;
            var picked = _picked;
            float scroll = _scroll;
            float w = _w, h = _h;
            var where = _rect != null ? _rect.anchoredPosition : Vector2.zero;
            Open();                       // which loads the saved placement, so put ours back
            _w = w;
            _h = h;
            if (_rect != null) _rect.anchoredPosition = where;
            _picked = picked;
            _scroll = scroll;
            if (picked != null && _field != null)
            {
                _field.interactable = true;
                _field.SetTextWithoutNotify(IconRegistry.Short(FreshIcon(picked)));
                _editLabel.text = $"{picked.Prefab} ({Categories.Label(picked.Cat)}) — icon:";
                _editLabel.color = MenuKit.Body;
            }
            Highlight();
            Layout();
        }

        /// <summary>The icon that row resolves to now, after the catalog was changed under it.</summary>
        private static string FreshIcon(Catalog.LegendRow picked)
        {
            foreach (var row in Catalog.Legend())
                if (row.Prefab == picked.Prefab && row.Cat == picked.Cat) return row.Icon;
            return picked.Icon;
        }

        private static RectTransform Handle(string name, Color color, Sprite sprite, System.Action<Vector2> onDrag)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(MenuKit.DragHandle));
            go.transform.SetParent(_root.transform, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            if (sprite != null) img.sprite = sprite;
            img.raycastTarget = true;
            var handle = go.GetComponent<MenuKit.DragHandle>();
            handle.OnDrag = onDrag;
            handle.OnEnd = SavePlacement;
            return (RectTransform)go.transform;
        }

        /// <summary>Everything is positioned from the current size, so resizing is one call.</summary>
        private static void Layout()
        {
            if (_root == null) return;
            _rect.sizeDelta = new Vector2(_w, _h);
            Stretch(_title.rectTransform, Pad, -Pad, -2f * Pad - 34f, TitleHeight);
            Stretch(_hint.rectTransform, Pad, -(_h - Pad - HintHeight), -2f * Pad - GripSize, HintHeight);

            float editY = _h - Pad - HintHeight - EditHeight - 4f;
            Stretch(_editLabel.rectTransform, Pad, -editY, -(_h * 0f) - 2f * Pad - 240f, EditHeight);
            Stretch((RectTransform)_field.transform, _w - Pad - 240f, -editY, -(_w - 240f), EditHeight);

            float viewportHeight = Mathf.Max(0f, _h - TitleHeight - HintHeight - EditHeight - 4f * Pad);
            Stretch(_viewport, Pad, -(Pad + TitleHeight + Pad * 0.5f), -2f * Pad, viewportHeight);

            // The title strip catches drags, but not the pause button sitting in its corner.
            Stretch(_mover, 0f, 0f, -40f, TitleHeight + Pad);

            _grip.anchorMin = _grip.anchorMax = new Vector2(1f, 0f);
            _grip.pivot = new Vector2(1f, 0f);
            _grip.anchoredPosition = new Vector2(-3f, 3f);
            _grip.sizeDelta = new Vector2(GripSize, GripSize);

            ClampScroll(viewportHeight);
        }

        private static void ClampScroll(float viewportHeight)
        {
            float max = Mathf.Max(0f, _contentHeight - viewportHeight);
            _scroll = Mathf.Clamp(_scroll, 0f, max);
            if (_content != null) _content.anchoredPosition = new Vector2(0f, _scroll);
        }

        private static int ColumnsFor(float width)
        {
            float inner = width - 2f * Pad;
            return Mathf.Clamp(Mathf.FloorToInt((inner + ColumnGap) / (ColumnMin + ColumnGap)), 1, MaxColumns);
        }

        private static float ColumnX(int column, float columnWidth) => column * (columnWidth + ColumnGap);

        private static void AddRow(Catalog.LegendRow row, float x, float y, float columnWidth, float inner)
        {
            var go = new GameObject("Row", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            go.transform.SetParent(_content, false);
            Stretch((RectTransform)go.transform, x, -y, -(inner - columnWidth), RowHeight);
            var back = go.GetComponent<Image>();
            bool here = _picked != null && _picked.Prefab == row.Prefab && _picked.Cat == row.Cat;
            back.color = here ? new Color(1f, 0.85f, 0.45f, 0.14f) : new Color(0f, 0f, 0f, 0f);
            back.raycastTarget = true;
            var button = go.GetComponent<Button>();
            button.transition = Selectable.Transition.None;
            var chosen = row;
            button.onClick.AddListener(() => Pick(chosen));
            _rowBacks.Add(new KeyValuePair<Catalog.LegendRow, Image>(row, back));

            var sprite = IconRegistry.Resolve(Minimap.instance, row.Icon);
            if (sprite != null)
            {
                var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                iconGo.transform.SetParent(go.transform, false);
                var irt = (RectTransform)iconGo.transform;
                irt.anchorMin = irt.anchorMax = new Vector2(0f, 0.5f);
                irt.pivot = new Vector2(0f, 0.5f);
                irt.anchoredPosition = new Vector2(6f, 0f);
                irt.sizeDelta = new Vector2(IconSize, IconSize);
                var img = iconGo.GetComponent<Image>();
                img.sprite = sprite;
                img.preserveAspect = true;
                img.raycastTarget = false;
            }

            float detailWidth = Mathf.Min(DetailWidth, Mathf.Max(140f, columnWidth * 0.55f));
            var color = row.Missing ? new Color(1f, 0.45f, 0.4f) : row.NoPrefab ? MenuKit.Dim : MenuKit.Body;
            var name = MenuKit.Text(go.transform, "Name", row.Name, 14f, color);
            Stretch(name.rectTransform, IconSize + 14f, 0f, -(IconSize + 14f) - detailWidth, RowHeight);
            name.alignment = TextAlignmentOptions.MidlineLeft;

            string key = IconRegistry.Short(row.Icon);
            string detail = row.Missing ? $"{row.Prefab} — '{key}' is not in this game"
                : row.NoPrefab ? $"{row.Prefab} — nothing by that name exists in this game"
                : $"{row.Prefab} — {key} ({row.Source})";
            var info = MenuKit.Text(go.transform, "Detail", detail, 12f, row.Missing ? new Color(1f, 0.45f, 0.4f) : MenuKit.Dim);
            var irt2 = info.rectTransform;
            irt2.anchorMin = irt2.anchorMax = new Vector2(1f, 0.5f);
            irt2.pivot = new Vector2(1f, 0.5f);
            irt2.anchoredPosition = new Vector2(-8f, 0f);
            irt2.sizeDelta = new Vector2(detailWidth, RowHeight);
            info.alignment = TextAlignmentOptions.MidlineRight;
        }

        private static void Stretch(RectTransform rt, float x, float y, float widthDelta, float height)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(widthDelta, height);
        }

        // ── moving and resizing ─────────────────────────────────────────────────────

        private static float Scale()
        {
            var canvas = _root != null ? _root.GetComponentInParent<Canvas>() : null;
            float scale = canvas != null ? canvas.rootCanvas.scaleFactor : 1f;
            return scale > 0f ? scale : 1f;
        }

        private static void OnMoveDrag(Vector2 screenDelta)
        {
            if (_rect == null) return;
            _rect.anchoredPosition += screenDelta / Scale();
            ClampToScreen();
        }

        /// <summary>The grip pulls the bottom-right corner, so the opposite corner stays put.</summary>
        private static void OnGripDrag(Vector2 screenDelta)
        {
            if (_rect == null) return;
            float scale = Scale();
            float nw = Mathf.Clamp(_w + screenDelta.x / scale, MinW, MaxW);
            float nh = Mathf.Clamp(_h - screenDelta.y / scale, MinH, MaxH);
            _rect.anchoredPosition += new Vector2((nw - _w) / 2f, -(nh - _h) / 2f);
            _w = nw;
            _h = nh;
            Layout();
        }

        /// <summary>Keep the middle on screen, so a panel dragged too far can always be dragged back.</summary>
        private static void ClampToScreen()
        {
            var parent = _rect.parent as RectTransform;
            if (parent == null) return;
            var half = parent.rect.size * 0.5f;
            var p = _rect.anchoredPosition;
            _rect.anchoredPosition = new Vector2(Mathf.Clamp(p.x, -half.x, half.x), Mathf.Clamp(p.y, -half.y, half.y));
        }

        private static void LoadPlacement()
        {
            var size = TgmConfig.ParseVector(TgmConfig.LegendSize != null ? TgmConfig.LegendSize.Value : null, new Vector3(560f, 620f, 0f));
            _w = Mathf.Clamp(size.x, MinW, MaxW);
            _h = Mathf.Clamp(size.y, MinH, MaxH);
        }

        private static void SavePlacement()
        {
            if (_rect == null) return;
            if (TgmConfig.LegendSize != null) TgmConfig.LegendSize.Value = $"{_w:0},{_h:0}";
            if (TgmConfig.LegendPosition != null)
                TgmConfig.LegendPosition.Value = $"{_rect.anchoredPosition.x:0},{_rect.anchoredPosition.y:0}";
        }

        // ── input ───────────────────────────────────────────────────────────────────

        /// <summary>The wheel value the camera would have used, captured before it was zeroed.</summary>
        internal static void NoteWheel(float value)
        {
            if (value != 0f) _wheel = value;
        }

        internal static void Update()
        {
            if (_root == null) return;
            if (_rebuildWanted) { _rebuildWanted = false; Rebuild(); return; }
            // Escape leaves the icon box first, as it does in every other text field, so a typo
            // can be abandoned without the whole panel going with it.
            if (ZInput.GetKeyDown(KeyCode.Escape) && !Typing) { Close(); return; }
            _pause?.Paint();
            float wheel = _wheel;
            _wheel = 0f;
            if (Mathf.Abs(wheel) > 0.001f && _content != null)
            {
                _scroll -= wheel * 120f;
                ClampScroll(_viewport != null ? _viewport.rect.height : 0f);
            }
        }
    }

    // The wheel belongs to the legend while it is open: the camera reads it straight from ZInput
    // and would otherwise zoom the player's view behind the panel. The real value is kept here
    // and handed to the list, then reported as zero so nothing else acts on it.
    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetMouseScrollWheel))]
    internal static class ZInput_GetMouseScrollWheel_Legend_Patch
    {
        private static void Postfix(ref float __result)
        {
            if (__result == 0f || !LegendPanel.IsOpen) return;
            LegendPanel.NoteWheel(__result);
            __result = 0f;
        }
    }

    /// <summary>
    /// Valheim frees the mouse only for its own screens, checked in GameCamera.UpdateMouseCapture,
    /// and "a text input is up" is one of those cases. Reporting one while the legend is open is
    /// what Jotunn's BlockInput does under the hood, and it hands over the pointer and stops the
    /// click reaching the player as an attack, without this mod taking a dependency on Jotunn.
    /// </summary>
    [HarmonyPatch(typeof(TextInput), nameof(TextInput.IsVisible))]
    internal static class TextInput_IsVisible_Legend_Patch
    {
        private static void Postfix(ref bool __result)
        {
            if (LegendPanel.IsOpen) __result = true;
        }
    }
}
