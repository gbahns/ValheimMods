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
    /// <para>Throttling a redraw is not the same as letting the drawing lag, and the difference
    /// is the whole of whether this looks like part of the map. Both maps are drawn to a window
    /// wider than they show and the image is panned across it every frame, which costs a rect;
    /// a redraw is only owed when the view reaches the edge of what was drawn, and until it
    /// lands the drawing moves with the map rather than waiting for it.</para>
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

        /// <summary>The most often a redraw may happen, however hard the map is being dragged.</summary>
        private const float EverySeconds = 0.08f;

        /// <summary>
        /// How much wider than the minimap the window is drawn.
        ///
        /// <para>The minimap slides a little every step, and redrawing on every step is the one
        /// thing this must not do -- walking is not an occasion. So it is drawn to a window
        /// twice the width the minimap shows and the image is panned across what is already
        /// there; a redraw happens only when the edge of the drawn window comes into view,
        /// which on a two hundred meter minimap is every fifty meters walked.</para>
        /// </summary>
        private const float SmallPad = 2f;

        /// <summary>How close the view may come to the drawn window's edge before redrawing.</summary>
        private const float SmallSlack = 0.08f;

        /// <summary>
        /// How much wider than the large map the window is drawn.
        ///
        /// <para>The large map used to be drawn for exactly what it showed, which meant the
        /// drawing could only move when it was redrawn -- and a redraw is throttled, so dragging
        /// the map slid the vanilla painting out from under a layer that stayed where it was
        /// until the next one landed. A tenth of a second of the drawing lagging the map is the
        /// whole of the illusion gone: it reads as a sticker on top rather than part of the
        /// map.</para>
        ///
        /// <para>So the large map is panned across a wider window, the same as the minimap: the
        /// image is re-aimed every frame, which is free, and the redraw underneath only has to
        /// keep up with the edge of the window coming into view. The padding is small because
        /// the buffer is a fixed size and every bit of it spent on margin is resolution the
        /// drawing does not get -- twelve percent buys about a twentieth of the view at each
        /// edge, which at the throttle is a drag of half a screen a second with nothing
        /// missing, and costs about a tenth of the sharpness this exists for.</para>
        /// </summary>
        private const float LargePad = 1.12f;

        private static RenderTexture _rt;
        private static RawImage _layer;
        private static RawImage _small;
        private static Material _flat;
        private static Rect _drawnFor = new Rect(-999f, -999f, 0f, 0f);
        private static int _drawnSignature;
        private static float _nextDraw;
        private static bool _failed;

        /// <summary>
        /// Whether the sharp layer is drawing at all. Asked by the twelve meter painting, which
        /// is the fallback: if this has given up, that is all there is.
        /// </summary>
        internal static bool Covering => !_failed;

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
            _small = null;
            _drawnFor = new Rect(-999f, -999f, 0f, 0f);
            _drawnSignature = 0;
        }

        private static void Hide()
        {
            if (_layer != null) _layer.enabled = false;
            if (_small != null) _small.enabled = false;
        }

        /// <summary>The same rect, grown about its own center.</summary>
        private static Rect Pad(Rect r, float by)
        {
            float w = r.width * by, h = r.height * by;
            return new Rect(r.center.x - w * 0.5f, r.center.y - h * 0.5f, w, h);
        }

        /// <summary>
        /// Where one rect sits inside another, as the fractions an image wants.
        ///
        /// <para>Kept inside the buffer. A drag faster than the redraw can follow would
        /// otherwise ask for texels past the edge, and the buffer clamps rather than ending, so
        /// whatever happened to be on that last row would be smeared off across the map in
        /// streaks. Holding at the edge instead costs a moment of the drawing lagging again --
        /// bounded by the margin, and only while the drag is outrunning the throttle -- which
        /// is the smaller of the two wrongs by a long way.</para>
        /// </summary>
        private static Rect Within(Rect seen, Rect window)
        {
            if (window.width <= 0.0000001f || window.height <= 0.0000001f)
                return new Rect(0f, 0f, 1f, 1f);
            float w = seen.width / window.width;
            float h = seen.height / window.height;
            return new Rect(Mathf.Clamp((seen.x - window.x) / window.width, 0f, Mathf.Max(0f, 1f - w)),
                            Mathf.Clamp((seen.y - window.y) / window.height, 0f, Mathf.Max(0f, 1f - h)),
                            w, h);
        }

        /// <summary>
        /// Whether what the map shows is still inside what was drawn, at the zoom it was drawn
        /// for. A changed zoom fails the last test and redraws.
        /// </summary>
        private static bool Holds(Rect window, Rect seen, float pad, float slack)
        {
            return seen.xMin >= window.xMin + window.width * slack
                   && seen.xMax <= window.xMax - window.width * slack
                   && seen.yMin >= window.yMin + window.height * slack
                   && seen.yMax <= window.yMax - window.height * slack
                   && Mathf.Abs(seen.width * pad - window.width) < window.width * 0.05f;
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
            bool large = map.m_mode == Minimap.MapMode.Large;
            bool small = map.m_mode == Minimap.MapMode.Small && MapPaint.SharpMinimap();
            if (!large && !small)
            {
                Hide();
                return;
            }

            try
            {
                if (!Ready(map)) return;

                if (large)
                {
                    if (_small != null) _small.enabled = false;
                    _layer.enabled = true;

                    // Drawn to a window a little wider than the map shows and panned across it,
                    // so that dragging moves the drawing in the same frame as the map rather
                    // than at the next redraw. Below the slack there is no margin to spare: the
                    // padding is thin, and the view leaving the drawn window at all is the
                    // moment a redraw is worth its cost.
                    var view = map.m_mapImageLarge.uvRect;
                    bool steady = Holds(_drawnFor, view, LargePad, 0f) && signature == _drawnSignature;
                    if (!steady && Time.unscaledTime >= _nextDraw)
                    {
                        _nextDraw = Time.unscaledTime + EverySeconds;
                        _drawnFor = Pad(view, LargePad);
                        _drawnSignature = signature;
                        Draw(map, cells, _drawnFor);
                    }
                    // Set every frame and free; the drawing underneath it is what is throttled.
                    _layer.uvRect = Within(view, _drawnFor);
                    return;
                }

                if (_layer != null) _layer.enabled = false;
                _small.enabled = true;

                // The minimap is drawn wider than it shows and panned across, so the image
                // takes whatever part of the buffer is currently under the minimap. This is set
                // every frame and costs nothing; the drawing underneath it is what is rare.
                var seen = map.m_mapImageSmall.uvRect;
                bool held = Holds(_drawnFor, seen, SmallPad, SmallSlack) && signature == _drawnSignature;
                if (!held && Time.unscaledTime >= _nextDraw)
                {
                    _nextDraw = Time.unscaledTime + EverySeconds;
                    _drawnFor = Pad(seen, SmallPad);
                    _drawnSignature = signature;
                    Draw(map, cells, _drawnFor);
                }
                _small.uvRect = Within(seen, _drawnFor);
            }
            catch (System.Exception e)
            {
                _failed = true;
                TheGreatestMapMod.Log.LogWarning(
                    "[TheGreatestMap] The sharp map layer could not be drawn (" + e.Message +
                    "), so the large map keeps the ordinary painting. Nothing else is affected.");
                Hide();
            }
        }

        /// <summary>Build the buffer and the images it is shown through, once.</summary>
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

            // One buffer for both maps. Valheim never shows the minimap and the large map at
            // the same time, so a second sixteen megabyte render texture would be sixteen
            // megabytes that is always idle; what the two need separately is an image each,
            // which costs nothing, and a window each, which is a rect.
            if (map.m_mapImageSmall != null && (_small == null || _small.gameObject == null))
            {
                var host = new GameObject("TGM_SharpMinimap", typeof(RectTransform));
                var rect = host.GetComponent<RectTransform>();
                rect.SetParent(map.m_mapImageSmall.transform, false);
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                _small = host.AddComponent<RawImage>();
                _small.raycastTarget = false;
                rect.SetAsFirstSibling();
            }
            if (_small != null) _small.texture = _rt;
            return true;
        }

        /// <summary>
        /// Paint the window. Ground first and buildings over it, in the order they should cover
        /// one another, which is what a painter does instead of ranking.
        /// </summary>
        /// <summary>
        /// A color as this buffer wants it, rather than as it was written down.
        ///
        /// <para>The game renders in linear color and the buffer is written through
        /// Hidden/Internal-Colored, which hands a vertex color to the hardware untouched. So a
        /// color picked in the config -- which is sRGB, like every color anybody ever types --
        /// was being stored as though it already were linear and came back lighter and flatter
        /// for it: the default wood, a medium brown, drew as pale tan.</para>
        ///
        /// <para>It hid until the twelve meter painting was switched off, because that goes
        /// through Texture2D.SetPixel, which does the conversion itself. Until then a correctly
        /// colored wash sat underneath the washed-out one and the eye took the brighter of the
        /// two for the paint.</para>
        /// </summary>
        private static Color Shade(Color authored) =>
            QualitySettings.activeColorSpace == ColorSpace.Linear ? authored.linear : authored;

        /// <summary>
        /// How far the shade of one patch of ground wanders from the color it was given, as a
        /// fraction, and nothing when the setting is off.
        /// </summary>
        private static float Variation() =>
            TgmConfig.PaintGroundVariation == null
                ? 0f : Mathf.Clamp01(TgmConfig.PaintGroundVariation.Value);

        /// <summary>Ground, as opposed to something somebody built out of a material.</summary>
        private static bool IsGround(BuiltKind kind) =>
            kind == BuiltKind.Dirt || kind == BuiltKind.Paved || kind == BuiltKind.Farm;

        /// <summary>The coarse patchiness, in meters, and the grain laid over it.</summary>
        private const float PatchMeters = 6f;
        private const float GrainMeters = 1.5f;

        /// <summary>
        /// A number from 0 to 1 that belongs to this patch of world and never changes.
        ///
        /// <para>Two lattices rather than one. A single coarse lattice is a checkerboard of
        /// flat squares and a single fine one is static; a broad patchiness with a grain over
        /// it is what ground actually looks like from above. Taken from the world position and
        /// not from the cell, so the pattern stays put when the map is zoomed, when the detail
        /// setting changes, and from one player's screen to another's -- a mottle that moved
        /// would read as the map flickering rather than as ground.</para>
        /// </summary>
        private static float Mottle(float x, float z)
        {
            float broad = Hashed(Mathf.FloorToInt(x / PatchMeters), Mathf.FloorToInt(z / PatchMeters));
            float grain = Hashed(Mathf.FloorToInt(x / GrainMeters), Mathf.FloorToInt(z / GrainMeters));
            return broad * 0.65f + grain * 0.35f;
        }

        private static float Hashed(int x, int z)
        {
            unchecked
            {
                int h = (x * 73856093) ^ (z * 19349663);
                h ^= h >> 13;
                h *= 1274126177;
                h ^= h >> 16;
                return ((h >> 8) & 0xFFFF) / 65535f;
            }
        }

        /// <summary>Quads sent before the buffer is closed and reopened.</summary>
        private const int BatchQuads = 8192;

        private static readonly Dictionary<BuiltKind, List<int>> _byKind =
            new Dictionary<BuiltKind, List<int>>();

        /// <summary>
        /// How much world one square of the drawing index covers.
        ///
        /// <para>Small enough that a minimap window touches a handful of them and large enough
        /// that a zoomed out map does not ask after millions. Thirty-two meters is about a
        /// fifth of what the minimap shows.</para>
        /// </summary>
        private const float BlockMeters = 32f;

        /// <summary>
        /// The most squares worth asking after one at a time. Past this the window covers more
        /// of the world than the picture has cells in it, and reading the whole list -- which
        /// is what this always did -- is the cheaper of the two.
        /// </summary>
        private const int MostBlocks = 8192;

        /// <summary>Long enough in one frame that the player saw it.</summary>
        private const int ReindexLoudMillis = 60;

        /// <summary>
        /// The cells of each kind again, this time gathered into squares of world, so that
        /// drawing a window reads the cells in that window rather than all of them.
        /// </summary>
        private static readonly Dictionary<BuiltKind, Dictionary<int, List<int>>> _blocked =
            new Dictionary<BuiltKind, Dictionary<int, List<int>>>();

        /// <summary>The squares the current window touches; reused, never reallocated.</summary>
        private static readonly List<List<int>> _slices = new List<List<int>>();

        private static int BlockKey(int bx, int bz) => (bx << 16) | (bz & 0xFFFF);

        private static int BlockOfCell(int key)
        {
            MapPaint.Cell(key, out int cx, out int cz);
            float cell = MapPaint.CellSize;
            return BlockKey(Mathf.FloorToInt(cx * cell / BlockMeters),
                            Mathf.FloorToInt(cz * cell / BlockMeters));
        }

        private static int _sortedHash = int.MinValue;
        private static float _sortedCell = -1f;

        /// <summary>
        /// The cells of each kind, as a list. Rebuilt only when the server's picture changes,
        /// and the lists are reused rather than reallocated, because this is the one place the
        /// whole table is touched and it is touched on the drawing path.
        /// </summary>
        private static Dictionary<BuiltKind, List<int>> Sorted(Dictionary<int, BuiltKind> cells, int hash)
        {
            // The grid as well as the picture: a cell key means a different piece of world
            // at a different cell size, so the squares it was filed under stop being true even
            // if every key is the same.
            if (hash == _sortedHash && Mathf.Approximately(_sortedCell, MapPaint.CellSize)) return _byKind;
            _sortedHash = hash;
            _sortedCell = MapPaint.CellSize;
            foreach (var kv in _byKind) kv.Value.Clear();
            foreach (var kv in _blocked)
                foreach (var square in kv.Value) square.Value.Clear();
            if (cells != null)
                foreach (var kv in cells)
                {
                    if (!_byKind.TryGetValue(kv.Value, out var list))
                        _byKind[kv.Value] = list = new List<int>();
                    list.Add(kv.Key);

                    if (!_blocked.TryGetValue(kv.Value, out var squares))
                        _blocked[kv.Value] = squares = new Dictionary<int, List<int>>();
                    int square2 = BlockOfCell(kv.Key);
                    if (!squares.TryGetValue(square2, out var here))
                        squares[square2] = here = new List<int>();
                    here.Add(kv.Key);
                }
            return _byKind;
        }

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

            // How thick a wall is drawn, and whether it is drawn at all. Zoomed out far enough,
            // an outline around everything is a dark smear rather than a building, and the fill
            // says more on its own; so the outlines fade out by not being drawn, at the zoom
            // where they stop being lines.
            //
            // Measured in pixels per meter and not in pixels per cell, which is what it was.
            // Tying it to the cell meant that recording the world four times more finely asked
            // for four times the zoom before a single wall would appear -- going to a meter a
            // cell switched the outlines off everywhere, which is the one change they existed
            // for. How much zoom it takes to draw a wall is a question about the wall.
            float nib = Mathf.Clamp(perMeterX * 0.56f, 1.5f, 5f);
            bool outlines = perMeterX >= 0.75f && MapPaint.Outlined();

            // Sorted by kind once per picture rather than walked once per kind per redraw.
            // The fill used to read the whole table for each of the seven kinds, which cost
            // nothing at three thousand cells and is most of a frame at fifty thousand -- and
            // this runs while the map is being dragged, which is the path that has stalled a
            // client into timeouts before.
            // Timed together, because together is how they are felt: both skip their work
            // unless the picture changed, so the frame that follows a new picture is the one
            // holding a re-index of every cell and a trace of every boundary at once, on top of
            // the fill below. Always started rather than only when the work is expected -- a
            // stopwatch costs nothing against a redraw, and the case worth catching is the one
            // nobody predicted.
            var reindex = System.Diagnostics.Stopwatch.StartNew();
            var byKind = Sorted(cells, MapPaint.CellsHash);
            if (outlines) MapOutline.Trace(byKind, MapPaint.CellsHash);
            reindex.Stop();
            if (reindex.ElapsedMilliseconds >= ReindexLoudMillis)
                TheGreatestMapMod.Log.LogWarning(
                    $"[TheGreatestMap] Sorting the map picture for drawing took {reindex.ElapsedMilliseconds} ms " +
                    $"in one frame for {cells.Count} cell(s) at {MapPaint.CellSize:0.##} m" +
                    (outlines ? ", outlines included" : "") + ". Raise Paint Detail Meters, or turn Draw " +
                    "Outlines off, if it keeps happening.");

            // The squares of world this window touches, worked out once. The fill read every
            // cell of every kind on every redraw and threw away the ones off screen, which
            // costs nothing at five thousand cells and is most of a frame at a hundred
            // thousand -- and it runs while the minimap pans, which is the path that has
            // stalled a client into timeouts and disconnects before.
            //
            // Half the window plus the five percent the cull allows past its edge.
            float reachX = view.width * 0.55f * size * metersPerMapPixel;
            float reachZ = view.height * 0.55f * size * metersPerMapPixel;
            float midX = (view.center.x - 0.5f) * size * metersPerMapPixel;
            float midZ = (view.center.y - 0.5f) * size * metersPerMapPixel;
            int bx0 = Mathf.FloorToInt((midX - reachX) / BlockMeters);
            int bx1 = Mathf.FloorToInt((midX + reachX) / BlockMeters);
            int bz0 = Mathf.FloorToInt((midZ - reachZ) / BlockMeters);
            int bz1 = Mathf.FloorToInt((midZ + reachZ) / BlockMeters);
            bool byBlock = (long)(bx1 - bx0 + 1) * (bz1 - bz0 + 1) <= MostBlocks;

            var previous = RenderTexture.active;
            RenderTexture.active = _rt;
            GL.PushMatrix();
            GL.LoadPixelMatrix(0f, Size, 0f, Size);
            GL.Clear(false, true, new Color(0f, 0f, 0f, 0f));
            _flat.SetPass(0);
            GL.Begin(GL.QUADS);

            // Immediate mode keeps everything between Begin and End in one buffer, and at a
            // meter a cell a developed world is tens of thousands of quads rather than the few
            // thousand this was written for. Closing and reopening costs a draw call and takes
            // the question away; the color has to be set again afterwards, because Begin does
            // not carry it over.
            int batched = 0;

            foreach (var kind in Order)
            {
                if (!MapPaint.Wants(kind)) continue;
                if (!byKind.TryGetValue(kind, out var mine)) continue;
                var authored = MapPaint.ColorFor(kind);
                var color = Shade(authored);
                GL.Color(color);
                // Ground only, and only when it is asked for: the per-cell color below costs a
                // call a quad, which is worth paying for ground and not for a wall.
                float vary = IsGround(kind) ? Variation() : 0f;
                bool mottled = vary > 0f;
                _slices.Clear();
                if (byBlock && _blocked.TryGetValue(kind, out var squares))
                {
                    for (int bz = bz0; bz <= bz1; bz++)
                        for (int bx = bx0; bx <= bx1; bx++)
                            if (squares.TryGetValue(BlockKey(bx, bz), out var near) && near.Count > 0)
                                _slices.Add(near);
                }
                else _slices.Add(mine);

                for (int s = 0; s < _slices.Count; s++)
                {
                    var slice = _slices[s];
                    for (int c = 0; c < slice.Count; c++)
                    {
                        var at = MapPaint.Center(slice[c]);

                        // World to the map's own normalized space -- Minimap's WorldToMapPoint,
                        // which is four lines and private, so it is four lines here instead of
                        // a lookup.
                        float mx = (at.x / metersPerMapPixel + size * 0.5f) / size;
                        float my = (at.z / metersPerMapPixel + size * 0.5f) / size;

                        float u = (mx - view.xMin) / view.width;
                        float v = (my - view.yMin) / view.height;
                        if (u < -0.05f || u > 1.05f || v < -0.05f || v > 1.05f) continue;   // off screen

                        // Asked after the screen test and not before it. Whether a cell has
                        // been uncovered is a read of the fog texture, and asking it of cells
                        // nowhere near the window was most of the work on a picture that is
                        // mostly somewhere else.
                        if (explored != null && !Seen(explored, shared, size, at.x, at.z, metersPerMapPixel))
                            continue;

                        if (mottled)
                        {
                            // Scaled in the color the player typed and converted afterwards,
                            // because a shade darker is something the eye judges in sRGB; doing
                            // it after the conversion would darken the midtones twice.
                            float k = 1f + (Mottle(at.x, at.z) - 0.5f) * 2f * vary;
                            GL.Color(Shade(new Color(Mathf.Clamp01(authored.r * k),
                                                     Mathf.Clamp01(authored.g * k),
                                                     Mathf.Clamp01(authored.b * k), authored.a)));
                        }

                        float x = u * Size, y = v * Size;
                        float x0 = x - wide * 0.5f, x1 = x + wide * 0.5f;
                        float y0 = y - tall * 0.5f, y1 = y + tall * 0.5f;
                        GL.Vertex3(x0, y0, 0f);
                        GL.Vertex3(x1, y0, 0f);
                        GL.Vertex3(x1, y1, 0f);
                        GL.Vertex3(x0, y1, 0f);
                        if (++batched >= BatchQuads)
                        {
                            GL.End();
                            GL.Begin(GL.QUADS);
                            GL.Color(color);
                            batched = 0;
                        }
                    }
                }

                // The wall over the wash, in the same pass as the fill it belongs to, so that a
                // stone hall standing in a wooden courtyard covers the courtyard's edge the way
                // it covers its color. Drawing every fill and then every outline would put wood
                // ink on top of stone.
                if (!outlines) continue;
                var walls = MapOutline.Of(kind);
                if (walls == null) continue;
                // Worked out from the color somebody chose rather than from the converted
                // one: Ink judges how light a fill is so it can go darker or lighter than it,
                // and that judgement is about how the color looks, not how it is stored.
                var ink = Shade(MapPaint.Ink(authored));
                GL.Color(ink);
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
                    if (++batched >= BatchQuads)
                    {
                        GL.End();
                        GL.Begin(GL.QUADS);
                        GL.Color(ink);
                        batched = 0;
                    }
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
