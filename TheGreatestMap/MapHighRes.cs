using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace TheGreatestMap
{
    /// <summary>
    /// The large map's painting, drawn at the resolution of what you are looking at rather than
    /// the resolution of the world.
    ///
    /// <para>The map texture is 2048 pixels across about twenty-four kilometers, which is twelve
    /// meters to a pixel: a four meter path cannot be drawn narrower than three times its width,
    /// and a wall is a smudge. The obvious fix -- a bigger texture -- does not survive the
    /// arithmetic. Two meters to a pixel over the whole world is 12288 squared, six hundred
    /// megabytes of video memory, and one meter is two and a half gigabytes. There is no
    /// resolution worth having at world scale.</para>
    ///
    /// <para>So this does not draw the world. It draws the window, into a buffer of its own that
    /// covers only what is on screen. Zoomed in on the large map that window is a kilometer or
    /// two, which at two thousand pixels is about a meter each -- sharp enough for a wall to be
    /// a line -- and it costs sixteen megabytes, a quarter of what the merely-twice-as-good
    /// world texture would have. The resolution comes from covering less ground, not from a
    /// bigger buffer.</para>
    ///
    /// <para>The price is that it has to be redrawn when the view moves, and the view moves on
    /// the map-dragging path, which is a path with history: canvas work in a pin postfix once
    /// stalled clients into dropped connections. So this is throttled, budgeted, and asleep
    /// entirely unless the large map is open -- during ordinary play nothing here runs at
    /// all.</para>
    ///
    /// <para>It is an overlay and not a replacement for the map texture, which is what keeps it
    /// out of a fight. Valheim's map shader samples four textures of its own and reads its UVs
    /// from a rect the game rewrites every frame; substituting one of those textures means
    /// guessing at a shader nobody here can read. Layering above it needs no such guess -- and
    /// the one thing the shader was doing for us, hiding what you have not explored, is cheaper
    /// to do here anyway by not drawing those cells in the first place.</para>
    /// </summary>
    internal static class MapHighRes
    {
        /// <summary>
        /// The window buffer, square, in pixels. Sixteen megabytes at ARGB32. Bigger buys
        /// resolution only until it passes what the screen can show, which this already has.
        /// </summary>
        private const int Size = 2048;

        /// <summary>
        /// How far the view must slide before it is worth redrawing, as a fraction of the window.
        /// Nothing moves less than a pixel's worth and gets a repaint for it.
        /// </summary>
        private const float Moved = 0.002f;

        /// <summary>The most often a redraw may happen, however hard the map is being dragged.</summary>
        private const float EverySeconds = 0.08f;

        private static RenderTexture _rt;
        private static RawImage _layer;
        private static Material _flat;
        private static Rect _drawnFor = new Rect(-999f, -999f, 0f, 0f);
        private static int _drawnSignature;
        private static float _nextDraw;
        private static bool _failed;

        private static readonly FieldInfoCache Explored = new FieldInfoCache("m_explored");
        private static readonly FieldInfoCache ExploredOthers = new FieldInfoCache("m_exploredOthers");

        /// <summary>A private Minimap field, looked up once and kept.</summary>
        private sealed class FieldInfoCache
        {
            private readonly string _name;
            private System.Reflection.FieldInfo _field;
            private bool _looked;

            internal FieldInfoCache(string name) { _name = name; }

            internal BitArray Of(Minimap map)
            {
                if (!_looked) { _looked = true; _field = AccessTools.Field(typeof(Minimap), _name); }
                return _field != null ? _field.GetValue(map) as BitArray : null;
            }
        }

        /// <summary>Thrown away when the map is rebuilt, which happens on every login.</summary>
        internal static void Forget()
        {
            _layer = null;
            _drawnFor = new Rect(-999f, -999f, 0f, 0f);
            _drawnSignature = 0;
        }

        /// <summary>
        /// Called every frame. Returns at once unless the large map is open and there is
        /// something to draw on it, which is the usual case.
        /// </summary>
        internal static void Update(Minimap map, Dictionary<int, BuiltKind> cells, int signature)
        {
            if (_failed) return;
            bool wanted = map != null && map.m_mode == Minimap.MapMode.Large
                          && cells != null && cells.Count > 0;
            if (!wanted)
            {
                if (_layer != null && _layer.enabled) _layer.enabled = false;
                return;
            }

            try
            {
                if (!Ready(map)) return;
                _layer.enabled = true;

                var view = map.m_mapImageLarge.uvRect;
                bool slid = Mathf.Abs(view.x - _drawnFor.x) > Moved * Mathf.Max(view.width, 0.0001f)
                            || Mathf.Abs(view.y - _drawnFor.y) > Moved * Mathf.Max(view.height, 0.0001f)
                            || !Mathf.Approximately(view.width, _drawnFor.width)
                            || !Mathf.Approximately(view.height, _drawnFor.height);
                if (!slid && signature == _drawnSignature) return;
                if (Time.unscaledTime < _nextDraw) return;

                _nextDraw = Time.unscaledTime + EverySeconds;
                _drawnFor = view;
                _drawnSignature = signature;
                Draw(map, cells, view);
            }
            catch (System.Exception e)
            {
                _failed = true;
                TheGreatestMapMod.Log.LogWarning(
                    "[TheGreatestMap] The sharp map layer could not be drawn (" + e.Message +
                    "), so the large map keeps the ordinary painting. Nothing else is affected.");
                if (_layer != null) _layer.enabled = false;
            }
        }

        /// <summary>Build the buffer and the image it is shown through, once.</summary>
        private static bool Ready(Minimap map)
        {
            if (map.m_mapImageLarge == null) return false;

            if (_rt == null || !_rt.IsCreated())
            {
                if (_rt != null) _rt.Release();
                _rt = new RenderTexture(Size, Size, 0, RenderTextureFormat.ARGB32)
                {
                    name = "TGM sharp map",
                    // Point, because the whole purpose is an edge that stays an edge. The
                    // vanilla map underneath is a separate image and keeps its own smoothing.
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    autoGenerateMips = false,
                };
                _rt.Create();
                _drawnFor = new Rect(-999f, -999f, 0f, 0f);
            }

            if (_flat == null)
            {
                var shader = Shader.Find("Hidden/Internal-Colored");
                if (shader == null)
                    // Shader.Find misses anything that came out of an asset bundle, and has been
                    // known to miss built-ins too depending on what got stripped.
                    foreach (var s in Resources.FindObjectsOfTypeAll<Shader>())
                        if (s != null && s.name == "Hidden/Internal-Colored") { shader = s; break; }
                if (shader == null) throw new System.Exception("no Hidden/Internal-Colored shader");
                _flat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                _flat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                _flat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                _flat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                _flat.SetInt("_ZWrite", 0);
            }

            // The Hud is rebuilt on every login, so a cached child can be a dead object.
            if (_layer == null || _layer.gameObject == null)
            {
                var host = new GameObject("TGM_SharpMap", typeof(RectTransform));
                var rect = host.GetComponent<RectTransform>();
                rect.SetParent(map.m_mapImageLarge.transform, false);
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                _layer = host.AddComponent<RawImage>();
                _layer.texture = _rt;
                // Never in the way of a click, a drag or a pin placement.
                _layer.raycastTarget = false;
                // Behind everything else under the map image, because Unity draws a canvas in
                // hierarchy order and a child added last is a child drawn last. Markers are what
                // the map is for and they have to stay on top of the painting -- and this is the
                // answer whether they are siblings of the map image or children of it, which is
                // worth not having to know.
                rect.SetAsFirstSibling();
            }
            _layer.texture = _rt;
            return true;
        }

        /// <summary>
        /// Paint the window. Ground first and buildings over it, in the order they should cover
        /// one another, which is what a painter does instead of ranking.
        /// </summary>
        private static void Draw(Minimap map, Dictionary<int, BuiltKind> cells, Rect view)
        {
            int size = map.m_textureSize;
            float metersPerMapPixel = map.m_pixelSize;
            if (size <= 0 || metersPerMapPixel <= 0f || view.width <= 0f || view.height <= 0f) return;

            var explored = Explored.Of(map);
            var shared = ExploredOthers.Of(map);

            // One cell, in buffer pixels. The window is wider than it is tall and the buffer is
            // square, so the two axes do not agree and are worked out apart.
            float cell = MapPaint.CellSize;
            float worldAcross = view.width * size * metersPerMapPixel;
            float worldUp = view.height * size * metersPerMapPixel;
            float wide = Mathf.Max(1f, cell / worldAcross * Size);
            float tall = Mathf.Max(1f, cell / worldUp * Size);

            var previous = RenderTexture.active;
            RenderTexture.active = _rt;
            GL.PushMatrix();
            GL.LoadPixelMatrix(0f, Size, 0f, Size);
            GL.Clear(false, true, new Color(0f, 0f, 0f, 0f));
            _flat.SetPass(0);
            GL.Begin(GL.QUADS);

            foreach (var kind in Order)
            {
                if (!MapPaint.Wants(kind)) continue;
                var color = MapPaint.ColorFor(kind);
                GL.Color(color);
                foreach (var kv in cells)
                {
                    if (kv.Value != kind) continue;
                    var at = MapPaint.Center(kv.Key);

                    // World to the map's own normalized space -- Minimap's WorldToMapPoint, which
                    // is four lines and private, so it is four lines here instead of a lookup.
                    float mx = (at.x / metersPerMapPixel + size * 0.5f) / size;
                    float my = (at.z / metersPerMapPixel + size * 0.5f) / size;

                    // Unexplored ground hides what is on it, the same as the game's own fog. Done
                    // by not drawing rather than by masking afterwards: it is one bit to read and
                    // it saves the quad.
                    if (explored != null)
                    {
                        int px = Mathf.Clamp((int)(mx * size), 0, size - 1);
                        int py = Mathf.Clamp((int)(my * size), 0, size - 1);
                        int bit = py * size + px;
                        bool seen = bit < explored.Length && explored[bit];
                        if (!seen && shared != null && bit < shared.Length) seen = shared[bit];
                        if (!seen) continue;
                    }

                    float u = (mx - view.xMin) / view.width;
                    float v = (my - view.yMin) / view.height;
                    if (u < -0.05f || u > 1.05f || v < -0.05f || v > 1.05f) continue;   // off screen

                    float x = u * Size, y = v * Size;
                    float x0 = x - wide * 0.5f, x1 = x + wide * 0.5f;
                    float y0 = y - tall * 0.5f, y1 = y + tall * 0.5f;
                    GL.Vertex3(x0, y0, 0f);
                    GL.Vertex3(x1, y0, 0f);
                    GL.Vertex3(x1, y1, 0f);
                    GL.Vertex3(x0, y1, 0f);
                }
            }

            GL.End();
            GL.PopMatrix();
            RenderTexture.active = previous;
        }

        /// <summary>
        /// Back to front. Ground is laid down first and building covers it, which says the same
        /// thing the rank did -- a hall standing in its courtyard is a hall -- without anything
        /// having to compare two kinds and decide.
        /// </summary>
        private static readonly BuiltKind[] Order =
        {
            BuiltKind.Farm, BuiltKind.Dirt, BuiltKind.Paved,
            BuiltKind.Wood, BuiltKind.Iron, BuiltKind.Stone, BuiltKind.Marble,
        };

        internal static List<string> Describe()
        {
            var lines = new List<string>();
            lines.Add(_failed
                ? "sharp large map: OFF, it threw once and was switched off for the session"
                : _rt == null
                    ? "sharp large map: not built yet (it is built when the large map first opens)"
                    : $"sharp large map: {Size} px over the window, " +
                      $"{(_layer != null && _layer.enabled ? "showing" : "hidden")}");
            return lines;
        }
    }
}
