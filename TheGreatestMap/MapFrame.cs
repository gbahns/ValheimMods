using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
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
        internal const float ButtonSize = 28f;      // the pause mark's size; every button matches it
        private const float ButtonGap = 8f;
        private const float Margin = 16f;
        private const float MinWidth = 420f, MinHeight = 300f;   // small enough to tuck away, big enough to read
        private const float TopReserve = Margin + ButtonSize + ButtonGap + 40f;  // our row and the biome name
        private const float SmallestIcons = 0.4f;
        private const float MoverHeight = 16f;

        private static RectTransform _grip, _mover, _maximize;
        private static GameObject _root;          // the large root we built our handles into
        private static Vector2 _base = new Vector2(float.NaN, float.NaN); // the game's own inset
        private static Vector2 _grow, _pos;
        private static bool _read;
        private static float _height = float.NaN;   // the rect height the zoom was last matched to
        private static readonly List<Column> _columns = new List<Column>();

        /// <summary>One of vanilla's icon panels, with the place and size it has when left alone.</summary>
        private sealed class Column
        {
            internal RectTransform Rect;
            internal Vector2 Pos;
            internal Vector3 Scale;
            internal float Reach;     // how far its top stands above the map's bottom edge
        }
        private static Vector2 _restoreGrow, _restorePos;
        private static bool _restorable;

        /// <summary>
        /// The middle of the nth button in the row along the map's top-right corner, counting from
        /// the right: the map pin, then maximize, then the pause mark. One place decides the row so
        /// the three cannot drift apart, and "Pause Button Offset" moves the whole row.
        ///
        /// The middle rather than a corner, because each button is pivoted there: growing one under
        /// the pointer then swells it evenly instead of dragging it down and to the left.
        /// </summary>
        internal static Vector2 Slot(int index)
        {
            float dx = TgmConfig.PauseButtonOffsetX != null ? TgmConfig.PauseButtonOffsetX.Value : 0f;
            float dy = TgmConfig.PauseButtonOffsetY != null ? TgmConfig.PauseButtonOffsetY.Value : 0f;
            float half = ButtonSize * 0.5f;
            return new Vector2(-Margin - half + dx - index * (ButtonSize + ButtonGap), -Margin - half + dy);
        }

        internal static void Reset()
        {
            _grip = null;
            _mover = null;
            _maximize = null;
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
            if (_root != map.m_largeRoot || _grip == null || _mover == null || _maximize == null)
            {
                _root = map.m_largeRoot;
                _base = new Vector2(float.NaN, float.NaN);
                Build(root);
                CaptureColumns(root);
                ClipToMap(map);
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

            _maximize = Handle(root, "TGM_MapMaximize", MaximizeColor, Square(), null);
            _maximize.anchorMin = _maximize.anchorMax = new Vector2(1f, 1f);
            _maximize.pivot = new Vector2(0.5f, 0.5f);
            _maximize.sizeDelta = new Vector2(ButtonSize, ButtonSize);
            _maximize.gameObject.AddComponent<Click>().OnClick = ToggleFullScreen;
            _maximize.gameObject.AddComponent<MenuKit.Glow>();

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
            if (onDrag == null) Object.Destroy(handle);   // a plain button, nothing to drag
            else { handle.OnDrag = onDrag; handle.OnEnd = Save; }
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
            if (_maximize != null)
            {
                if (_maximize.anchoredPosition != Slot(1)) _maximize.anchoredPosition = Slot(1);
                var img = _maximize.GetComponent<Image>();
                var hover = _maximize.GetComponent<MenuKit.Glow>();
                bool lit = hover != null && hover.Over;
                // Orange while the map is full, the same orange the pause mark wears while pausing
                // is on, so "this is switched on" reads the same way along the whole row.
                var normal = IsFull() ? PauseToggle.Paused : MaximizeColor;
                if (img != null) img.color = MenuKit.Lit(normal, lit);
                _maximize.localScale = MenuKit.Magnified(Vector3.one, lit);
            }
            MoveBiomeName(root);
            FitColumns(parent, size);
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

        private static readonly Color MaximizeColor = new Color(1f, 0.93f, 0.72f, 0.85f);

        /// <summary>
        /// Keep markers and their labels inside the map. Both are given a marker's own place, and
        /// the game decides what to draw by asking whether that one point is on the visible map --
        /// so a marker just inside the bottom edge is drawn with its icon and its words hanging off
        /// the frame and over whatever lies beyond. Both roots cover exactly the map, so masking
        /// them cuts anything that overhangs off at the edge. This is also what lets markers be
        /// kept past the edge rather than culled at it: see Minimap_IsPointVisible_Patch.
        /// </summary>
        private static void ClipToMap(Minimap map)
        {
            foreach (var root in new[] { map.m_pinNameRootLarge, map.m_pinRootLarge })
                if (root != null && root.GetComponent<RectMask2D>() == null)
                    root.gameObject.AddComponent<RectMask2D>();
        }

        /// <summary>The icon panels as the game leaves them, before any shrinking of ours.</summary>
        private static void CaptureColumns(RectTransform root)
        {
            _columns.Clear();
            foreach (var name in new[] { "IconPanel", "IconPanel2" })
            {
                var rt = root.Find(name) as RectTransform;
                if (rt == null) continue;
                _columns.Add(new Column
                {
                    Rect = rt,
                    Pos = rt.anchoredPosition,
                    Scale = rt.localScale,
                    Reach = Mathf.Abs(rt.anchoredPosition.y) + rt.rect.height * 0.5f,
                });
            }
        }

        /// <summary>
        /// Vanilla's icon buttons are two panels of a fixed size, anchored to the map's bottom
        /// edge, standing some 480 units tall between them. A map pulled in smaller than that is
        /// shorter than its own buttons, and they hang off the top and bottom of it. They are
        /// scaled down to fit, place and all, so the column keeps its proportions and its margin
        /// from the right edge, and put back the moment there is room again.
        /// </summary>
        private static void FitColumns(RectTransform parent, Vector2 size)
        {
            if (_columns.Count == 0 || parent == null) return;
            float reach = 0f;
            foreach (var c in _columns) if (c.Reach > reach) reach = c.Reach;
            if (reach <= 1f) return;
            float room = Mathf.Max(0f, parent.rect.height + size.y - TopReserve);
            float factor = Mathf.Clamp(room / reach, SmallestIcons, 1f);
            foreach (var c in _columns)
            {
                if (c.Rect == null) continue;
                var pos = c.Pos * factor;
                var scale = c.Scale * factor;
                if (c.Rect.anchoredPosition != pos) c.Rect.anchoredPosition = pos;
                if (c.Rect.localScale != scale) c.Rect.localScale = scale;
            }
        }

        /// <summary>
        /// The game writes the biome you are pointing at in the map's top-right corner, which is
        /// where our own buttons want to be. Rather than crowd in beside it, the name is moved down
        /// to sit under the row: it is a plain child of the map root, and the game only ever sets
        /// its text, so where it sits is ours to decide.
        /// </summary>
        private static void MoveBiomeName(RectTransform root)
        {
            var biome = root.Find("large_biome") as RectTransform;
            if (biome == null) return;
            float want = Slot(0).y - ButtonSize - ButtonGap - biome.rect.height * 0.5f;
            if (!Mathf.Approximately(biome.anchoredPosition.y, want))
                biome.anchoredPosition = new Vector2(biome.anchoredPosition.x, want);
        }

        /// <summary>The map is as large as the screen allows.</summary>
        internal static bool IsFull()
        {
            if (float.IsNaN(_base.x)) return false;
            var headroom = -_base;
            return _grow.x >= headroom.x - 1f && _grow.y >= headroom.y - 1f;
        }

        /// <summary>Double-clicking the strip fills the screen, and doing it again goes back.</summary>
        private static void ToggleFullScreen()
        {
            if (float.IsNaN(_base.x)) return;
            var headroom = -_base;
            if (IsFull())
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

        internal sealed class Click : MonoBehaviour, IPointerClickHandler
        {
            public System.Action OnClick;
            void IPointerClickHandler.OnPointerClick(PointerEventData e)
            {
                if (OnClick != null) OnClick();
            }
        }

        private static Sprite _square;

        /// <summary>The usual maximize mark: an open square, drawn rather than shipped.</summary>
        private static Sprite Square()
        {
            if (_square != null) return _square;
            const int n = 24, border = 3, inset = 3;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    bool inside = x >= inset && x < n - inset && y >= inset && y < n - inset;
                    bool hollow = x >= inset + border && x < n - inset - border
                                  && y >= inset + border && y < n - inset - border;
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(inside && !hollow ? 255 : 0));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.hideFlags = HideFlags.HideAndDontSave;
            _square = Sprite.Create(tex, new Rect(0f, 0f, n, n), new Vector2(0.5f, 0.5f), 100f);
            _square.hideFlags = HideFlags.HideAndDontSave;
            return _square;
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

    /// <summary>
    /// The game keeps a marker only while the point it stands on is inside the visible map, and a
    /// marker is drawn centered on that point with its label beside it. So a marker vanished whole
    /// while half of it was still on the map, and one with a label lost the words while they were
    /// still perfectly readable. This widens the test by enough to cover an icon and a fair label;
    /// what then overhangs the frame is cut off by the masks on the two pin roots rather than
    /// drawn over the rest of the screen.
    /// </summary>
    [HarmonyPatch(typeof(Minimap), "IsPointVisible")]
    internal static class Minimap_IsPointVisible_Patch
    {
        private const float EdgePixels = 220f;   // half an icon, and a label of ordinary length

        private static void Postfix(Minimap __instance, Vector3 p, RawImage map, ref bool __result)
        {
            if (__result || map == null || __instance == null) return;
            // The minimap asks this too, and it has no mask of its own: keeping markers past its
            // edge puts their labels on the HUD beside it. Only the large map, whose pin roots are
            // masked, can afford to be generous.
            if (map != __instance.m_mapImageLarge) return;
            var rect = map.rectTransform.rect;
            if (rect.width <= 1f || rect.height <= 1f) return;
            var uv = map.uvRect;
            // The same arithmetic as the game's own WorldToMapPoint, inlined: this runs for every
            // marker off the map's edge, which is most of them on a map zoomed in.
            float half = __instance.m_textureSize * 0.5f;
            float mx = (p.x / __instance.m_pixelSize + half) / __instance.m_textureSize;
            float my = (p.z / __instance.m_pixelSize + half) / __instance.m_textureSize;
            float ex = EdgePixels / rect.width * uv.width;
            float ey = EdgePixels / rect.height * uv.height;
            __result = mx > uv.xMin - ex && mx < uv.xMax + ex && my > uv.yMin - ey && my < uv.yMax + ey;
        }
    }
}
