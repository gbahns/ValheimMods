using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TheGreatestMap
{
    /// <summary>Just enough runtime UI for a small menu: the game's TMP font, a panel, and hoverable button rows.</summary>
    internal static class MenuKit
    {
        internal static readonly Color PanelColor = new Color(0.06f, 0.045f, 0.03f, 0.97f);
        internal static readonly Color OutlineColor = new Color(0.75f, 0.62f, 0.4f, 0.9f);
        internal static readonly Color RowColor = new Color(1f, 1f, 1f, 0.04f);
        internal static readonly Color RowHover = new Color(1f, 1f, 1f, 0.2f);
        internal static readonly Color Body = new Color(0.95f, 0.92f, 0.85f);
        internal static readonly Color Dim = new Color(0.55f, 0.52f, 0.48f);
        internal static readonly Color Header = new Color(1f, 0.85f, 0.45f);

        private static TMP_FontAsset _font;
        private static Material _fontMaterial;

        private static void EnsureFont()
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

        internal static TextMeshProUGUI Text(Transform parent, string name, string text, float size, Color color)
        {
            EnsureFont();
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            go.SetActive(false); // TMP's Awake looks up a default font the game lacks unless one is set first
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            if (_font != null) t.font = _font;
            if (_fontMaterial != null) t.fontSharedMaterial = _fontMaterial;
            t.text = text ?? "";
            t.fontSize = size;
            t.alignment = TextAlignmentOptions.Left;
            t.color = color;
            t.raycastTarget = false;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            go.SetActive(true);
            return t;
        }

        internal static Image Panel(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = PanelColor;
            img.raycastTarget = true;
            var outline = go.AddComponent<Outline>();
            outline.effectColor = OutlineColor;
            outline.effectDistance = new Vector2(1f, -1f);
            return img;
        }

        internal sealed class Hover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            public Image Background;
            public bool Enabled = true;
            public void OnPointerEnter(PointerEventData e) { if (Enabled && Background != null) Background.color = RowHover; }
            public void OnPointerExit(PointerEventData e) { if (Background != null) Background.color = RowColor; }
        }

        /// <summary>A clickable row; a disabled one is dimmed and inert.</summary>
        internal static RectTransform Row(Transform parent, string label, bool enabled, Action onClick)
        {
            var go = new GameObject("Row", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(Hover));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = RowColor;
            img.raycastTarget = true;
            var hover = go.GetComponent<Hover>();
            hover.Background = img;
            hover.Enabled = enabled;
            var btn = go.GetComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.targetGraphic = img;
            btn.interactable = enabled;
            if (enabled && onClick != null) btn.onClick.AddListener(() => onClick());
            var text = Text(go.transform, "Label", label, 15f, enabled ? Body : Dim);
            var trt = text.rectTransform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(10f, 0f);
            trt.offsetMax = new Vector2(-10f, 0f);
            return (RectTransform)go.transform;
        }
    }

    /// <summary>
    /// Right-click on one of this mod's markers on the map screen: a small menu instead of the
    /// vanilla erase. Hide this one, hide everything with the same icon, hide the whole kind,
    /// cross off, erase (only where the server allows it), and show everything again.
    /// </summary>
    internal static class MarkerMenu
    {
        private const float Width = 250f;
        private const float RowHeight = 26f;
        private const float HeaderHeight = 28f;
        private const float Pad = 4f;

        private static GameObject _root;
        private static RectTransform _rect;
        private static Camera _camera;
        private static int _openedFrame = -10;

        internal static bool IsOpen => _root != null;

        internal static void Open(SharedPin pin, Vector2 screenPos)
        {
            Close();
            var map = Minimap.instance;
            if (map == null || pin == null || map.m_largeRoot == null) return;
            var parent = map.m_largeRoot.transform as RectTransform;
            if (parent == null) return;

            var panel = MenuKit.Panel(parent, "TGM_MarkerMenu");
            _root = panel.gameObject;
            _rect = panel.rectTransform;
            _rect.anchorMin = _rect.anchorMax = new Vector2(0f, 1f);
            _rect.pivot = new Vector2(0f, 1f);

            var items = BuildItems(pin);
            float height = Pad + HeaderHeight + items.Count * RowHeight + Pad;
            _rect.sizeDelta = new Vector2(Width, height);

            string title = !string.IsNullOrEmpty(pin.Name) ? pin.Name : IconRegistry.DisplayName(pin.Icon);
            var header = MenuKit.Text(_root.transform, "Header", title, 15f, MenuKit.Header);
            var hrt = header.rectTransform;
            hrt.anchorMin = new Vector2(0f, 1f);
            hrt.anchorMax = new Vector2(1f, 1f);
            hrt.pivot = new Vector2(0f, 1f);
            hrt.anchoredPosition = new Vector2(10f, -Pad);
            hrt.sizeDelta = new Vector2(-20f, HeaderHeight);
            header.alignment = TextAlignmentOptions.MidlineLeft;

            float y = -(Pad + HeaderHeight);
            foreach (var item in items)
            {
                var action = item.Value;
                var row = MenuKit.Row(_root.transform, item.Key, action != null, () => { Close(); action(); });
                row.anchorMin = new Vector2(0f, 1f);
                row.anchorMax = new Vector2(1f, 1f);
                row.pivot = new Vector2(0f, 1f);
                row.anchoredPosition = new Vector2(0f, y);
                row.sizeDelta = new Vector2(0f, RowHeight);
                y -= RowHeight;
            }

            // Top-left corner at the pointer, kept inside the map panel.
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
        }

        private static List<KeyValuePair<string, Action>> BuildItems(SharedPin pin)
        {
            var items = new List<KeyValuePair<string, Action>>();
            string id = pin.Id;
            string icon = pin.Icon;
            string iconName = IconRegistry.DisplayName(icon);
            var kind = ClientPins.KindOf(pin);
            bool canEdit = !TgmConfig.RequireMapOutToEdit.Value || PocketMap.IsOut;

            items.Add(Item("Hide this marker", () =>
            {
                ViewPrefs.Hide(id);
                ClientPins.Restyle();
                Note("Marker hidden. 'Show all hidden' in any marker's menu brings it back.");
            }));
            // A marker drawn with one of vanilla's own filterable pin icons is already hidden and
            // shown by that icon's button on the map, which also covers pins the player placed by
            // hand with it. Offering our own switch beside it only splits one job in two.
            bool vanillaHides = IconRegistry.VanillaFilters(icon);
            if (!vanillaHides)
            {
                items.Add(Item($"Hide all {iconName} markers", () =>
                {
                    TgmConfig.AddHiddenIcon(icon);
                    ClientPins.Restyle();
                    Note($"Hiding all {iconName} markers (Display > Hidden Icons).");
                }));
                if (kind.HasValue)
                {
                    var k = kind.Value;
                    string kindLabel = Categories.Label(k).ToLowerInvariant();
                    items.Add(Item($"Hide all {kindLabel}", () =>
                    {
                        if (TgmConfig.ShowKind.TryGetValue(k, out var entry)) entry.Value = false;
                        ClientPins.Restyle();
                        Note($"Hiding all {kindLabel} (Display > Show {Categories.Label(k)}).");
                    }));
                }
            }

            bool isChecked = pin.Checked;
            items.Add(Item(isChecked ? "Uncross" : "Cross off", canEdit ? () => ClientPins.SetChecked(id, !isChecked) : (Action)null, "take out your map"));

            if (!TgmConfig.AllowErasingMarkers.Value)
                items.Add(Item("Erase (off on this server)", null));
            else
                items.Add(Item("Erase for everyone", canEdit ? () => { if (ClientPins.EraseById(id)) Note("Marker erased."); } : (Action)null, "take out your map"));

            if (ViewPrefs.Count > 0 || TgmConfig.AnythingHidden())
                items.Add(Item("Show all hidden", () =>
                {
                    ViewPrefs.Clear();
                    TgmConfig.ShowEverything();
                    ClientPins.Restyle();
                    Note("Showing every marker again.");
                }));
            return items;
        }

        private static KeyValuePair<string, Action> Item(string label, Action action, string whyDisabled = null)
        {
            if (action == null && !string.IsNullOrEmpty(whyDisabled)) label += " (" + whyDisabled + ")";
            return new KeyValuePair<string, Action>(label, action);
        }

        internal static void Close()
        {
            if (_root != null) UnityEngine.Object.Destroy(_root);
            _root = null;
            _rect = null;
        }

        /// <summary>Every frame: the menu goes away with the map screen, on Escape, or on a click anywhere else.</summary>
        internal static void Update()
        {
            if (_root == null) return;
            var map = Minimap.instance;
            if (map == null || map.m_mode != Minimap.MapMode.Large || ZInput.GetKeyDown(KeyCode.Escape)) { Close(); return; }
            if (Time.frameCount - _openedFrame <= 1) return; // the click that opened it
            if (ZInput.GetMouseButtonDown(0) || ZInput.GetMouseButtonDown(1))
            {
                if (!RectTransformUtility.RectangleContainsScreenPoint(_rect, ZInput.pointerPosition, _camera)) Close();
            }
        }

        private static void Note(string text) => TheGreatestMapMod.Message(text);
    }
}
