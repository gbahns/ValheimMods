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
    /// The list behind a left click on the marker button: one column, one row per kind that has
    /// markers on your map, and a triangle on any kind that is several things -- ore into copper,
    /// tin and silver -- so a type can be switched on its own. Clicking a row changes that one
    /// thing and leaves the list open, so several can be changed at once.
    ///
    /// <para><b>Why one column.</b> It was two: kinds on the left, and on the right the markers
    /// that had been crossed off, listed again by type. Side by side reads as two halves of one
    /// thing, and they were not -- the right column held a subset of the left column's markers
    /// sorted on a different axis, so a searched burial chamber appeared twice and hiding it in
    /// one place explained nothing about the other. Being done with something is a state a marker
    /// is in, not a place it lives, so it is now a count and a switch on the row the marker
    /// already has, and the column is gone.</para>
    ///
    /// <para>What that costs is height, which is what the scroll is for: the list can be as long
    /// as the map has kinds, several kinds can be open at once, and a kind with forty types lists
    /// all forty.</para>
    /// </summary>
    internal static class KindMenu
    {
        /// <summary>
        /// One column, wide enough for a kind's name, its count and four buttons at the end --
        /// which it has to be, since the buttons appear over the tail of the longest names.
        /// </summary>
        private const float Width = 370f;

        /// <summary>The strip down the right edge the scroll bar lives in, kept clear on every row.</summary>
        private const float Bar = 6f;

        private const float RowWidth = Width - Bar;
        private const float RowHeight = 26f;
        private const float HeaderHeight = 26f;
        private const float Pad = 4f;
        private const float IconSize = 20f;
        private const float Indent = 16f;

        /// <summary>
        /// The strip at the head of every kind row where the open/close triangle lives, held open
        /// on the rows that have no triangle as well. A column of names that steps in and out by
        /// fourteen pixels depending on whether that kind happens to have types reads as two
        /// lists rather than one.
        /// </summary>
        private const float Twist = 14f;

        /// <summary>
        /// Three levels, each stepped in far enough to hold its own triangle: the heading, the
        /// kinds under it, the types under those. The triangle sits in the step, which is why a
        /// level's indent is the one above it plus a step rather than a step plus a triangle.
        /// </summary>
        private const float GroupIndent = Twist;
        private const float KindIndent = Twist + Indent;
        private const float TypeIndent = Twist + Indent * 2f;

        /// <summary>
        /// The shortest the scrolling part is allowed to be. A map squeezed into a small window
        /// still gets a list worth reading rather than a slot showing two rows at a time.
        /// </summary>
        private const float MinViewport = RowHeight * 6f;

        /// <summary>How much of the title strip's right end the gear and the stick pin take.</summary>
        private const float CornerButtons = 76f;

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
            /// <summary>A heading rather than a thing: drawn in the header gold while it is shown.</summary>
            public bool Head;
            public TextMeshProUGUI Label;
            public Image Icon;
            /// <summary>
            /// What color this row's picture is drawn in, where that is not plain white. Read on
            /// every refresh rather than written once, so changing the color in the settings
            /// shows here as it does on the map.
            /// </summary>
            public Func<Color> Tint;
        }

        private static readonly List<Entry> _entries = new List<Entry>();

        /// <summary>
        /// One per thing that is repainted from what it means rather than from where it is: a
        /// small button that can be lit, a count that can change. Run whenever the list is
        /// refreshed, which is after every press on it.
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

        /// <summary>The part of the list that scrolls, and what scrolls inside it.</summary>
        private static RectTransform _viewport, _content;
        private static Image _barTrack, _barHandle;
        private static float _contentHeight;
        private static float _viewportHeight;
        private static float _scroll;
        private static float _wheel;

        /// <summary>
        /// Which kinds are unfolded, showing the types under them. As many at once as you like:
        /// what used to allow only one was the panel having no scroll, so opening a second ran
        /// the list off the bottom of the map. Kept for the session rather than written to the
        /// config, because it is where you are in the list rather than a preference.
        /// </summary>
        private static readonly HashSet<Category> _unfolded = new HashSet<Category>();

        /// <summary>
        /// Which halves of the plants are unfolded, while the list is split that way. Separate
        /// from the kinds above because these are not kinds: a half is a handful of types out of
        /// four of them. Kept for the session, like the kinds.
        /// </summary>
        private static readonly HashSet<Harvest> _unfoldedPlants = new HashSet<Harvest>();

        /// <summary>
        /// Which headings are folded away. Empty to begin with, so the list opens saying
        /// everything it used to say: a panel that opens as four words is quiet, but it is quiet
        /// about the markers somebody opened it to look at. Kept for the session, like the kinds.
        /// </summary>
        private static readonly HashSet<Group> _foldedGroups = new HashSet<Group>();

        /// <summary>
        /// The headings the list drew this time round, which is not every heading there is: one
        /// with no kinds listed under it and none of the map's own pins is left out altogether.
        /// Written where they are drawn rather than worked out again, so the corner button cannot
        /// come to a different answer than the list it stands over -- and read every frame, which
        /// is reason enough not to work it out twice.
        /// </summary>
        private static readonly HashSet<Group> _headed = new HashSet<Group>();

        /// <summary>The types on the map under each kind, commonest first.</summary>
        private static Dictionary<Category, List<Focus.TypeCount>> _types;

        /// <summary>
        /// Every type on the map in one list, in the order the rows are to be read in. Kept
        /// beside the per-kind buckets because the plant halves cut across kinds: a list built by
        /// walking four buckets would come out in kind order whatever the order setting says,
        /// while this one is already in it.
        /// </summary>
        private static List<Focus.TypeCount> _allTypes;

        /// <summary>
        /// The types in each plant half, in the order their rows are to be read in. Worked out
        /// when the list is built rather than on every refresh: the rows under a kind are fixed
        /// at that moment too, and a refresh happens whenever the pointer moves.
        /// </summary>
        private static readonly Dictionary<Harvest, List<Focus.TypeCount>> _plantTypes =
            new Dictionary<Harvest, List<Focus.TypeCount>>();

        /// <summary>How many markers each kind has, for the number after its name.</summary>
        private static readonly Dictionary<Category, int> _counts = new Dictionary<Category, int>();

        /// <summary>
        /// And how many of them are actually drawn. A row saying "Resources (2279)" over a map
        /// showing a thousand of them is the panel reporting the catalog rather than the map;
        /// where the two differ the row says both, and where they agree it says the one number.
        /// </summary>
        private static readonly Dictionary<Category, int> _shown = new Dictionary<Category, int>();
        private static readonly Dictionary<string, int> _shownByIcon = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The same two numbers for the whole map, for the line under the find box.</summary>
        private static int _all, _allShown;

        /// <summary>
        /// How many of each kind, and of each type, have been crossed off. The count that used to
        /// be the whole right column: it is the same number, read off the same markers, written on
        /// the row the markers already have.
        /// </summary>
        private static readonly Dictionary<Category, int> _done = new Dictionary<Category, int>();
        private static readonly Dictionary<string, int> _doneByIcon = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Crossed-off markers that belong to no kind at all -- the ones placed by hand with
        /// nothing on them, and the ones recorded before a marker remembered what it was. They
        /// have no row of their own among the kinds, so they get one at the foot of the list
        /// rather than losing their switch when the column went.
        /// </summary>
        private static readonly Dictionary<string, int> _doneOther = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Whether the row that puts everything back was drawn. Checked on every refresh: that
        /// row is there only while there is something to put back, and a press on any row above
        /// it can change the answer.
        /// </summary>
        private static bool _resetShown;

        /// <summary>
        /// One per row whose buttons wait for the pointer. Run every frame rather than on a
        /// press, because arriving on a row is not a press.
        /// </summary>
        private static readonly List<Action> _hovers = new List<Action>();

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

        /// <summary>Is there more list than there is room to draw it?</summary>
        internal static bool CanScroll => _root != null && _contentHeight > _viewportHeight + 0.5f;

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
        /// <summary>
        /// The gear beside the stick pin: what this list shows, and how the map draws it.
        /// Pressed again it goes back to the markers.
        /// </summary>
        private static void BuildGear()
        {
            var go = new GameObject("Gear", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
                                    typeof(Button), typeof(MenuKit.Glow));
            go.transform.SetParent(_root.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-30f, -6f);
            rt.sizeDelta = new Vector2(22f, 22f);
            var hit = go.GetComponent<Image>();
            hit.color = new Color(1f, 1f, 1f, 0.07f);
            hit.raycastTarget = true;

            var face = new GameObject("Cog", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            face.transform.SetParent(go.transform, false);
            var frt = (RectTransform)face.transform;
            frt.anchorMin = frt.anchorMax = frt.pivot = new Vector2(0.5f, 0.5f);
            frt.anchoredPosition = Vector2.zero;
            frt.sizeDelta = new Vector2(16f, 16f);
            _gearImage = face.GetComponent<Image>();
            _gearImage.sprite = MenuKit.Gear();
            _gearImage.preserveAspect = true;
            _gearImage.raycastTarget = false;
            _gearGlow = go.GetComponent<MenuKit.Glow>();

            var button = go.GetComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = hit;
            // In front of the list rather than in the place of it: a player changing the order
            // of the types wants to watch the order change.
            button.onClick.AddListener(() => SettingsMenu.Toggle(ZInput.pointerPosition));
            MenuKit.Tip(go, "Settings", "What this list shows, and how the map draws it");
            PaintGear();
        }

        private static Image _gearImage;
        private static MenuKit.Glow _gearGlow;

        private static void PaintGear()
        {
            if (_gearImage == null) return;
            bool over = _gearGlow != null && _gearGlow.Over;
            _gearImage.color = over ? MenuKit.HoverColor
                : SettingsMenu.IsOpen ? new Color(1f, 0.85f, 0.45f, 1f)
                : new Color(0.75f, 0.73f, 0.7f, 0.55f);
        }

        /// <summary>
        /// The triangle in the corner: one press opens every heading, and the next shuts them
        /// all. The headings and nothing else -- the kinds under them keep their own triangles
        /// and whatever state the reader left them in.
        ///
        /// <para>Which is the level the whole list reads at. Opening every kind as well would put
        /// two hundred type rows on the screen at one press, a view nobody wants and one no
        /// second press can get back to, since it cannot know which kinds had been open. The
        /// headings are four words, so this is the one fold a reader can undo by eye.</para>
        ///
        /// <para>It is the twisties' own control, raised a level, so it says what they say: a
        /// triangle lying on its side for shut and turned down for open, showing the state the
        /// list is in rather than the state the press would put it in. Any heading folded counts
        /// as shut, because what a reader wants from this button when part of the list is away is
        /// the rest of it back -- not half of what is in front of them folded to match.</para>
        /// </summary>
        private static void BuildFoldAll()
        {
            var go = new GameObject("FoldAll", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
                                    typeof(Button), typeof(MenuKit.Glow));
            go.transform.SetParent(_root.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-54f, -6f);
            rt.sizeDelta = new Vector2(22f, 22f);
            var hit = go.GetComponent<Image>();
            hit.color = new Color(1f, 1f, 1f, 0.07f);
            hit.raycastTarget = true;

            var face = new GameObject("Triangle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            face.transform.SetParent(go.transform, false);
            var frt = (RectTransform)face.transform;
            frt.anchorMin = frt.anchorMax = frt.pivot = new Vector2(0.5f, 0.5f);
            frt.anchoredPosition = Vector2.zero;
            frt.sizeDelta = new Vector2(12f, 12f);
            _foldImage = face.GetComponent<Image>();
            _foldImage.sprite = MenuKit.Triangle();
            _foldImage.preserveAspect = true;
            _foldImage.raycastTarget = false;
            _foldRect = frt;
            _foldGlow = go.GetComponent<MenuKit.Glow>();

            var button = go.GetComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = hit;
            button.onClick.AddListener(() =>
            {
                if (AnythingFolded()) OpenEverything(); else ShutEverything();
                // The rows themselves change rather than their colors, so this is a redraw in
                // place, asked for between frames like every other one here.
                _rebuildWanted = true;
            });
            MenuKit.Tip(go, "Open or shut", "Open every heading at once, or shut them all. The kinds under them " +
                                            "keep their own triangles.");
            PaintFoldAll();
        }

        private static Image _foldImage;
        private static RectTransform _foldRect;
        private static MenuKit.Glow _foldGlow;

        private static void PaintFoldAll()
        {
            if (_foldImage == null) return;
            bool open = !AnythingFolded();
            bool over = _foldGlow != null && _foldGlow.Over;
            _foldImage.color = over ? MenuKit.HoverColor
                : open ? new Color(1f, 0.85f, 0.45f, 1f)
                : new Color(0.75f, 0.73f, 0.7f, 0.55f);
            // Turned a quarter turn to point down at what it opened, the way the row triangles do
            // and the way the stick pin turns rather than changing picture.
            if (_foldRect != null) _foldRect.localRotation = Quaternion.Euler(0f, 0f, open ? -90f : 0f);
        }

        /// <summary>
        /// Is any heading shut? Only the headings the list actually drew are asked -- a heading
        /// with nothing under it is not on the screen, and one of those left in the folded set
        /// would have the corner saying shut over a list with nothing folded in it.
        /// </summary>
        private static bool AnythingFolded()
        {
            foreach (var group in _headed) if (_foldedGroups.Contains(group)) return true;
            return false;
        }

        private static void OpenEverything()
        {
            _foldedGroups.Clear();
        }

        private static void ShutEverything()
        {
            // Shut, the list is the handful of words it is made of, and nothing is lost: a
            // heading opens again onto kinds in whatever state they were left in.
            foreach (var group in _headed) _foldedGroups.Add(group);
        }

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
            MenuKit.Tip(go, "Pin the list",
                "Keep this list open: it stops closing when you click the map, and comes back with the map");
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

        /// <summary>
        /// Remember where it is now, so pinning it -- or letting go of it after a drag -- puts it
        /// back here next time.
        ///
        /// <para>This also moves the spot the list draws itself at, which is the same number read
        /// a different way. Without that, every redraw in place went back to where the list was
        /// first opened: unfolding a kind after dragging the list across the map would have
        /// snatched it back under the button it came from.</para>
        /// </summary>
        private static void RememberSpot()
        {
            if (_rect == null) return;
            var corners = new Vector3[4];
            _rect.GetWorldCorners(corners);
            var top = RectTransformUtility.WorldToScreenPoint(_camera, corners[1]);
            _screenPos = top;
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

        /// <summary>
        /// Redraw an open list, for a setting that changes its rows rather than their colors --
        /// the order the types come in, say. Asked for between frames, like every other redraw
        /// in place, because a setting can change while the list is being drawn.
        /// </summary>
        internal static void Relayout()
        {
            if (_root != null) _rebuildWanted = true;
        }

        internal static void Open(Vector2 screenPos)
        {
            // A click on the button while the list is open closes it. Whether that click reaches
            // the button or the "clicked away" check first depends on script order, so a list
            // closed this frame cannot be reopened by the same click.
            if (Time.frameCount == _closedFrame) return;
            float scroll = _rebuilding ? _scroll : 0f;
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

            Recount();

            // The types under each kind, for the rows that unfold. Read from the markers rather
            // than from the catalog, so a kind lists what is really out there: this is the same
            // reading the find box turns into rows on Enter, asked of everything instead of a
            // word.
            _types = new Dictionary<Category, List<Focus.TypeCount>>();
            _allTypes = Focus.Types("");
            SortPlants();
            foreach (var type in _allTypes)
            {
                if (!type.Kind.HasValue) continue;
                if (!_types.TryGetValue(type.Kind.Value, out var list))
                    _types[type.Kind.Value] = list = new List<Focus.TypeCount>();
                list.Add(type);
            }

            var panel = MenuKit.Panel(parent, "TGM_KindMenu");
            _root = panel.gameObject;
            _rect = panel.rectTransform;
            _rect.anchorMin = _rect.anchorMax = new Vector2(0f, 1f);
            _rect.pivot = new Vector2(0f, 1f);
            _entries.Clear();

            // The foot's height has to be known before the scrolling part can be given what is
            // left over, and whether the put-back row is there is part of that.
            _resetShown = AnythingPutBack();
            float head = BuildHead(map);
            float foot = (_resetShown ? 3f : 2f) * RowHeight;

            // Anything outside the viewport is clipped, which is what allows scrolling without a
            // full ScrollRect and its scrollbars. The same arrangement the map legend uses.
            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewport.transform.SetParent(_root.transform, false);
            _viewport = (RectTransform)viewport.transform;

            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);
            _content = (RectTransform)content.transform;
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0f, 1f);
            _content.sizeDelta = Vector2.zero;

            _contentHeight = -BuildList(map);
            _content.sizeDelta = new Vector2(0f, _contentHeight);

            var pr = parent.rect;
            float room = Mathf.Max(MinViewport, pr.height - head - foot - Pad * 3f);
            _viewportHeight = Mathf.Min(_contentHeight, room);
            Place(_viewport, 0f, -head, RowWidth, _viewportHeight);

            BuildScrollBar(head);
            float y = -head - _viewportHeight;
            y = BuildFoot(y);

            float height = -y + Pad;
            _rect.sizeDelta = new Vector2(Width, height);
            BuildFoldAll();
            BuildGear();
            BuildPin();

            var canvas = parent.GetComponentInParent<Canvas>();
            _camera = canvas != null && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.rootCanvas.worldCamera : null;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screenPos, _camera, out var local);
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
            _scroll = scroll;
            ClampScroll();
            Refresh();
        }

        /// <summary>
        /// Draw the list again in place, keeping what was typed, which kinds are open and where
        /// the list was scrolled to. Used when the rows themselves have to change rather than only
        /// their colors. The close is ours rather than a click elsewhere, so it must not count as
        /// the click-away that stops the list reopening in the same frame.
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

        // ── the three parts ─────────────────────────────────────────────────────────

        /// <summary>
        /// The part that stays put at the top: what this is, the master switch, the find box and
        /// what the map is showing. None of it scrolls, because all of it is how you get out of
        /// wherever the list has got you. Returns its height.
        /// </summary>
        private static float BuildHead(Minimap map)
        {
            float y = -Pad;
            // The master lives in the title strip rather than among the kinds: it is this
            // window's power switch, not a peer of "Berries". It used to be a row four below
            // another row called "Show all", and between them they were the most confusing thing
            // in the panel.
            // Pinned, the title strip is what you drag the list about by. Built before the
            // things that sit in it, so the master switch and the stick pin stay on top of it and
            // keep taking their own clicks: in a Unity canvas the later child is the nearer one.
            if (Pinned) BuildMover();
            Header(_results != null ? "Matches" : "Markers", 0f, y);
            // A box to check rather than a word that changes, because the word was a promise it
            // could not keep: with the markers off it read "show all", and pressing it put the
            // master switch back without showing anything a kind or a type was hiding. What it
            // does is hold every marker off at once, so it says that and shows whether it is on.
            // Clear of the two round buttons in the corner and level with them: the stick pin
            // sits 6 from the right edge and the gear 30, both 22 across, so the strip they
            // occupy is 52 wide and its middle is 17 below the top.
            CheckBox(Width - CornerButtons - 6f - 76f, -(17f - 20f * 0.5f), 76f,
                "hide all",
                () => !AllShown,
                () => { MarkerToggle.ToggleAll(); Refresh(); },
                "All markers",
                "Take everything you have put on the map off it at once: this mod's markers, the pins " +
                "you placed by hand, your death markers and your bed. The game's own landmarks stay -- " +
                "boss altars, traders, Hildir's camps, the center of the world -- and so do players, " +
                "pings and shouts. Unchecking it leaves every switch below exactly as it was.");
            y -= HeaderHeight;

            FindBox(0f, y);
            y -= RowHeight;

            // What the map is showing, which is also how to stop: the row is the way back to
            // everything. It is built whether or not anything is focused, because focusing happens
            // by typing in the box above it and the row has to be there to say so.
            ActionRow(_root.transform, 0f, y, Focus.Clear, () =>
                {
                    // Every switch, not only the find box. This line used to count what the find
                    // box matched, so hiding two thousand resources left it saying "showing all
                    // 2662 markers" while the row at the foot offered to put a kind back. Counted
                    // with everything else, in the one pass over the markers.
                    if (Focus.On) return $"Only {Focus.What()} — {_allShown} of {_all}, click for all";
                    return _allShown < _all
                        ? $"Showing {_allShown} of {_all} markers"
                        : $"Showing all {_all} markers";
                },
                () => !Focus.On);
            y -= RowHeight;
            return -y;
        }

        /// <summary>
        /// The scrolling middle: every kind that has markers, each unfolding into its types, or
        /// the matches for what was typed. Returns the y below its last row, measured from the top
        /// of the scrolling area.
        /// </summary>
        private static float BuildList(Minimap map)
        {
            float y = 0f;
            _headed.Clear();
            if (_results != null)
            {
                ActionRow(_content, 0f, y, () =>
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
                    var none = MenuKit.Text(_content, "None", "Nothing on the map matches.", 13f, MenuKit.Dim);
                    Place(none.rectTransform, 10f, y, RowWidth - 20f, RowHeight);
                    none.alignment = TextAlignmentOptions.MidlineLeft;
                    y -= RowHeight;
                }
                foreach (var match in _results)
                {
                    var m = match;
                    SwitchRow(map, _content, 0f, y, 0f, m.Icon, Named(m.Label, m.Count, m.Count),
                        () => Focus.SetIcon(m.Icon),
                        () => Focus.Icon != null && IconRegistry.SameKey(Focus.Icon, m.Icon),
                        onLocate: () => Locate.Icon(m.Icon, m.Label));
                    y -= RowHeight;
                }
                return y;
            }

            // Under headings, because nineteen kinds in one flat list put a raspberry bush and a
            // ship at the same level and they are not the same sort of thing at all. Every kind
            // belongs to exactly one heading, and a heading is drawn only if something under it
            // is worth listing.
            foreach (var group in Categories.AllGroups)
            {
                var g = group;
                var kinds = new List<Category>();
                foreach (var kind in Categories.All) if (Categories.GroupOf(kind) == g && Listed(kind)) kinds.Add(kind);
                // A heading stands for its vanilla pins as well, and for nothing else if that is
                // all it has: Your Stuff with no sightings on the map still has the hundred pins
                // the player dropped by hand under it.
                var vanilla = VanillaRows(map, g);
                if (kinds.Count == 0 && vanilla.Count == 0) continue;
                _headed.Add(g);
                bool resources = AllResources(kinds);

                // The heading is a switch like every other row here: pressed, it hides the kinds
                // under it, which is the setting those kinds already had. The triangle folds it.
                var heading = SwitchRow(map, _content, 0f, y, GroupIndent, null,
                    GroupHeading(map, g, kinds),
                    () =>
                    {
                        bool show = !GroupShown(map, g, kinds);
                        SetKinds(kinds, show);
                        VanillaPins.SetAll(map, g, show);
                    },
                    () => AllShown && GroupShown(map, g, kinds),
                    caption: () => GroupHeading(map, g, kinds),
                    doneCount: () => DoneIn(kinds),
                    doneShown: () => GroupDoneShown(kinds),
                    doneToggle: () => SetKindsDone(kinds, !GroupDoneShown(kinds)),
                    gone: resources,
                    // Everything under this heading at once, which is the fold a reader wants
                    // most: the corner button is about the four headings and a row's own triangle
                    // is about that row, and between them was the thing nobody had -- open the
                    // seven kinds under Resources and leave Places alone.
                    onFold: AnyFoldable(kinds)
                        ? (Action)(() =>
                        {
                            bool open = !ChildrenOpen(kinds);
                            SetChildrenOpen(kinds, open);
                            // Opening what is under a shut heading has to show it, or the press
                            // does its work out of sight and reads as having done nothing.
                            if (open) _foldedGroups.Remove(g);
                            _rebuildWanted = true;
                        })
                        : null,
                    foldOpen: () => ChildrenOpen(kinds),
                    head: true, icon: false, tip: Categories.GroupTip(g));
                Twisty(heading, GroupIndent, !_foldedGroups.Contains(g),
                       () => { if (!_foldedGroups.Remove(g)) _foldedGroups.Add(g); });
                y -= RowHeight;
                if (_foldedGroups.Contains(g)) continue;

                bool plantsDone = false;
                foreach (var kind in kinds)
                {
                    // The other players are vanilla's markers rather than ours, and the row for
                    // them answers a different question, so it is built in full below rather than
                    // squeezed through the same call.
                    if (kind == Category.Player) { y = PlayersRow(map, y); continue; }
                    // The plants, split by whether they can be farmed, stand in for the five kinds
                    // their types come from. A grown kind that has nothing in either half keeps
                    // its own row, so no kind can lose its switch by being left out of both.
                    if (SplitPlants && Categories.IsGrown(kind) && HasTypes(kind))
                    {
                        if (plantsDone) continue;
                        plantsDone = true;
                        y = PlantRow(map, Harvest.Farm, y);
                        y = PlantRow(map, Harvest.Gather, y);
                        continue;
                    }
                    y = KindRow(map, kind, y);
                }
                // Below the kinds rather than among them: these are not kinds, nothing records
                // them, and what they mean is the player's own business. Under the heading all
                // the same, because the question the heading asks is the one they answer.
                foreach (var type in vanilla) y = VanillaRow(map, type, y);
            }
            return LooseEnds(y);
        }

        /// <summary>
        /// A kind that has a row: one with markers, one that is only ever placed by hand (the
        /// list is what places it), one the server is answering for right now, or one the game
        /// marks by itself.
        ///
        /// <para>Structures, portals, camps and boss altars are also hidden and shown by vanilla's
        /// own icon buttons, so listing them here is redundant, but leaving them out was worse: a
        /// player looking for the switch expects to find it with the rest, and does not care which
        /// mod owns the icon. The two are not identical, and the difference is in our favor:
        /// vanilla's button hides every pin with that icon, while this one covers only the markers
        /// this mod recorded.</para>
        /// </summary>
        private static bool Listed(Category kind)
        {
            if (Categories.IsPlacedOnly(kind)) return true;
            if (Categories.IsLive(kind))
                return kind == Category.Portal ? Portals.HaveList : Transports.HaveList;
            return _counts.ContainsKey(kind) || Categories.VanillaMarks(kind);
        }

        /// <summary>One kind, and its types under it when it is unfolded. Returns the y below them.</summary>
        private static float KindRow(Minimap map, Category kind, float y)
        {
            {
                var k = kind;
                var row = SwitchRow(map, _content, 0f, y, KindIndent, IconKeyFor(k), KindLabel(k),
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
                    placeKind: k,
                    // Read again on every refresh rather than written once: recording goes on
                    // while a pinned list stays open for hours, and a number that was true this
                    // morning reads exactly like one that is true now.
                    caption: () => KindLabel(k),
                    // And how many of them you have finished with, which used to be a whole
                    // column of its own on the other side of the panel.
                    doneCount: () => DoneOf(k),
                    doneShown: () => DoneShown(k),
                    doneToggle: () => ToggleDone(k),
                    gone: Categories.IsResource(k),
                    // Portals are the one thing on the map not drawn in gold -- they are the
                    // points you travel between and have a color of their own for it -- so the
                    // row wears that color too. A list whose pictures are the map's pictures can
                    // be read against the map without translating; one picture quietly in the
                    // wrong color is a question nobody should have to ask.
                    tint: k == Category.Portal ? (Func<Color>)Portals.MarkerColor : null);
                y -= RowHeight;
                y = Unfold(map, row, y, k);
            }
            return y;
        }

        /// <summary>Is the list standing the two plant halves in place of the five plant kinds?</summary>
        private static bool SplitPlants =>
            TgmConfig.SplitPlantsByFarming != null && TgmConfig.SplitPlantsByFarming.Value;

        private static bool HasTypes(Category kind)
        {
            return _types != null && _types.TryGetValue(kind, out var types) && types.Count > 0;
        }

        /// <summary>
        /// One half of the plants -- what can be farmed, or what cannot -- and its types under it
        /// when it is unfolded. Drawn where a kind row is drawn and at a kind's indent, because
        /// that is what it stands in for: the types under it keep the type indent they always had,
        /// so this adds no level to the list and changes nothing about how it is read down.
        ///
        /// <para>No "only" button and no ping, which every kind row has. Both of those point at
        /// one kind or one icon, and a half is neither -- it is a handful of types out of four
        /// kinds, and there is nothing in the focus or the pointer to aim at a set. The rows under
        /// it have both, which is where they are wanted anyway: you ping raspberries, not
        /// "everything wild".</para>
        /// </summary>
        private static float PlantRow(Minimap map, Harvest half, float y)
        {
            var types = PlantTypes(half);
            if (types.Count == 0) return y;
            var h = half;
            var row = SwitchRow(map, _content, 0f, y, KindIndent, Categories.HarvestIcon(h), PlantHeading(h),
                () => SetPlantsShown(h, !AnyPlantShown(h)),
                () => AllShown && AnyPlantShown(h),
                caption: () => PlantHeading(h),
                doneCount: () => DonePlants(h),
                // Every plant is a supply rather than a place, so crossing one off means it is
                // gone -- and a half is the types it lists, so its switch is theirs and nobody
                // else's. It used to be the one switch every deposit in the game shared, which
                // made picking the raspberries put the worked copper back on the map.
                doneShown: () => ClearedShown(PlantTypes(h)),
                doneToggle: () => CrossedOff.SetClearedDrawn(PlantIcons(h), !CrossedOff.AnyClearedDrawn(PlantIcons(h))),
                gone: true,
                tip: Categories.HarvestTip(h));
            y -= RowHeight;
            Twisty(row, KindIndent, _unfoldedPlants.Contains(h),
                   () => { if (!_unfoldedPlants.Remove(h)) _unfoldedPlants.Add(h); });
            if (!_unfoldedPlants.Contains(h)) return y;
            foreach (var type in types)
            {
                TypeRow(map, y, type.Kind.Value, type);
                y -= RowHeight;
            }
            return y;
        }

        /// <summary>The icons of the types in one half: what its own switches are about.</summary>
        private static List<string> PlantIcons(Harvest half)
        {
            var icons = new List<string>();
            foreach (var type in PlantTypes(half)) icons.Add(type.Icon);
            return icons;
        }

        /// <summary>The types in one half, in the order their rows are read in.</summary>
        private static List<Focus.TypeCount> PlantTypes(Harvest half)
        {
            return _plantTypes.TryGetValue(half, out var list) ? list : Empty;
        }

        private static readonly List<Focus.TypeCount> Empty = new List<Focus.TypeCount>();

        /// <summary>
        /// Split the plant types into the two halves and put each half in order.
        ///
        /// <para>Most first and A to Z are already right, the flat list being in that order
        /// across every kind at once. Progression is not, and cannot be: the catalog's order runs
        /// down one kind at a time and says nothing about where one kind's entries stand against
        /// another's, so a half took its rows in kind order whatever the biomes -- magecap, a
        /// Mistlands mushroom, above the Black Forest's carrot seeds. The catalog is still what
        /// orders everything else, including both halves' fallback for a type it has never heard
        /// of; what the halves add is the one thing a per-kind list cannot hold.</para>
        /// </summary>
        private static void SortPlants()
        {
            _plantTypes.Clear();
            if (_allTypes == null) return;
            foreach (var type in _allTypes)
            {
                if (!type.Kind.HasValue || !Categories.IsGrown(type.Kind.Value)) continue;
                var half = Catalog.CanBeFarmed(type.Icon) ? Harvest.Farm : Harvest.Gather;
                if (!_plantTypes.TryGetValue(half, out var list)) _plantTypes[half] = list = new List<Focus.TypeCount>();
                list.Add(type);
            }
            if (TgmConfig.TypeRowOrder != null && TgmConfig.TypeRowOrder.Value != TypeOrder.Progression) return;
            foreach (var kv in _plantTypes)
                kv.Value.Sort((a, b) =>
                {
                    int ra = Catalog.PlantOrderOf(a.Icon), rb = Catalog.PlantOrderOf(b.Icon);
                    if (ra != rb) return ra.CompareTo(rb);
                    int ca = Catalog.OrderOf(a.Icon), cb = Catalog.OrderOf(b.Icon);
                    if (ca != cb) return ca.CompareTo(cb);
                    return string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase);
                });
        }

        /// <summary>"Wild (1893)", or how many of those are drawn where the two differ.</summary>
        private static string PlantHeading(Harvest half)
        {
            int n = 0, lit = 0;
            foreach (var type in PlantTypes(half))
            {
                n += type.Count;
                _shownByIcon.TryGetValue(type.Icon, out int some);
                lit += some;
            }
            return Named(Categories.HarvestLabel(half), lit, n);
        }

        private static int DonePlants(Harvest half)
        {
            int n = 0;
            foreach (var type in PlantTypes(half))
            {
                _doneByIcon.TryGetValue(type.Icon, out int some);
                n += some;
            }
            return n;
        }

        private static bool AnyPlantShown(Harvest half)
        {
            foreach (var type in PlantTypes(half))
                if (IsShown(type.Kind.Value) && !TgmConfig.IsIconHidden(type.Icon)) return true;
            return false;
        }

        /// <summary>
        /// Show or hide a whole half. Nothing is stored per half: this is each type set one at a
        /// time, by the same call a type's own row uses -- which matters, because the kinds these
        /// types belong to have members in the other half, so the kind's switch cannot be the one
        /// that does it.
        /// </summary>
        private static void SetPlantsShown(Harvest half, bool show)
        {
            foreach (var type in PlantTypes(half)) SetTypeShown(type.Kind.Value, type.Icon, show);
        }

        /// <summary>
        /// The other players, which are vanilla's markers rather than ours: nothing here records
        /// them and nothing hides them, but "where is everybody" is the same question this list
        /// answers for everything else, so it belongs in the same place. The row itself is the
        /// spotlight the H key gives; the ping only makes them wave.
        ///
        /// <para>Shown even when there are none, dimmed, saying so. Leaving the row out entirely
        /// when nobody is sharing looks exactly like the feature not existing, which is how it
        /// read the first time somebody went looking for it. The count is read afresh on every
        /// refresh rather than fixed when the list opened: players come and go, and a pinned list
        /// stays open for hours.</para>
        /// </summary>
        private static float PlayersRow(Minimap map, float y)
        {
            Func<int> players = () => PlayerSpotlight.Players(map);
            SwitchRow(map, _content, 0f, y, KindIndent, "pin:Player", "Players",
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
                // "(none sharing)" is not a count and stays whatever the counts are doing: it
                // says why the row is dim, which is the one thing a reader needs from it.
                caption: () => players() > 0 ? Named("Players", players(), players()) : "Players (none sharing)",
                placeKind: Category.Player);
            return y - RowHeight;
        }

        /// <summary>
        /// The crossed-off markers belonging to no kind, which would otherwise have lost their
        /// switch along with the column it used to live in. Below every heading, because they
        /// belong under none of them: that is what is wrong with them.
        /// </summary>
        private static float LooseEnds(float y)
        {
            foreach (var bucket in new[] { CrossedOff.Placed, CrossedOff.Unsorted })
            {
                if (!_doneOther.TryGetValue(bucket, out int n) || n == 0) continue;
                var group = bucket;
                // Read again on every refresh, like every other count here: crossing one off is
                // a thing you do with the list open.
                Func<int> live = () => { _doneOther.TryGetValue(group, out int now); return now; };
                var label = MenuKit.Text(_content, "Other", group, 13f, MenuKit.Dim);
                Place(label.rectTransform, 10f + Twist, y, RowWidth - 160f, RowHeight);
                label.alignment = TextAlignmentOptions.MidlineLeft;
                DoneButton(null, y, live, () => !CrossedOff.IsGroupHidden(group),
                           () => CrossedOff.SetGroupHidden(group, !CrossedOff.IsGroupHidden(group)), false);
                y -= RowHeight;
            }
            return y;
        }

        /// <summary>
        /// The part that stays put at the bottom: everything that is about the whole map rather
        /// than about one kind. Returns the y below its last row.
        /// </summary>
        private static float BuildFoot(float y)
        {
            // One switch for everything you are finished with, which is what the right-hand
            // column was for before being a column stopped making sense. The per-kind and
            // per-type switches are still there, on the rows they belong to.
            ActionRow(_root.transform, 0f, y, () =>
                {
                    if (CrossedOff.AnyShown()) CrossedOff.HideAll();
                    else CrossedOff.ShowAll();
                },
                () =>
                {
                    int done = DoneTotal();
                    return (CrossedOff.AnyShown() ? "Hide" : "Show") +
                           (done > 0 ? $" everything crossed off ({done})" : " everything crossed off");
                },
                () => DoneTotal() == 0);
            y -= RowHeight;

            ActionRow(_root.transform, 0f, y, Erasures.Toggle,
                () => Erasures.On ? "Hide erased markers" : "Show erased markers", null);
            y -= RowHeight;

            // The game's ping, which had a button of its own on the strip this mod now takes off
            // the map. It is the one thing on that strip no row above stands for: a ping is a
            // gesture rather than a kind of marker, so there is nothing to list and nothing to
            // switch -- which puts it here, with the rest of what is about the whole map.
            //
            // Only while the game's buttons are hidden. With them on the map its own button is a
            // few pixels away, and a second way to the same thing is a second thing to read.
            if (VanillaPins.HidingControls)
            {
                ActionRow(_root.transform, 0f, y, () => VanillaPins.TogglePing(Minimap.instance),
                    () => VanillaPins.PingArmed(Minimap.instance)
                        ? "Pinging - double-click the map, or click here to stop"
                        : "Ping a spot out loud",
                    null);
                y -= RowHeight;
            }

            // The two label switches used to sit here. They are about how the whole map is
            // drawn rather than about which markers are on it, which is a different question from
            // the one every other row in this list answers -- so they live behind the gear beside
            // the marker button now, with the rest of the drawing settings.

            // Everything back: the kinds, the master switch, the crossed-off groups, the icons
            // hidden one picture at a time and the markers hidden one at a time. The same work as
            // tgm_show, and it has to be, because this is the button people press.
            //
            // It used to put back only the kinds, the master switch and the crossed-off groups,
            // which left "Hide all Cloudberries markers" with no way back on the map at all: the
            // one control that undid it lived in a marker's own menu, and every marker it applied
            // to was hidden. A button called "Show all" that shows most of it is worse than no
            // button, because it answers the question and the answer is wrong.
            //
            // Drawn only when it would put something back, and naming what that is. It used to be
            // called "Show all" and sit four rows under another one called "Show all markers",
            // and between them they were the most confusing thing in the panel.
            if (_resetShown)
            {
                var putBack = MenuKit.Row(_root.transform, "", true, () =>
                {
                    ViewPrefs.Clear();
                    TgmConfig.ShowEverything();
                    ClientPins.Restyle();
                    Refresh();
                });
                Place(putBack, 0f, y, RowWidth, RowHeight);
                var putBackLabel = putBack.GetComponentInChildren<TextMeshProUGUI>();
                if (putBackLabel != null) putBackLabel.alignment = TextAlignmentOptions.MidlineLeft;
                _entries.Add(new Entry { Text = PutBackText, Quiet = () => false, Label = putBackLabel });
                y -= RowHeight;
            }
            return y;
        }

        /// <summary>
        /// The strip across the title that moves the list, for a list that is staying open.
        ///
        /// <para>Only while pinned, because only then is there anywhere for it to stay: an
        /// unpinned list is put up by a click and goes away at the next one, and opens where the
        /// button is every time, so moving it would be a gesture with nothing to remember it.
        /// Pinned, it is furniture, and furniture that cannot be moved off the part of the map
        /// you are reading is furniture in the way.</para>
        ///
        /// <para>Where it ends up is remembered the way pinning already remembered it -- as a
        /// fraction of the screen, so it lands in the same place on any monitor.</para>
        /// </summary>
        private static void BuildMover()
        {
            var go = new GameObject("Mover", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
                                    typeof(MenuKit.DragHandle));
            go.transform.SetParent(_root.transform, false);
            // Invisible, and still the thing the pointer finds: a transparent image is a hit
            // target, which is the whole trick. The title above it is text with its raycast off.
            var img = go.GetComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0f);
            img.raycastTarget = true;
            // Up to the master switch and no further, so the one word in the strip is still a
            // button rather than a place the panel can be grabbed by.
            Place((RectTransform)go.transform, 0f, 0f, Width - CornerButtons - 6f - 76f, HeaderHeight + Pad);
            var handle = go.GetComponent<MenuKit.DragHandle>();
            handle.OnDrag = OnMoveDrag;
            handle.OnEnd = RememberSpot;
        }

        private static void OnMoveDrag(Vector2 screenDelta)
        {
            if (_rect == null) return;
            var canvas = _root != null ? _root.GetComponentInParent<Canvas>() : null;
            float scale = canvas != null ? canvas.rootCanvas.scaleFactor : 1f;
            _rect.anchoredPosition += screenDelta / (scale > 0f ? scale : 1f);
            ClampToMap();
        }

        /// <summary>
        /// Keep the whole list on the map. The map is a rect rather than the screen, so there is
        /// no reason to settle for keeping part of it reachable: it can simply be held inside.
        /// </summary>
        private static void ClampToMap()
        {
            var parent = _rect.parent as RectTransform;
            if (parent == null) return;
            var pr = parent.rect;
            var size = _rect.sizeDelta;
            var p = _rect.anchoredPosition;
            _rect.anchoredPosition = new Vector2(
                Mathf.Clamp(p.x, 0f, Mathf.Max(0f, pr.width - size.x)),
                Mathf.Clamp(p.y, Mathf.Min(0f, -(pr.height - size.y)), 0f));
        }

        /// <summary>
        /// A hairline down the right edge saying how much list there is and where in it you are.
        /// Without it a panel that happens to end on a whole row looks like the end of the list.
        /// </summary>
        private static void BuildScrollBar(float head)
        {
            var track = new GameObject("BarTrack", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            track.transform.SetParent(_root.transform, false);
            _barTrack = track.GetComponent<Image>();
            _barTrack.color = new Color(1f, 1f, 1f, 0.05f);
            _barTrack.raycastTarget = false;
            Place((RectTransform)track.transform, RowWidth + 1f, -head, 3f, _viewportHeight);

            var handle = new GameObject("BarHandle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            handle.transform.SetParent(_root.transform, false);
            _barHandle = handle.GetComponent<Image>();
            _barHandle.color = new Color(1f, 0.85f, 0.45f, 0.45f);
            _barHandle.raycastTarget = false;
            Place((RectTransform)handle.transform, RowWidth + 1f, -head, 3f, _viewportHeight);
            _barTop = head;
            PaintScrollBar();
        }

        /// <summary>Where the scrolling part starts, so the bar can be redrawn without being told again.</summary>
        private static float _barTop;

        private static void PaintScrollBar()
        {
            bool show = CanScroll;
            if (_barTrack != null) _barTrack.enabled = show;
            if (_barHandle == null) { return; }
            _barHandle.enabled = show;
            if (!show) return;
            float fraction = _viewportHeight / _contentHeight;
            float height = Mathf.Max(18f, _viewportHeight * fraction);
            float travel = _viewportHeight - height;
            float max = Mathf.Max(1f, _contentHeight - _viewportHeight);
            Place((RectTransform)_barHandle.transform, RowWidth + 1f,
                  -(_barTop + travel * Mathf.Clamp01(_scroll / max)), 3f, height);
        }

        private static void ClampScroll()
        {
            float max = Mathf.Max(0f, _contentHeight - _viewportHeight);
            _scroll = Mathf.Clamp(_scroll, 0f, max);
            if (_content != null) _content.anchoredPosition = new Vector2(0f, _scroll);
            PaintScrollBar();
        }

        /// <summary>The wheel value the camera would have used, captured before it was zeroed.</summary>
        internal static void NoteWheel(float value)
        {
            if (value != 0f) _wheel = value;
        }

        // ── rows ────────────────────────────────────────────────────────────────────

        private static bool AnyShown(Category cat)
        {
            foreach (var group in CrossedOff.GroupsOf(cat))
                if (!CrossedOff.IsGroupHidden(group)) return true;
            return false;
        }

        /// <summary>
        /// A heading and what is under it: "Places (143)". The number is the markers of every
        /// kind under it added up, which for the dynamic ones is what the server is answering
        /// right now rather than anything recorded.
        /// </summary>
        private static string GroupHeading(Minimap map, Group group, List<Category> kinds)
        {
            int n = 0, lit = 0;
            foreach (var kind in kinds)
            {
                n += KindCount(map, kind);
                if (Categories.IsLive(kind) || kind == Category.Player) lit += KindCount(map, kind);
                else { _shown.TryGetValue(kind, out int some); lit += some; }
            }
            VanillaPins.Tally(map, group, out int theirs, out int theirsLit);
            n += theirs;
            lit += theirsLit;
            return Named(Categories.GroupLabel(group), lit, n);
        }

        private static readonly Harvest[] Halves = { Harvest.Farm, Harvest.Gather };

        /// <summary>
        /// Does this kind have a triangle of its own? The same three questions <see cref="Unfold"/>
        /// asks before it draws one, in the same order, so a heading's fold button and the rows
        /// under it cannot disagree about what there is to open.
        /// </summary>
        private static bool Foldable(Category kind)
        {
            if (_types == null || !_types.TryGetValue(kind, out var types) || types.Count == 0) return false;
            // Drawn in the plant halves instead, which have triangles of their own.
            if (SplitPlants && Categories.IsGrown(kind)) return false;
            return types.Count > 1 || !SaysTheSame(types[0].Label, Categories.Label(kind));
        }

        /// <summary>Are the plant halves drawn under this heading, in place of some of its kinds?</summary>
        private static bool Halved(List<Category> kinds)
        {
            if (!SplitPlants) return false;
            foreach (var kind in kinds) if (Categories.IsGrown(kind) && HasTypes(kind)) return true;
            return false;
        }

        /// <summary>Is there anything under this heading to open or shut?</summary>
        private static bool AnyFoldable(List<Category> kinds)
        {
            foreach (var kind in kinds) if (Foldable(kind)) return true;
            if (Halved(kinds)) foreach (var half in Halves) if (PlantTypes(half).Count > 0) return true;
            return false;
        }

        /// <summary>
        /// Is every row under this heading that can be opened open? All of them rather than any,
        /// because the button says what the next press will do and the press a reader wants from a
        /// half-open heading is the rest of it open -- the same rule the corner button keeps.
        /// </summary>
        private static bool ChildrenOpen(List<Category> kinds)
        {
            foreach (var kind in kinds) if (Foldable(kind) && !_unfolded.Contains(kind)) return false;
            if (Halved(kinds))
                foreach (var half in Halves)
                    if (PlantTypes(half).Count > 0 && !_unfoldedPlants.Contains(half)) return false;
            return true;
        }

        private static void SetChildrenOpen(List<Category> kinds, bool open)
        {
            foreach (var kind in kinds)
            {
                if (!Foldable(kind)) continue;
                if (open) _unfolded.Add(kind); else _unfolded.Remove(kind);
            }
            if (!Halved(kinds)) return;
            foreach (var half in Halves)
            {
                if (PlantTypes(half).Count == 0) continue;
                if (open) _unfoldedPlants.Add(half); else _unfoldedPlants.Remove(half);
            }
        }

        /// <summary>
        /// Is anything under this heading being drawn -- a kind of ours, or a type of vanilla's?
        /// Both, because the heading's switch covers both, and a switch that reads only half of
        /// what it sets says hide when there is nothing left to hide.
        /// </summary>
        private static bool GroupShown(Minimap map, Group group, List<Category> kinds)
        {
            return AnyKindShown(kinds) || VanillaPins.AnyShown(map, group);
        }

        /// <summary>The map's own pin types that belong under this heading and have any pins.</summary>
        private static List<Minimap.PinType> VanillaRows(Minimap map, Group group)
        {
            var list = new List<Minimap.PinType>();
            var types = group == Group.Yours ? VanillaPins.Yours
                      : group == Group.Places ? VanillaPins.Theirs : null;
            if (types == null) return list;
            foreach (var type in types) if (VanillaPins.CountOf(type) > 0) list.Add(type);
            return list;
        }

        /// <summary>
        /// One of the map's own pin types: its picture, what you call it, how many there are, and
        /// the switch -- which is vanilla's own switch, so its button under the map grays out with
        /// this row.
        ///
        /// <para>No crossed-off count, nothing of these being recorded to cross off, and no
        /// filter: a focus names a kind or an icon of ours and has nothing to say about a pin this
        /// mod never wrote. The five placeable ones get the place-by-hand button, which arms
        /// vanilla's own placing for that icon -- the other half of what its button under the map
        /// does -- so for those five this row is the button, and that is what makes doing without
        /// the button strip thinkable.</para>
        /// </summary>
        private static float VanillaRow(Minimap map, Minimap.PinType type, float y)
        {
            var t = type;
            Func<string> caption = () => Named(VanillaPins.Label(t),
                                               VanillaPins.ShownCount(map, t), VanillaPins.CountOf(t));
            SwitchRow(map, _content, 0f, y, KindIndent, VanillaPins.IconKey(t), caption(),
                () => VanillaPins.Toggle(map, t),
                () => AllShown && VanillaPins.Drawn(map, t),
                onPlace: VanillaPins.Placeable(t) ? (Action)(() => VanillaPins.Place(map, t)) : null,
                placeLit: () => VanillaPins.Armed(map, t),
                onLocate: () => Locate.Vanilla(map, t, VanillaPins.Label(t)),
                pingLit: () => Locate.Pointing(t),
                caption: caption,
                tip: VanillaPins.Tip(t));
            return y - RowHeight;
        }

        /// <summary>
        /// How many markers a kind has. A live kind counts the server's answer and a player
        /// counts who is sharing, because for those there is nothing recorded to count.
        /// </summary>
        private static int KindCount(Minimap map, Category kind)
        {
            if (Categories.IsLive(kind))
                return kind == Category.Portal ? Portals.Count : Transports.CountOf(kind == Category.Cart);
            if (kind == Category.Player) return PlayerSpotlight.Players(map);
            _counts.TryGetValue(kind, out int n);
            return n;
        }

        private static bool AnyKindShown(List<Category> kinds)
        {
            foreach (var kind in kinds) if (IsShown(kind)) return true;
            return false;
        }

        /// <summary>
        /// Show or hide every kind under a heading. Nothing is stored per heading: this is the
        /// per-kind setting that already existed, set several at once.
        /// </summary>
        private static void SetKinds(List<Category> kinds, bool show)
        {
            foreach (var kind in kinds)
                if (TgmConfig.ShowKind.TryGetValue(kind, out var entry) && entry.Value != show) entry.Value = show;
        }

        /// <summary>True when everything under a heading is a supply rather than a place.</summary>
        private static bool AllResources(List<Category> kinds)
        {
            foreach (var kind in kinds) if (!Categories.IsResource(kind)) return false;
            return kinds.Count > 0;
        }

        private static int DoneIn(List<Category> kinds)
        {
            int n = 0;
            foreach (var kind in kinds) n += DoneOf(kind);
            return n;
        }

        /// <summary>
        /// Is anything crossed off under this heading still drawn? Only the kinds that have
        /// something crossed off are asked: a kind with none of them would otherwise answer
        /// "shown" forever and leave the heading's switch saying hide when there is nothing left
        /// to hide.
        /// </summary>
        private static bool GroupDoneShown(List<Category> kinds)
        {
            foreach (var kind in kinds) if (DoneOf(kind) > 0 && DoneShown(kind)) return true;
            return false;
        }

        private static void SetKindsDone(List<Category> kinds, bool show)
        {
            foreach (var kind in kinds) if (DoneOf(kind) > 0) SetKindDone(kind, show);
        }

        /// <summary>
        /// How many of this kind you have crossed off. Read afresh on every refresh, like the
        /// counts beside the names, because crossing one off is a thing you do with the list open.
        /// </summary>
        private static int DoneOf(Category kind)
        {
            _done.TryGetValue(kind, out int n);
            return n;
        }

        private static int DoneTotal()
        {
            int n = 0;
            foreach (var kv in _done) n += kv.Value;
            foreach (var kv in _doneOther) n += kv.Value;
            return n;
        }

        /// <summary>
        /// Are this kind's crossed-off markers drawn? A used-up deposit and a searched place are
        /// different ideas in the model underneath -- one thing is gone, the other is merely done
        /// with -- but each is this kind's own either way, which it was not until 4.7.0: every
        /// deposit and plant in the game shared one switch, so the button on the copper row put
        /// the picked raspberries back with the worked copper.
        /// </summary>
        private static bool DoneShown(Category kind)
        {
            if (Categories.IsResource(kind)) return ClearedShown(TypesOf(kind));
            if (CrossedOff.Splits(kind)) return AnyShown(kind);
            return !CrossedOff.IsGroupHidden(Categories.Label(kind));
        }

        private static List<Focus.TypeCount> TypesOf(Category kind)
        {
            return _types != null && _types.TryGetValue(kind, out var types) ? types : Empty;
        }

        /// <summary>
        /// Are any of these types' used-up markers still drawn?
        ///
        /// <para>Only the types that have one crossed off are asked. A type the catalog knows and
        /// this map has none of counts as drawn, there being nothing of it to hide, and asking
        /// about it would leave a button dark over markers it had hidden -- the same reason the
        /// heading's switch asks only the kinds that have something crossed off.</para>
        /// </summary>
        private static bool ClearedShown(IEnumerable<Focus.TypeCount> types)
        {
            if (types == null) return false;
            foreach (var type in types)
            {
                _doneByIcon.TryGetValue(type.Icon, out int n);
                if (n > 0 && CrossedOff.ClearedDrawn(type.Icon)) return true;
            }
            return false;
        }

        /// <summary>
        /// The types a kind's used-up markers can be, as icons: what the catalog lists for it, and
        /// whatever its markers are actually wearing on this map. Both, because a marker can carry
        /// an icon of somebody's own choosing and a type can be in the catalog without one of it
        /// having been found yet -- and a switch for "all of these" has to mean all of them.
        /// </summary>
        private static List<string> ClearedIcons(Category kind)
        {
            var icons = CrossedOff.ClearedIconsOf(kind);
            foreach (var type in TypesOf(kind)) icons.Add(type.Icon);
            return icons;
        }

        private static void ToggleDone(Category kind) => SetKindDone(kind, !DoneShown(kind));

        private static void SetKindDone(Category kind, bool show)
        {
            if (Categories.IsResource(kind))
            {
                CrossedOff.SetClearedDrawn(ClearedIcons(kind), show);
                return;
            }
            if (CrossedOff.Splits(kind)) { CrossedOff.SetTypesHidden(kind, !show); return; }
            CrossedOff.SetGroupHidden(Categories.Label(kind), !show);
        }

        private static void ActionRow(Transform host, float x, float y, Action onClick, Func<string> text, Func<bool> quiet)
        {
            var row = MenuKit.Row(host, "", true, () =>
            {
                onClick();
                ClientPins.Restyle();
                Refresh();
            });
            Place(row, x, y, RowWidth, RowHeight);
            var label = row.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null) label.alignment = TextAlignmentOptions.MidlineLeft;
            _entries.Add(new Entry { Text = text, Quiet = quiet ?? (() => false), Label = label });
        }

        private static RectTransform SwitchRow(Minimap map, Transform host, float x, float y, float indent,
                                               string iconKey, string text, Action onClick, Func<bool> shown,
                                               Action onPlace = null, Action onOnly = null, Action onLocate = null,
                                               Func<string> caption = null, Category? placeKind = null,
                                               Func<bool> onlyLit = null, Func<bool> pingLit = null,
                                               Func<bool> placeLit = null,
                                               Action onFold = null, Func<bool> foldOpen = null,
                                               Func<Color> tint = null,
                                               Func<int> doneCount = null, Func<bool> doneShown = null,
                                               Action doneToggle = null, bool gone = false,
                                               bool head = false, bool icon = true, string tip = null)
        {
            var row = MenuKit.Row(host, "", true, () =>
            {
                onClick();
                ClientPins.Restyle();
                Refresh();
            });
            Place(row, x, y, RowWidth, RowHeight);
            var entry = new Entry { Shown = shown, Caption = caption, Head = head, Tint = tint };
            MenuKit.Tip(row.gameObject, null, tip);

            // A heading has no picture, there being no one picture for "Places". Its name starts
            // where a kind's picture would, which is what outdents it.
            if (icon)
            {
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
            }

            // Open or shut everything under this row. Drawn rather than waiting for the pointer,
            // unlike the four below: those do something to the markers and a reader scanning
            // names does not want to be asked about them, while this does nothing to the map at
            // all -- it is the shape of the list, which is what the reader is looking at. It
            // takes the first column, which a row that has one of these never uses for placing.
            if (onFold != null && onPlace == null)
            {
                var folder = RowIcon(row, MenuKit.Triangle(), 6f, RowHeight - 8f, () => onFold(),
                    lit: foldOpen,
                    tipTopic: "Open or shut",
                    tip: "Open everything under this heading, or shut it all. Each kind keeps its own triangle.");
                var face = folder.transform.Find("Glyph") as RectTransform;
                if (face != null && foldOpen != null)
                    // Turned a quarter turn to point down at what it opened, the way the row
                    // triangles and the corner button do.
                    _lights.Add(() => face.localRotation = Quaternion.Euler(0f, 0f, foldOpen() ? -90f : 0f));
            }

            // Each button keeps its own column, so a row missing one leaves a gap rather than
            // sliding the others along: ships have no "place by hand", and their only and ping
            // should still sit under everyone else's.
            bool anyButton = onPlace != null || onOnly != null || onLocate != null || doneToggle != null;
            float right = 6f;
            // Kept so the row can hide them again until the pointer arrives, and so each knows
            // whether it is lit -- a button doing something is shown whether or not anybody is
            // pointing at it. One list against the other, button by button: it is each button's
            // own business whether it has something to say.
            var group = new List<GameObject>(4);
            var lit = new List<Func<bool>>(4);
            if (anyButton)
            {
                if (onPlace != null)
                {
                    // A row for one of the map's own pin types has no kind to arm and passes its
                    // own answer: vanilla's selected icon, which is a different question asked of
                    // a different place.
                    Func<bool> armed = placeLit ?? (() => placeKind.HasValue && ManualPlace.Armed.HasValue
                                             && ManualPlace.Armed.Value == placeKind.Value);
                    group.Add(RowButton(row, "+", 15f, right, RowHeight - 8f, onPlace,
                        lit: armed,
                        tipTopic: "Place Manually",
                        tip: "Double-click on the map to place this marker manually"));
                    lit.Add(armed);
                }
                right += RowHeight - 4f;
                if (onOnly != null)
                {
                    var only = onlyLit ?? (() => placeKind.HasValue && Focus.Kind.HasValue
                                                 && Focus.Kind.Value == placeKind.Value);
                    group.Add(RowButton(row, "only", 11f, right, 34f, () => { onOnly(); Refresh(); },
                        lit: only,
                        tipTopic: "Only",
                        tip: "Hide every other kind of marker"));
                    lit.Add(only);
                }
                right += 38f;
                // Pinging leaves the list open and changes nothing, so unlike the others there
                // is nothing to redraw afterwards -- the pulse paints itself.
                if (onLocate != null)
                {
                    var ping = pingLit ?? (() => placeKind.HasValue && Locate.Pointing(placeKind.Value));
                    group.Add(RowIcon(row, MenuKit.Ping(), right, 22f, onLocate,
                        lit: ping,
                        tipTopic: "Ping",
                        tip: "Make every marker of this kind wave"));
                    lit.Add(ping);
                }
                right += 26f;
                // How many of these you are finished with, and the switch for them. The count is
                // on the button because the two belong together: a column of counts with the
                // switches somewhere else is what this panel used to be.
                if (doneToggle != null && doneCount != null && doneShown != null)
                {
                    var button = DoneButton(row, 0f, doneCount, doneShown, doneToggle, gone, right);
                    if (button != null)
                    {
                        group.Add(button);
                        var drawn = doneShown;
                        // Lit, and so shown without the pointer, while those markers are hidden:
                        // a row quietly leaving twelve markers off the map has to say so.
                        lit.Add(() => doneCount() > 0 && !drawn());
                    }
                }
                right += 52f;
            }
            float buttons = right;

            entry.Label = row.GetComponentInChildren<TextMeshProUGUI>();
            if (entry.Label != null)
            {
                entry.Label.text = text;
                entry.Label.alignment = TextAlignmentOptions.MidlineLeft;
                entry.Label.rectTransform.offsetMin = new Vector2(icon ? indent + IconSize + 14f : indent + 8f, 0f);
                // Keep the name clear of the buttons rather than letting a long kind run under
                // them, where it reads as part of the word on the button. While the buttons are
                // waiting for the pointer the name has the whole row and gives it back when they
                // appear: it is left-aligned, so its beginning never moves and the only thing cut
                // is a tail long enough to reach them.
                entry.Label.rectTransform.offsetMax = new Vector2(group.Count > 0 && onFold == null ? -6f : -buttons, 0f);
                entry.Label.overflowMode = TextOverflowModes.Ellipsis;
            }
            _entries.Add(entry);

            // The buttons wait for the pointer.
            //
            // Twenty rows with four buttons each is eighty things to read before the first
            // decision, and what a reader wants from this list is the names. So they are drawn on
            // the row the pointer is resting on, and on any row whose button is already doing
            // something: an armed kind, a filtered one, or one whose crossed-off markers are
            // hidden, has to go on saying so, or the only way to find what you asked for is to go
            // hunting for it.
            if (group.Count > 0)
            {
                // Glow rather than the row's own Hover, which only paints. Moving onto a button
                // inside the row does not leave the row -- the pointer's chain holds every
                // ancestor of what it is over -- so the buttons do not vanish from under the hand
                // reaching for them.
                var glow = row.gameObject.GetComponent<MenuKit.Glow>() ?? row.gameObject.AddComponent<MenuKit.Glow>();
                var label = entry.Label;
                foreach (var go in group) go.SetActive(false);
                _hovers.Add(() =>
                {
                    bool over = glow.Over;
                    // Each button on its own account, not the row's. One lit button used to bring
                    // the other three out with it, which meant pressing "hide everything crossed
                    // off" filled the list with four buttons a row -- the eighty things to read
                    // this rule exists to keep off the screen, put there by one press. A lit
                    // button is saying something about itself; its neighbors have nothing to add.
                    bool any = over;
                    for (int i = 0; i < group.Count; i++)
                    {
                        bool show = over || lit[i]();
                        if (group[i].activeSelf != show) group[i].SetActive(show);
                        if (show) any = true;
                    }
                    // The whole run of buttons is cleared whenever any of them is out, rather
                    // than measuring to the leftmost one that happens to be showing: the name
                    // would step in and out by the width of a button as the lit ones changed. A
                    // row with a fold button has one out at all times.
                    if (label != null)
                        label.rectTransform.offsetMax = new Vector2(any || onFold != null ? -buttons : -6f, 0f);
                });
            }
            return row;
        }

        /// <summary>
        /// The crossed-off count, which is also its switch: "12 done" on a row of places, "12
        /// gone" on a row of deposits, lit while those twelve are being drawn and dark while they
        /// are not. Nothing is drawn at all when the count is zero -- a switch for nothing is one
        /// more thing to read.
        ///
        /// <para>Given no row to sit in, it places itself in the panel at that y, which is how the
        /// buckets of crossed-off markers belonging to no kind keep their switch.</para>
        /// </summary>
        private static GameObject DoneButton(RectTransform row, float y, Func<int> count, Func<bool> shownNow,
                                             Action toggle, bool gone, float right = 6f)
        {
            if (count() == 0) return null;
            // "Done" on every one of them. It said "gone" on a deposit, the thing being used up
            // rather than finished with, and the distinction is real but it is not the button's
            // business: a reader going down a column wants one word for one action, and the tip
            // and the topic still say which of the two it is.
            string Word() => $"{count()} done";
            string tip = gone
                ? "Stop drawing the ones of these that have been used up, keeping the record of them"
                : "Stop drawing the ones you have crossed off, keeping the record of them";
            if (row != null)
            {
                var button = RowButton(row, Word(), 11f, right, 52f, () => { toggle(); ClientPins.Restyle(); Refresh(); },
                                       lit: () => count() > 0 && !shownNow(),
                                       tipTopic: gone ? "Used up" : "Finished with", tip: tip);
                var text = button.GetComponentInChildren<TextMeshProUGUI>();
                if (text != null) _lights.Add(() => text.text = Word());
                return button;
            }

            var loose = new GameObject("DoneButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            loose.transform.SetParent(_content, false);
            Place((RectTransform)loose.transform, RowWidth - 58f, y - 4f, 52f, RowHeight - 8f);
            var img = loose.GetComponent<Image>();
            img.color = MenuKit.ButtonIdle;
            img.raycastTarget = true;
            var btn = loose.GetComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => { toggle(); ClientPins.Restyle(); Refresh(); });
            MenuKit.Tip(loose, gone ? "Used up" : "Finished with", tip);
            var word = MenuKit.Text(loose.transform, "Glyph", Word(), 11f, MenuKit.GlyphIdle);
            word.alignment = TextAlignmentOptions.Center;
            word.raycastTarget = false;
            var wrt = word.rectTransform;
            wrt.anchorMin = Vector2.zero;
            wrt.anchorMax = Vector2.one;
            wrt.offsetMin = Vector2.zero;
            wrt.offsetMax = Vector2.zero;
            _lights.Add(() =>
            {
                bool on = !shownNow();
                word.text = Word();
                img.color = on ? MenuKit.ButtonOn : MenuKit.ButtonIdle;
                word.color = on ? MenuKit.GlyphOn : MenuKit.GlyphIdle;
            });
            return loose;
        }

        /// <summary>The same button with a picture in it, for an action a word would only clutter.</summary>
        private static GameObject RowIcon(RectTransform row, Sprite sprite, float right, float width, Action onClick,
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
            return go;
        }

        private static GameObject RowButton(RectTransform row, string glyph, float size, float right, float width, Action onClick,
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
            return go;
        }

        /// <summary>
        /// A box to check, with its words beside it: a setting that is either applied or not,
        /// rather than an action. Named for what checking it does, so the words never have to
        /// change under the pointer -- the box is what says whether it is on.
        /// </summary>
        private static void CheckBox(float x, float y, float width, string text, Func<bool> on,
                                     Action toggle, string tipTopic, string tip)
        {
            var go = new GameObject("CheckBox", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            go.transform.SetParent(_root.transform, false);
            Place((RectTransform)go.transform, x, y, width, 20f);
            var back = go.GetComponent<Image>();
            back.color = MenuKit.ButtonIdle;
            back.raycastTarget = true;
            var button = go.GetComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = back;
            button.onClick.AddListener(() => toggle());
            MenuKit.Tip(go, tipTopic, tip);

            var box = Mark(go.transform, MenuKit.Box(), 13f, 5f);
            var tick = Mark(go.transform, MenuKit.Check(), 10f, 6.5f);

            var label = MenuKit.Text(go.transform, "Glyph", text, 11f, MenuKit.GlyphIdle);
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.raycastTarget = false;
            var lrt = label.rectTransform;
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(22f, 0f);
            lrt.offsetMax = Vector2.zero;
            _lights.Add(() =>
            {
                bool lit = on();
                tick.enabled = lit;
                back.color = lit ? MenuKit.ButtonOn : MenuKit.ButtonIdle;
                box.color = lit ? MenuKit.GlyphOn : MenuKit.GlyphIdle;
                label.color = lit ? MenuKit.GlyphOn : MenuKit.GlyphIdle;
            });
        }

        /// <summary>One of the drawn marks inside a checkbox, at its left.</summary>
        private static Image Mark(Transform host, Sprite sprite, float size, float left)
        {
            var go = new GameObject("Mark", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(host, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(left, 0f);
            rt.sizeDelta = new Vector2(size, size);
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.preserveAspect = true;
            img.raycastTarget = false;
            return img;
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
            Place((RectTransform)go.transform, x + 8f, y - 2f, RowWidth - 16f, RowHeight - 6f);
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
            Place(header.rectTransform, x + 10f, y, Width - CornerButtons - 6f - 76f - 20f, HeaderHeight);
            header.alignment = TextAlignmentOptions.MidlineLeft;
        }

        /// <summary>
        /// A kind's name with how many of it are on the map. The number is the point of several of
        /// these rows: "Ore (3)" and "Ore (219)" are different maps, and the only way to tell them
        /// apart before this was to look at the map and count. A live kind counts the server's
        /// answer instead of recorded markers, because its pins are that answer.
        /// </summary>
        private static string KindLabel(Category kind)
        {
            if (Categories.IsLive(kind))
            {
                int live = kind == Category.Portal ? Portals.Count : Transports.CountOf(kind == Category.Cart);
                return Named(Categories.Label(kind), live, live);
            }
            _counts.TryGetValue(kind, out int n);
            _shown.TryGetValue(kind, out int lit);
            return Named(Categories.Label(kind), lit, n);
        }

        /// <summary>
        /// A name and its count: "Ore (219)" while all of them are drawn, "Ore (88 of 219)" while
        /// some are not, and the bare name where there are none at all.
        ///
        /// <para>None and all are both the plain total. "(0 of 219)" and "(219 of 219)" are sums
        /// nobody needed done: a row with none of it drawn is already dim, and a row with all of
        /// it drawn is the ordinary case. The pair is for the state that cannot be read off the
        /// row at all -- a heading whose kinds are mostly on, a kind with one of its types put
        /// away.</para>
        /// </summary>
        private static string Named(string label, int shown, int total)
        {
            if (TgmConfig.ShowCounts != null && !TgmConfig.ShowCounts.Value) return label;
            if (total <= 0) return label;
            return shown > 0 && shown < total ? $"{label} ({shown} of {total})" : $"{label} ({total})";
        }

        /// <summary>
        /// Count the markers of each kind, and how many of each kind and each type have been
        /// crossed off. Done afresh on every refresh rather than once when the list opened, for
        /// the reason the captions give: a pinned list stays open while the recording goes on
        /// around it, and crossing something off is a thing you do with the list open.
        /// </summary>
        private static void Recount()
        {
            // The map's own pins, which are in the same list as ours and have to be told apart
            // from them. Counted here so every number in the panel is read in one place and at
            // one moment.
            VanillaPins.Recount(Minimap.instance);
            _counts.Clear();
            _shown.Clear();
            _shownByIcon.Clear();
            _done.Clear();
            _doneByIcon.Clear();
            _doneOther.Clear();
            _all = 0;
            _allShown = 0;
            foreach (var pin in ClientPins.All)
            {
                _all++;
                // Asked once per marker and used by every row above: the line under the find box,
                // the headings, the kinds and the types all count the same answer.
                bool drawn = ClientPins.IsShown(pin);
                if (drawn) _allShown++;
                var kind = ClientPins.KindOf(pin);
                if (kind.HasValue)
                {
                    _counts.TryGetValue(kind.Value, out int have);
                    _counts[kind.Value] = have + 1;
                    if (drawn)
                    {
                        _shown.TryGetValue(kind.Value, out int lit);
                        _shown[kind.Value] = lit + 1;
                    }
                }
                if (drawn)
                {
                    string key = IconRegistry.Normalize(ClientPins.IconOf(pin)) ?? IconRegistry.FallbackKey;
                    _shownByIcon.TryGetValue(key, out int litIcon);
                    _shownByIcon[key] = litIcon + 1;
                }
                if (!pin.Checked) continue;
                if (!kind.HasValue)
                {
                    string bucket = CrossedOff.GroupOf(pin);
                    if (bucket == null) continue;
                    _doneOther.TryGetValue(bucket, out int other);
                    _doneOther[bucket] = other + 1;
                    continue;
                }
                _done.TryGetValue(kind.Value, out int done);
                _done[kind.Value] = done + 1;
                // Keyed exactly as the type rows are, which is how a type's count finds its row:
                // the normalized icon, falling back to the same key the type list falls back to.
                string icon = IconRegistry.Normalize(ClientPins.IconOf(pin)) ?? IconRegistry.FallbackKey;
                _doneByIcon.TryGetValue(icon, out int byIcon);
                _doneByIcon[icon] = byIcon + 1;
            }
        }

        /// <summary>
        /// The triangle at the head of a kind row, and the types under it while it is open.
        /// Returns the y below whatever was drawn.
        ///
        /// This is what the list was missing: a map where ore means copper, tin, silver and
        /// obsidian cannot be quietened kind by kind without losing the one thing you were looking
        /// for, and until now the only switch for a single type lived in the right-click menu of a
        /// marker of that type -- which you had to find on the map first.
        /// </summary>
        private static float Unfold(Minimap map, RectTransform row, float y, Category kind)
        {
            if (row == null || _types == null) return y;
            if (!_types.TryGetValue(kind, out var types) || types.Count == 0) return y;
            // One type usually needs no triangle, the row under it saying what the row above it
            // already says -- but only where the two are the same word. "Seeds (4)" over a map
            // holding nothing but carrot seeds says how many and not which, and the one thing
            // that would have said which was being hidden for being the only one. So the test is
            // whether the type is named after its kind rather than whether it is alone.
            if (types.Count == 1 && SaysTheSame(types[0].Label, Categories.Label(kind))) return y;
            var k = kind;
            Twisty(row, KindIndent, _unfolded.Contains(kind),
                   () => { if (!_unfolded.Remove(k)) _unfolded.Add(k); });
            if (!_unfolded.Contains(kind)) return y;

            foreach (var type in types)
            {
                TypeRow(map, y, kind, type);
                y -= RowHeight;
            }
            return y;
        }

        /// <summary>
        /// Two names that say the same thing, give or take the plural: "Campfire" is what the
        /// Campfires row already says, while "Carrot Seeds" is not what the Seeds row says.
        ///
        /// <para>The plural is made rather than taken off, which it was until 4.7.0: trimming the
        /// s off both left "Workbench" against "Workbenche", so the Workbenches row kept a
        /// triangle that opened onto one row saying Workbench. Anything whose plural is not a
        /// bare s was in the same boat -- benches, bushes, berries -- and guessing which letters
        /// to put back is how that goes wrong. Building the plural cannot: a word is the same word
        /// as another if adding s, es or ies to one of them gets you the other.</para>
        /// </summary>
        private static bool SaysTheSame(string type, string kind)
        {
            if (string.IsNullOrEmpty(type) || string.IsNullOrEmpty(kind)) return false;
            return Plural(type, kind) || Plural(kind, type);
        }

        /// <summary>Is <paramref name="many"/> the plural of <paramref name="one"/>, or the same word?</summary>
        private static bool Plural(string one, string many)
        {
            if (string.Equals(one, many, StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(one + "s", many, StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(one + "es", many, StringComparison.OrdinalIgnoreCase)) return true;
            return one.EndsWith("y", StringComparison.OrdinalIgnoreCase)
                   && string.Equals(one.Substring(0, one.Length - 1) + "ies", many,
                                    StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The triangle itself: pressed, the types under that kind are listed under it. A button of
        /// its own rather than another meaning for the row, which already means "show or hide
        /// these".
        /// </summary>
        private static void Twisty(RectTransform row, float indent, bool open, Action toggle)
        {
            var go = new GameObject("Twisty", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            go.transform.SetParent(row, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            // In the step this level is indented by, which is what keeps one level's triangles
            // in a column of their own.
            rt.anchoredPosition = new Vector2(indent - Twist + 2f, 0f);
            rt.sizeDelta = new Vector2(Twist, RowHeight - 6f);
            var hit = go.GetComponent<Image>();
            // Invisible: the triangle is what there is to press, and a box drawn round every one
            // of these would be a second grid down the side of the names.
            hit.color = new Color(1f, 1f, 1f, 0f);
            hit.raycastTarget = true;
            var button = go.GetComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = hit;
            button.onClick.AddListener(() =>
            {
                toggle();
                // The rows themselves change rather than their colors, so this is a redraw in
                // place -- the same thing pressing Enter in the find box asks for.
                _rebuildWanted = true;
            });

            var glyph = new GameObject("Triangle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            glyph.transform.SetParent(go.transform, false);
            var grt = (RectTransform)glyph.transform;
            grt.anchorMin = grt.anchorMax = grt.pivot = new Vector2(0.5f, 0.5f);
            grt.anchoredPosition = Vector2.zero;
            grt.sizeDelta = new Vector2(9f, 9f);
            // Turned a quarter turn to point down at what it opened, the way the stick pin in the
            // corner turns rather than changing picture.
            grt.localRotation = Quaternion.Euler(0f, 0f, open ? -90f : 0f);
            var img = glyph.GetComponent<Image>();
            img.sprite = MenuKit.Triangle();
            img.preserveAspect = true;
            img.raycastTarget = false;
            img.color = open ? MenuKit.Header : new Color(1f, 0.85f, 0.45f, 0.5f);
        }

        /// <summary>
        /// One type under an unfolded kind: its icon, its name, how many there are, how many of
        /// those you are finished with, and the same questions the kind row answers, asked of the
        /// type. No "place by hand": arming is by kind, and a marker placed by hand wears the
        /// kind's own icon, so a type would have nothing to arm.
        /// </summary>
        private static void TypeRow(Minimap map, float y, Category kind, Focus.TypeCount type)
        {
            var t = type;
            string group = CrossedOff.GroupFor(kind, t.Icon);
            bool gone = Categories.IsResource(kind);
            SwitchRow(map, _content, 0f, y, TypeIndent, t.Icon, TypeLabel(t),
                () => ToggleType(kind, t.Icon),
                () => AllShown && IsShown(kind) && !TgmConfig.IsIconHidden(t.Icon),
                caption: () => TypeLabel(t),
                onOnly: () => Focus.SetIcon(t.Icon),
                onLocate: () => Locate.Icon(t.Icon, t.Label),
                onlyLit: () => Focus.Icon != null && IconRegistry.SameKey(Focus.Icon, t.Icon),
                pingLit: () => Locate.PointingAt(t.Icon),
                // The crossed-off ones of this type alone -- a player done with their burial
                // chambers who still wants the sunken crypts they have cleared. That was the one
                // thing the right-hand column could do that the kind rows could not, and this is
                // where it lands now it is gone: on the type it is about. Used-up deposits and
                // plants are hidden by icon rather than by group, having no group of their own,
                // and that is the same thing asked a different way.
                doneCount: () =>
                {
                    _doneByIcon.TryGetValue(t.Icon, out int n);
                    return n;
                },
                doneShown: () => gone ? CrossedOff.ClearedDrawn(t.Icon) : !CrossedOff.IsGroupHidden(group),
                doneToggle: () =>
                {
                    if (gone)
                    {
                        CrossedOff.SetClearedDrawn(new[] { t.Icon }, !CrossedOff.ClearedDrawn(t.Icon));
                        return;
                    }
                    CrossedOff.SetGroupHidden(group, !CrossedOff.IsGroupHidden(group));
                },
                gone: gone);
        }

        /// <summary>Flip one type, from a row that is dim or bright accordingly.</summary>
        private static void ToggleType(Category kind, string icon)
        {
            SetTypeShown(kind, icon, !(IsShown(kind) && !TgmConfig.IsIconHidden(icon)));
        }

        /// <summary>
        /// Show or hide one type.
        ///
        /// <para>A type is hidden by its icon, the only thing on a marker that says which type it
        /// is -- the same list a marker's own right-click menu writes to, so the two places that
        /// can hide a type agree with each other.</para>
        ///
        /// <para><b>A type can also be hidden by its kind</b>, and that is what this has to be
        /// careful about. Hide Berries and then press Raspberries, and the obvious reading of the
        /// press -- add raspberries to the hidden icons -- hid what was already hidden: a dim row
        /// pressed, and nothing changed. A switch has to switch.</para>
        ///
        /// <para>So showing a type whose kind is hiding it hands that hiding over to the icons
        /// before turning the kind back on: every other type of that kind goes on the hidden list,
        /// where the kind's own switch had been keeping it. The map then changes by exactly the
        /// one row that was pressed -- raspberries appear, blueberries and cloudberries stay away
        /// -- which is what the row promises and what pressing the kind's own row would not do.
        /// The way back out is the kind's row, or the put-back row at the foot, which counts those
        /// types and says so.</para>
        ///
        /// <para>The master switch is deliberately not part of this. While it is off nothing is
        /// drawn at all and every row in the list says so by being dim; taking a press on a type
        /// as a reason to turn the whole mod's markers back on would be the one switch in here
        /// that reaches outside what it names.</para>
        /// </summary>
        private static void SetTypeShown(Category kind, string icon, bool show)
        {
            // What this row is about, leaving the master out of it.
            bool drawn = IsShown(kind) && !TgmConfig.IsIconHidden(icon);
            if (drawn == show) return;
            if (!show) { TgmConfig.AddHiddenIcon(icon); return; }
            if (!IsShown(kind))
            {
                if (_types != null && _types.TryGetValue(kind, out var siblings))
                    foreach (var other in siblings)
                        if (!IconRegistry.SameKey(other.Icon, icon)) TgmConfig.AddHiddenIcon(other.Icon);
                if (TgmConfig.ShowKind.TryGetValue(kind, out var entry)) entry.Value = true;
            }
            TgmConfig.RemoveHiddenIcon(icon);
        }

        /// <summary>A type's name, with how many of it are drawn out of how many there are.</summary>
        private static string TypeLabel(Focus.TypeCount type)
        {
            _shownByIcon.TryGetValue(type.Icon, out int lit);
            return Named(type.Label, lit, type.Count);
        }

        /// <summary>
        /// Is there anything for the put-back row to put back? The master switch is left out of
        /// this on purpose: it has a button of its own saying exactly what it does, and a row
        /// offering to undo it is the duplication that made this panel confusing. A focus is left
        /// out for the same reason -- the row under the find box is about nothing else.
        /// </summary>
        private static bool AnythingPutBack()
        {
            if (TgmConfig.HiddenKindCount() > 0 || TgmConfig.HiddenIconCount() > 0) return true;
            if (ViewPrefs.Count > 0 || CrossedOff.HiddenGroupCount() > 0) return true;
            if (CrossedOff.HiddenClearedCount() > 0) return true;
            return TgmConfig.ShowClearedDeposits != null && !TgmConfig.ShowClearedDeposits.Value;
        }

        /// <summary>
        /// What the put-back row will put back, counted. A row that says "Show everything hidden"
        /// and nothing else leaves the reader to wonder what they are about to get, and on a map
        /// somebody has spent a month quietening that is a fair thing to wonder.
        /// </summary>
        private static string PutBackText()
        {
            var parts = new List<string>(5);
            int kinds = TgmConfig.HiddenKindCount();
            int icons = TgmConfig.HiddenIconCount();
            int single = ViewPrefs.Count;
            int groups = CrossedOff.HiddenGroupCount() + CrossedOff.HiddenClearedCount();
            if (kinds > 0) parts.Add(kinds == 1 ? "1 kind" : kinds + " kinds");
            if (icons > 0) parts.Add(icons == 1 ? "1 type" : icons + " types");
            if (single > 0) parts.Add(single == 1 ? "1 marker" : single + " markers");
            if (groups > 0) parts.Add(groups == 1 ? "1 crossed off" : groups + " crossed off");
            if (TgmConfig.ShowClearedDeposits != null && !TgmConfig.ShowClearedDeposits.Value)
                parts.Add("cleared deposits");
            return parts.Count == 0
                ? "Show everything hidden"
                : "Show everything hidden — " + string.Join(", ", parts.ToArray());
        }

        private static string IconKeyFor(Category kind)
        {
            return TgmConfig.CategoryIcon.TryGetValue(kind, out var entry) ? entry.Value : Categories.DefaultIcon(kind);
        }

        private static bool IsShown(Category kind)
        {
            return !TgmConfig.ShowKind.TryGetValue(kind, out var entry) || entry.Value;
        }

        /// <summary>Top-left anchored, so a row can be placed in the panel or in what scrolls.</summary>
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
            Recount();
            foreach (var light in _lights) light();
            foreach (var hover in _hovers) hover();
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
                entry.Label.color = shown ? (entry.Head ? MenuKit.Header : MenuKit.Body) : MenuKit.Dim;
                if (entry.Icon != null)
                {
                    // The same dimming either way: a hidden row's picture goes faint, in whatever
                    // color that picture is.
                    var tint = entry.Tint != null ? entry.Tint() : Color.white;
                    entry.Icon.color = shown ? tint : new Color(tint.r, tint.g, tint.b, 0.3f);
                }
            }
            // The put-back row is drawn only while there is something to put back, and the press
            // that just happened may have been the one that changed that. A row coming or going is
            // a change to the rows rather than to their colors, so it takes a redraw in place.
            if (AnythingPutBack() != _resetShown) _rebuildWanted = true;
        }

        internal static void Close()
        {
            if (_root != null) Object.Destroy(_root);
            _root = null;
            _rect = null;
            _viewport = null;
            _content = null;
            _barTrack = null;
            _barHandle = null;
            _contentHeight = 0f;
            _viewportHeight = 0f;
            _wheel = 0f;
            _entries.Clear();
            _lights.Clear();
            _hovers.Clear();
            _find = null;
            _pinImage = null;
            _pinGlow = null;
            _gearImage = null;
            _gearGlow = null;
            // The settings belong to this list and have nothing to sit in front of once it is
            // gone.
            SettingsMenu.Close();
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
            PaintGear();
            PaintFoldAll();
            SettingsMenu.Update();
            float wheel = _wheel;
            _wheel = 0f;
            if (Mathf.Abs(wheel) > 0.001f)
            {
                _scroll -= wheel * RowHeight * 3f;
                ClampScroll();
            }
            // Hovering is not a press, so the rows that hide their buttons are asked every frame
            // rather than after a click.
            foreach (var hover in _hovers) hover();
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
            {
                // A click in the settings is a click in this list: they are its own, standing in
                // front of it, and an unpinned list that closed under them would take them with
                // it the first time anybody pressed one.
                if (SettingsMenu.Hovered) return;
                if (!RectTransformUtility.RectangleContainsScreenPoint(_rect, ZInput.pointerPosition, _camera)) Close();
            }
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

    /// <summary>
    /// The wheel belongs to the list only while the pointer is on it and there is more list than
    /// fits: the map reads the wheel straight from ZInput to zoom, and a list that can be pinned
    /// open all session must not take the zoom away from the map behind it.
    /// </summary>
    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetMouseScrollWheel))]
    internal static class ZInput_GetMouseScrollWheel_KindMenu_Patch
    {
        private static void Postfix(ref float __result)
        {
            if (__result == 0f || !KindMenu.CanScroll || !KindMenu.Hovered) return;
            KindMenu.NoteWheel(__result);
            __result = 0f;
        }
    }
}
