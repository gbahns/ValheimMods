using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Comfortometer
{
    /// <summary>Keybind polling through Valheim's own ZInput, gated so a hotkey never fires off typing.</summary>
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

        /// <summary>True while any modifier of <paramref name="shortcut"/> is held.</summary>
        internal static bool ModifierHeld(KeyboardShortcut shortcut)
        {
            foreach (var modifier in shortcut.Modifiers)
                if (ZInput.GetKey(modifier)) return true;
            return false;
        }

        /// <summary>
        /// Nothing else has the keyboard: no console, chat, prompt or menu, and no text box of
        /// any kind, whoever it belongs to.
        /// </summary>
        internal static bool CanTakeInput()
        {
            if (Console.IsVisible() || TextInput.IsVisible() || Menu.IsActive()) return false;
            if (Chat.instance != null && Chat.instance.HasFocus()) return false;
            if (Minimap.instance != null && Minimap.InTextInput()) return false;
            // The hammer's build menu search box, new in Valheim 1.0: vanilla's own key handling
            // checks exactly this, and a hotkey must not fire off a letter being typed there.
            if (BuildSearchFocused()) return false;
            // Any other text box: a sign, a filter, another mod's field.
            if (UiBits.AnyFieldFocused()) return false;
            return true;
        }

        private static bool BuildSearchFocused()
        {
            var hud = Hud.instance;
            if (hud == null || hud.m_buildUi == null) return false;
            try { return hud.m_buildUi.SearchFieldFocused; }
            catch { return false; }   // a build menu torn down under us mid-frame
        }
    }

    /// <summary>
    /// The little that this mod needs of a UI toolkit: TextMeshPro labels in the game's font,
    /// a flat panel, top-left placement, a drag handle and the resize grip. No Jotunn.
    /// </summary>
    internal static class UiBits
    {
        internal static readonly Color Gold = new Color(1f, 0.85f, 0.45f);
        internal static readonly Color Dim = new Color(0.62f, 0.6f, 0.56f);
        internal static readonly Color Body = new Color(0.95f, 0.92f, 0.85f);
        internal static readonly Color Red = new Color(1f, 0.36f, 0.3f);
        internal static readonly Color Green = new Color(0.55f, 0.9f, 0.5f);

        private static TMP_FontAsset _font;
        private static Material _fontMaterial;

        internal static void EnsureFont()
        {
            if (_font != null) return;
            // The game's regular serif face, when it is loaded, so bold is a choice and not the
            // only weight the HUD's own text happens to carry.
            foreach (var f in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
            {
                if (f == null) continue;
                string n = f.name;
                if (n.IndexOf("AveriaSerifLibre", StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (n.IndexOf("Bold", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                _font = f;
                _fontMaterial = f.material;
                return;
            }
            TMP_Text source = null;
            if (MessageHud.instance != null) source = MessageHud.instance.m_messageCenterText;
            else if (TextInput.instance != null && TextInput.instance.m_topic != null) source = TextInput.instance.m_topic;
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

        // ── keyboard focus ──────────────────────────────────────────────────────────

        private static int _anyFocusedFrame = -1000;
        private static int _observedFrame = -1;
        private static bool _typingNow;

        /// <summary>
        /// True while any text box at all has the keyboard, and for two frames after it lets go,
        /// so the Enter or Escape that ends the typing is not also read as a keypress.
        /// </summary>
        internal static bool AnyFieldFocused()
        {
            ObserveFocus();
            return _typingNow || Time.frameCount - _anyFocusedFrame <= 2;
        }

        /// <summary>Looks at the focused field once per frame; call it before anything reads a key.</summary>
        internal static void ObserveFocus()
        {
            if (_observedFrame == Time.frameCount) return;
            _observedFrame = Time.frameCount;
            var events = EventSystem.current;
            var selected = events != null ? events.currentSelectedGameObject : null;
            _typingNow = selected != null && IsTyping(selected);
            if (_typingNow) _anyFocusedFrame = Time.frameCount;
        }

        private static bool IsTyping(GameObject go)
        {
            var tmp = go.GetComponent<TMP_InputField>();
            if (tmp != null && tmp.isFocused) return true;
            var legacy = go.GetComponent<InputField>();
            return legacy != null && legacy.isFocused;
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
            t.richText = true;
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

        /// <summary>Reports pointer drags in screen pixels, for the mover and the resize grip.</summary>
        internal sealed class DragHandle : MonoBehaviour, IDragHandler, IEndDragHandler
        {
            public Action<Vector2> OnDrag;
            public Action OnEnd;
            void IDragHandler.OnDrag(PointerEventData eventData) { OnDrag?.Invoke(eventData.delta); }
            void IEndDragHandler.OnEndDrag(PointerEventData eventData) { OnEnd?.Invoke(); }
        }

        private static Sprite _grip;

        /// <summary>Three diagonal lines tucked into the lower-right corner: the usual resize grip.</summary>
        internal static Sprite Grip()
        {
            if (_grip != null) return _grip;
            const int n = 24;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int k = x + (n - 1 - y);        // grows towards the bottom-right corner
                    bool on = k >= n - 3 && (k - (n - 3)) % 6 < 2;
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(on ? 255 : 0));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.hideFlags = HideFlags.HideAndDontSave;
            _grip = Sprite.Create(tex, new Rect(0f, 0f, n, n), new Vector2(0.5f, 0.5f), 100f);
            _grip.hideFlags = HideFlags.HideAndDontSave;
            return _grip;
        }

        /// <summary>
        /// A small button in the game's own style: its "button" sprite from the UI atlas with an
        /// orange label, as the close x on the other DeathMonger panels. Falls back to a flat
        /// plate when the atlas is not loaded.
        /// </summary>
        internal static Button ValheimButton(Transform parent, string name, string label, Action onClick)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            var sprite = FindSprite("button");
            if (sprite != null)
            {
                image.sprite = sprite;
                image.type = Image.Type.Sliced;
                image.pixelsPerUnitMultiplier = 2f;
            }
            else image.color = new Color(0.25f, 0.2f, 0.15f, 0.9f);
            image.raycastTarget = true;
            var button = go.GetComponent<Button>();
            button.transition = Selectable.Transition.ColorTint;
            button.colors = new ColorBlock
            {
                normalColor      = new Color(0.824f, 0.824f, 0.824f, 1f),
                highlightedColor = new Color(1.3f, 1.3f, 1.3f, 1f),
                pressedColor     = new Color(0.537f, 0.556f, 0.556f, 1f),
                selectedColor    = new Color(0.824f, 0.824f, 0.824f, 1f),
                disabledColor    = new Color(0.566f, 0.566f, 0.566f, 0.502f),
                colorMultiplier  = 1f,
                fadeDuration     = 0.1f,
            };
            button.targetGraphic = image;
            if (onClick != null) button.onClick.AddListener(() => onClick());
            var text = Text(go.transform, "Label", label, 14f, TextAlignmentOptions.Center, new Color(1f, 0.631f, 0.235f));
            text.fontStyle = FontStyles.Bold;
            Stretch(text.rectTransform);
            return button;
        }

        private static Sprite FindSprite(string name)
        {
            foreach (var atlas in Resources.FindObjectsOfTypeAll<UnityEngine.U2D.SpriteAtlas>())
            {
                if (atlas == null || atlas.name != "UIAtlas") continue;
                var sprite = atlas.GetSprite(name);
                if (sprite != null) return sprite;
            }
            foreach (var sprite in Resources.FindObjectsOfTypeAll<Sprite>())
                if (sprite != null && sprite.name == name) return sprite;
            return null;
        }

        internal static bool TryPair(string text, out float a, out float b)
        {
            a = 0f;
            b = 0f;
            if (string.IsNullOrEmpty(text)) return false;
            var parts = text.Split(',');
            return parts.Length == 2
                && float.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out a)
                && float.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out b);
        }

        internal static string Pair(float a, float b) => $"{Mathf.RoundToInt(a)},{Mathf.RoundToInt(b)}";
    }
}
