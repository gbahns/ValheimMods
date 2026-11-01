using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TheGreatestMap
{
    /// <summary>Just enough runtime UI for a small menu: the game's TMP font, a panel, and hoverable button rows.</summary>
    internal static class MenuKit
    {
        internal static readonly Color PanelColor = new Color(0.06f, 0.045f, 0.03f, 0.97f);
        internal static readonly Color OutlineColor = new Color(0.75f, 0.62f, 0.4f, 0.9f);
        internal static readonly Color RowColor = new Color(1f, 1f, 1f, 0.04f);
        internal static readonly Color RowHover = new Color(1f, 1f, 1f, 0.2f);
        internal static readonly Color Body = new Color(0.95f, 0.92f, 0.85f);
        internal static readonly Color Dim = new Color(0.55f, 0.52f, 0.48f);
        internal static readonly Color Header = new Color(1f, 0.85f, 0.45f);

        private static TMP_FontAsset _font;
        private static Material _fontMaterial;

        private static void EnsureFont()
        {
            if (_font != null) return;
            TMP_Text source = null;
            if (TextInput.instance != null && TextInput.instance.m_topic != null) source = TextInput.instance.m_topic;
            else if (MessageHud.instance != null) source = MessageHud.instance.m_messageCenterText;
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

        private static Sprite _tack;

        /// <summary>
        /// A stick pin: round head, collar, needle. Drawn here rather than taken from the game,
        /// which has no such picture, and deliberately not the map-pin shape the marker button
        /// wears -- that one means "a marker", and this one means "stay put".
        /// </summary>
        internal static Sprite Tack()
        {
            if (_tack != null) return _tack;
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    int hits = 0;
                    // Sampled nine times a pixel, as the map pin is, so the round head and the
                    // taper of the needle come out smooth at the size this is actually drawn.
                    for (int sy = 0; sy < 3; sy++)
                        for (int sx = 0; sx < 3; sx++)
                            if (InTack(x + (sx + 0.5f) / 3f, y + (sy + 0.5f) / 3f)) hits++;
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(255 * hits / 9));
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.hideFlags = HideFlags.HideAndDontSave;
            _tack = Sprite.Create(tex, new Rect(0f, 0f, n, n), new Vector2(0.5f, 0.5f), 100f);
            _tack.hideFlags = HideFlags.HideAndDontSave;
            return _tack;
        }

        private static Sprite _ping;

        /// <summary>
        /// A ping: a point with two rings going out from it, which is what the button does to
        /// the markers it names. Drawn rather than written because a word in a row of rows reads
        /// as another name, while a picture reads as a thing to press.
        /// </summary>
        internal static Sprite Ping()
        {
            if (_ping != null) return _ping;
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    int hits = 0;
                    for (int sy = 0; sy < 3; sy++)
                        for (int sx = 0; sx < 3; sx++)
                            if (InPing(x + (sx + 0.5f) / 3f, y + (sy + 0.5f) / 3f)) hits++;
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(255 * hits / 9));
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.hideFlags = HideFlags.HideAndDontSave;
            _ping = Sprite.Create(tex, new Rect(0f, 0f, n, n), new Vector2(0.5f, 0.5f), 100f);
            _ping.hideFlags = HideFlags.HideAndDontSave;
            return _ping;
        }

        private static bool InPing(float x, float y)
        {
            float d = Mathf.Sqrt((x - 32f) * (x - 32f) + (y - 32f) * (y - 32f));
            if (d <= 7f) return true;                       // the point itself
            if (d >= 14f && d <= 17.5f) return true;        // the near ring
            if (d >= 24f && d <= 27.5f) return true;        // and the far one
            return false;
        }

        private static Sprite _triangle;

        /// <summary>
        /// A right-pointing triangle, for the head of a row that unfolds into the rows under it.
        /// Drawn rather than written for the same reason the ping is: the game's font is a display
        /// face, and what it has beyond the Latin alphabet cannot be relied on. Turned a quarter
        /// turn to point down, the way the stick pin turns.
        /// </summary>
        internal static Sprite Triangle()
        {
            if (_triangle != null) return _triangle;
            const int n = 32;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    int hits = 0;
                    for (int sy = 0; sy < 3; sy++)
                        for (int sx = 0; sx < 3; sx++)
                            if (InTriangle(x + (sx + 0.5f) / 3f, y + (sy + 0.5f) / 3f, n)) hits++;
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(255 * hits / 9));
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.hideFlags = HideFlags.HideAndDontSave;
            _triangle = Sprite.Create(tex, new Rect(0f, 0f, n, n), new Vector2(0.5f, 0.5f), 100f);
            _triangle.hideFlags = HideFlags.HideAndDontSave;
            return _triangle;
        }

        /// <summary>Flat side at the left, point at the right, and no wider than it is tall.</summary>
        private static bool InTriangle(float x, float y, float n)
        {
            float lo = n * 0.22f, hi = n * 0.78f;
            if (x < lo || x > hi) return false;
            float t = (x - lo) / (hi - lo);              // 0 at the flat side, 1 at the point
            float half = n * 0.5f * (1f - t);
            return Mathf.Abs(y - n * 0.5f) <= half;
        }

        private static bool InTack(float x, float y)
        {
            float dx = x - 32f;
            if (dx * dx + (y - 45f) * (y - 45f) <= 12f * 12f) return true;    // head
            if (y >= 29f && y <= 35f && dx >= -10f && dx <= 10f) return true; // collar
            if (y >= 7f && y < 29f)                                           // needle, tapering to a point
            {
                float half = 3.2f * (y - 7f) / 22f;
                return dx >= -half && dx <= half;
            }
            return false;
        }

        internal static TextMeshProUGUI Text(Transform parent, string name, string text, float size, Color color)
        {
            EnsureFont();
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            go.SetActive(false); // TMP's Awake looks up a default font the game lacks unless one is set first
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            if (_font != null) t.font = _font;
            if (_fontMaterial != null) t.fontSharedMaterial = _fontMaterial;
            t.text = text ?? "";
            t.fontSize = size;
            t.alignment = TextAlignmentOptions.Left;
            t.color = color;
            t.raycastTarget = false;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            go.SetActive(true);
            return t;
        }

        internal static Image Panel(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = PanelColor;
            img.raycastTarget = true;
            var outline = go.AddComponent<Outline>();
            outline.effectColor = OutlineColor;
            outline.effectDistance = new Vector2(1f, -1f);
            return img;
        }

        /// <summary>Reports pointer drags in screen pixels, for moving a panel or a resize grip.</summary>
        /// <summary>
        /// Remembers whether the pointer is over this thing, so whoever paints it can brighten it.
        /// A flag rather than a color of its own, because these buttons are repainted every frame
        /// from what they mean -- paused, hidden, shown -- and a color set here would be wiped.
        /// </summary>
        internal sealed class Glow : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            internal bool Over;
            void IPointerEnterHandler.OnPointerEnter(PointerEventData e) { Over = true; }
            void IPointerExitHandler.OnPointerExit(PointerEventData e) { Over = false; }
        }

        /// <summary>The color of a map button the pointer is resting on: bright orange, whatever
        /// it was showing before, so the answer to "which one am I about to press" needs no
        /// comparing. It keeps its own color the moment the pointer leaves.</summary>
        internal static readonly Color HoverColor = new Color(1f, 0.6f, 0.13f, 1f);

        /// <summary>How much larger a map button is drawn while the pointer is on it.</summary>
        internal const float HoverMagnify = 1.1f;

        internal static Color Lit(Color c, bool lit) => lit ? HoverColor : c;

        /// <summary>
        /// The colors of a small button that is currently doing something -- armed for placing,
        /// or filtering the map. A button that is on should say so without being read: it is the
        /// difference between a list of things you could do and a list that also tells you what
        /// you already asked for.
        /// </summary>
        internal static readonly Color ButtonIdle = new Color(1f, 1f, 1f, 0.07f);
        internal static readonly Color ButtonOn = new Color(1f, 0.6f, 0.13f, 0.35f);
        internal static readonly Color GlyphIdle = new Color(1f, 0.85f, 0.45f, 0.9f);
        internal static readonly Color GlyphOn = new Color(1f, 0.95f, 0.8f, 1f);

        /// <summary>
        /// A tooltip for one of our own small buttons, drawn by the same panel that names a
        /// marker when you hover it on the map.
        ///
        /// This went the long way round first: hang one of the game's own UITooltip components on
        /// the button and let it do the work. The component needs a prefab it cannot make, which
        /// has to be borrowed from something else in the scene that has one -- and nearly every
        /// tooltip in the game sits on a panel switched off while the map is up, so at the one
        /// moment we asked there was nothing to borrow. Our own panel needs nothing borrowed, and
        /// it is the panel these buttons sit next to anyway, so the two now match by being the
        /// same thing rather than by resembling each other.
        /// </summary>
        internal sealed class Hint : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            internal static Hint Over;

            internal string Title = "";
            internal string Body = "";

            void IPointerEnterHandler.OnPointerEnter(PointerEventData e) { Over = this; }
            void IPointerExitHandler.OnPointerExit(PointerEventData e) { if (ReferenceEquals(Over, this)) Over = null; }
            private void OnDisable() { if (ReferenceEquals(Over, this)) Over = null; }

            /// <summary>
            /// The body, as the one block of text it is.
            ///
            /// <para>It used to be broken into lines here, at thirty-eight characters. A character
            /// count is not a width: the game's font is proportional, so thirty-eight narrow
            /// letters fit the panel and thirty-eight wide ones run off the end of it, where they
            /// were cut with an ellipsis. And a tooltip longer than four of those lines simply
            /// lost the rest, silently. The panel wraps and measures its own text now, so this
            /// hands over the whole thing.</para>
            /// </summary>
            internal List<string> Lines() => new List<string> { Body };
        }

        /// <summary>Give something a tooltip of our own, shown while the pointer rests on it.</summary>
        internal static void Tip(GameObject go, string topic, string text)
        {
            if (go == null || string.IsNullOrEmpty(text)) return;
            var hint = go.GetComponent<Hint>() ?? go.AddComponent<Hint>();
            hint.Title = topic ?? "";
            hint.Body = text;
        }

        /// <summary>Grown by a tenth while hovered, from whatever size the button is normally.</summary>
        internal static Vector3 Magnified(Vector3 normal, bool lit) => lit ? normal * HoverMagnify : normal;

        internal sealed class DragHandle : MonoBehaviour, IDragHandler, IEndDragHandler
        {
            public Action<Vector2> OnDrag;
            public Action OnEnd;
            void IDragHandler.OnDrag(PointerEventData e) { OnDrag?.Invoke(e.delta); }
            void IEndDragHandler.OnEndDrag(PointerEventData e) { OnEnd?.Invoke(); }
        }

        private static Sprite _box, _check;

        private static readonly Dictionary<string, Sprite> _drawn = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// One of this mod's own pictures, by name, for the icon key "drawn:&lt;name&gt;".
        ///
        /// <para>Every other icon in the mod is something the game already has a picture of, which
        /// is almost always the right answer -- a copper vein should look like copper ore, and
        /// nothing hand-drawn competes with Valheim's own art. The exception is a kind that stands
        /// for several things at once: there is no item in the game that means "berries, any of
        /// them", and borrowing one of the three means the kind and one of its own types wear the
        /// same picture.</para>
        /// </summary>
        internal static Sprite Drawn(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (_drawn.TryGetValue(name, out var had)) return had;
            Sprite made = null;
            switch (name.ToLowerInvariant())
            {
                case "berries":   made = Berries(); break;
                case "mushrooms": made = Mushrooms(); break;
                case "herbs":     made = Herbs(); break;
                case "dungeons":  made = Dungeons(); break;
                case "spawners":  made = Spawners(); break;
            }
            _drawn[name] = made;
            return made;
        }

        /// <summary>
        /// Three berries in a cluster on a stem, in their own colors rather than white.
        ///
        /// <para>Drawn with a gap carved along the line where two of them meet, instead of relying
        /// on their colors to tell them apart. The map paints every recorded marker one pale gold,
        /// so whatever this is colored it arrives there as a single-color silhouette: without the
        /// gaps the cluster would land on the map as one gold blob. In the marker list, which
        /// draws an icon as it is, the colors show.</para>
        /// </summary>
        private static Sprite Berries()
        {
            const int n = 64;
            const float r = n * 0.215f;      // each berry
            const float gap = 2.2f;          // the seam carved where two of them meet
            var at = new[]
            {
                new Vector2(n * 0.33f, n * 0.56f),
                new Vector2(n * 0.67f, n * 0.56f),
                new Vector2(n * 0.50f, n * 0.25f),
            };
            var skin = new Color(0.58f, 0.34f, 0.72f);
            var leaf = new Color(0.36f, 0.56f, 0.26f);
            var stemFrom = new Vector2(n * 0.50f, n * 0.70f);
            var stemTo = new Vector2(n * 0.50f, n * 0.88f);
            var leafAt = new Vector2(n * 0.63f, n * 0.84f);

            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int berry = 0, green = 0;
                    Vector2 nearest = Vector2.zero;
                    for (int sy = 0; sy < 3; sy++)
                        for (int sx = 0; sx < 3; sx++)
                        {
                            var p = new Vector2(x + (sx + 0.5f) / 3f, y + (sy + 0.5f) / 3f);
                            // The two closest berries: inside one of them, and far enough from the
                            // next for the seam between them to show.
                            float first = float.MaxValue, second = float.MaxValue;
                            Vector2 mine = Vector2.zero;
                            foreach (var c in at)
                            {
                                float d = Vector2.Distance(p, c);
                                if (d < first) { second = first; first = d; mine = c; }
                                else if (d < second) second = d;
                            }
                            if (first <= r && second - first >= gap) { berry++; nearest = mine; continue; }
                            if (ToSegment(p, stemFrom, stemTo) <= n * 0.028f
                                || Vector2.Distance(p, leafAt) <= n * 0.075f) green++;
                        }
                    if (berry > 0)
                    {
                        // A little lighter towards the top left of each berry, which is all it
                        // takes to stop three flat discs reading as three holes.
                        var off = (new Vector2(x + 0.5f, y + 0.5f) - nearest) / r;
                        float lift = Mathf.Clamp01(0.5f - (off.x - off.y) * 0.45f);
                        var c = Color.Lerp(skin * 0.78f, Color.Lerp(skin, Color.white, 0.35f), lift);
                        px[y * n + x] = new Color(c.r, c.g, c.b, berry / 9f);
                    }
                    else if (green > 0)
                    {
                        px[y * n + x] = new Color(leaf.r, leaf.g, leaf.b, green / 9f);
                    }
                }
            return Bake(tex, px, n);
        }


        /// <summary>
        /// An empty square: the box half of a checkbox, drawn once at runtime like the rest of the
        /// marks here. A ring rather than a filled square, so the row behind it shows through.
        /// </summary>
        internal static Sprite Box()
        {
            if (_box != null) return _box;
            const int n = 32;
            const int edge = 3;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    bool on = x < edge || y < edge || x >= n - edge || y >= n - edge;
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(on ? 255 : 0));
                }
            _box = Bake(tex, px, n);
            return _box;
        }

        /// <summary>
        /// A tick, for inside the box. Written as two strokes rather than as a letter because the
        /// game's font is a display face and what it has beyond the Latin alphabet cannot be
        /// relied on -- which is the same reason the buttons in the marker list say words.
        /// </summary>
        internal static Sprite Check()
        {
            if (_check != null) return _check;
            const int n = 32;
            float t = n * 0.115f;                      // half the stroke's width
            var a = new Vector2(n * 0.16f, n * 0.52f); // the short stroke's start, down the left
            var b = new Vector2(n * 0.40f, n * 0.26f); // the corner at the bottom
            var c = new Vector2(n * 0.86f, n * 0.78f); // the long stroke's end, up the right
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int hits = 0;
                    for (int sy = 0; sy < 3; sy++)
                        for (int sx = 0; sx < 3; sx++)
                        {
                            var p = new Vector2(x + (sx + 0.5f) / 3f, y + (sy + 0.5f) / 3f);
                            if (ToSegment(p, a, b) <= t || ToSegment(p, b, c) <= t) hits++;
                        }
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(255 * hits / 9));
                }
            _check = Bake(tex, px, n);
            return _check;
        }

        /// <summary>
        /// Two mushrooms, a big one and a small one: a cap and a stalk each, far enough apart to
        /// stay two things when the map paints the whole picture one color.
        ///
        /// <para>Two rather than one because this stands for the kind, and the kind holds six
        /// mushrooms -- one of anything is a picture of that one thing, which is the mistake this
        /// whole set of icons exists to undo.</para>
        /// </summary>
        private static Sprite Mushrooms()
        {
            const int n = 64;
            var cap = new Color(0.74f, 0.31f, 0.27f);
            var stalk = new Color(0.90f, 0.86f, 0.74f);

            // centre of the cap, its half width and height, the stalk's half width and its foot
            var big = new Vector4(n * 0.34f, n * 0.52f, n * 0.21f, n * 0.17f);
            var small = new Vector4(n * 0.73f, n * 0.40f, n * 0.145f, n * 0.115f);
            float bigStalk = n * 0.055f, smallStalk = n * 0.040f, foot = n * 0.16f;

            bool InCap(Vector2 p, Vector4 m)
            {
                if (p.y < m.y) return false;                        // the dome only
                float dx = (p.x - m.x) / m.z, dy = (p.y - m.y) / m.w;
                return dx * dx + dy * dy <= 1f;
            }
            bool InStalk(Vector2 p, Vector4 m, float half)
                => p.y >= foot && p.y <= m.y && Mathf.Abs(p.x - m.x) <= half;

            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int caps = 0, stalks = 0;
                    for (int sy = 0; sy < 3; sy++)
                        for (int sx = 0; sx < 3; sx++)
                        {
                            var p = new Vector2(x + (sx + 0.5f) / 3f, y + (sy + 0.5f) / 3f);
                            if (InCap(p, big) || InCap(p, small)) caps++;
                            else if (InStalk(p, big, bigStalk) || InStalk(p, small, smallStalk)) stalks++;
                        }
                    if (caps > 0) px[y * n + x] = new Color(cap.r, cap.g, cap.b, caps / 9f);
                    else if (stalks > 0) px[y * n + x] = new Color(stalk.r, stalk.g, stalk.b, stalks / 9f);
                }
            return Bake(tex, px, n);
        }

        /// <summary>
        /// A sprig: a stem with four leaves off it, which is what "herbs" looks like in any
        /// kitchen and is neither the thistle nor the dandelion the kind holds.
        /// </summary>
        private static Sprite Herbs()
        {
            const int n = 64;
            var green = new Color(0.40f, 0.62f, 0.29f);
            var stemColor = new Color(0.30f, 0.47f, 0.22f);
            var stemFrom = new Vector2(n * 0.50f, n * 0.10f);
            var stemTo = new Vector2(n * 0.50f, n * 0.84f);
            float stemHalf = n * 0.030f;

            // centre, the way it grows, half its length, half its width
            var leaves = new[]
            {
                new[] { n * 0.36f, n * 0.34f, -0.58f, 0.81f, n * 0.185f, n * 0.078f },
                new[] { n * 0.64f, n * 0.47f,  0.58f, 0.81f, n * 0.185f, n * 0.078f },
                new[] { n * 0.37f, n * 0.62f, -0.52f, 0.85f, n * 0.160f, n * 0.068f },
                new[] { n * 0.50f, n * 0.88f,  0.00f, 1.00f, n * 0.120f, n * 0.060f },
            };

            bool InLeaf(Vector2 p, float[] l)
            {
                var d = new Vector2(l[2], l[3]).normalized;
                var off = p - new Vector2(l[0], l[1]);
                float along = Vector2.Dot(off, d) / l[4];
                float across = (off.x * d.y - off.y * d.x) / l[5];
                return along * along + across * across <= 1f;
            }

            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int leaf = 0, stem = 0;
                    for (int sy = 0; sy < 3; sy++)
                        for (int sx = 0; sx < 3; sx++)
                        {
                            var p = new Vector2(x + (sx + 0.5f) / 3f, y + (sy + 0.5f) / 3f);
                            bool onLeaf = false;
                            foreach (var l in leaves) if (InLeaf(p, l)) { onLeaf = true; break; }
                            if (onLeaf) leaf++;
                            else if (ToSegment(p, stemFrom, stemTo) <= stemHalf) stem++;
                        }
                    if (leaf > 0) px[y * n + x] = new Color(green.r, green.g, green.b, leaf / 9f);
                    else if (stem > 0) px[y * n + x] = new Color(stemColor.r, stemColor.g, stemColor.b, stem / 9f);
                }
            return Bake(tex, px, n);
        }

        /// <summary>
        /// A cave mouth: a dome standing on the ground with a way in cut out of it.
        ///
        /// <para>The kinds under Dungeons are a burial chamber, a sunken crypt, a frost cave, a
        /// troll cave, an infested mine and a bear's den, and each of those wears the trophy of
        /// whatever lives in it. There is no picture of "a dungeon" to borrow -- the keys the game
        /// has are each for one dungeon, and a torch is a light source -- and the thing all six
        /// have in common is a hole in the ground you walk into.</para>
        ///
        /// <para>The way in is cut out rather than colored dark, because the map paints every
        /// recorded marker one color: a hole has to be a hole or it is not there at all.</para>
        /// </summary>
        private static Sprite Dungeons()
        {
            const int n = 64;
            var rock = new Color(0.52f, 0.49f, 0.46f);
            var ground = n * 0.13f;
            var hill = new Vector2(n * 0.5f, n * 0.26f);     // the dome's centre, on the ground
            float hillR = n * 0.35f;
            var mouth = new Vector2(n * 0.5f, n * 0.25f);    // the way in, the same shape smaller
            float mouthR = n * 0.145f;

            bool Inside(Vector2 p, Vector2 c, float r)
                => p.y >= ground && (p.y <= c.y ? Mathf.Abs(p.x - c.x) <= r : Vector2.Distance(p, c) <= r);

            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int hits = 0;
                    for (int sy = 0; sy < 3; sy++)
                        for (int sx = 0; sx < 3; sx++)
                        {
                            var p = new Vector2(x + (sx + 0.5f) / 3f, y + (sy + 0.5f) / 3f);
                            if (Inside(p, hill, hillR) && !Inside(p, mouth, mouthR)) hits++;
                        }
                    if (hits > 0) px[y * n + x] = new Color(rock.r, rock.g, rock.b, hits / 9f);
                }
            return Bake(tex, px, n);
        }

        /// <summary>
        /// A skull: what comes out of a nest, a draugr pile, a bone pile or a charred stone.
        ///
        /// <para>Each of those wears the trophy of its own creature, which is the thing worth
        /// knowing before breaking one open. The kind itself needs the one picture they have in
        /// common, and what a spawner is, is a place that keeps sending something at you. The
        /// skeleton's own trophy was standing in for that, which made the kind and one of its
        /// four members the same picture.</para>
        /// </summary>
        private static Sprite Spawners()
        {
            const int n = 64;
            var bone = new Color(0.88f, 0.86f, 0.78f);
            var head = new Vector2(n * 0.5f, n * 0.56f);
            float headR = n * 0.235f;
            var jawLeft = n * 0.375f;
            var jawRight = n * 0.625f;
            var jawTop = n * 0.40f;
            var jawFoot = n * 0.23f;
            var eyeL = new Vector2(n * 0.415f, n * 0.59f);
            var eyeR = new Vector2(n * 0.585f, n * 0.59f);
            float eyeR2 = n * 0.078f;
            var nose = new Vector2(n * 0.5f, n * 0.465f);
            float noseR = n * 0.042f;

            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int hits = 0;
                    for (int sy = 0; sy < 3; sy++)
                        for (int sx = 0; sx < 3; sx++)
                        {
                            var p = new Vector2(x + (sx + 0.5f) / 3f, y + (sy + 0.5f) / 3f);
                            bool solid = Vector2.Distance(p, head) <= headR
                                || (p.x >= jawLeft && p.x <= jawRight && p.y >= jawFoot && p.y <= jawTop);
                            if (!solid) continue;
                            // The sockets are cut out, not shaded: on the map this arrives as one
                            // flat color, and a skull without holes in it is a stone.
                            if (Vector2.Distance(p, eyeL) <= eyeR2) continue;
                            if (Vector2.Distance(p, eyeR) <= eyeR2) continue;
                            if (Vector2.Distance(p, nose) <= noseR) continue;
                            hits++;
                        }
                    if (hits > 0) px[y * n + x] = new Color(bone.r, bone.g, bone.b, hits / 9f);
                }
            return Bake(tex, px, n);
        }

        /// <summary>How far a point lies from a line segment, for drawing a stroke of some width.</summary>
        private static float ToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float len = ab.sqrMagnitude;
            float t = len <= 0.0001f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / len);
            return Vector2.Distance(p, a + ab * t);
        }

        /// <summary>The last few lines every one of these drawn marks ends with.</summary>
        private static Sprite Bake(Texture2D tex, Color32[] px, int n)
        {
            tex.SetPixels32(px);
            tex.Apply(false, true);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.hideFlags = HideFlags.HideAndDontSave;
            var sprite = Sprite.Create(tex, new Rect(0f, 0f, n, n), new Vector2(0.5f, 0.5f), 100f);
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        private static Sprite _gear;

        /// <summary>
        /// The usual gear: a toothed ring with a hole through it, drawn once at runtime like the
        /// stick pin and the resize grip rather than shipped as a picture. White, so whoever puts
        /// it on a button can tint it.
        /// </summary>
        internal static Sprite Gear()
        {
            if (_gear != null) return _gear;
            const int n = 64;
            const int teeth = 8;
            const float cx = n * 0.5f, cy = n * 0.5f;
            const float rim = n * 0.30f, root = n * 0.235f, hole = n * 0.115f;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int hits = 0;
                    for (int sy = 0; sy < 3; sy++)
                        for (int sx = 0; sx < 3; sx++)
                        {
                            float fx = x + (sx + 0.5f) / 3f - cx;
                            float fy = y + (sy + 0.5f) / 3f - cy;
                            float d = Mathf.Sqrt(fx * fx + fy * fy);
                            if (d <= hole) continue;                 // the bore through the middle
                            // A tooth every so many degrees: the rim steps out where the cosine
                            // is high, which gives flat-topped teeth rather than a wavy edge.
                            float edge = Mathf.Cos(teeth * Mathf.Atan2(fy, fx)) > 0.25f ? rim : root;
                            if (d <= edge) hits++;
                        }
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(255 * hits / 9));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.hideFlags = HideFlags.HideAndDontSave;
            _gear = Sprite.Create(tex, new Rect(0f, 0f, n, n), new Vector2(0.5f, 0.5f), 100f);
            _gear.hideFlags = HideFlags.HideAndDontSave;
            return _gear;
        }

        private static Sprite _grip;

        /// <summary>Three diagonal lines in the corner: the usual resize grip, drawn rather than shipped.</summary>
        internal static Sprite Grip()
        {
            if (_grip != null) return _grip;
            const int n = 24;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int k = x + (n - 1 - y);
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

        internal sealed class Hover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            public Image Background;
            public bool Enabled = true;
            public void OnPointerEnter(PointerEventData e) { if (Enabled && Background != null) Background.color = RowHover; }
            public void OnPointerExit(PointerEventData e) { if (Background != null) Background.color = RowColor; }
        }

        /// <summary>A clickable row; a disabled one is dimmed and inert.</summary>
        internal static RectTransform Row(Transform parent, string label, bool enabled, Action onClick)
        {
            var go = new GameObject("Row", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(Hover));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = RowColor;
            img.raycastTarget = true;
            var hover = go.GetComponent<Hover>();
            hover.Background = img;
            hover.Enabled = enabled;
            var btn = go.GetComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.targetGraphic = img;
            btn.interactable = enabled;
            if (enabled && onClick != null) btn.onClick.AddListener(() => onClick());
            var text = Text(go.transform, "Label", label, 15f, enabled ? Body : Dim);
            var trt = text.rectTransform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(10f, 0f);
            trt.offsetMax = new Vector2(-10f, 0f);
            return (RectTransform)go.transform;
        }
    }

    /// <summary>
    /// Right-click on one of this mod's markers on the map screen: a small menu instead of the
    /// vanilla erase. Hide this one, hide everything with the same icon, hide the whole kind,
    /// cross off, erase (only where the server allows it), and show everything again.
    /// </summary>
    internal static class MarkerMenu
    {
        private const float Width = 250f;
        private const float RowHeight = 26f;
        private const float HeaderHeight = 28f;
        private const float Pad = 4f;

        private static GameObject _root;
        private static RectTransform _rect;
        private static Camera _camera;
        private static int _openedFrame = -10;

        internal static bool IsOpen => _root != null;

        /// <summary>Is the pointer over the menu itself, rather than merely is it open?</summary>
        internal static bool Hovered =>
            _root != null && _rect != null
            && RectTransformUtility.RectangleContainsScreenPoint(_rect, ZInput.pointerPosition, _camera);

        internal static void Open(SharedPin pin, Vector2 screenPos)
        {
            Close();
            var map = Minimap.instance;
            if (map == null || pin == null || map.m_largeRoot == null) return;
            var parent = map.m_largeRoot.transform as RectTransform;
            if (parent == null) return;

            var panel = MenuKit.Panel(parent, "TGM_MarkerMenu");
            _root = panel.gameObject;
            _rect = panel.rectTransform;
            _rect.anchorMin = _rect.anchorMax = new Vector2(0f, 1f);
            _rect.pivot = new Vector2(0f, 1f);

            var items = BuildItems(pin);
            float height = Pad + HeaderHeight + items.Count * RowHeight + Pad;
            _rect.sizeDelta = new Vector2(Width, height);

            string title = !string.IsNullOrEmpty(pin.Name) ? pin.Name : IconRegistry.DisplayName(pin.Icon);
            var header = MenuKit.Text(_root.transform, "Header", title, 15f, MenuKit.Header);
            var hrt = header.rectTransform;
            hrt.anchorMin = new Vector2(0f, 1f);
            hrt.anchorMax = new Vector2(1f, 1f);
            hrt.pivot = new Vector2(0f, 1f);
            hrt.anchoredPosition = new Vector2(10f, -Pad);
            hrt.sizeDelta = new Vector2(-20f, HeaderHeight);
            header.alignment = TextAlignmentOptions.MidlineLeft;

            float y = -(Pad + HeaderHeight);
            foreach (var item in items)
            {
                var action = item.Value;
                var row = MenuKit.Row(_root.transform, item.Key, action != null, () => { Close(); action(); });
                row.anchorMin = new Vector2(0f, 1f);
                row.anchorMax = new Vector2(1f, 1f);
                row.pivot = new Vector2(0f, 1f);
                row.anchoredPosition = new Vector2(0f, y);
                row.sizeDelta = new Vector2(0f, RowHeight);
                y -= RowHeight;
            }

            // Top-left corner at the pointer, kept inside the map panel.
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
        }

        private static List<KeyValuePair<string, Action>> BuildItems(SharedPin pin)
        {
            var items = new List<KeyValuePair<string, Action>>();
            var id = pin.Key;
            string icon = pin.Icon;
            string iconName = IconRegistry.DisplayName(icon);
            var kind = ClientPins.KindOf(pin);
            bool canEdit = !TgmConfig.RequireMapOutToEdit.Value || PocketMap.IsOut;

            // Naming it first, because it is the one thing in here that adds something rather
            // than taking something away, and because a marker worth right-clicking is often one
            // you want to say something about.
            items.Add(Item(string.IsNullOrEmpty(pin.Name) ? "Add a name" : "Change the name",
                canEdit ? (Action)(() => LabelEditor.Open(pin, ZInput.pointerPosition)) : null,
                "take the map out first"));

            items.Add(Item("Hide this marker", () =>
            {
                ViewPrefs.Hide(id);
                ClientPins.Restyle();
                Note("Marker hidden. 'Show all hidden' in any marker's menu brings it back.");
            }));
            // A marker drawn with one of vanilla's own filterable pin icons is already hidden and
            // shown by that icon's button on the map, which also covers pins the player placed by
            // hand with it. Offering our own switch beside it only splits one job in two.
            // The per-icon switch is skipped for markers drawn on one of vanilla's own icons,
            // because every structure, portal, camp or boss altar shares that one picture, so it
            // would say exactly what the kind switch below says.
            if (!IconRegistry.VanillaFilters(icon))
            {
                items.Add(Item($"Hide all {iconName} markers", () =>
                {
                    TgmConfig.AddHiddenIcon(icon);
                    ClientPins.Restyle();
                    Note($"Hiding all {iconName} markers (Display > Hidden Icons).");
                }));
            }
            // The kind switch is always offered, vanilla-icon kinds included. Vanilla's own icon
            // button covers those too, so it is redundant, but a player looking for the switch
            // expects it here with the rest and should not have to know which mod owns the
            // picture. They are not the same thing either: vanilla's button hides every pin with
            // that icon, this one only the markers this mod recorded.
            if (kind.HasValue)
            {
                var k = kind.Value;
                string kindLabel = Categories.Label(k).ToLowerInvariant();
                items.Add(Item($"Hide all {kindLabel}", () =>
                {
                    if (TgmConfig.ShowKind.TryGetValue(k, out var entry)) entry.Value = false;
                    ClientPins.Restyle();
                    Note($"Hiding all {kindLabel} (Display > Show {Categories.Label(k)}).");
                }));
            }

            bool isChecked = pin.Checked;
            items.Add(Item(isChecked ? "Uncross" : "Cross off", canEdit ? () => ClientPins.SetChecked(id, !isChecked) : (Action)null, "take out your map"));
            // A crossed-off marker is where a player is most likely to want the rest of them gone,
            // so the switch that covers it is offered right here.
            if (isChecked && CrossedOff.IsCleared(pin))
            {
                items.Add(Item("Hide all cleared deposits", () =>
                {
                    TgmConfig.ShowClearedDeposits.Value = false;
                    ClientPins.Restyle();
                    Note("Hiding cleared deposits (Display > Show Cleared Deposits).");
                }));
            }
            else if (isChecked)
            {
                string group = CrossedOff.GroupOf(pin);
                string what = group == CrossedOff.Placed ? "crossed-off placed markers" : "searched " + group.ToLowerInvariant();
                items.Add(Item($"Hide {what}", () =>
                {
                    CrossedOff.SetGroupHidden(group, true);
                    ClientPins.Restyle();
                    Note($"Hiding {what} (Display > Hidden Searched Places).");
                }));
                var owner = CrossedOff.OwnerOf(group);
                if (owner.HasValue)
                {
                    string all = Categories.Label(owner.Value).ToLowerInvariant();
                    items.Add(Item($"Hide all searched {all}", () =>
                    {
                        CrossedOff.SetTypesHidden(owner.Value, true);
                        ClientPins.Restyle();
                        Note($"Hiding searched {all} (Display > Hidden Searched Places).");
                    }));
                }
            }

            // A note you wrote is yours to take back; a recorded marker stands for something real,
            // so removing it is a repair rather than an everyday action.
            bool mayDelete = ClientPins.CanDelete(pin, out string whyNot);
            string label = pin.Auto ? "Erase for everyone" : "Delete this marker";
            if (!mayDelete) items.Add(Item($"{label} ({whyNot})", null));
            else items.Add(Item(label, canEdit
                ? (Action)(() => { if (ClientPins.EraseById(id)) Note(pin.Auto ? "Marker erased." : "Marker deleted."); })
                : null, "take out your map"));

            if (TgmConfig.AnythingHidden())
                items.Add(Item("Show all hidden", () =>
                {
                    ViewPrefs.Clear();
                    TgmConfig.ShowEverything();
                    ClientPins.Restyle();
                    Note("Showing every marker again.");
                }));
            return items;
        }

        private static KeyValuePair<string, Action> Item(string label, Action action, string whyDisabled = null)
        {
            if (action == null && !string.IsNullOrEmpty(whyDisabled)) label += " (" + whyDisabled + ")";
            return new KeyValuePair<string, Action>(label, action);
        }

        internal static void Close()
        {
            if (_root != null) UnityEngine.Object.Destroy(_root);
            _root = null;
            _rect = null;
        }

        /// <summary>Every frame: the menu goes away with the map screen, on Escape, or on a click anywhere else.</summary>
        internal static void Update()
        {
            if (_root == null) return;
            var map = Minimap.instance;
            if (map == null || map.m_mode != Minimap.MapMode.Large || ZInput.GetKeyDown(KeyCode.Escape)) { Close(); return; }
            if (Time.frameCount - _openedFrame <= 1) return; // the click that opened it
            if (ZInput.GetMouseButtonDown(0) || ZInput.GetMouseButtonDown(1))
            {
                if (!RectTransformUtility.RectangleContainsScreenPoint(_rect, ZInput.pointerPosition, _camera)) Close();
            }
        }

        private static void Note(string text) => TheGreatestMapMod.Message(text);
    }
}
