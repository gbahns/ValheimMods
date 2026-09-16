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

        private static List<RectTransform> VanillaButtons(Minimap map)
        {
            var list = new List<RectTransform>();
            void Add(Image frame)
            {
                if (frame == null || frame.transform.parent == null) return;
                var rt = frame.transform.parent as RectTransform;
                if (rt != null && !list.Contains(rt)) list.Add(rt);
            }
            Add(map.m_selectedIcon0); Add(map.m_selectedIcon1); Add(map.m_selectedIcon2); Add(map.m_selectedIcon3); Add(map.m_selectedIcon4);
            Add(map.m_selectedIconDeath); Add(map.m_selectedIconBoss); Add(map.m_selectedIconPing);
            return list;
        }

        private static void Build(Minimap map)
        {
            var template = Template(map);
            if (template == null) return;
            var panel = template.transform.parent;
            if (panel == null) return;
            var templateRect = (RectTransform)template.transform;
            float baseX = templateRect.anchoredPosition.x;

            // The spacing vanilla uses between the buttons that share the template's parent.
            float topY = templateRect.anchoredPosition.y;
            float pitch = 0f;
            var sameParent = new List<RectTransform>();
            foreach (var rt in VanillaButtons(map))
                if (rt.parent == panel && Mathf.Abs(rt.anchoredPosition.x - baseX) < 1f) sameParent.Add(rt);
            sameParent.Sort((a, b) => b.anchoredPosition.y.CompareTo(a.anchoredPosition.y));
            if (sameParent.Count > 0) topY = sameParent[0].anchoredPosition.y;
            if (sameParent.Count >= 2) pitch = sameParent[0].anchoredPosition.y - sameParent[1].anchoredPosition.y;
            if (pitch <= 0f) pitch = Mathf.Max(templateRect.rect.height, 24f) + 4f;

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

            // Above every vanilla button in this column, the death and boss icons included. Those
            // two need not share the others' parent or anchoring, so the column is picked out by
            // where the buttons actually are on screen and the clearance is measured there too.
            var ourRect = (RectTransform)go.transform;
            ourRect.anchoredPosition = new Vector2(baseX, topY + pitch);
            float scale = Mathf.Abs(panel.lossyScale.y) > 0.0001f ? panel.lossyScale.y : 1f;
            float columnX = WorldCenterX(templateRect);
            float tolerance = Mathf.Max(templateRect.rect.width * Mathf.Abs(templateRect.lossyScale.x), 8f) * 0.75f;
            float highest = float.MinValue;
            foreach (var rt in VanillaButtons(map))
            {
                if (rt == ourRect || Mathf.Abs(WorldCenterX(rt) - columnX) > tolerance) continue;
                float top = WorldTop(rt);
                if (top > highest) highest = top;
            }
            if (highest > float.MinValue)
            {
                float gap = Mathf.Max(4f, pitch - Mathf.Max(templateRect.rect.height, 1f));
                float lift = (highest + gap * scale - WorldBottom(ourRect)) / scale;
                if (lift > 0f) ourRect.anchoredPosition += new Vector2(0f, lift);
            }
            go.transform.SetAsLastSibling();
            Recolor(force: true);
        }

        private static readonly Vector3[] _corners = new Vector3[4];

        /// <summary>Corners run bottom-left, top-left, top-right, bottom-right.</summary>
        private static float WorldTop(RectTransform rt)
        {
            rt.GetWorldCorners(_corners);
            return Mathf.Max(_corners[1].y, _corners[2].y);
        }

        private static float WorldBottom(RectTransform rt)
        {
            rt.GetWorldCorners(_corners);
            return Mathf.Min(_corners[0].y, _corners[3].y);
        }

        /// <summary>The world x of this button's left edge, so other things can keep clear of it.</summary>
        internal static bool TryGetWorldLeft(out float x)
        {
            x = 0f;
            if (_button == null) return false;
            var rt = _button.transform as RectTransform;
            if (rt == null) return false;
            rt.GetWorldCorners(_corners);
            x = Mathf.Min(_corners[0].x, _corners[1].x);
            return true;
        }

        private static float WorldCenterX(RectTransform rt)
        {
            rt.GetWorldCorners(_corners);
            return (_corners[0].x + _corners[2].x) * 0.5f;
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

        private static void Recolor(bool force = false)
        {
            bool shown = TgmConfig.ShowAllMarkers == null || TgmConfig.ShowAllMarkers.Value;
            if (!force && shown == _lastShown) return;
            _lastShown = shown;
            var color = shown ? Gold : Off;
            foreach (var img in _tinted) if (img != null) img.color = color;
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
