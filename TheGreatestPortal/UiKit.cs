using System;
using System.Collections.Generic;
using System.Text;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TheGreatestPortal
{
    /// <summary>Keybind polling through Valheim's own ZInput.</summary>
    internal static class Keys
    {
        internal static bool IsDown(KeyboardShortcut shortcut)
        {
            if (shortcut.MainKey == KeyCode.None) return false;
            if (!ZInput.GetKeyDown(shortcut.MainKey)) return false;
            foreach (var modifier in shortcut.Modifiers)
                if (!ZInput.GetKey(modifier)) return false;
            return true;
        }

        /// <summary>No text box, console or chat has the keyboard.</summary>
        internal static bool CanTakeInput()
        {
            if (Console.IsVisible() || TextInput.IsVisible() || Menu.IsActive()) return false;
            if (Chat.instance != null && Chat.instance.HasFocus()) return false;
            if (Minimap.instance != null && Minimap.InTextInput()) return false;
            return true;
        }
    }

    /// <summary>
    /// Small runtime UI toolkit: TextMeshPro labels in the game's font, list rows with hover
    /// and right-click, section headers, scroll lists with a scrollbar and wheel scrolling,
    /// plain toggles and buttons, and clones of the game's own buttons and text fields. No Jotunn.
    /// </summary>
    internal static class UiKit
    {
        internal const float RowHeight = 28f;
        internal const float RowSpacing = 2f;
        internal const float ListPadding = 3f;
        internal static readonly Color RowColor = new Color(1f, 1f, 1f, 0.07f);
        internal static readonly Color RowHover = new Color(1f, 1f, 1f, 0.22f);
        internal static readonly Color RowSelected = new Color(1f, 0.78f, 0.3f, 0.35f);
        internal static readonly Color RowSelectedHover = new Color(1f, 0.78f, 0.3f, 0.5f);
        internal static readonly Color Gold = new Color(1f, 0.85f, 0.45f);
        internal static readonly Color Header = new Color(1f, 0.72f, 0.32f);
        internal static readonly Color Dim = new Color(0.78f, 0.75f, 0.7f);
        internal static readonly Color Body = new Color(0.95f, 0.92f, 0.85f);

        private static TMP_FontAsset _font;
        private static Material _fontMaterial;

        internal static void EnsureFont()
        {
            if (_font != null) return;
            TMP_Text source = null;
            if (TextInput.instance != null && TextInput.instance.m_topic != null) source = TextInput.instance.m_topic;
            else if (MessageHud.instance != null) source = MessageHud.instance.m_messageCenterText;
            if (source != null && source.font != null)
            {
                _font = source.font;
                _fontMaterial = source.fontSharedMaterial;
                return;
            }
            // No HUD text to borrow from: take the game's own serif UI font, or any font asset at all.
            TMP_FontAsset any = null;
            foreach (var f in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
            {
                if (f == null) continue;
                if (any == null) any = f;
                if (f.name.IndexOf("Averia", StringComparison.OrdinalIgnoreCase) >= 0) { any = f; break; }
            }
            if (any == null) return;
            _font = any;
            _fontMaterial = any.material;
        }

        // ── keyboard focus ──────────────────────────────────────────────────────────

        private static readonly List<TMP_InputField> _fields = new List<TMP_InputField>();
        private static int _focusedFrame = -1000;

        internal static void Track(TMP_InputField field)
        {
            if (field != null && !_fields.Contains(field)) _fields.Add(field);
        }

        /// <summary>
        /// True while one of this mod's text fields has the keyboard, and for two frames after
        /// it lets go, so the key that blurred it (Escape, Enter) is not also read by the game.
        /// </summary>
        internal static bool TextFocused()
        {
            for (int i = _fields.Count - 1; i >= 0; i--)
            {
                var f = _fields[i];
                if (f == null) { _fields.RemoveAt(i); continue; }
                if (f.isFocused && f.gameObject.activeInHierarchy)
                {
                    _focusedFrame = Time.frameCount;
                    return true;
                }
            }
            return Time.frameCount - _focusedFrame <= 2;
        }

        internal static bool IsFocused(TMP_InputField field) => field != null && field.isFocused;

        // ── layout ──────────────────────────────────────────────────────────────────

        /// <summary>Top-left anchored placement inside the parent: x to the right, y downwards.</summary>
        internal static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
        }

        internal static void Stretch(RectTransform rt, float pad = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(pad, pad);
            rt.offsetMax = new Vector2(-pad, -pad);
        }

        // ── elements ────────────────────────────────────────────────────────────────

        internal static TextMeshProUGUI Text(Transform parent, string name, string text, float size, TextAlignmentOptions align, Color? color = null)
        {
            EnsureFont();
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            // Inactive while the component is added: TextMeshPro's Awake looks up Unity's default
            // font (which the game does not ship, so it warns) unless a font is already assigned.
            go.SetActive(false);
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            if (_font != null) t.font = _font;
            if (_fontMaterial != null) t.fontSharedMaterial = _fontMaterial;
            t.text = text ?? "";
            t.fontSize = size;
            t.alignment = align;
            t.color = color ?? Body;
            t.raycastTarget = false;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            go.SetActive(true);
            return t;
        }

        internal static Image Panel(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = true;
            return img;
        }

        // ── scroll lists ────────────────────────────────────────────────────────────

        /// <summary>
        /// Mouse-wheel scrolling by whole rows, a configurable number per notch, whatever size
        /// of delta the input module reports. The ScrollRect's own wheel handling is switched off.
        /// </summary>
        internal sealed class WheelScroller : MonoBehaviour, IScrollHandler
        {
            public ScrollRect Scroll;

            public void OnScroll(PointerEventData eventData)
            {
                if (Scroll == null || Scroll.content == null || Scroll.viewport == null) return;
                float dir = Mathf.Sign(eventData.scrollDelta.y);
                if (dir == 0f) return;
                float range = Scroll.content.rect.height - Scroll.viewport.rect.height;
                if (range <= 0f) return;
                float rows = TgpConfig.ListScrollRows != null ? TgpConfig.ListScrollRows.Value : 4;
                float step = rows * (RowHeight + RowSpacing) / range;
                Scroll.verticalNormalizedPosition = Mathf.Clamp01(Scroll.verticalNormalizedPosition + dir * step);
            }
        }

        /// <summary>A vertical scroll list with a scrollbar that appears when needed. Returns the content transform rows are added to.</summary>
        internal static RectTransform ScrollList(Transform parent, string name, out ScrollRect scroll)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(ScrollRect), typeof(WheelScroller));
            go.transform.SetParent(parent, false);
            var bg = go.GetComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.35f);
            bg.raycastTarget = true;
            scroll = go.GetComponent<ScrollRect>();
            go.GetComponent<WheelScroller>().Scroll = scroll;

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewport.transform.SetParent(go.transform, false);
            var vrt = viewport.GetComponent<RectTransform>();
            Stretch(vrt, ListPadding);

            var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content.transform.SetParent(viewport.transform, false);
            var crt = content.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0f, 1f);
            crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(0f, 1f);
            crt.anchoredPosition = Vector2.zero;
            crt.sizeDelta = Vector2.zero;
            var vlg = content.GetComponent<VerticalLayoutGroup>();
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.spacing = RowSpacing;
            int pad = (int)ListPadding;
            vlg.padding = new RectOffset(pad, pad, pad, pad);
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // scrollbar on the right, shown only when the content overflows
            var sbGo = new GameObject("Scrollbar", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Scrollbar));
            sbGo.transform.SetParent(go.transform, false);
            var sbRt = sbGo.GetComponent<RectTransform>();
            sbRt.anchorMin = new Vector2(1f, 0f);
            sbRt.anchorMax = new Vector2(1f, 1f);
            sbRt.pivot = new Vector2(1f, 1f);
            sbRt.anchoredPosition = new Vector2(-3f, -3f);
            sbRt.sizeDelta = new Vector2(10f, -6f);
            var sbBg = sbGo.GetComponent<Image>();
            sbBg.color = new Color(0f, 0f, 0f, 0.4f);
            var area = new GameObject("Sliding Area", typeof(RectTransform));
            area.transform.SetParent(sbGo.transform, false);
            Stretch(area.GetComponent<RectTransform>(), 1f);
            var handle = new GameObject("Handle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            handle.transform.SetParent(area.transform, false);
            Stretch(handle.GetComponent<RectTransform>());
            var handleImg = handle.GetComponent<Image>();
            handleImg.color = new Color(1f, 0.8f, 0.45f, 0.55f);
            var sb = sbGo.GetComponent<Scrollbar>();
            sb.handleRect = handle.GetComponent<RectTransform>();
            sb.targetGraphic = handleImg;
            sb.direction = Scrollbar.Direction.BottomToTop;
            var colors = sb.colors;
            colors.highlightedColor = new Color(1.2f, 1.2f, 1.2f, 1f);
            colors.pressedColor = new Color(1.4f, 1.4f, 1.4f, 1f);
            colors.colorMultiplier = 1.5f;
            sb.colors = colors;

            scroll.content = crt;
            scroll.viewport = vrt;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 0f;     // WheelScroller does the wheel
            scroll.inertia = false;
            scroll.verticalScrollbar = sb;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
            scroll.verticalScrollbarSpacing = 2f;
            return crt;
        }

        /// <summary>Scrolls so that row <paramref name="index"/> of <paramref name="count"/> equal-height rows is in view.</summary>
        internal static void ScrollToRow(ScrollRect scroll, int index, int count)
        {
            if (scroll == null || scroll.viewport == null || index < 0 || count <= 0) return;
            float contentH = 2f * ListPadding + count * RowHeight + Mathf.Max(0, count - 1) * RowSpacing;
            float viewH = scroll.viewport.rect.height;
            float range = contentH - viewH;
            if (range <= 0f || viewH <= 0f) return;
            float top = (1f - scroll.verticalNormalizedPosition) * range;
            float rowTop = ListPadding + index * (RowHeight + RowSpacing);
            float rowBottom = rowTop + RowHeight;
            if (rowTop < top) top = rowTop;
            else if (rowBottom > top + viewH) top = rowBottom - viewH;
            else return;
            scroll.verticalNormalizedPosition = Mathf.Clamp01(1f - top / range);
        }

        internal static void ClearChildren(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(t.GetChild(i).gameObject);
        }

        /// <summary>Pointer tracking plus right-click for a row or a panel.</summary>
        internal sealed class Hover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
        {
            public bool Over;
            public Action OnRightClick;
            public Action<bool> OnHoverChanged;

            public void OnPointerEnter(PointerEventData eventData) { Over = true; OnHoverChanged?.Invoke(true); }
            public void OnPointerExit(PointerEventData eventData) { Over = false; OnHoverChanged?.Invoke(false); }
            public void OnPointerClick(PointerEventData eventData)
            {
                if (eventData.button == PointerEventData.InputButton.Right) OnRightClick?.Invoke();
            }
            private void OnDisable() { if (Over) { Over = false; OnHoverChanged?.Invoke(false); } }
        }

        internal sealed class RowHandle
        {
            public GameObject Root;
            public Image Background;
            public TextMeshProUGUI Label;
            public TextMeshProUGUI Right;
            public Hover Hover;
            public bool Selected;

            public void SetSelected(bool on) { Selected = on; Refresh(); }
            public void Refresh()
            {
                if (Background == null) return;
                bool over = Hover != null && Hover.Over;
                Background.color = Selected ? (over ? RowSelectedHover : RowSelected) : (over ? RowHover : RowColor);
            }
        }

        /// <summary>Seconds between two clicks on a row for them to count as a double-click.</summary>
        internal const float DoubleClickSeconds = 0.35f;

        internal static RowHandle Row(Transform content, string label, string right, Action onClick, Action onRightClick, float fontSize = 17f, Color? labelColor = null, Action onDoubleClick = null)
        {
            var go = new GameObject("Row", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(LayoutElement), typeof(Hover));
            go.transform.SetParent(content, false);
            var img = go.GetComponent<Image>();
            img.color = RowColor;
            img.raycastTarget = true;
            var le = go.GetComponent<LayoutElement>();
            le.preferredHeight = RowHeight;
            le.minHeight = RowHeight;
            var btn = go.GetComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.targetGraphic = img;
            var handle = new RowHandle { Root = go, Background = img, Hover = go.GetComponent<Hover>() };
            handle.Hover.OnHoverChanged = _ => handle.Refresh();
            handle.Hover.OnRightClick = onRightClick;
            if (onClick != null || onDoubleClick != null)
            {
                float lastClick = -10f;
                btn.onClick.AddListener(() =>
                {
                    onClick?.Invoke();
                    if (onDoubleClick == null) return;
                    float now = Time.unscaledTime;
                    if (now - lastClick <= DoubleClickSeconds)
                    {
                        lastClick = -10f;
                        onDoubleClick();
                    }
                    else lastClick = now;
                });
            }

            bool hasRight = !string.IsNullOrEmpty(right);
            var lbl = Text(go.transform, "Label", label, fontSize, TextAlignmentOptions.Left, labelColor);
            var lrt = lbl.rectTransform;
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(10f, 0f);
            lrt.offsetMax = new Vector2(hasRight ? -96f : -10f, 0f);
            handle.Label = lbl;
            if (hasRight)
            {
                var r = Text(go.transform, "Right", right, fontSize - 2f, TextAlignmentOptions.Right, Dim);
                var rrt = r.rectTransform;
                rrt.anchorMin = new Vector2(1f, 0f);
                rrt.anchorMax = new Vector2(1f, 1f);
                rrt.pivot = new Vector2(1f, 0.5f);
                rrt.anchoredPosition = new Vector2(-10f, 0f);
                rrt.sizeDelta = new Vector2(84f, 0f);
                handle.Right = r;
            }
            handle.Refresh();
            return handle;
        }

        /// <summary>A section title in a list: same height as a row. With <paramref name="onClick"/> it folds its group.</summary>
        internal static RowHandle SectionHeader(Transform content, string title, Action onClick = null)
        {
            var go = new GameObject("Header", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(LayoutElement), typeof(Hover));
            go.transform.SetParent(content, false);
            var img = go.GetComponent<Image>();
            var normal = new Color(1f, 0.72f, 0.32f, 0.10f);
            var hover = new Color(1f, 0.72f, 0.32f, 0.24f);
            img.color = normal;
            img.raycastTarget = true;
            var le = go.GetComponent<LayoutElement>();
            le.preferredHeight = RowHeight;
            le.minHeight = RowHeight;
            var lbl = Text(go.transform, "Label", title, 16f, TextAlignmentOptions.Left, Header);
            lbl.fontStyle = FontStyles.Bold;
            var lrt = lbl.rectTransform;
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(8f, 0f);
            lrt.offsetMax = new Vector2(-8f, 0f);
            if (onClick != null)
            {
                var btn = go.AddComponent<Button>();
                btn.transition = Selectable.Transition.None;
                btn.targetGraphic = img;
                btn.onClick.AddListener(() => onClick());
                go.GetComponent<Hover>().OnHoverChanged = over => img.color = over ? hover : normal;
            }
            return new RowHandle { Root = go, Background = null, Label = lbl };
        }

        // ── context menu ────────────────────────────────────────────────────────────

        /// <summary>
        /// One right-click menu shared by every row: a short list of labelled actions that pops up
        /// at the pointer and goes away on a choice, on Escape, or on a click anywhere else.
        /// </summary>
        internal static class ContextMenu
        {
            private const float Width = 190f;
            private static GameObject _root;
            private static RectTransform _rt;
            private static Hover _hover;
            private static int _openedFrame = -10;

            internal static bool IsOpen => _root != null && _root.activeSelf;

            internal static void Show(RectTransform parent, Vector3 screenPos, List<KeyValuePair<string, Action>> items)
            {
                if (parent == null || items == null || items.Count == 0) return;
                if (_root == null)
                {
                    _root = new GameObject("TGP_ContextMenu", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Hover));
                    var bg = _root.GetComponent<Image>();
                    bg.color = new Color(0.06f, 0.045f, 0.03f, 0.97f);
                    bg.raycastTarget = true;
                    var outline = _root.AddComponent<Outline>();
                    outline.effectColor = new Color(0.75f, 0.62f, 0.4f, 0.9f);
                    outline.effectDistance = new Vector2(1f, -1f);
                    _rt = _root.GetComponent<RectTransform>();
                    _hover = _root.GetComponent<Hover>();
                }
                _root.transform.SetParent(parent, false);
                _root.transform.SetAsLastSibling();
                ClearChildren(_root.transform);

                float y = 3f;
                foreach (var item in items)
                {
                    var action = item.Value;
                    var row = Row(_root.transform, item.Key, null, () => { Close(); action?.Invoke(); }, null, 15f);
                    Place(row.Root.GetComponent<RectTransform>(), 3f, y, Width - 6f, RowHeight);
                    y += RowHeight + RowSpacing;
                }
                float height = y + 1f;

                // Anchor the top-left corner at the pointer, kept inside the parent.
                var canvas = parent.GetComponentInParent<Canvas>();
                var cam = canvas != null && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.rootCanvas.worldCamera : null;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screenPos, cam, out Vector2 local);
                var pr = parent.rect;
                float x = local.x - pr.xMin;          // distance from the parent's left edge
                float top = pr.yMax - local.y;        // distance from the parent's top edge
                if (x + Width > pr.width) x = Mathf.Max(0f, pr.width - Width);
                if (top + height > pr.height) top = Mathf.Max(0f, pr.height - height);
                Place(_rt, x, top, Width, height);

                _root.SetActive(true);
                _openedFrame = Time.frameCount;
            }

            internal static void Close()
            {
                if (_root != null && _root.activeSelf) _root.SetActive(false);
            }

            /// <summary>Call every frame while open: Escape or a click off the menu closes it.</summary>
            internal static void Update()
            {
                if (!IsOpen) return;
                if (ZInput.GetKeyDown(KeyCode.Escape)) { Close(); return; }
                if (Time.frameCount - _openedFrame <= 1) return;     // the click that opened it
                if ((ZInput.GetMouseButtonDown(0) || ZInput.GetMouseButtonDown(1)) && !(_hover != null && _hover.Over)) Close();
            }
        }

        /// <summary>A small text button for secondary actions such as "Expand all".</summary>
        internal static Button LinkButton(Transform parent, string name, string label, Action onClick)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(Hover));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            var normal = new Color(1f, 1f, 1f, 0.06f);
            var hover = new Color(1f, 1f, 1f, 0.2f);
            img.color = normal;
            img.raycastTarget = true;
            var btn = go.GetComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.targetGraphic = img;
            if (onClick != null) btn.onClick.AddListener(() => onClick());
            var text = Text(go.transform, "Label", label, 13f, TextAlignmentOptions.Center, Gold);
            Stretch(text.rectTransform, 2f);
            go.GetComponent<Hover>().OnHoverChanged = over => img.color = over ? hover : normal;
            return btn;
        }

        /// <summary>A checkbox drawn from scratch: dark box, gold tick, label to the right.</summary>
        internal static Toggle SimpleToggle(Transform parent, string name, string label, bool value, Action<bool> onChanged)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Toggle));
            go.transform.SetParent(parent, false);
            var toggle = go.GetComponent<Toggle>();

            var box = Panel(go.transform, "Box", new Color(0.08f, 0.06f, 0.04f, 0.9f));
            var brt = box.rectTransform;
            brt.anchorMin = new Vector2(0f, 0.5f);
            brt.anchorMax = new Vector2(0f, 0.5f);
            brt.pivot = new Vector2(0f, 0.5f);
            brt.anchoredPosition = Vector2.zero;
            brt.sizeDelta = new Vector2(26f, 26f);
            var outline = box.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.75f, 0.62f, 0.4f, 0.9f);
            outline.effectDistance = new Vector2(1f, -1f);

            var check = Panel(box.transform, "Check", Gold);
            check.raycastTarget = false;
            Stretch(check.rectTransform, 6f);

            var text = Text(go.transform, "Label", label, 17f, TextAlignmentOptions.Left);
            var trt = text.rectTransform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(36f, 0f);
            trt.offsetMax = Vector2.zero;

            toggle.targetGraphic = box;
            toggle.graphic = check;
            toggle.toggleTransition = Toggle.ToggleTransition.None;
            toggle.transition = Selectable.Transition.ColorTint;
            var colors = toggle.colors;
            colors.highlightedColor = new Color(1.4f, 1.4f, 1.4f, 1f);
            colors.colorMultiplier = 1.5f;
            toggle.colors = colors;
            toggle.isOn = value;
            check.gameObject.SetActive(value);
            toggle.onValueChanged.AddListener(v =>
            {
                check.gameObject.SetActive(v);
                onChanged?.Invoke(v);
            });
            return toggle;
        }

        internal static void SetToggle(Toggle toggle, bool value)
        {
            if (toggle == null) return;
            toggle.SetIsOnWithoutNotify(value);
            if (toggle.graphic != null) toggle.graphic.gameObject.SetActive(value);
        }

        /// <summary>A plain button, used when the game's own button cannot be cloned.</summary>
        internal static Button SimpleButton(Transform parent, string name, string label, Action onClick)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = new Color(0.25f, 0.18f, 0.1f, 0.95f);
            var outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(0.75f, 0.62f, 0.4f, 0.9f);
            outline.effectDistance = new Vector2(1f, -1f);
            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.highlightedColor = new Color(1.3f, 1.3f, 1.3f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            colors.colorMultiplier = 1.5f;
            btn.colors = colors;
            if (onClick != null) btn.onClick.AddListener(() => onClick());
            var text = Text(go.transform, "Label", label, 18f, TextAlignmentOptions.Center);
            Stretch(text.rectTransform, 2f);
            return btn;
        }

        /// <summary>A copy of one of the game's buttons with our own label and click handler.</summary>
        internal static Button CloneButton(Button template, Transform parent, string name, string label, Action onClick)
        {
            var go = UnityEngine.Object.Instantiate(template.gameObject, parent);
            go.name = name;
            go.SetActive(true);
            var btn = go.GetComponent<Button>();
            KillPersistent(btn.onClick);
            btn.onClick.RemoveAllListeners();
            btn.interactable = true;
            if (onClick != null) btn.onClick.AddListener(() => onClick());
            SetLabel(go, label);
            return btn;
        }

        /// <summary>Sets the text of a cloned control and stops the game's localizer from overwriting it.</summary>
        internal static void SetLabel(GameObject go, string label)
        {
            foreach (var l in go.GetComponentsInChildren<Localize>(true)) UnityEngine.Object.Destroy(l);
            var tmp = go.GetComponentInChildren<TMP_Text>(true);
            if (tmp != null) { tmp.text = label; return; }
            var legacy = go.GetComponentInChildren<Text>(true);
            if (legacy != null) legacy.text = label;
        }

        /// <summary>The game's text field (the one its own prompts use), copied under <paramref name="parent"/>.</summary>
        internal static TMP_InputField CloneInputField(Transform parent, string name, string placeholder, int charLimit, Action<string> onChanged, Action<string> onSubmit)
        {
            var src = TextInput.instance != null && TextInput.instance.m_panel != null
                ? TextInput.instance.m_panel.GetComponentInChildren<TMP_InputField>(true) : null;
            if (src == null) return null;
            var go = UnityEngine.Object.Instantiate(src.gameObject, parent);
            go.name = name;
            go.SetActive(true);
            var field = go.GetComponent<TMP_InputField>();
            PrepareInput(field, placeholder, charLimit, onChanged, onSubmit);
            return field;
        }

        /// <summary>Detaches a cloned field from the game's prompt logic and wires up our own handlers.</summary>
        internal static void PrepareInput(TMP_InputField field, string placeholder, int charLimit, Action<string> onChanged, Action<string> onSubmit)
        {
            if (field == null) return;
            KillPersistent(field.onSubmit);
            KillPersistent(field.onEndEdit);
            KillPersistent(field.onValueChanged);
            KillPersistent(field.onDeselect);
            KillPersistent(field.onSelect);
            field.onSubmit.RemoveAllListeners();
            field.onEndEdit.RemoveAllListeners();
            field.onValueChanged.RemoveAllListeners();
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.characterLimit = charLimit;
            field.restoreOriginalTextOnEscape = false;
            foreach (var l in field.GetComponentsInChildren<Localize>(true)) UnityEngine.Object.Destroy(l);
            var ph = field.placeholder as TMP_Text;
            if (ph != null) ph.text = placeholder ?? "";
            if (onChanged != null) field.onValueChanged.AddListener(v => onChanged(v));
            if (onSubmit != null) field.onSubmit.AddListener(v => onSubmit(v));
            Track(field);
        }

        /// <summary>Switches off the listeners wired up in the game's prefab, so a clone does not drive the original.</summary>
        internal static void KillPersistent(UnityEventBase evt)
        {
            if (evt == null) return;
            for (int i = 0; i < evt.GetPersistentEventCount(); i++) evt.SetPersistentListenerState(i, UnityEventCallState.Off);
        }

        internal static string PersistentMethods(UnityEventBase evt)
        {
            if (evt == null) return "";
            var sb = new StringBuilder();
            for (int i = 0; i < evt.GetPersistentEventCount(); i++)
            {
                if (sb.Length > 0) sb.Append(',');
                sb.Append(evt.GetPersistentMethodName(i));
            }
            return sb.ToString();
        }

        internal static string Distance(Vector3 a, Vector3 b)
        {
            float d = Utils.DistanceXZ(a, b);
            return d < 1000f ? $"{d:0} m" : $"{d / 1000f:0.0} km";
        }

        /// <summary>The transform path of <paramref name="child"/> below <paramref name="root"/>, for finding its twin in a clone.</summary>
        internal static string PathBelow(Transform child, Transform root)
        {
            if (child == null || root == null) return null;
            var parts = new List<string>();
            var t = child;
            while (t != null && t != root) { parts.Add(t.name); t = t.parent; }
            if (t != root) return null;
            parts.Reverse();
            return string.Join("/", parts);
        }

        /// <summary>A readable dump of a UI hierarchy, for the log.</summary>
        internal static string Describe(Transform t, int depth = 0, int maxDepth = 6)
        {
            var sb = new StringBuilder();
            Describe(t, depth, maxDepth, sb);
            return sb.ToString();
        }

        private static void Describe(Transform t, int depth, int maxDepth, StringBuilder sb)
        {
            sb.Append(' ', depth * 2).Append(t.name).Append(t.gameObject.activeSelf ? "" : " (inactive)").Append(" [");
            var comps = t.GetComponents<Component>();
            for (int i = 0; i < comps.Length; i++)
            {
                if (comps[i] == null) continue;
                if (i > 0) sb.Append(", ");
                sb.Append(comps[i].GetType().Name);
            }
            sb.Append("]\n");
            if (depth >= maxDepth) return;
            for (int i = 0; i < t.childCount; i++) Describe(t.GetChild(i), depth + 1, maxDepth, sb);
        }
    }
}
