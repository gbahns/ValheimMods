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

        /// <summary>Give the buffer back; it is rebuilt on demand if it is wanted again.</summary>
        private static void Release()
        {
            if (_rt == null) return;
            _rt.Release();
            Object.Destroy(_rt);
            _rt = null;
            _drawnFor = new Rect(-999f, -999f, 0f, 0f);
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

            // Two different questions, and they free different things. Switched off -- by the
            // server, by this player, or because there is nothing to draw -- gives the buffer
            // back, because sixteen megabytes of video memory for a feature somebody is not
            // using is sixteen megabytes too many, and the default is not using it. Merely
            // closing the map only hides the layer: the map is opened and closed constantly and
            // releasing a render texture each time would be thrashing, not thrift.
            bool on = map != null && MapPaint.Wanted() && cells != null && cells.Count > 0;
            if (!on)
            {
                if (_layer != null) _layer.enabled = false;
                Release();
                return;
            }
            if (map.m_mode != Minimap.MapMode.Large)
            {
                if (_layer != null) _layer.enabled = false;
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

            // World meters to buffer pixels, worked out once rather than per cell: Minimap's own
            // WorldToMapPoint folded into the window transform, so a point costs a multiply and
            // an add. The fill still goes the long way round because it needs the map-space
            // coordinate for the fog bit anyway; the outlines, which there are far more of, do
            // not.
            float perMeterX = Size / worldAcross;
            float perMeterY = Size / worldUp;
            float offsetX = (0.5f - view.xMin) / view.width * Size;
            float offsetY = (0.5f - view.yMin) / view.height * Size;

            // How thick a wall is drawn, and whether it is drawn at all. Zoomed out far enough
            // that a cell is a few pixels, an outline around everything is a dark smear rather
            // than a building, and the fill says more on its own. So the outlines fade out by
            // not being drawn, at the zoom where they stop being lines.
            float nib = Mathf.Clamp(wide * 0.14f, 1.5f, 5f);
            bool outlines = wide >= 3f && MapPaint.Outlined();
            if (outlines) MapOutline.Trace(cells, MapPaint.CellsHash);

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

                    if (explored != null && !Seen(explored, shared, size, at.x, at.z, metersPerMapPixel))
                        continue;

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

                // The wall over the wash, in the same pass as the fill it belongs to, so that a
                // stone hall standing in a wooden courtyard covers the courtyard's edge the way
                // it covers its color. Drawing every fill and then every outline would put wood
                // ink on top of stone.
                if (!outlines) continue;
                var walls = MapOutline.Of(kind);
                if (walls == null) continue;
                GL.Color(MapPaint.Ink(color));
                float half = nib * 0.5f;
                for (int i = 0; i < walls.Count; i++)
                {
                    var wall = walls[i];
                    float sx0 = wall.X0 * cell * perMeterX + offsetX;
                    float sy0 = wall.Z0 * cell * perMeterY + offsetY;
                    float sx1 = wall.X1 * cell * perMeterX + offsetX;
                    float sy1 = wall.Z1 * cell * perMeterY + offsetY;
                    if (sx0 < -nib && sx1 < -nib) continue;
                    if (sy0 < -nib && sy1 < -nib) continue;
                    if (sx0 > Size + nib && sx1 > Size + nib) continue;
                    if (sy0 > Size + nib && sy1 > Size + nib) continue;

                    // Fogged per wall rather than per loop: a building half inside what you have
                    // uncovered should come to an end where your knowledge does, which is what
                    // the fill already does a cell at a time.
                    if (explored != null && !Seen(explored, shared, size,
                            (wall.X0 + wall.X1) * 0.5f * cell, (wall.Z0 + wall.Z1) * 0.5f * cell,
                            metersPerMapPixel))
                        continue;

                    float dx = sx1 - sx0, dy = sy1 - sy0;
                    float len = Mathf.Sqrt(dx * dx + dy * dy);
                    if (len < 0.001f) continue;
                    // Extended half a nib past each end, which squares off the corner where the
                    // next wall starts -- a cheap miter, and at this width an exact one would
                    // look no different.
                    float ex = dx / len * half, ey = dy / len * half;
                    float nx = -ey, ny = ex;
                    sx0 -= ex; sy0 -= ey; sx1 += ex; sy1 += ey;
                    GL.Vertex3(sx0 + nx, sy0 + ny, 0f);
                    GL.Vertex3(sx1 + nx, sy1 + ny, 0f);
                    GL.Vertex3(sx1 - nx, sy1 - ny, 0f);
                    GL.Vertex3(sx0 - nx, sy0 - ny, 0f);
                }
            }

            GL.End();
            GL.PopMatrix();
            RenderTexture.active = previous;
        }

        /// <summary>
        /// Has this player uncovered this piece of world, by walking it or by being told about
        /// it? Unexplored ground hides what is on it, the same as the game's own fog, and that
        /// is done here by not drawing rather than by masking afterwards: it is one bit to read
        /// and it saves the quad.
        /// </summary>
        private static bool Seen(BitArray explored, BitArray shared, int size,
                                 float worldX, float worldZ, float metersPerMapPixel)
        {
            int px = Mathf.Clamp((int)(worldX / metersPerMapPixel + size * 0.5f), 0, size - 1);
            int py = Mathf.Clamp((int)(worldZ / metersPerMapPixel + size * 0.5f), 0, size - 1);
            int bit = py * size + px;
            if (bit < explored.Length && explored[bit]) return true;
            return shared != null && bit < shared.Length && shared[bit];
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
                      $"{(_layer != null && _layer.enabled ? "showing" : "hidden")}, " +
                      $"{MapOutline.Count} straightened wall(s)" +
                      (MapPaint.Outlined() ? "" : " (outlines switched off)"));
            return lines;
        }
    }
}
