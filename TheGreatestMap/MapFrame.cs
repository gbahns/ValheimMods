using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TheGreatestMap
{
    /// <summary>
    /// The large map is drawn inside a fixed margin — measured at 88% by 81% of the screen, with a
    /// uniform frame of wasted room around it — and on a big screen that is a lot of map going
    /// spare. This grows it, and moves it, and remembers where you put it.
    ///
    /// It works on m_largeRoot's own rect rather than scaling anything. Everything inside is
    /// stretched to that rect: the map image, the pin root and the pin name root all cover exactly
    /// the same ground, so growing it grows them together, and pins keep landing where they belong
    /// because they are placed from the image's rect. Scaling would have magnified the labels and
    /// buttons along with the map; this way the map gets bigger and they stay the size they were,
    /// so a bigger map is a less crowded one. Nothing about the game's own math needs changing: the
    /// zoom recomputes the visible width from the rect's aspect every frame, and clicks go through
    /// ScreenPointToLocalPointInRectangle, which respects whatever we have done to the transform.
    ///
    /// Vanilla only ever calls SetActive on that object, so its size and position are ours to set.
    /// </summary>
    internal static class MapFrame
    {
        private const float GripSize = 22f;
        private const float MinWidth = 420f, MinHeight = 300f;   // small enough to tuck away, big enough to read
        private const float MoverHeight = 16f;

        private static RectTransform _grip, _mover;
        private static GameObject _root;          // the large root we built our handles into
        private static Vector2 _base = new Vector2(float.NaN, float.NaN); // the game's own inset
        private static Vector2 _grow, _pos;
        private static bool _read;
        private static float _height = float.NaN;   // the rect height the zoom was last matched to
        private static Vector2 _restoreGrow, _restorePos;
        private static bool _restorable;

        internal static void Reset()
        {
            _grip = null;
            _mover = null;
            _root = null;
            _read = false;
        }

        internal static void Update()
        {
            var map = Minimap.instance;
            if (map == null || map.m_largeRoot == null) return;
            var root = map.m_largeRoot.transform as RectTransform;
            if (root == null) return;

            // The HUD is rebuilt on every login, which takes the map with it, so handles built into
            // the old one are gone and the ones we hold are dead references.
            if (_root != map.m_largeRoot || _grip == null || _mover == null)
            {
                _root = map.m_largeRoot;
                _base = new Vector2(float.NaN, float.NaN);
                Build(root);
            }
            if (float.IsNaN(_base.x)) _base = root.sizeDelta; // whatever the game asks for, before we touch it
            if (!_read) { Load(); _read = true; }
            Apply(map, root);
        }

        private static void Build(RectTransform root)
        {
            _mover = Handle(root, "TGM_MapMover", new Color(0f, 0f, 0f, 0f), null, OnMove);
            _mover.anchorMin = new Vector2(0f, 1f);
            _mover.anchorMax = new Vector2(1f, 1f);
            _mover.pivot = new Vector2(0.5f, 1f);
            _mover.sizeDelta = new Vector2(0f, MoverHeight);
            _mover.anchoredPosition = Vector2.zero;
            _mover.gameObject.AddComponent<DoubleClick>().OnDouble = ToggleFullScreen;

            _grip = Handle(root, "TGM_MapGrip", new Color(1f, 0.85f, 0.45f, 0.65f), MenuKit.Grip(), OnResize);
            _grip.anchorMin = _grip.anchorMax = new Vector2(1f, 0f);
            _grip.pivot = new Vector2(1f, 0f);
            _grip.sizeDelta = new Vector2(GripSize, GripSize);
            _grip.anchoredPosition = new Vector2(-4f, 4f);
        }

        private static RectTransform Handle(RectTransform parent, string name, Color color, Sprite sprite, System.Action<Vector2> onDrag)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(MenuKit.DragHandle));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            if (sprite != null) img.sprite = sprite;
            img.raycastTarget = true;
            var handle = go.GetComponent<MenuKit.DragHandle>();
            handle.OnDrag = onDrag;
            handle.OnEnd = Save;
            go.transform.SetAsLastSibling(); // above the map image, so the drag is ours and not a pan
            return (RectTransform)go.transform;
        }

        /// <summary>
        /// The map is anchored to the whole screen with its pivot in the middle, so its size is an
        /// inset (sizeDelta) and its place is an offset from the middle. Growing the inset by d
        /// widens it by d/2 on each side, which is why moving one edge also moves the middle.
        /// </summary>
        private static void Apply(Minimap map, RectTransform root)
        {
            var parent = root.parent as RectTransform;
            var headroom = -_base;                                   // reaching 0,0 fills the screen
            // Smaller than the game's own size is allowed as well, down to something still worth
            // looking at. The floor never pushes the map bigger, however little screen there is.
            var floor = parent != null
                ? new Vector2(Mathf.Min(0f, MinWidth - parent.rect.width - _base.x),
                              Mathf.Min(0f, MinHeight - parent.rect.height - _base.y))
                : Vector2.zero;
            _grow = new Vector2(Mathf.Clamp(_grow.x, floor.x, Mathf.Max(floor.x, headroom.x)),
                                Mathf.Clamp(_grow.y, floor.y, Mathf.Max(floor.y, headroom.y)));
            var size = _base + _grow;
            // Keep it on screen: at full size there is nowhere to go, and a map dragged off the
            // edge would take its own handles with it.
            if (parent != null)
            {
                var slack = new Vector2(Mathf.Max(0f, -size.x) * 0.5f, Mathf.Max(0f, -size.y) * 0.5f);
                _pos = new Vector2(Mathf.Clamp(_pos.x, -slack.x, slack.x), Mathf.Clamp(_pos.y, -slack.y, slack.y));
            }
            if (root.sizeDelta != size || root.anchoredPosition != _pos)
            {
                root.sizeDelta = size;
                root.anchoredPosition = _pos;
                // The game lays the markers out only when it thinks something moved, and resizing
                // the map is not one of the things it knows about. Without this they keep the
                // places worked out for the old rect: measured from its lower-left corner, which
                // the growing map carries down and to the left, taking every marker with it.
                ClientPins.Restyle();
                MatchZoom(map, parent, size);
            }

            // The catcher that closes the map when you click beside it is stretched to the map and
            // cancels the game's own inset to cover the screen. Ours has to cancel what we did too,
            // or it grows past the screen edge as the map grows.
            var outside = root.Find("OutsideMapClick") as RectTransform;
            if (outside != null)
            {
                var want = -size;
                if (outside.sizeDelta != want) outside.sizeDelta = want;
                if (outside.anchoredPosition != -_pos) outside.anchoredPosition = -_pos;
            }
        }

        /// <summary>Double-clicking the strip fills the screen, and doing it again goes back.</summary>
        private static void ToggleFullScreen()
        {
            if (float.IsNaN(_base.x)) return;
            var headroom = -_base;
            bool full = _grow.x >= headroom.x - 1f && _grow.y >= headroom.y - 1f;
            if (full)
            {
                // Back to where it was before, or to the size the game itself draws if this map has
                // never been anywhere else.
                _grow = _restorable ? _restoreGrow : Vector2.zero;
                _pos = _restorable ? _restorePos : Vector2.zero;
            }
            else
            {
                _restoreGrow = _grow;
                _restorePos = _pos;
                _restorable = true;
                _grow = headroom;
                _pos = Vector2.zero;
            }
            Save();
        }

        internal sealed class DoubleClick : MonoBehaviour, IPointerClickHandler
        {
            public System.Action OnDouble;
            void IPointerClickHandler.OnPointerClick(PointerEventData e)
            {
                if (e != null && e.clickCount == 2 && OnDouble != null) OnDouble();
            }
        }

        /// <summary>
        /// Keep the map drawn at the same scale as it grows, so a bigger window shows more of the
        /// world rather than the same world drawn bigger. The scale is the rect's height over the
        /// zoom -- on both axes, since the visible width is derived from the rect's aspect -- so
        /// holding it steady means letting the zoom out by exactly the proportion the height grew.
        /// Widening alone already shows more and needs nothing.
        /// </summary>
        private static void MatchZoom(Minimap map, RectTransform parent, Vector2 size)
        {
            if (parent == null) return;
            float height = parent.rect.height + size.y;
            if (height <= 1f) return;
            bool wanted = TgmConfig.MapResizeShowsMore == null || TgmConfig.MapResizeShowsMore.Value;
            if (wanted && !float.IsNaN(_height) && _height > 1f && !Mathf.Approximately(height, _height))
                map.LargeZoom *= height / _height;
            _height = height;
        }

        private static float Scale()
        {
            var canvas = _grip != null ? _grip.GetComponentInParent<Canvas>() : null;
            float scale = canvas != null ? canvas.scaleFactor : 1f;
            return scale > 0f ? scale : 1f;
        }

        private static void OnMove(Vector2 screenDelta)
        {
            _pos += screenDelta / Scale();
        }

        /// <summary>The grip pulls the lower-right corner, so the upper-left stays where it is.</summary>
        private static void OnResize(Vector2 screenDelta)
        {
            var d = screenDelta / Scale();
            var before = _grow;
            _grow += new Vector2(d.x, -d.y);
            var moved = _grow - before;      // after clamping in Apply this may be less than asked
            _pos += new Vector2(moved.x, -moved.y) * 0.5f;
        }

        // ── the remembered placement ────────────────────────────────────────────────

        private static void Load()
        {
            _grow = Parse(TgmConfig.MapExtraSize != null ? TgmConfig.MapExtraSize.Value : null);
            _pos = Parse(TgmConfig.MapPosition != null ? TgmConfig.MapPosition.Value : null);
        }

        private static void Save()
        {
            if (TgmConfig.MapExtraSize != null) TgmConfig.MapExtraSize.Value = Text(_grow);
            if (TgmConfig.MapPosition != null) TgmConfig.MapPosition.Value = Text(_pos);
        }

        /// <summary>Re-read the saved placement, for when the settings are edited by hand.</summary>
        internal static void Reload() => _read = false;

        private static Vector2 Parse(string s)
        {
            if (string.IsNullOrEmpty(s)) return Vector2.zero;
            var parts = s.Split(',');
            if (parts.Length != 2) return Vector2.zero;
            if (float.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
                float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float y))
                return new Vector2(x, y);
            return Vector2.zero;
        }

        private static string Text(Vector2 v) =>
            v.x.ToString("0.#", CultureInfo.InvariantCulture) + "," + v.y.ToString("0.#", CultureInfo.InvariantCulture);
    }
}
