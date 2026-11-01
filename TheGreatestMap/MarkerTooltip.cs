using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TheGreatestMap
{
    /// <summary>
    /// What the marker under the pointer actually is. This earns its place because the map is
    /// deliberately short on text: dungeons, camps, structures and plants carry no label by
    /// default, which is what keeps a map with hundreds of markers readable. The information is
    /// still worth having, just not painted over the world, so hovering is where it lives.
    /// </summary>
    internal static class MarkerTooltip
    {
        private const float Width = 230f;
        private const float LineHeight = 17f;
        private const float TitleHeight = 20f;
        private const float Pad = 6f;
        private const float Interval = 0.05f;

        private static GameObject _root;
        private static RectTransform _rect;
        private static TextMeshProUGUI _title;
        private static readonly List<TextMeshProUGUI> _lines = new List<TextMeshProUGUI>();
        private static Camera _camera;
        private static string _shownId;
        private static float _next;

        internal static void Reset()
        {
            if (_root != null) UnityEngine.Object.Destroy(_root);
            _root = null;
            _rect = null;
            _title = null;
            _lines.Clear();
            _shownId = null;
        }

        internal static void Update()
        {
            var map = Minimap.instance;
            if (map == null) { Hide(); return; }
            if (map.m_mode != Minimap.MapMode.Large) { Hide(); return; }
            // One of our own buttons under the pointer explains itself, and does so ahead of
            // everything below: the pointer being over a panel is exactly when the rest of this
            // stands down, so a button's tooltip has to be answered before that rule applies.
            var hint = MenuKit.Hint.Over;
            if (hint != null) { ShowHint(map, hint); return; }
            if (TgmConfig.MarkerTooltips == null || !TgmConfig.MarkerTooltips.Value) { Hide(); return; }
            // Over a menu rather than over the map: a tooltip about whatever marker lies under
            // the panel would be answering a question nobody asked. Being open is not enough --
            // the list can be pinned open for a whole session, and suppressing tooltips for that
            // long made hovering useless, which is how an evening went looking for a tooltip bug
            // that was this line.
            if (MarkerMenu.Hovered) { Hide(); return; }
            if (KindMenu.Hovered) { Hide(); return; }
            if (LabelEditor.Hovered) { Hide(); return; }
            if (Portals.PortalListHovered()) { Hide(); return; }
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + Interval;

            var world = Access.ScreenToWorldPoint(map, ZInput.pointerPosition);
            float radius = Access.PinInteractRadius(map);
            var pin = ClientPins.ClosestPin(map, world, radius);
            var shared = ClientPins.SharedFor(pin);
            if (shared != null) { Show(map, shared); return; }
            // A ship or a cart, which is not a marker at all and so is not among the pins any of
            // the usual lookups walk. Worth naming on hover above all when the labels are off,
            // which they are by default: otherwise the map shows a hull and no word about it.
            var vessel = Transports.HoveredAt(world, radius);
            if (vessel != null) { ShowVessel(map, vessel); return; }
            Hide();
        }

        /// <summary>Everything the tooltip reads, so any change to it is a change on screen.</summary>
        private static string Describes(SharedPin shared)
        {
            // Encode rather than ToString: this has to tell two markers apart, and ToString
            // rounds the position to two decimals for the sake of a readable log line.
            return shared.Key.Encode() + "|" + (shared.Checked ? "1" : "0") + "|" + shared.Name + "|" + ClientPins.IconOf(shared)
                 + "|" + shared.Author + "|" + shared.Created;
        }

        private static void Hide()
        {
            if (_root != null && _root.activeSelf) _root.SetActive(false);
            _shownId = null;
        }

        /// <summary>
        /// A vessel's tooltip. It says plainly that this is where the thing is rather than where
        /// somebody found it, because everything else the map shows is the opposite, and there is
        /// no finder and no date to show for it -- a ship has no history here, only a position.
        /// </summary>
        private static void ShowHint(Minimap map, MenuKit.Hint hint)
        {
            var parent = map.m_largeRoot != null ? map.m_largeRoot.transform as RectTransform : null;
            if (parent == null) return;
            if (_root == null) Build(parent);
            if (_root == null) return;
            string key = "hint|" + hint.Title + "|" + hint.Body;
            if (key != _shownId)
            {
                _shownId = key;
                Write(hint.Title, hint.Lines());
            }
            if (!_root.activeSelf) _root.SetActive(true);
            Place(parent);
        }

        private static void ShowVessel(Minimap map, Transports.Hovered vessel)
        {
            var parent = map.m_largeRoot != null ? map.m_largeRoot.transform as RectTransform : null;
            if (parent == null) return;
            if (_root == null) Build(parent);
            if (_root == null) return;

            string key = "vessel|" + vessel.Title + "|" + vessel.Prefab;
            if (key != _shownId)
            {
                _shownId = key;
                var lines = new List<string> { vessel.Kind + ", where it is now" };
                if (!string.Equals(vessel.Prefab, vessel.Title, StringComparison.OrdinalIgnoreCase))
                    lines.Add(vessel.Prefab);
                Write(vessel.Title, lines);
            }
            if (!_root.activeSelf) _root.SetActive(true);
            Place(parent);
        }

        private static void Show(Minimap map, SharedPin shared)
        {
            var parent = map.m_largeRoot != null ? map.m_largeRoot.transform as RectTransform : null;
            if (parent == null) return;
            if (_root == null) Build(parent);
            if (_root == null) return;

            // Keyed on what the tooltip says, not on which marker it is: crossing one off while
            // the pointer rests on it changes the words without changing the marker, and the
            // tooltip would go on claiming the opposite of what was just clicked.
            string key = Describes(shared);
            if (key != _shownId)
            {
                _shownId = key;
                Fill(shared);
            }
            if (!_root.activeSelf) _root.SetActive(true);
            Place(parent);
        }

        /// <summary>Title plus up to three quiet lines; unused ones are switched off rather than rebuilt.</summary>
        private static void Fill(SharedPin shared)
        {
            var kind = ClientPins.KindOf(shared);
            // What this one is, which for a dungeon or a structure is not the same as its kind: a
            // frost cave, not "Dungeons". Most markers carry no label on the map, so the tooltip is
            // where a player reads it, and naming the kind alone told them what they could see.
            // What it is, preferred over what its icon suggests: a marker that remembers its own
            // prefab can say "Swamp Hut" where the house pin could only ever say "Structures".
            string what = Catalog.NameForSource(shared.Source);
            if (what == null && kind.HasValue) what = Catalog.TypeNameFor(kind.Value, ClientPins.IconOf(shared));
            if (what == null && kind.HasValue) what = Categories.Label(kind.Value);

            string title = !string.IsNullOrEmpty(shared.Name) ? shared.Name
                : (what ?? IconRegistry.DisplayName(ClientPins.IconOf(shared)));

            var lines = new List<string>();
            // A second line only when it says something the title does not. A marker named by its
            // finder keeps that name above and says what it is below; one already titled by what
            // it is has nothing left to repeat.
            bool said = what == null || string.Equals(what, title, StringComparison.OrdinalIgnoreCase);
            if (!shared.Auto) lines.Add(said ? "Placed marker" : "Placed - " + what);
            else if (!said) lines.Add(what);

            if (shared.Checked)
                lines.Add(kind.HasValue && Categories.IsResource(kind.Value) ? "Cleared" : "Searched");

            // The raw icon key, which is the field that goes wrong: a marker showing a plain dot
            // is usually one whose key names an item this game does not have, and nothing else on
            // screen says so. The prefab it came from is not stored on a marker, so that level of
            // detail lives in the legend rather than here.
            lines.Add(ClientPins.IconOf(shared));

            string who = !string.IsNullOrEmpty(shared.Author) ? shared.Author : null;
            string when = When(shared.Created);
            if (who != null && when != null) lines.Add($"{who}, {when}");
            else if (who != null) lines.Add(who);
            else if (when != null) lines.Add(when);

            Write(title, lines);
        }

        /// <summary>Put a title and some lines in the panel, switching off the ones left over.</summary>
        private static void Write(string title, List<string> lines)
        {
            // No strip of empty panel above the text when there is no title to put in it. Most of
            // the tooltips on the marker list carry only a body.
            bool titled = !string.IsNullOrEmpty(title);
            if (_title.gameObject.activeSelf != titled) _title.gameObject.SetActive(titled);
            _title.text = title ?? "";

            while (_lines.Count < lines.Count) AddLine();
            float inner = Width - 20f;          // what Stretch leaves between the panel's sides
            float y = Pad + (titled ? TitleHeight : 0f);
            for (int i = 0; i < _lines.Count; i++)
            {
                bool used = i < lines.Count;
                if (_lines[i].gameObject.activeSelf != used) _lines[i].gameObject.SetActive(used);
                if (!used) continue;
                var line = _lines[i];
                line.text = lines[i];
                // Asked of the text itself at the width it has to fit: a line of one word and a
                // line of forty take the room each of them needs.
                float height = Mathf.Max(LineHeight, line.GetPreferredValues(lines[i], inner, 0f).y);
                Stretch(line.rectTransform, -y, height);
                y += height;
            }
            _rect.sizeDelta = new Vector2(Width, y + Pad);
        }

        private static string When(long ticks)
        {
            if (ticks <= 0L) return null;
            try
            {
                var then = new DateTime(ticks, DateTimeKind.Utc).ToLocalTime();
                var age = DateTime.Now - then;
                if (age.TotalMinutes < 1) return "just now";
                if (age.TotalHours < 1) return $"{(int)age.TotalMinutes} min ago";
                if (age.TotalDays < 1) return $"{(int)age.TotalHours} h ago";
                if (age.TotalDays < 7) return $"{(int)age.TotalDays} d ago";
                return then.ToString("d MMM");
            }
            catch (Exception) { return null; }
        }

        private static void Build(RectTransform parent)
        {
            var panel = MenuKit.Panel(parent, "TGM_MarkerTooltip");
            _root = panel.gameObject;
            _rect = panel.rectTransform;
            _rect.anchorMin = _rect.anchorMax = new Vector2(0f, 1f);
            _rect.pivot = new Vector2(0f, 1f);
            _rect.sizeDelta = new Vector2(Width, 80f);
            panel.raycastTarget = false; // never eat a click meant for the map

            _title = MenuKit.Text(_root.transform, "Title", "", 15f, MenuKit.Header);
            Stretch(_title.rectTransform, -Pad, TitleHeight);
            _title.alignment = TextAlignmentOptions.MidlineLeft;

            for (int i = 0; i < 4; i++) AddLine();
        }

        /// <summary>
        /// One line of the tooltip, which may take several lines of its own: these wrap and are
        /// measured, rather than being cut off at the panel's edge. Where a caller hands over a
        /// whole paragraph -- which is what a tooltip's text is -- that is one of these.
        /// </summary>
        private static TextMeshProUGUI AddLine()
        {
            var line = MenuKit.Text(_root.transform, "Line" + _lines.Count, "", 13f, MenuKit.Dim);
            line.alignment = TextAlignmentOptions.TopLeft;
            line.textWrappingMode = TextWrappingModes.Normal;
            line.overflowMode = TextOverflowModes.Overflow;
            _lines.Add(line);
            return line;
        }

        private static void Stretch(RectTransform rt, float y, float height)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(10f, y);
            rt.sizeDelta = new Vector2(-20f, height);
        }

        /// <summary>Beside the pointer, flipping to the other side rather than running off the map.</summary>
        private static void Place(RectTransform parent)
        {
            var canvas = parent.GetComponentInParent<Canvas>();
            _camera = canvas != null && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.rootCanvas.worldCamera : null;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, ZInput.pointerPosition, _camera, out var local);
            var pr = parent.rect;
            float height = _rect.sizeDelta.y;
            float x = local.x - pr.xMin + 18f;
            float top = pr.yMax - local.y + 18f;
            if (x + Width > pr.width) x = Mathf.Max(0f, x - Width - 36f);
            if (top + height > pr.height) top = Mathf.Max(0f, top - height - 36f);
            _rect.anchoredPosition = new Vector2(x, -top);
            _root.transform.SetAsLastSibling();
        }
    }
}
