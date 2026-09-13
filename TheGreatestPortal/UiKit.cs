using System;
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
    /// and right-click, scroll lists, plain toggles and buttons, and helpers for reusing the
    /// game's own buttons. No Jotunn.
    /// </summary>
    internal static class UiKit
    {
        internal const float RowHeight = 28f;
        internal static readonly Color RowColor = new Color(1f, 1f, 1f, 0.07f);
        internal static readonly Color RowHover = new Color(1f, 1f, 1f, 0.22f);
        internal static readonly Color RowSelected = new Color(1f, 0.78f, 0.3f, 0.35f);
        internal static readonly Color RowSelectedHover = new Color(1f, 0.78f, 0.3f, 0.5f);
        internal static readonly Color Gold = new Color(1f, 0.85f, 0.45f);
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
            if (source == null) return;
            _font = source.font;
            _fontMaterial = source.fontSharedMaterial;
        }

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

        /// <summary>A vertical scroll list. Returns the content transform rows are added to.</summary>
        internal static RectTransform ScrollList(Transform parent, string name, out ScrollRect scroll)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(ScrollRect));
            go.transform.SetParent(parent, false);
            var bg = go.GetComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.35f);
            bg.raycastTarget = true;
            scroll = go.GetComponent<ScrollRect>();

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewport.transform.SetParent(go.transform, false);
            var vrt = viewport.GetComponent<RectTransform>();
            Stretch(vrt, 3f);

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
            vlg.spacing = 2f;
            vlg.padding = new RectOffset(3, 3, 3, 3);
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.content = crt;
            scroll.viewport = vrt;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;
            scroll.inertia = false;
            return crt;
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

        internal static RowHandle Row(Transform content, string label, string right, Action onClick, Action onRightClick, float fontSize = 17f, Color? labelColor = null)
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
            if (onClick != null) btn.onClick.AddListener(() => onClick());

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
            if (onChanged != null) toggle.onValueChanged.AddListener(v => onChanged(v));
            return toggle;
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

        /// <summary>Sets the text of a cloned control and stops the game's localiser from overwriting it.</summary>
        internal static void SetLabel(GameObject go, string label)
        {
            foreach (var l in go.GetComponentsInChildren<Localize>(true)) UnityEngine.Object.Destroy(l);
            var tmp = go.GetComponentInChildren<TMP_Text>(true);
            if (tmp != null) { tmp.text = label; return; }
            var legacy = go.GetComponentInChildren<Text>(true);
            if (legacy != null) legacy.text = label;
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
            var parts = new System.Collections.Generic.List<string>();
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
