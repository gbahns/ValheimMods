using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TheGreatestMap
{
    /// <summary>
    /// One button above vanilla's icon buttons on the right edge of the large map, drawn as a map
    /// pin in the same pale gold as recorded markers. A right click hides or shows every marker this
    /// mod put on the map at once; a left click opens a list of the kinds, for hiding them one at
    /// a time. The button is a clone of vanilla's first icon button with the picture swapped, so it
    /// keeps the game's frame and follows the UI scale, and nothing on the clone takes pointer
    /// events except a transparent surface of ours, so vanilla's own handlers can never fire.
    /// </summary>
    internal static class MarkerToggle
    {
        private static GameObject _button;
        private static readonly List<Image> _tinted = new List<Image>();
        private static Sprite _pin;
        private static MenuKit.Glow _hover;
        private static Vector3 _normalScale = Vector3.one;
        private static bool _lastShown = true;

        private static readonly Color Gold = new Color(1f, 0.93f, 0.72f, 1f);
        private static readonly Color Off = new Color(0.45f, 0.44f, 0.42f, 1f);

        internal static void Reset()
        {
            Destroy();
            KindMenu.Close();
        }

        private static void Destroy()
        {
            if (_button != null) Object.Destroy(_button);
            _button = null;
            _tinted.Clear();
        }

        internal static void Update()
        {
            var map = Minimap.instance;
            if (map == null || map.m_mode != Minimap.MapMode.Large)
            {
                KindMenu.Close();
                return;
            }
            if (TgmConfig.MarkerButton == null || !TgmConfig.MarkerButton.Value)
            {
                if (_button != null) Destroy();
                KindMenu.Close();
                return;
            }
            if (_button == null) Build(map);
            Position();
            Recolor();
            KindMenu.Update();
        }

        /// <summary>Vanilla's first icon button (the one carrying the "selected" frame for Icon0).</summary>
        private static GameObject Template(Minimap map)
        {
            var frame = map.m_selectedIcon0;
            if (frame == null || frame.transform.parent == null) return null;
            return frame.transform.parent.gameObject;
        }

        private static void Build(Minimap map)
        {
            var template = Template(map);
            if (template == null) return;
            var panel = template.transform.parent;
            if (panel == null) return;
            var templateRect = (RectTransform)template.transform;

            var go = Object.Instantiate(template, panel);
            go.name = "TGM_MarkerToggle";
            go.SetActive(true);
            _button = go;

            // Vanilla's click routes are cleared, and then made unreachable: pointer events only
            // reach a graphic that is a raycast target, so none of the clone's graphics is one and
            // our own transparent surface below takes every click.
            foreach (var b in go.GetComponentsInChildren<Button>(true)) b.onClick = new Button.ButtonClickedEvent();
            foreach (var h in go.GetComponentsInChildren<UIInputHandler>(true))
            {
                h.m_onLeftClick = null; h.m_onLeftDown = null; h.m_onLeftUp = null;
                h.m_onRightClick = null; h.m_onRightDown = null; h.m_onRightUp = null;
                h.m_onMiddleClick = null; h.m_onMiddleDown = null; h.m_onMiddleUp = null;
                h.m_onPointerEnter = null; h.m_onPointerExit = null;
            }
            foreach (var graphic in go.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;

            var vanillaIcon = IconRegistry.Resolve(map, "pin:Icon0");
            var pin = PinSprite();
            _tinted.Clear();
            foreach (var img in go.GetComponentsInChildren<Image>(true))
                if (vanillaIcon != null && img.sprite == vanillaIcon) { img.sprite = pin; _tinted.Add(img); }
            var rootImage = go.GetComponent<Image>();
            if (_tinted.Count == 0 && rootImage != null) { rootImage.sprite = pin; _tinted.Add(rootImage); }

            // No "selected" frame: this button only toggles.
            var framePath = PathBelow(template.transform, map.m_selectedIcon0.transform);
            var frame = framePath != null ? go.transform.Find(framePath) : null;
            var frameImage = frame != null ? frame.GetComponent<Image>() : null;
            if (frameImage != null) frameImage.enabled = false;

            var click = new GameObject("TGM_Click", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            click.transform.SetParent(go.transform, false);
            var clickRect = (RectTransform)click.transform;
            clickRect.anchorMin = Vector2.zero;
            clickRect.anchorMax = Vector2.one;
            clickRect.offsetMin = Vector2.zero;
            clickRect.offsetMax = Vector2.zero;
            var clickImage = click.GetComponent<Image>();
            clickImage.color = new Color(0f, 0f, 0f, 0f);
            clickImage.raycastTarget = true;
            click.AddComponent<ToggleButton>();
            _hover = click.AddComponent<MenuKit.Glow>();

            var source = go.GetComponent<UITooltip>();
            if (source != null && source.m_tooltipPrefab != null)
            {
                var tooltip = click.AddComponent<UITooltip>();
                tooltip.m_tooltipPrefab = source.m_tooltipPrefab;
                tooltip.m_topic = "The Greatest Map";
                tooltip.m_text = "Right-click to hide or show every marker. Left-click for the list of kinds.";
            }

            var element = go.GetComponent<LayoutElement>();
            if (element == null) element = go.AddComponent<LayoutElement>();
            element.ignoreLayout = true;

            JoinTheRow(map, (RectTransform)go.transform, templateRect);
            go.transform.SetAsLastSibling();
            Recolor(force: true);
        }

        /// <summary>
        /// Take a place in the row of buttons along the map's top-right corner, beside the pause
        /// mark and the maximize square, and shrink to their size. The clone comes from vanilla's
        /// icon column, which is drawn large and anchored to the map's bottom edge -- both wrong
        /// here, so it leaves that panel for the map root and is scaled down to match its
        /// neighbors. Scaling rather than resizing keeps the button's own insides in proportion.
        /// </summary>
        private static void JoinTheRow(Minimap map, RectTransform ourRect, RectTransform templateRect)
        {
            var root = map.m_largeRoot != null ? map.m_largeRoot.transform as RectTransform : null;
            if (root == null) return;
            ourRect.SetParent(root, false);
            ourRect.anchorMin = ourRect.anchorMax = new Vector2(1f, 1f);
            ourRect.pivot = new Vector2(0.5f, 0.5f);
            float side = Mathf.Max(1f, templateRect.rect.height);
            float scale = MapFrame.ButtonSize / side;
            _normalScale = new Vector3(scale, scale, 1f);
            ourRect.localScale = _normalScale;
        }

        /// <summary>
        /// Nudge the button clear of the biome name, which the map writes in this same corner while
        /// you point at it. Applied to the measured position rather than added to wherever the
        /// button is now, so it cannot creep, and read every frame so a change to the setting shows
        /// at once.
        /// </summary>
        private static void Position()
        {
            if (_button == null) return;
            var rect = (RectTransform)_button.transform;
            float dx = TgmConfig.MarkerButtonOffsetX != null ? TgmConfig.MarkerButtonOffsetX.Value : 0f;
            float dy = TgmConfig.MarkerButtonOffsetY != null ? TgmConfig.MarkerButtonOffsetY.Value : 0f;
            var want = MapFrame.Slot(0) + new Vector2(dx, dy);
            // A large enough offset would push the button off the top of a map opened out to the
            // whole screen. It is pivoted in its middle, so its top is half a button above its own
            // y: hold that on screen whatever the offset asks for.
            var parent = rect.parent as RectTransform;
            if (parent != null)
            {
                var corners = new Vector3[4];
                parent.GetWorldCorners(corners);
                float scale = Mathf.Abs(parent.lossyScale.y) > 0.0001f ? parent.lossyScale.y : 1f;
                float highest = (Screen.height - 2f - corners[1].y) / scale - MapFrame.ButtonSize * 0.5f;
                if (want.y > highest) want.y = highest;
            }
            if (rect.anchoredPosition != want) rect.anchoredPosition = want;
        }

        /// <summary>"Child/Grandchild" path from an ancestor to a descendant, or null if not related.</summary>
        private static string PathBelow(Transform ancestor, Transform descendant)
        {
            if (descendant == null || ancestor == null || descendant == ancestor) return null;
            var parts = new List<string>();
            var t = descendant;
            while (t != null && t != ancestor) { parts.Insert(0, t.name); t = t.parent; }
            return t == ancestor ? string.Join("/", parts) : null;
        }

        private static bool _lastLit, _lastOpen;

        private static void Recolor(bool force = false)
        {
            bool shown = TgmConfig.ShowAllMarkers == null || TgmConfig.ShowAllMarkers.Value;
            bool lit = _hover != null && _hover.Over;
            bool open = KindMenu.IsOpen;
            if (!force && shown == _lastShown && lit == _lastLit && open == _lastOpen) return;
            _lastLit = lit;
            _lastOpen = open;
            _lastShown = shown;
            // Orange while the list is up, the same orange the pause mark wears while pausing is on:
            // along this row, orange means this one is doing something.
            var color = MenuKit.Lit(open ? PauseToggle.Paused : (shown ? Gold : Off), lit);
            foreach (var img in _tinted) if (img != null) img.color = color;
            if (_button != null) _button.transform.localScale = MenuKit.Magnified(_normalScale, lit);
        }

        internal static void ToggleAll()
        {
            if (TgmConfig.ShowAllMarkers == null) return;
            TgmConfig.ShowAllMarkers.Value = !TgmConfig.ShowAllMarkers.Value;
            TheGreatestMapMod.Message(TgmConfig.ShowAllMarkers.Value ? "Showing this mod's markers." : "Hiding this mod's markers.");
            ClientPins.Restyle();
            Recolor();
        }

        /// <summary>
        /// Right click hides or shows every marker, which is what right click does on each of
        /// vanilla's icon buttons too. Left click opens the kind list, since selecting a pin type
        /// to place, vanilla's left click, means nothing for markers nobody places by hand.
        /// </summary>
        internal sealed class ToggleButton : MonoBehaviour, IPointerClickHandler
        {
            public void OnPointerClick(PointerEventData eventData)
            {
                if (eventData.button == PointerEventData.InputButton.Right) ToggleAll();
                else if (eventData.button == PointerEventData.InputButton.Left)
                {
                    if (KindMenu.IsOpen) KindMenu.Close();
                    else KindMenu.Open(ZInput.pointerPosition);
                }
            }
        }

        // ── the drawn map pin ───────────────────────────────────────────────────────

        /// <summary>
        /// A classic map pin, drawn once at runtime: a ring-shaped head over a tapering tail.
        /// White, so the button can tint it gold when markers are shown and gray when they are not.
        /// </summary>
        private static Sprite PinSprite()
        {
            if (_pin != null) return _pin;
            const int n = 64;
            const float cx = n * 0.5f, cy = n * 0.625f, head = n * 0.28f, hole = n * 0.105f, tip = n * 0.05f;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    int hits = 0;
                    for (int sy = 0; sy < 3; sy++)
                        for (int sx = 0; sx < 3; sx++)
                            if (InPin(x + (sx + 0.5f) / 3f, y + (sy + 0.5f) / 3f, cx, cy, head, hole, tip)) hits++;
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(255 * hits / 9));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.hideFlags = HideFlags.HideAndDontSave;
            _pin = Sprite.Create(tex, new Rect(0f, 0f, n, n), new Vector2(0.5f, 0.5f), 100f);
            _pin.hideFlags = HideFlags.HideAndDontSave;
            return _pin;
        }

        private static bool InPin(float x, float y, float cx, float cy, float head, float hole, float tip)
        {
            float dx = x - cx, dy = y - cy;
            float d = Mathf.Sqrt(dx * dx + dy * dy);
            if (d <= hole) return false;                 // the hole through the head
            if (d <= head) return true;
            if (y < cy && y >= tip)                      // the tail, widening from the point up to the head
            {
                float w = head * (y - tip) / (cy - tip);
                if (Mathf.Abs(dx) <= w) return true;
            }
            return false;
        }
    }
}
