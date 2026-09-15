using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TheGreatestMap
{
    /// <summary>
    /// One button above vanilla's icon buttons on the right edge of the large map, drawn as a map
    /// pin in the same pale gold as recorded markers. A left click hides or shows every marker this
    /// mod put on the map at once; a right click opens a list of the kinds, for hiding them one at
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

    /// <summary>
    /// The kind list behind a right click on the marker button: one row per kind that has markers
    /// on your map, showing its icon. Clicking a row hides or shows that kind and leaves the list
    /// open, so several can be changed at once.
    /// </summary>
    internal static class KindMenu
    {
        private const float Width = 230f;
        private const float RowHeight = 26f;
        private const float HeaderHeight = 26f;
        private const float Pad = 4f;
        private const float IconSize = 20f;

        private sealed class Entry
        {
            public Category Kind;
            public TextMeshProUGUI Label;
            public Image Icon;
        }

        private static readonly List<Entry> _entries = new List<Entry>();
        private static GameObject _root;
        private static RectTransform _rect;
        private static Camera _camera;
        private static TextMeshProUGUI _masterLabel;
        private static int _openedFrame = -10;
        private static int _closedFrame = -10;

        internal static bool IsOpen => _root != null;

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

            var present = new List<Category>();
            var seen = new HashSet<Category>();
            foreach (var pin in ClientPins.All)
            {
                var kind = ClientPins.KindOf(pin);
                if (kind.HasValue) seen.Add(kind.Value);
            }
            // Every kind that has markers, vanilla-icon ones included. Structures, portals, camps
            // and boss altars are also hidden and shown by vanilla's own icon buttons, so listing
            // them here is redundant, but leaving them out was worse: a player looking for the
            // switch expects to find it with the rest, and does not care which mod owns the icon.
            // The two are not identical, and the difference is in our favor: vanilla's button hides
            // every pin with that icon, while this one covers only the markers this mod recorded.
            foreach (var kind in Categories.All)
                if (seen.Contains(kind)) present.Add(kind);

            var panel = MenuKit.Panel(parent, "TGM_KindMenu");
            _root = panel.gameObject;
            _rect = panel.rectTransform;
            _rect.anchorMin = _rect.anchorMax = new Vector2(0f, 1f);
            _rect.pivot = new Vector2(0f, 1f);
            int rowCount = 1 + present.Count + (present.Count > 0 ? 1 : 0); // master, the kinds, "show every kind"
            float height = Pad + HeaderHeight + rowCount * RowHeight + Pad;
            _rect.sizeDelta = new Vector2(Width, height);

            var header = MenuKit.Text(_root.transform, "Header", "Marker kinds", 15f, MenuKit.Header);
            var hrt = header.rectTransform;
            hrt.anchorMin = new Vector2(0f, 1f);
            hrt.anchorMax = new Vector2(1f, 1f);
            hrt.pivot = new Vector2(0f, 1f);
            hrt.anchoredPosition = new Vector2(10f, -Pad);
            hrt.sizeDelta = new Vector2(-20f, HeaderHeight);
            header.alignment = TextAlignmentOptions.MidlineLeft;

            _entries.Clear();
            float y = -(Pad + HeaderHeight);

            // The master, so it is reachable from this list too, not only from a right click.
            var master = MenuKit.Row(_root.transform, "", true, () => { MarkerToggle.ToggleAll(); Refresh(); });
            Place(master, y);
            _masterLabel = master.GetComponentInChildren<TextMeshProUGUI>();
            y -= RowHeight;

            foreach (var kind in present)
            {
                AddRow(map, kind, y);
                y -= RowHeight;
            }
            if (present.Count > 0)
            {
                var showAll = MenuKit.Row(_root.transform, "Show every kind", true, () =>
                {
                    foreach (var k in Categories.All)
                        if (TgmConfig.ShowKind.TryGetValue(k, out var e) && !e.Value) e.Value = true;
                    if (TgmConfig.ShowAllMarkers != null) TgmConfig.ShowAllMarkers.Value = true;
                    ClientPins.Restyle();
                    Refresh();
                });
                Place(showAll, y);
            }

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

        private static string IconKeyFor(Category kind)
        {
            return TgmConfig.CategoryIcon.TryGetValue(kind, out var entry) ? entry.Value : Categories.DefaultIcon(kind);
        }

        private static bool IsShown(Category kind)
        {
            return !TgmConfig.ShowKind.TryGetValue(kind, out var entry) || entry.Value;
        }

        private static void Place(RectTransform row, float y)
        {
            row.anchorMin = new Vector2(0f, 1f);
            row.anchorMax = new Vector2(1f, 1f);
            row.pivot = new Vector2(0f, 1f);
            row.anchoredPosition = new Vector2(0f, y);
            row.sizeDelta = new Vector2(0f, RowHeight);
        }

        private static void AddRow(Minimap map, Category kind, float y)
        {
            var entry = new Entry { Kind = kind };
            var row = MenuKit.Row(_root.transform, "", true, () =>
            {
                if (TgmConfig.ShowKind.TryGetValue(kind, out var e)) e.Value = !e.Value;
                ClientPins.Restyle();
                Refresh();
            });
            Place(row, y);

            var sprite = IconRegistry.Resolve(map, IconKeyFor(kind)) ?? IconRegistry.Resolve(map, IconRegistry.FallbackKey);
            var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            iconGo.transform.SetParent(row, false);
            var irt = (RectTransform)iconGo.transform;
            irt.anchorMin = new Vector2(0f, 0.5f);
            irt.anchorMax = new Vector2(0f, 0.5f);
            irt.pivot = new Vector2(0f, 0.5f);
            irt.anchoredPosition = new Vector2(8f, 0f);
            irt.sizeDelta = new Vector2(IconSize, IconSize);
            entry.Icon = iconGo.GetComponent<Image>();
            entry.Icon.sprite = sprite;
            entry.Icon.raycastTarget = false;
            entry.Icon.preserveAspect = true;

            entry.Label = row.GetComponentInChildren<TextMeshProUGUI>();
            if (entry.Label != null)
            {
                entry.Label.text = Categories.Label(kind);
                entry.Label.alignment = TextAlignmentOptions.MidlineLeft;
                entry.Label.rectTransform.offsetMin = new Vector2(IconSize + 14f, 0f);
            }
            _entries.Add(entry);
        }

        /// <summary>
        /// Draw each row the way the map draws that kind: full strength when shown, dimmed when
        /// hidden. While the master is off every kind is dimmed, since nothing is drawn either way.
        /// </summary>
        private static void Refresh()
        {
            bool all = TgmConfig.ShowAllMarkers == null || TgmConfig.ShowAllMarkers.Value;
            if (_masterLabel != null)
            {
                _masterLabel.text = all ? "Hide all markers" : "Show all markers";
                _masterLabel.color = MenuKit.Header;
            }
            foreach (var entry in _entries)
            {
                bool shown = all && (!TgmConfig.ShowKind.TryGetValue(entry.Kind, out var e) || e.Value);
                if (entry.Label != null) entry.Label.color = shown ? MenuKit.Body : MenuKit.Dim;
                if (entry.Icon != null) entry.Icon.color = shown ? Color.white : new Color(1f, 1f, 1f, 0.3f);
            }
        }

        internal static void Close()
        {
            if (_root != null) Object.Destroy(_root);
            _root = null;
            _rect = null;
            _masterLabel = null;
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
