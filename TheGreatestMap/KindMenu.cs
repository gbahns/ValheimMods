using System;
using BepInEx.Configuration;
using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace TheGreatestMap
{
    /// <summary>
    /// The list behind a left click on the marker button, in two columns. On the left, one row per
    /// kind that has markers on your map, showing its icon. On the right, the markers that have been
    /// crossed off: used-up deposits as one switch, and searched places by type, so burial chambers
    /// you are done with can go while the sunken crypts stay. Clicking a row changes that one thing
    /// and leaves the list open, so several can be changed at once.
    /// </summary>
    internal static class KindMenu
    {
        private const float ColumnWidth = 250f;
        private const float Gap = 8f;
        private const float Width = ColumnWidth * 2f + Gap;
        private const float RowHeight = 26f;
        private const float HeaderHeight = 26f;
        private const float Pad = 4f;
        private const float IconSize = 20f;
        private const float Indent = 16f;

        /// <summary>
        /// A switch row is an icon and a name, drawn the way the map draws what it covers: full
        /// strength when shown, dimmed when hidden. An action row is gold text saying what a click
        /// will do, as the master row does.
        /// </summary>
        private sealed class Entry
        {
            public Func<bool> Shown;
            public Func<string> Text;
            /// <summary>Live text for a switch row, for one whose label can change while the list is open.</summary>
            public Func<string> Caption;
            public Func<bool> Quiet;
            public TextMeshProUGUI Label;
            public Image Icon;
        }

        private static readonly List<Entry> _entries = new List<Entry>();

        /// <summary>
        /// One per small button that can be on: armed for placing, or filtering the map. Run
        /// whenever the list is refreshed, which is after every press on it.
        /// </summary>
        private static readonly List<Action> _lights = new List<Action>();
        private static GameObject _root;
        private static RectTransform _rect;
        private static Camera _camera;
        private static int _openedFrame = -10;
        private static int _closedFrame = -10;
        private static TMP_InputField _find;
        private static Vector2 _screenPos;
        private static List<Focus.TypeCount> _results;
        private static string _pendingText;
        private static bool _rebuilding;
        private static bool _rebuildWanted;

        internal static bool IsOpen => _root != null;

        /// <summary>
        /// Is the pointer over the list itself? Not the same question as whether the list is
        /// open, which is what anything wanting to stand out of its way used to ask. Pinned open
        /// it is furniture at the edge of the map, and the map behind it stays as usable as it
        /// ever was -- so "open" stopped being a reason for anything else to stand down.
        /// </summary>
        internal static bool Hovered =>
            _root != null && _rect != null
            && RectTransformUtility.RectangleContainsScreenPoint(_rect, ZInput.pointerPosition, _camera);

        private static bool Pinned => TgmConfig.PinMarkerMenu != null && TgmConfig.PinMarkerMenu.Value;

        /// <summary>
        /// Where a pinned list sits, as a fraction of the map so it lands in the same place on
        /// any screen. Remembered across sessions, because the point of pinning it is not having
        /// to put it back.
        /// </summary>
        private static Vector2 PinnedSpot
        {
            get
            {
                var v = TgmConfig.PinnedMenuSpot != null ? TgmConfig.PinnedMenuSpot.Value : Vector2.zero;
                return v;
            }
            set { if (TgmConfig.PinnedMenuSpot != null) TgmConfig.PinnedMenuSpot.Value = value; }
        }

        /// <summary>
        /// Put a pinned list back when the map opens, without a click. Called while the large map
        /// is up; does nothing once it is already there.
        /// </summary>
        internal static void Restore(Minimap map)
        {
            if (!Pinned || _root != null || map == null || map.m_largeRoot == null) return;
            if (ClientPins.Count == 0) return;
            var parent = map.m_largeRoot.transform as RectTransform;
            if (parent == null) return;
            var spot = PinnedSpot;
            var rect = parent.rect;
            // A spot of nothing means it has never been pinned anywhere: open where the button is.
            var screen = spot == Vector2.zero
                ? new Vector2(Screen.width * 0.72f, Screen.height * 0.75f)
                : new Vector2(spot.x * Screen.width, spot.y * Screen.height);
            _closedFrame = -10;
            Open(screen);
        }

        private static Image _pinImage;
        private static MenuKit.Glow _pinGlow;

        /// <summary>
        /// The stick pin in the corner: pressed, the list stops closing when you click elsewhere
        /// and comes back by itself with the map. A picture rather than a row because it is about
        /// the list itself rather than about any of the markers in it, and because the corner is
        /// where a window's own controls belong.
        /// </summary>
        private static void BuildPin()
        {
            // Two objects, because a rect turns about its pivot and this one's pivot is its
            // top right corner -- that is how it is pinned to the corner of the panel. Rotating
            // that rect swings the whole icon out of the panel instead of turning it. So the
            // outer one only places and catches the click, and the picture inside it, centered in
            // its own square, is what turns.
            var go = new GameObject("Pin", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
                                    typeof(Button), typeof(MenuKit.Glow));
            go.transform.SetParent(_root.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-6f, -6f);
            rt.sizeDelta = new Vector2(22f, 22f);
            var hit = go.GetComponent<Image>();
            hit.color = new Color(1f, 1f, 1f, 0.07f);
            hit.raycastTarget = true;

            var face = new GameObject("Tack", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            face.transform.SetParent(go.transform, false);
            var frt = (RectTransform)face.transform;
            frt.anchorMin = frt.anchorMax = frt.pivot = new Vector2(0.5f, 0.5f);
            frt.anchoredPosition = Vector2.zero;
            frt.sizeDelta = new Vector2(16f, 16f);
            _pinImage = face.GetComponent<Image>();
            _pinImage.sprite = MenuKit.Tack();
            _pinImage.preserveAspect = true;
            _pinImage.raycastTarget = false;

            _pinGlow = go.GetComponent<MenuKit.Glow>();

            var button = go.GetComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = hit;
            button.onClick.AddListener(() =>
            {
                if (TgmConfig.PinMarkerMenu == null) return;
                bool now = !TgmConfig.PinMarkerMenu.Value;
                TgmConfig.PinMarkerMenu.Value = now;
                if (now) RememberSpot();
                PaintPin();
            });
            PaintPin();
        }

        private static void PaintPin()
        {
            if (_pinImage == null) return;
            bool over = _pinGlow != null && _pinGlow.Over;
            _pinImage.color = over ? MenuKit.HoverColor
                : Pinned ? new Color(1f, 0.85f, 0.45f, 1f)
                : new Color(0.75f, 0.73f, 0.7f, 0.55f);
            ((RectTransform)_pinImage.transform).localRotation = Quaternion.Euler(0f, 0f, Pinned ? 0f : -90f);
        }

        /// <summary>Remember where it is now, so pinning it puts it back here next time.</summary>
        private static void RememberSpot()
        {
            if (_rect == null || _camera != null) { }
            if (_rect == null) return;
            var corners = new Vector3[4];
            _rect.GetWorldCorners(corners);
            var top = RectTransformUtility.WorldToScreenPoint(_camera, corners[1]);
            if (Screen.width > 0 && Screen.height > 0)
                PinnedSpot = new Vector2(top.x / Screen.width, top.y / Screen.height);
        }

        /// <summary>
        /// True while the find box has the keyboard. Every hotkey in this mod is gated on it, and
        /// the game's own are too, by way of the TextInput patch below -- otherwise searching for
        /// a frost cave takes the map out, opens the inventory and lights a torch on the way.
        /// </summary>
        internal static bool Typing => _find != null && _find.isFocused;

        private static bool AllShown => TgmConfig.ShowAllMarkers == null || TgmConfig.ShowAllMarkers.Value;

        internal static void Open(Vector2 screenPos)
        {
            // A click on the button while the list is open closes it. Whether that click reaches
            // the button or the "clicked away" check first depends on script order, so a list
            // closed this frame cannot be reopened by the same click.
            if (Time.frameCount == _closedFrame) return;
            Close();
            _screenPos = screenPos;
            if (!_rebuilding) _results = null;
            var map = Minimap.instance;
            if (map == null || map.m_largeRoot == null) return;
            var parent = map.m_largeRoot.transform as RectTransform;
            if (parent == null) return;
            if (ClientPins.Count == 0)
            {
                TheGreatestMapMod.Message("No markers on your map yet.");
                return;
            }

            var seen = new HashSet<Category>();
            int cleared = 0;
            var searched = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var groupIcon = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pin in ClientPins.All)
            {
                var kind = ClientPins.KindOf(pin);
                if (kind.HasValue) seen.Add(kind.Value);
                if (!pin.Checked) continue;
                if (CrossedOff.IsCleared(pin)) { cleared++; continue; }
                string group = CrossedOff.GroupOf(pin);
                searched.TryGetValue(group, out int n);
                searched[group] = n + 1;
                if (!groupIcon.ContainsKey(group)) groupIcon[group] = ClientPins.IconOf(pin);
            }

            var panel = MenuKit.Panel(parent, "TGM_KindMenu");
            _root = panel.gameObject;
            _rect = panel.rectTransform;
            _rect.anchorMin = _rect.anchorMax = new Vector2(0f, 1f);
            _rect.pivot = new Vector2(0f, 1f);
            _entries.Clear();

            float left = BuildKinds(map, seen);
            float right = BuildCrossedOff(map, cleared, searched, groupIcon);
            float y = Mathf.Min(left, right);

            var divider = new GameObject("Divider", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            divider.transform.SetParent(_root.transform, false);
            var dimg = divider.GetComponent<Image>();
            dimg.color = new Color(MenuKit.OutlineColor.r, MenuKit.OutlineColor.g, MenuKit.OutlineColor.b, 0.35f);
            dimg.raycastTarget = false;
            Place((RectTransform)divider.transform, ColumnWidth + Gap * 0.5f - 0.5f, -Pad, 1f, -y - Pad);

            // Everything back: the kinds, the master switch, the crossed-off groups, the icons
            // hidden one picture at a time and the markers hidden one at a time. The same work as
            // tgm_show, and it has to be, because this is the button people press.
            //
            // It used to put back only the kinds, the master switch and the crossed-off groups,
            // which left "Hide all Cloudberries markers" with no way back on the map at all: the
            // one control that undid it lived in a marker's own menu, and every marker it applied
            // to was hidden. A button called "Show all" that shows most of it is worse than no
            // button, because it answers the question and the answer is wrong.
            var showAll = MenuKit.Row(_root.transform, "Show all", true, () =>
            {
                ViewPrefs.Clear();
                TgmConfig.ShowEverything();
                ClientPins.Restyle();
                Refresh();
            });
            Place(showAll, 0f, y, Width, RowHeight);
            y -= RowHeight;

            // Side by side, because they are the same question asked of two things, and a player
            // deciding to quieten the map wants to see both answers at once rather than hunt for
            // the second one. Half the panel each, which is a column's width -- the longest
            // either says is "Hide Marker Labels".
            ActionRow(0f, y, Erasures.Toggle,
                () => Erasures.On ? "Hide erased markers" : "Show erased markers", null);
            y -= RowHeight;

            float half = (Width - Gap) * 0.5f;
            LabelButton(0f, y, half,
                () => TgmConfig.ShowMarkerLabels,
                () => TgmConfig.ShowMarkerLabels != null && TgmConfig.ShowMarkerLabels.Value
                    ? "Hide Marker Labels" : "Show Marker Labels");
            LabelButton(half + Gap, y, half,
                () => TgmConfig.ShowPortalLabels,
                () => TgmConfig.ShowPortalLabels != null && TgmConfig.ShowPortalLabels.Value
                    ? "Hide Portal Labels" : "Show Portal Labels");
            y -= RowHeight;

            // Pinning is about this list's own behavior rather than about the markers, so it sits
            // apart from them at the foot, next to the other row that acts on the whole list.

            float height = -y + Pad;
            _rect.sizeDelta = new Vector2(Width, height);
            BuildPin();

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
            if (_pendingText != null && _find != null)
            {
                _find.SetTextWithoutNotify(_pendingText);
                _find.ActivateInputField();
            }
            _pendingText = null;
            Refresh();
        }

        /// <summary>
        /// Draw the list again in place, keeping what was typed. Used when the rows themselves have
        /// to change rather than only their colors -- showing the types that matched instead of the
        /// kinds. The close is ours rather than a click elsewhere, so it must not count as the
        /// click-away that stops the list reopening in the same frame.
        /// </summary>
        private static void Rebuild()
        {
            if (_root == null) return;
            _rebuildWanted = false;
            _pendingText = _find != null ? _find.text : null;
            var pos = _screenPos;
            _rebuilding = true;
            Close();
            _closedFrame = -10;
            try { Open(pos); }
            finally { _rebuilding = false; }
        }

        /// <summary>The left column. Returns the y below its last row.</summary>
        private static float BuildKinds(Minimap map, HashSet<Category> seen)
        {
            const float x = 0f;
            float y = -Pad;
            Header(_results != null
                ? "Matches   only / ping"
                : "Marker kinds   + place, only, ping", x, y);
            y -= HeaderHeight;

            FindBox(x, y);
            y -= RowHeight;

            // What the map is showing, which is also how to stop: the row is the way back to
            // everything. It is built whether or not anything is focused, because focusing happens
            // by typing in the box above it and the row has to be there to say so.
            ActionRow(x, y, Focus.Clear, () =>
                {
                    Focus.Tally(out int shown, out int all);
                    return Focus.On
                        ? $"Only {Focus.What()} — {shown} of {all}, click for all"
                        : $"Showing all {all} markers";
                },
                () => !Focus.On);
            y -= RowHeight;

            // The master, so it is reachable from this list too, not only from a right click on the button.
            ActionRow(x, y, () => { MarkerToggle.ToggleAll(); Refresh(); },
                () => AllShown ? "Hide all markers" : "Show all markers", null);
            y -= RowHeight;


            if (_results != null)
            {
                ActionRow(x, y, () =>
                    {
                        // Empty the box as well, or the kinds come back with a search still
                        // written above them and nothing being searched for.
                        if (_find != null) _find.SetTextWithoutNotify("");
                        Focus.Clear();
                        _results = null;
                        _rebuildWanted = true;
                    },
                    () => "Back to the kinds", null);
                y -= RowHeight;
                if (_results.Count == 0)
                {
                    var none = MenuKit.Text(_root.transform, "None", "Nothing on the map matches.", 13f, MenuKit.Dim);
                    Place(none.rectTransform, x + 10f, y, ColumnWidth - 20f, RowHeight);
                    none.alignment = TextAlignmentOptions.MidlineLeft;
                    y -= RowHeight;
                }
                foreach (var match in _results)
                {
                    var m = match;
                    SwitchRow(map, x, y, 0f, m.Icon, $"{m.Label} ({m.Count})",
                        () => Focus.SetIcon(m.Icon),
                        () => Focus.Icon != null && IconRegistry.SameKey(Focus.Icon, m.Icon),
                        onLocate: () => Locate.Icon(m.Icon, m.Label));
                    y -= RowHeight;
                }
                return y;
            }

            // Every kind that has markers, vanilla-icon ones included. Structures, portals, camps
            // and boss altars are also hidden and shown by vanilla's own icon buttons, so listing
            // them here is redundant, but leaving them out was worse: a player looking for the
            // switch expects to find it with the rest, and does not care which mod owns the icon.
            // The two are not identical, and the difference is in our favor: vanilla's button hides
            // every pin with that icon, while this one covers only the markers this mod recorded.
            foreach (var kind in Categories.All)
            {
                // A kind that is only ever placed by hand has no markers until one is placed, so
                // it must be listed anyway -- it is the list that places it. A live kind has no
                // markers ever, its pins being the server's answer, so it is listed as soon as
                // there is an answer to draw from.
                bool live = Categories.IsLive(kind)
                    && (kind == Category.Portal ? Portals.HaveList : Transports.HaveList);
                // Players have a row of their own further down -- the live one, which is also
                // where a hand-placed player marker is armed and filtered -- so the kind is not
                // listed twice.
                if (kind == Category.Player) continue;
                if (!seen.Contains(kind) && !Categories.IsPlacedOnly(kind) && !live
                    && !Categories.VanillaMarks(kind)) continue;
                var k = kind;
                SwitchRow(map, x, y, 0f, IconKeyFor(k), Categories.Label(k),
                    () => { if (TgmConfig.ShowKind.TryGetValue(k, out var e)) e.Value = !e.Value; },
                    () => AllShown && IsShown(k),
                    // The small pin at the row's end arms that kind for placing by hand. It is a
                    // button of its own rather than another meaning for the row, which already
                    // means "show or hide these". A live kind cannot be placed: a hand-written
                    // ship would be a memory of a thing that moves, which is the whole reason
                    // these are not recorded.
                    //
                    // A kind the game marks by itself can be: nothing records a trader, because
                    // vanilla already pins the ones you have met, but "the trader is over there"
                    // is a perfectly good thing to write down for somebody who has not met him,
                    // and refusing to let a player place one was a rule about recording leaking
                    // into a rule about writing.
                    Categories.IsLive(k)
                        ? (Action)null
                        : () => {
                            // Pressing it again puts the arming away, which is what a button that
                            // stays lit has to mean -- there was no way to stop placing short of
                            // closing the map.
                            if (ManualPlace.Armed.HasValue && ManualPlace.Armed.Value == k)
                            {
                                ManualPlace.Cancel();
                                Refresh();
                                return;
                            }
                            if (!ManualPlace.Arm(map, k)) return;
                            if (Pinned) Refresh(); else Close();
                        },
                    // And "only": the same row's switch would need twenty others turned off to say
                    // this, so it says it in one press, and says it again to take it back.
                    () => Focus.SetKind(k),
                    // And the ping, which answers the same question the other way round: leave
                    // the map as it is and make these ones wave. Live kinds answer it too, even
                    // though the server drew those pins rather than this map.
                    () => Locate.Kind(k),
                    placeKind: k);
                y -= RowHeight;
            }

            // The other players, which are vanilla's markers rather than ours: nothing here
            // records them and nothing hides them, but "where is everybody" is the same question
            // this column answers for everything else, so it belongs in the same place. The row
            // itself is the spotlight the H key gives; the ping only makes them wave.
            // Shown even when there are none, dimmed, saying so. Leaving the row out entirely
            // when nobody is sharing looks exactly like the feature not existing, which is how
            // it read the first time somebody went looking for it. The count is read afresh on
            // every refresh rather than fixed when the list opened: players come and go, and a
            // pinned list stays open for hours.
            Func<int> players = () => PlayerSpotlight.Players(map);
            SwitchRow(map, x, y, 0f, "pin:Player", "Players",
                () =>
                {
                    if (players() > 0) PlayerSpotlight.Toggle(map);
                    else TheGreatestMapMod.Message("No other players are sharing their position.");
                },
                () => players() > 0,
                // Placing and filtering work on the markers people leave about each other --
                // where somebody is building, where they went down -- which is a different thing
                // from the live positions the row itself switches, and the only sensible place
                // to keep it is the row already about people.
                onPlace: () =>
                {
                    if (ManualPlace.Armed.HasValue && ManualPlace.Armed.Value == Category.Player)
                    {
                        ManualPlace.Cancel();
                        Refresh();
                        return;
                    }
                    if (!ManualPlace.Arm(map, Category.Player)) return;
                    if (Pinned) Refresh(); else Close();
                },
                onOnly: () => Focus.SetKind(Category.Player),
                onLocate: () => Locate.Players(map),
                caption: () => players() > 0 ? $"Players ({players()})" : "Players (none sharing)",
                placeKind: Category.Player);
            y -= RowHeight;
            return y;
        }

        /// <summary>
        /// The right column. Used-up deposits first, then searched places: structures on their own,
        /// then dungeons with one switch for all of them and one per type, then anything else that
        /// has been crossed off. A searched group appears once something in it has been crossed off;
        /// before that, the marker's own right-click menu is where the choice comes up.
        /// </summary>
        private static float BuildCrossedOff(Minimap map, int cleared, Dictionary<string, int> searched, Dictionary<string, string> groupIcon)
        {
            float x = ColumnWidth + Gap;
            float y = -Pad;
            Header("Crossed off", x, y);
            y -= HeaderHeight;

            ActionRow(x, y, () =>
                {
                    if (TgmConfig.ShowClearedDeposits != null) TgmConfig.ShowClearedDeposits.Value = !TgmConfig.ShowClearedDeposits.Value;
                },
                () => $"{(TgmConfig.ShowClearedDeposits == null || TgmConfig.ShowClearedDeposits.Value ? "Hide" : "Show")} cleared deposits ({cleared})",
                () => cleared == 0);
            y -= RowHeight;

            void Group(string group, float indent)
            {
                if (!searched.TryGetValue(group, out int count)) return;
                groupIcon.TryGetValue(group, out var icon);
                var g = group;
                SwitchRow(map, x, y, indent, icon, $"{g} ({count})",
                    () => CrossedOff.SetGroupHidden(g, !CrossedOff.IsGroupHidden(g)),
                    () => AllShown && !CrossedOff.IsGroupHidden(g));
                y -= RowHeight;
            }

            // Dungeons and structures are each several things wearing one kind, so each gets a
            // switch for the lot and a row per type under it.
            foreach (var kind in Categories.All)
            {
                if (!CrossedOff.Splits(kind)) { Group(Categories.Label(kind), 0f); continue; }
                var groups = CrossedOff.GroupsOf(kind);
                int total = 0;
                foreach (var group in groups)
                    if (searched.TryGetValue(group, out int n)) total += n;
                var owner = kind;
                string label = Categories.Label(kind).ToLowerInvariant();
                ActionRow(x, y, () => CrossedOff.SetTypesHidden(owner, AnyShown(owner)),
                    () => $"{(AnyShown(owner) ? "Hide" : "Show")} searched {label} ({total})",
                    () => total == 0);
                y -= RowHeight;
                foreach (var group in groups) Group(group, Indent);
            }

            foreach (var kind in Categories.All)
            {
                if (Categories.IsResource(kind) || CrossedOff.HasTypes(kind)) continue;   // done above
                Group(Categories.Label(kind), 0f);
            }
            Group(CrossedOff.Placed, 0f);
            Group(CrossedOff.Unsorted, 0f);
            return y;
        }

        private static bool AnyShown(Category cat)
        {
            foreach (var group in CrossedOff.GroupsOf(cat))
                if (!CrossedOff.IsGroupHidden(group)) return true;
            return false;
        }

        /// <summary>
        /// A button of its own at the end of a row, so the row keeps meaning what it meant: a small
        /// pin that arms the kind for placing by hand, and "only", which narrows the map to it.
        /// Written rather than drawn as a glyph, because the game's font is a display face and what
        /// it has beyond the Latin alphabet cannot be relied on.
        /// </summary>
        /// <summary>The same button with a picture in it, for an action a word would only clutter.</summary>
        private static void RowIcon(RectTransform row, Sprite sprite, float right, float width, Action onClick,
                                    Func<bool> lit = null, string tipTopic = null, string tip = null)
        {
            var go = new GameObject("RowIcon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            go.transform.SetParent(row, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = new Vector2(-right, 0f);
            rt.sizeDelta = new Vector2(width, RowHeight - 8f);
            var back = go.GetComponent<Image>();
            back.color = MenuKit.ButtonIdle;
            back.raycastTarget = true;
            var button = go.GetComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = back;
            button.onClick.AddListener(() => onClick());
            MenuKit.Tip(go, tipTopic, tip);
            if (lit != null) _lights.Add(() => back.color = lit() ? MenuKit.ButtonOn : MenuKit.ButtonIdle);

            var icon = new GameObject("Glyph", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            icon.transform.SetParent(go.transform, false);
            var irt = (RectTransform)icon.transform;
            irt.anchorMin = irt.anchorMax = irt.pivot = new Vector2(0.5f, 0.5f);
            irt.anchoredPosition = Vector2.zero;
            irt.sizeDelta = new Vector2(RowHeight - 13f, RowHeight - 13f);
            var img = icon.GetComponent<Image>();
            img.sprite = sprite;
            img.preserveAspect = true;
            img.raycastTarget = false;
            img.color = new Color(1f, 0.85f, 0.45f, 0.9f);
        }

        private static void RowButton(RectTransform row, string glyph, float size, float right, float width, Action onClick,
                                      Func<bool> lit = null, string tipTopic = null, string tip = null)
        {
            var go = new GameObject("RowButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            go.transform.SetParent(row, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = new Vector2(-right, 0f);
            rt.sizeDelta = new Vector2(width, RowHeight - 8f);
            var img = go.GetComponent<Image>();
            img.color = MenuKit.ButtonIdle;
            img.raycastTarget = true;
            var button = go.GetComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = img;
            button.onClick.AddListener(() => onClick());
            MenuKit.Tip(go, tipTopic, tip);

            var text = MenuKit.Text(go.transform, "Glyph", glyph, size, MenuKit.GlyphIdle);
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            var grt = text.rectTransform;
            grt.anchorMin = Vector2.zero;
            grt.anchorMax = Vector2.one;
            grt.offsetMin = Vector2.zero;
            grt.offsetMax = Vector2.zero;
            if (lit != null) _lights.Add(() =>
            {
                bool on = lit();
                img.color = on ? MenuKit.ButtonOn : MenuKit.ButtonIdle;
                text.color = on ? MenuKit.GlyphOn : MenuKit.GlyphIdle;
            });
        }

        /// <summary>
        /// The find box. Typing narrows the map as the letters go in, so the map answers before the
        /// word is finished; Enter turns what matched into rows, one per type, to pick exactly one
        /// of them. Emptying the box shows everything again, which is why there is no clear button.
        /// </summary>
        private static void FindBox(float x, float y)
        {
            var go = new GameObject("Find", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(TMP_InputField));
            go.transform.SetParent(_root.transform, false);
            Place((RectTransform)go.transform, x + 8f, y - 2f, ColumnWidth - 16f, RowHeight - 6f);
            go.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.14f);

            var text = MenuKit.Text(go.transform, "Text", "", 13f, MenuKit.Body);
            text.alignment = TextAlignmentOptions.MidlineLeft;
            var trt = text.rectTransform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(6f, 0f);
            trt.offsetMax = new Vector2(-6f, 0f);

            var hint = MenuKit.Text(go.transform, "Hint", "find a marker...", 13f, MenuKit.Dim);
            hint.alignment = TextAlignmentOptions.MidlineLeft;
            var hrt = hint.rectTransform;
            hrt.anchorMin = Vector2.zero;
            hrt.anchorMax = Vector2.one;
            hrt.offsetMin = new Vector2(6f, 0f);
            hrt.offsetMax = new Vector2(-6f, 0f);

            _find = go.GetComponent<TMP_InputField>();
            _find.textViewport = (RectTransform)go.transform;
            _find.textComponent = text;
            _find.placeholder = hint;
            _find.lineType = TMP_InputField.LineType.SingleLine;
            // TextMeshPro draws its caret in its own dark default and ignores caretColor until it
            // is told to use a custom one, which on a dark panel leaves you typing blind.
            _find.customCaretColor = true;
            _find.caretColor = new Color(1f, 0.85f, 0.45f, 1f);
            _find.caretWidth = 2;
            _find.caretBlinkRate = 0.8f;
            _find.selectionColor = new Color(1f, 0.85f, 0.45f, 0.45f);
            _find.onValueChanged.AddListener(typed => { Focus.Search(typed); Refresh(); });
            _find.onSubmit.AddListener(typed =>
            {
                Focus.Search(typed);
                _results = Focus.Types(typed);
                _rebuildWanted = true;
            });
        }

        private static void Header(string text, float x, float y)
        {
            var header = MenuKit.Text(_root.transform, "Header", text, 15f, MenuKit.Header);
            Place(header.rectTransform, x + 10f, y, ColumnWidth - 20f, HeaderHeight);
            header.alignment = TextAlignmentOptions.MidlineLeft;
        }

        /// <summary>
        /// A button that turns one label switch off and on, captioned with what pressing it will
        /// do rather than with the state it is in. "Hide Marker Labels" is an instruction; "Marker
        /// Labels: on" is a report that leaves the reader to work out what a press does.
        /// </summary>
        private static void LabelButton(float x, float y, float width, Func<ConfigEntry<bool>> entry, Func<string> text)
        {
            var row = MenuKit.Row(_root.transform, "", true, () =>
            {
                var e = entry();
                if (e != null) e.Value = !e.Value;
                Refresh();
            });
            Place(row, x, y, width, RowHeight);
            var label = row.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null) label.alignment = TextAlignmentOptions.Center;
            _entries.Add(new Entry { Text = text, Quiet = () => false, Label = label });
        }

        private static void ActionRow(float x, float y, Action onClick, Func<string> text, Func<bool> quiet)
        {
            var row = MenuKit.Row(_root.transform, "", true, () =>
            {
                onClick();
                ClientPins.Restyle();
                Refresh();
            });
            Place(row, x, y, ColumnWidth, RowHeight);
            var label = row.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null) label.alignment = TextAlignmentOptions.MidlineLeft;
            _entries.Add(new Entry { Text = text, Quiet = quiet ?? (() => false), Label = label });
        }

        private static void SwitchRow(Minimap map, float x, float y, float indent, string iconKey, string text, Action onClick, Func<bool> shown, Action onPlace = null, Action onOnly = null, Action onLocate = null, Func<string> caption = null, Category? placeKind = null)
        {
            var row = MenuKit.Row(_root.transform, "", true, () =>
            {
                onClick();
                ClientPins.Restyle();
                Refresh();
            });
            Place(row, x, y, ColumnWidth, RowHeight);
            var entry = new Entry { Shown = shown, Caption = caption };

            var sprite = (iconKey != null ? IconRegistry.Resolve(map, iconKey) : null) ?? IconRegistry.Resolve(map, IconRegistry.FallbackKey);
            var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            iconGo.transform.SetParent(row, false);
            var irt = (RectTransform)iconGo.transform;
            irt.anchorMin = new Vector2(0f, 0.5f);
            irt.anchorMax = new Vector2(0f, 0.5f);
            irt.pivot = new Vector2(0f, 0.5f);
            irt.anchoredPosition = new Vector2(8f + indent, 0f);
            irt.sizeDelta = new Vector2(IconSize, IconSize);
            entry.Icon = iconGo.GetComponent<Image>();
            entry.Icon.sprite = sprite;
            entry.Icon.raycastTarget = false;
            entry.Icon.preserveAspect = true;

            // Each button keeps its own column, so a row missing one leaves a gap rather than
            // sliding the others along: ships have no "place by hand", and their only and ping
            // should still sit under everyone else's.
            bool anyButton = onPlace != null || onOnly != null || onLocate != null;
            float right = 6f;
            if (anyButton)
            {
                if (onPlace != null)
                    RowButton(row, "+", 15f, right, RowHeight - 8f, onPlace,
                        lit: () => placeKind.HasValue && ManualPlace.Armed.HasValue
                                   && ManualPlace.Armed.Value == placeKind.Value,
                        tipTopic: "Place Manually",
                        tip: "Double-click on the map to place this marker manually");
                right += RowHeight - 4f;
                if (onOnly != null)
                    RowButton(row, "only", 11f, right, 34f, () => { onOnly(); Refresh(); },
                        lit: () => placeKind.HasValue && Focus.Kind.HasValue
                                   && Focus.Kind.Value == placeKind.Value,
                        tipTopic: "Only",
                        tip: "Hide all other kinds of markers");
                right += 38f;
                // Pinging leaves the list open and changes nothing, so unlike the others there
                // is nothing to redraw afterwards -- the pulse paints itself.
                if (onLocate != null)
                    RowIcon(row, MenuKit.Ping(), right, 22f, onLocate,
                        lit: () => placeKind.HasValue && Locate.Pointing(placeKind.Value),
                        tipTopic: "Ping",
                        tip: "Ping every marker of this kind");
                right += 26f;
            }
            float buttons = right;

            entry.Label = row.GetComponentInChildren<TextMeshProUGUI>();
            if (entry.Label != null)
            {
                entry.Label.text = text;
                entry.Label.alignment = TextAlignmentOptions.MidlineLeft;
                entry.Label.rectTransform.offsetMin = new Vector2(indent + IconSize + 14f, 0f);
                // Keep the name clear of the buttons rather than letting a long kind run under
                // them, where it reads as part of the word on the button.
                entry.Label.rectTransform.offsetMax = new Vector2(-buttons, 0f);
                entry.Label.overflowMode = TextOverflowModes.Ellipsis;
            }
            _entries.Add(entry);
        }

        private static string IconKeyFor(Category kind)
        {
            return TgmConfig.CategoryIcon.TryGetValue(kind, out var entry) ? entry.Value : Categories.DefaultIcon(kind);
        }

        private static bool IsShown(Category kind)
        {
            return !TgmConfig.ShowKind.TryGetValue(kind, out var entry) || entry.Value;
        }

        /// <summary>Top-left anchored, so both columns can share one panel.</summary>
        private static void Place(RectTransform rt, float x, float y, float width, float height)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(width, height);
        }

        /// <summary>
        /// Redraw every row from the current settings. While the master is off every switch row is
        /// dimmed, since nothing is drawn either way.
        /// </summary>
        private static void Refresh()
        {
            foreach (var light in _lights) light();
            foreach (var entry in _entries)
            {
                if (entry.Label == null) continue;
                if (entry.Text != null)
                {
                    entry.Label.text = entry.Text();
                    entry.Label.color = entry.Quiet() ? MenuKit.Dim : MenuKit.Header;
                    continue;
                }
                if (entry.Caption != null) entry.Label.text = entry.Caption();
                bool shown = entry.Shown == null || entry.Shown();
                entry.Label.color = shown ? MenuKit.Body : MenuKit.Dim;
                if (entry.Icon != null) entry.Icon.color = shown ? Color.white : new Color(1f, 1f, 1f, 0.3f);
            }
        }

        internal static void Close()
        {
            if (_root != null) Object.Destroy(_root);
            _root = null;
            _rect = null;
            _entries.Clear();
            _lights.Clear();
            _find = null;
            _pinImage = null;
            _pinGlow = null;
            _closedFrame = Time.frameCount;
        }

        internal static void Update()
        {
            if (_root == null) return;
            var map = Minimap.instance;
            if (map == null || map.m_mode != Minimap.MapMode.Large) { Close(); return; }
            // Asked for between frames rather than from the find box's own callback, which would
            // destroy the box while it was still handling the Enter that asked for this.
            if (_rebuildWanted) { Rebuild(); return; }
            PaintPin();
            // Escape leaves the find box first, as it does in every other text field, so a search
            // can be abandoned without the whole list going with it.
            // Escape still closes it, pinned or not: a player reaching for Escape wants the
            // thing in front of them gone, and unpinning to do that would be a poor answer.
            if (ZInput.GetKeyDown(KeyCode.Escape) && !Typing) { Close(); return; }
            // A click lands in the box as a click in the panel, so the list stays open and the
            // click-away test below is never reached while typing.
            if (Time.frameCount - _openedFrame <= 1) return; // the click that opened it
            // Pinned, a click elsewhere on the map is just a click on the map -- placing a
            // marker, dragging, reading something -- and the list stays where it was put.
            if (!Pinned && (ZInput.GetMouseButtonDown(0) || ZInput.GetMouseButtonDown(1)))
                if (!RectTransformUtility.RectangleContainsScreenPoint(_rect, ZInput.pointerPosition, _camera)) Close();
        }
    }

    /// <summary>
    /// While the find box has the keyboard, the game is told a text input is up, which is how it
    /// already knows to leave its own keys alone. Only while typing: the list is full of clickable
    /// rows the rest of the time, and claiming a text input throughout would take keys the player
    /// still wants. The same trick the legend uses, for the same reason.
    /// </summary>
    [HarmonyPatch(typeof(TextInput), nameof(TextInput.IsVisible))]
    internal static class TextInput_IsVisible_KindMenu_Patch
    {
        private static void Postfix(ref bool __result)
        {
            if (KindMenu.Typing) __result = true;
        }
    }
}
