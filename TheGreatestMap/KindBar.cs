using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TheGreatestMap
{
    /// <summary>
    /// A column of kind buttons under vanilla's icon buttons on the right edge of the large map,
    /// one per kind that has markers on your map. A click, left or right, toggles that kind's
    /// "Show &lt;Kind&gt;" switch, and the button grays out while the kind is hidden, the way
    /// vanilla's own filter buttons do. Each button is a clone of vanilla's first icon button
    /// with the picture swapped for the kind's icon, so it keeps the game's look at any
    /// resolution or UI scale.
    /// </summary>
    internal static class KindBar
    {
        private sealed class Entry
        {
            public Category Kind;
            public GameObject Root;
            public List<Image> Tinted;
            public bool Shown;
        }

        private static readonly List<Entry> _entries = new List<Entry>();
        private static string _signature = "";
        private static int _lastCount = -1;
        private static float _nextCheck;

        internal static void Reset()
        {
            Clear();
            _signature = "";
            _lastCount = -1;
        }

        private static void Clear()
        {
            foreach (var e in _entries) if (e.Root != null) Object.Destroy(e.Root);
            _entries.Clear();
        }

        internal static void Update()
        {
            var map = Minimap.instance;
            if (map == null || map.m_mode != Minimap.MapMode.Large) return;
            if (TgmConfig.ShowKindButtons == null || !TgmConfig.ShowKindButtons.Value)
            {
                if (_entries.Count > 0) { Clear(); _signature = ""; }
                return;
            }
            bool lost = false;
            foreach (var e in _entries) if (e.Root == null) { lost = true; break; }
            if (lost || ClientPins.Count != _lastCount || Time.unscaledTime >= _nextCheck)
            {
                _lastCount = ClientPins.Count;
                _nextCheck = Time.unscaledTime + 1f;
                var present = PresentKinds();
                string sig = Signature(present);
                if (lost || sig != _signature)
                {
                    _signature = sig;
                    Rebuild(map, present);
                }
            }
            Recolor();
        }

        private static HashSet<Category> PresentKinds()
        {
            var present = new HashSet<Category>();
            foreach (var pin in ClientPins.All)
            {
                var kind = ClientPins.KindOf(pin);
                if (kind.HasValue) present.Add(kind.Value);
            }
            return present;
        }

        private static string Signature(HashSet<Category> present)
        {
            var sb = new StringBuilder();
            foreach (var kind in Categories.All)
                if (present.Contains(kind)) sb.Append(kind).Append(',');
            return sb.ToString();
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

        private static void Rebuild(Minimap map, HashSet<Category> present)
        {
            Clear();
            var template = Template(map);
            if (template == null || present.Count == 0) return;
            var panel = template.transform.parent;
            if (panel == null) return;
            bool autoLayout = panel.GetComponent<LayoutGroup>() != null;

            // Where vanilla's column ends and how far apart its buttons sit.
            var buttons = VanillaButtons(map);
            var templateRect = (RectTransform)template.transform;
            RectTransform lowest = null;
            var ys = new List<float>();
            foreach (var rt in buttons)
            {
                if (rt.parent != panel) continue;
                if (lowest == null || rt.anchoredPosition.y < lowest.anchoredPosition.y) lowest = rt;
                if (!ys.Contains(rt.anchoredPosition.y)) ys.Add(rt.anchoredPosition.y);
            }
            ys.Sort((a, b) => b.CompareTo(a));
            float spacing = ys.Count >= 2 ? ys[0] - ys[1] : templateRect.rect.height + 4f;
            if (spacing <= 0f) spacing = templateRect.rect.height + 4f;

            Sprite vanillaIcon = IconRegistry.Resolve(map, "pin:Icon0");
            string framePath = PathBelow(template.transform, map.m_selectedIcon0.transform);

            int i = 0;
            foreach (var kind in Categories.All)
            {
                if (!present.Contains(kind)) continue;
                string iconKey = TgmConfig.CategoryIcon.TryGetValue(kind, out var iconEntry) ? iconEntry.Value : Categories.DefaultIcon(kind);
                var sprite = IconRegistry.Resolve(map, iconKey) ?? IconRegistry.Resolve(map, IconRegistry.FallbackKey);

                var go = Object.Instantiate(template, panel);
                go.name = "TGM_Kind_" + kind;
                go.SetActive(true);

                // Vanilla's own click handling on the clone is disarmed; ours goes on instead.
                foreach (var b in go.GetComponentsInChildren<Button>(true)) b.onClick = new Button.ButtonClickedEvent();
                foreach (var h in go.GetComponentsInChildren<UIInputHandler>(true))
                {
                    h.m_onLeftClick = null; h.m_onLeftDown = null; h.m_onLeftUp = null;
                    h.m_onRightClick = null; h.m_onRightDown = null; h.m_onRightUp = null;
                    h.m_onMiddleClick = null; h.m_onMiddleDown = null; h.m_onMiddleUp = null;
                    h.m_onPointerEnter = null; h.m_onPointerExit = null;
                }

                // The picture: every image showing the Fire pin gets the kind's icon; the root
                // image is what vanilla tints gray when filtered, so it is tinted here too.
                var tinted = new List<Image>();
                foreach (var img in go.GetComponentsInChildren<Image>(true))
                {
                    if (vanillaIcon != null && img.sprite == vanillaIcon) { img.sprite = sprite; tinted.Add(img); }
                }
                var rootImage = go.GetComponent<Image>();
                if (tinted.Count == 0 && rootImage != null && sprite != null) { rootImage.sprite = sprite; tinted.Add(rootImage); }
                if (rootImage != null && !tinted.Contains(rootImage)) tinted.Add(rootImage);

                // No "selected" frame: these buttons only toggle.
                var frame = framePath != null ? go.transform.Find(framePath) : null;
                var frameImage = frame != null ? frame.GetComponent<Image>() : null;
                if (frameImage != null) frameImage.enabled = false;

                var tooltip = go.GetComponent<UITooltip>();
                if (tooltip != null) { tooltip.m_topic = ""; tooltip.m_text = Categories.Label(kind); }

                go.AddComponent<KindButton>().Kind = kind;

                if (!autoLayout && lowest != null)
                {
                    var rt = (RectTransform)go.transform;
                    rt.anchoredPosition = new Vector2(lowest.anchoredPosition.x, lowest.anchoredPosition.y - spacing * (i + 1));
                }
                go.transform.SetAsLastSibling();

                _entries.Add(new Entry { Kind = kind, Root = go, Tinted = tinted, Shown = true });
                i++;
            }
            Recolor(force: true);
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
            foreach (var e in _entries)
            {
                if (e.Root == null) continue;
                bool shown = !TgmConfig.ShowKind.TryGetValue(e.Kind, out var entry) || entry.Value;
                if (!force && shown == e.Shown) continue;
                e.Shown = shown;
                var color = shown ? Color.white : Color.gray;
                foreach (var img in e.Tinted) if (img != null) img.color = color;
            }
        }

        internal static void Toggle(Category kind)
        {
            if (!TgmConfig.ShowKind.TryGetValue(kind, out var entry)) return;
            entry.Value = !entry.Value;
            string label = Categories.Label(kind).ToLowerInvariant();
            TheGreatestMapMod.Message(entry.Value ? $"Showing {label}." : $"Hiding {label}.");
            Recolor();
        }

        /// <summary>Left or right click on a kind button toggles the kind.</summary>
        internal sealed class KindButton : MonoBehaviour, IPointerClickHandler
        {
            public Category Kind;

            public void OnPointerClick(PointerEventData eventData)
            {
                if (eventData.button != PointerEventData.InputButton.Left && eventData.button != PointerEventData.InputButton.Right) return;
                KindBar.Toggle(Kind);
            }
        }
    }
}
