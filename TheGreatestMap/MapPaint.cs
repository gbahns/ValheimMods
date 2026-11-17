using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace TheGreatestMap
{
    /// <summary>What was found standing on a piece of ground.</summary>
    internal enum BuiltKind : byte
    {
        None = 0,
        Wood = 1,
        Stone = 2,
        Iron = 3,
        Paved = 4,
        Dirt = 5,
        Farm = 6,
        Marble = 7,
    }

    /// <summary>
    /// How many of each kind of thing stand in one place -- one cell, or one map pixel.
    ///
    /// <para>A struct rather than an array so that a dictionary of these is a single block of
    /// memory instead of one small allocation per entry; there can be hundreds of thousands of
    /// cells, and the per-entry version of this was eleven megabytes of garbage a sweep.</para>
    /// </summary>
    internal struct Tally
    {
        public int Wood, Stone, Iron, Marble, Paved, Dirt, Farm;

        public void Add(BuiltKind kind)
        {
            switch (kind)
            {
                case BuiltKind.Wood: Wood++; break;
                case BuiltKind.Stone: Stone++; break;
                case BuiltKind.Iron: Iron++; break;
                case BuiltKind.Marble: Marble++; break;
                case BuiltKind.Paved: Paved++; break;
                case BuiltKind.Dirt: Dirt++; break;
                case BuiltKind.Farm: Farm++; break;
            }
        }

        /// <summary>
        /// What this place is, all told.
        ///
        /// <para>Building beats ground outright, because a hall standing in its own paved
        /// courtyard is a hall and the hall is the thing worth seeing from across the map.
        /// Within a tier it is a count and not a hierarchy: the place says what most of it is
        /// made of, so a wooden longhouse on stone footings is a wooden longhouse.</para>
        ///
        /// <para>Rank breaks a tie, and breaks it toward the heavier material. A dead heat
        /// between wood and stone is a judgment call either way, and stone is the more useful
        /// guess -- it is the rarer material, so it carries more information about what is
        /// there.</para>
        /// </summary>
        public BuiltKind Winner() =>
            Wood + Stone + Iron + Marble > 0
                ? Most(BuiltKind.Wood, Wood, BuiltKind.Stone, Stone, BuiltKind.Iron, Iron, BuiltKind.Marble, Marble)
                : Most(BuiltKind.Paved, Paved, BuiltKind.Dirt, Dirt, BuiltKind.Farm, Farm, BuiltKind.None, 0);

        private static BuiltKind Most(BuiltKind a, int na, BuiltKind b, int nb, BuiltKind c, int nc,
                                      BuiltKind d, int nd)
        {
            var best = a;
            int most = na;
            if (nb > most || (nb == most && MapPaint.Rank(b) > MapPaint.Rank(best))) { best = b; most = nb; }
            if (nc > most || (nc == most && MapPaint.Rank(c) > MapPaint.Rank(best))) { best = c; most = nc; }
            if (nd > most || (nd == most && MapPaint.Rank(d) > MapPaint.Rank(best))) { best = d; most = nd; }
            return most > 0 ? best : BuiltKind.None;
        }
    }

    /// <summary>
    /// Player building and paved road, painted into the map itself rather than pinned on top of it.
    ///
    /// <para>A marker answers "what is here"; a shape answers "how big is it and what runs
    /// between". A base is not a point, and neither is a road -- pinning them would say a
    /// longhouse and a shed are the same thing, and a road would need a pin every few meters,
    /// which is the one thing this mod has learned the hard way never to do: the map stalls at
    /// a few thousand pins, and a road network is tens of thousands.</para>
    ///
    /// <para>So the ground is colored instead. The game draws the world map from a single
    /// texture, and that texture is read by the large map, the minimap and the parchment in your
    /// hands alike, so one write shows up in all three with the right scale, panning and fog
    /// already handled. Unexplored ground stays dark, which is the right behavior for free: you
    /// do not see somebody's base through the fog, only once you have been there.</para>
    ///
    /// <para>What it costs: one texture upload when the picture changes, never per frame. Every
    /// pixel changed is remembered as it was, so switching the painting off puts the map back
    /// exactly, and the game regenerating its map (a world load) is noticed by the texture
    /// changing underneath us rather than by patching the generator.</para>
    ///
    /// <para>The resolution is the map's own, which is coarse -- a map pixel is several meters of
    /// world. A shed is a pixel, a base is a patch, and a road is a line somewhat wider than the
    /// road really is. That is a feature as much as a limit: it matches everything else drawn on
    /// that map, so the result reads as terrain rather than as an overlay somebody bolted on.</para>
    ///
    /// <para>The color is not the whole of the map, which is why coloring alone came out muddled.
    /// The game builds it from three pictures: a flat color per biome, a mask that draws forest
    /// over it, and a height that decides where the sea is and how the land is shaded. Writing
    /// only the color left the black forest drawing trees over our paint and the sea drawn over it
    /// entirely -- a dock painted below the water line is painted under water. So a painted pixel
    /// has its forest cleared and, if it was under the water line, is lifted a meter above it.
    /// What is drawn is then the paint and nothing else, which is what "solid" has to mean on a
    /// map the game shades for itself.</para>
    /// </summary>
    internal static class MapPaint
    {
        /// <summary>
        /// How large a piece of world one recorded cell is, in meters.
        ///
        /// <para>The server decides this and says so in the picture it sends, rather than both
        /// ends reading it out of a setting. They have to agree exactly -- a client drawing a
        /// server's keys on a different grid draws the world in the wrong place and at the wrong
        /// size -- and a number that travels with the thing it describes cannot drift out of step
        /// with it the way two copies of a setting can.</para>
        /// </summary>
        internal static float CellSize { get; private set; } = DefaultCellSize;

        /// <summary>
        /// A meter, which is the smallest floor piece. Not the smallest the key can hold --
        /// that is CellFloor, half a meter -- because a meter is where the eye stops being able
        /// to tell, and the cost is four times the cells for each halving.
        /// </summary>
        internal const float DefaultCellSize = 1f;

        /// <summary>
        /// Change the grid. Everything already drawn was drawn on the old one, so it comes off
        /// the map before anything is drawn on the new one.
        /// </summary>
        /// <summary>
        /// Half a meter is the floor the key packing can take. A cell index is a signed sixteen
        /// bit number, so it reaches 32768 cells either way: 16 km at half a meter against a
        /// world that stops at 10.5, and 8 km at a quarter, which the world would run off the
        /// end of. Going finer than this means a wider key, not a smaller number here.
        /// </summary>
        internal const float CellFloor = 0.5f;

        internal const float CellCeiling = 16f;

        /// <summary>Whether asking for this size would actually change the grid.</summary>
        internal static bool WouldRegrid(float meters) =>
            !Mathf.Approximately(Mathf.Clamp(meters, CellFloor, CellCeiling), CellSize);

        internal static void UseCellSize(float meters)
        {
            float asked = meters;
            meters = Mathf.Clamp(meters, CellFloor, CellCeiling);
            // Asking for a size that cannot be had used to do nothing and say nothing, which
            // reads as the setting being ignored rather than as a limit being hit.
            if (!Mathf.Approximately(asked, meters) && !Mathf.Approximately(asked, _complained))
            {
                _complained = asked;
                TheGreatestMapMod.Log.LogWarning(
                    $"[TheGreatestMap] Paint Detail Meters is {asked:0.###}, which is outside " +
                    $"{CellFloor:0.##} to {CellCeiling:0.#} m; recording at {meters:0.##} m instead.");
            }
            if (Mathf.Approximately(meters, CellSize)) return;
            CellSize = meters;
            _cells.Clear();
            Forget();
            _dirty = true;
        }

        private static float _complained = float.NaN;

        /// <summary>Offset that keeps a support square index positive, so two pack into one int.</summary>
        private const int CellBias = 16384;

        internal static int Key(Vector3 world)
        {
            return Pack(Mathf.FloorToInt(world.x / CellSize), Mathf.FloorToInt(world.z / CellSize));
        }

        /// <summary>The key of a cell named by its own indices rather than by a point in it.</summary>
        internal static int KeyOf(int cx, int cz) =>
            Pack(cx, cz);

        /// <summary>
        /// Two cell indices in one int, each a plain signed sixteen bit number.
        ///
        /// <para>This used to add a bias to keep both positive, and the bias was the thing
        /// holding the grid back. The high half is recovered with an arithmetic shift, so it
        /// only survives the round trip while it reads as a positive sixteen bit number -- and
        /// a biased index did that for only half of its range, giving the east-west axis
        /// plus or minus 16384 cells against the north-south axis's 49151. Signed, both axes
        /// get the full plus or minus 32768, which is what lets a cell be half a meter in a
        /// world that stops at ten and a half kilometers.</para>
        /// </summary>
        private static int Pack(int cx, int cz) => (cx << 16) | (cz & 0xFFFF);

        /// <summary>The low half, read back as the signed number it was written as.</summary>
        private static int Low(int key) => (short)(key & 0xFFFF);

        /// <summary>
        /// The side of the squares the noise gate counts pieces in.
        ///
        /// <para>Fixed, and deliberately not the cell size. Whether a patch of world is a
        /// building or somebody's dropped torch is a fact about the world, so the window it is
        /// judged over has to be a distance and not a number of cells -- a gate measured in
        /// cells asks a different question at every grid size, and below three meters it asked
        /// nothing at all, because both thresholds rounded down to one and the filter switched
        /// itself off exactly where a fine grid most needs it.</para>
        /// </summary>
        internal const float SupportMeters = 12f;

        internal static int Block(Vector3 world) =>
            BlockOf(Mathf.FloorToInt(world.x / SupportMeters), Mathf.FloorToInt(world.z / SupportMeters));

        /// <summary>The support square dx, dz squares from the one this point is in.</summary>
        internal static int BlockNear(Vector3 world, int dx, int dz) =>
            BlockOf(Mathf.FloorToInt(world.x / SupportMeters) + dx,
                    Mathf.FloorToInt(world.z / SupportMeters) + dz);

        private static int BlockOf(int bx, int bz) =>
            ((bx + CellBias) << 16) | ((bz + CellBias) & 0xFFFF);

        /// <summary>
        /// The key of the cell dx, dz cells away. Here rather than at the caller because the way
        /// two cell numbers pack into one int is this file's business and nobody else's.
        /// </summary>
        internal static int Neighbor(int key, int dx, int dz)
        {
            int cx = (key >> 16) + dx;
            int cz = Low(key) + dz;
            return Pack(cx, cz);
        }

        /// <summary>
        /// The cell indices a key stands for. Same reason Neighbor is here: how two cell numbers
        /// pack into one int is this file's business, and a tracer that needs the corners of a
        /// cell should not have to know it.
        /// </summary>
        internal static void Cell(int key, out int cx, out int cz)
        {
            cx = key >> 16;
            cz = Low(key);
        }

        internal static Vector3 Center(int key)
        {
            int cx = key >> 16;
            int cz = Low(key);
            return new Vector3((cx + 0.5f) * CellSize, 0f, (cz + 0.5f) * CellSize);
        }

        /// <summary>
        /// What stands on each cell. Replaced wholesale by the server's picture; nothing here is
        /// saved on the client, because the server is the one that knows the whole world.
        /// </summary>
        private static readonly Dictionary<int, BuiltKind> _cells = new Dictionary<int, BuiltKind>();

        /// <summary>Every map pixel we have changed, and the color it had before we touched it.</summary>
        private static readonly Dictionary<int, Color32> _under = new Dictionary<int, Color32>();

        /// <summary>The same pixels' forest mask, which is what was washing the color out.</summary>
        private static readonly Dictionary<int, Color32> _underMask = new Dictionary<int, Color32>();

        /// <summary>And their height, kept only for the pixels that had to be lifted out of the water.</summary>
        private static readonly Dictionary<int, float> _underHeight = new Dictionary<int, float>();

        /// <summary>
        /// What we last wrote to each pixel. A repaint then touches only what actually moved: a
        /// sweep that found one new shed writes nine pixels rather than every pixel in the world.
        /// </summary>
        private static readonly Dictionary<int, Color32> _shown = new Dictionary<int, Color32>();

        private static Texture2D _painted;      // the texture _under belongs to
        private static bool _dirty;
        private static float _nextApply;
        private static string _lastColors = "";

        /// <summary>How long after a change before the texture is rewritten. One upload, not one per cell.</summary>
        private const float Settle = 1.5f;

        /// <summary>How often a few of our own pixels are read back to see whether they survived.</summary>
        private const float CheckSeconds = 2f;

        /// <summary>
        /// How many pixels that check reads. A wipe is never partial -- the game writes the whole
        /// picture at once -- so a handful answers the question, and a handful is free.
        /// </summary>
        private const int Sentinels = 16;

        private static float _nextCheck;

        internal static int CellCount => _cells.Count;
        internal static int PaintedPixels => _under.Count;

        /// <summary>
        /// Everything we believed about the map is wrong: paint it all again from nothing.
        ///
        /// <para>Not the same as <see cref="Reset"/>, which is for a world change. This is for the
        /// map being rewritten underneath us while we hold the same texture -- which the game does
        /// at every world load, and did seven seconds after our first paint: the colors we had
        /// written were replaced, the colors we had remembered as "underneath" were the empty
        /// texture from before the map existed, and because we only touch what moved, nothing was
        /// ever drawn again.</para>
        /// </summary>
        private static void Forget()
        {
            MapHighRes.Forget();
            _under.Clear();
            _underMask.Clear();
            _underHeight.Clear();
            _shown.Clear();
            _dirty = true;
        }

        internal static void Reset()
        {
            // Not a restore: on a world change the texture is gone anyway, and reaching into a
            // dead one would be the error this guards against.
            _cells.Clear();
            _under.Clear();
            _underMask.Clear();
            _underHeight.Clear();
            _shown.Clear();
            _painted = null;
            _dirty = false;
            _nextApply = 0f;
        }

        /// <summary>The server's picture of the world, whole. Returns true if it differs from what we had.</summary>
        internal static bool Take(Dictionary<int, BuiltKind> cells)
        {
            if (cells == null) return false;
            if (cells.Count == _cells.Count)
            {
                bool same = true;
                foreach (var kv in cells)
                    if (!_cells.TryGetValue(kv.Key, out var had) || had != kv.Value) { same = false; break; }
                if (same) return false;
            }
            _cells.Clear();
            int hash = cells.Count;
            foreach (var kv in cells) { _cells[kv.Key] = kv.Value; hash ^= kv.Key * 31 + (int)kv.Value; }
            _cellsHash = hash;
            _dirty = true;
            return true;
        }

        /// <summary>
        /// A fingerprint of the cells, kept rather than recomputed.
        ///
        /// <para>The sharp layer is asked every frame whether anything changed, and walking every
        /// cell to answer would be tens of thousands of dictionary steps a frame for an answer
        /// that is almost always no. The cells only change when a picture arrives, so that is
        /// where this is worked out.</para>
        /// </summary>
        private static int _cellsHash;

        /// <summary>
        /// Changes when the picture does, and not when a color does. The outlines depend on
        /// where things are and not on what color they are drawn in, so they are kept across a
        /// color change that the fill has to repaint for.
        /// </summary>
        internal static int CellsHash => _cellsHash;

        internal static void Update()
        {
            var map = Minimap.instance;
            if (map == null) return;
            var tex = map.m_mapTexture;
            if (tex == null) return;

            // A different texture altogether: whatever we remembered was underneath belongs to one
            // that no longer exists, and this one is somebody else's.
            if (!ReferenceEquals(tex, _painted))
            {
                _painted = tex;
                Forget();
            }

            // The same texture, rewritten. This is the one that caught us out: the game generates
            // its world map into the texture it already had, which it did seven seconds after our
            // first paint, and nothing about the texture itself changes when it does. So the only
            // honest way to know is to read back something we wrote.
            if (Time.unscaledTime >= _nextCheck)
            {
                _nextCheck = Time.unscaledTime + CheckSeconds;
                if (Wiped(tex, map.m_textureSize))
                {
                    TheGreatestMapMod.Log.LogDebug(
                        "[TheGreatestMap] The game rewrote the world map; painting it again.");
                    Forget();
                }
            }

            // A color change is as much a change as a new cell, and costs the same repaint.
            string colors = ColorSignature();
            if (colors != _lastColors) { _lastColors = colors; _dirty = true; }

            // The sharp layer is asked every frame and answers in one comparison unless the
            // large map is open; it is not gated on _dirty because it also has to follow the
            // view, which moves without anything about the picture changing.
            MapHighRes.Update(map, _cells, Signature(colors));

            if (!_dirty || Time.unscaledTime < _nextApply) return;
            _dirty = false;
            _nextApply = Time.unscaledTime + Settle;
            Paint(map, tex);
        }

        /// <summary>Is what we last wrote still there?</summary>
        private static bool Wiped(Texture2D tex, int size)
        {
            if (_shown.Count == 0 || size <= 0) return false;
            try
            {
                int looked = 0;
                foreach (var kv in _shown)
                {
                    if (!Same(tex.GetPixel(kv.Key % size, kv.Key / size), kv.Value)) return true;
                    if (++looked >= Sentinels) break;
                }
            }
            catch (System.Exception) { return false; }
            return false;
        }

        /// <summary>Cells and colors together, so either changing redraws the sharp layer.</summary>
        private static int Signature(string colors) =>
            (colors != null ? colors.GetHashCode() : 0) * 31 + _cellsHash;

        private static string ColorSignature()
        {
            return (Wanted() ? "1" : "0") + "|" + SpreadPixels() + "|" + (OverWater() ? "1" : "0")
                   + "|" + (Outlined() ? "1" : "0") + "|"
                   + Show(BuiltKind.Wood) + Show(BuiltKind.Stone)
                   + Show(BuiltKind.Iron) + Show(BuiltKind.Marble) + Show(BuiltKind.Paved)
                   + Show(BuiltKind.Dirt) + Show(BuiltKind.Farm);
        }

        /// <summary>How far each painted pixel is grown into its neighbors, in map pixels.</summary>
        private static int SpreadPixels() =>
            Mathf.Clamp(TgmConfig.PaintSpread != null ? TgmConfig.PaintSpread.Value : 1, 0, 4);

        /// <summary>Is a pixel under the water line lifted above it so that it can be seen?</summary>
        private static bool OverWater() =>
            TgmConfig.PaintOverWater == null || TgmConfig.PaintOverWater.Value;

        /// <summary>How far above the water line a lifted pixel goes. Enough to be land, no more.</summary>
        private const float Lift = 1f;

        private static float WaterLine() =>
            ZoneSystem.instance != null ? ZoneSystem.instance.m_waterLevel : 30f;

        /// <summary>No forest, no mistlands haze, no ashlands sea: whatever we paint, drawn plainly.</summary>
        private static readonly Color32 Clear = new Color32(0, 0, 0, 0);

        private static FieldInfo _maskField;
        private static FieldInfo _heightField;
        private static bool _lookedForFields;

        /// <summary>
        /// The two pictures beside the colors. Both are private to the game, so they are reached
        /// by reflection rather than through the publicized assembly -- that compiles against a
        /// private field happily and then throws when it runs.
        /// </summary>
        private static void FindFields()
        {
            if (_lookedForFields) return;
            _lookedForFields = true;
            _maskField = AccessTools.Field(typeof(Minimap), "m_forestMaskTexture");
            _heightField = AccessTools.Field(typeof(Minimap), "m_heightTexture");
            if (_maskField == null || _heightField == null)
                TheGreatestMapMod.Log.LogWarning(
                    "[TheGreatestMap] The map's forest or height picture has moved in this version of the game. " +
                    "The painting will still color the map, but forest will draw over it and anything over water " +
                    "will not show. Markers are not affected.");
        }

        private static Texture2D MaskTexture(Minimap map)
        {
            FindFields();
            return _maskField != null ? _maskField.GetValue(map) as Texture2D : null;
        }

        private static Texture2D HeightTexture(Minimap map)
        {
            FindFields();
            return _heightField != null ? _heightField.GetValue(map) as Texture2D : null;
        }

        private static bool _maskRefused;
        private static bool _heightRefused;
        private static long _lastPaintMs;
        private static int _lastPaintPixels;

        /// <summary>
        /// Clear the forest over one pixel, remembering what it was.
        ///
        /// <para>Written as a thing that can fail on its own. These two pictures are in whatever
        /// format the game chose -- the heights are half-precision reds -- and a format that will
        /// not take a single pixel write would otherwise throw in the middle of the paint, leaving
        /// the colors half written and nothing uploaded, every time, forever. So each is tried
        /// once and given up on with a word in the log, and the painting carries on without
        /// it.</para>
        /// </summary>
        private static bool ClearForest(Texture2D mask, int pixel, int x, int y)
        {
            if (mask == null || _maskRefused) return false;
            try
            {
                if (!_underMask.ContainsKey(pixel)) _underMask[pixel] = mask.GetPixel(x, y);
                mask.SetPixel(x, y, Clear);
                return true;
            }
            catch (System.Exception e)
            {
                _maskRefused = true;
                _underMask.Clear();
                TheGreatestMapMod.Log.LogWarning(
                    "[TheGreatestMap] The map's forest picture would not take a write (" + e.Message +
                    "); the painting will show but forest will draw over it.");
                return false;
            }
        }

        /// <summary>Lift one pixel above the water line, remembering the height it had.</summary>
        private static bool LiftAboveWater(Texture2D height, int pixel, int x, int y, float line)
        {
            if (height == null || _heightRefused) return false;
            try
            {
                float was = height.GetPixel(x, y).r;
                if (was >= line) return false;
                if (!_underHeight.ContainsKey(pixel)) _underHeight[pixel] = was;
                height.SetPixel(x, y, new Color(line, 0f, 0f, 0f));
                return true;
            }
            catch (System.Exception e)
            {
                _heightRefused = true;
                _underHeight.Clear();
                TheGreatestMapMod.Log.LogWarning(
                    "[TheGreatestMap] The map's height picture would not take a write (" + e.Message +
                    "); the painting will show on land but not over water.");
                return false;
            }
        }

        private static string Show(BuiltKind kind) => Wants(kind) ? ColorFor(kind).ToString() : "-";

        /// <summary>
        /// Is the whole thing switched on? The server's answer and not the player's: it decides
        /// whether the sweeping and the sampling happen at all, so it is asked by everything that
        /// does that work, not just by the drawing.
        ///
        /// <para>The fallback is off rather than on, which is the other way round from every
        /// other setting here. They all default to on, so answering "on" for a setting that has
        /// not been bound yet matches what it will say in a moment; this one defaults to off, and
        /// the same reasoning gives the opposite answer.</para>
        /// </summary>
        internal static bool On() =>
            TgmConfig.PaintEnabled != null && TgmConfig.PaintEnabled.Value;

        /// <summary>Is any of this to be drawn on this player's map at all?</summary>
        internal static bool Wanted() =>
            On() && (TgmConfig.ShowMapPaint == null || TgmConfig.ShowMapPaint.Value);

        /// <summary>Are the edges drawn as lines, or is the fill left to speak for itself?</summary>
        /// <summary>Whether the minimap gets the sharp layer as well as the large map.</summary>
        internal static bool SharpMinimap() =>
            TgmConfig.PaintSharpMinimap == null || TgmConfig.PaintSharpMinimap.Value;

        internal static bool Outlined() =>
            TgmConfig.PaintOutlines == null || TgmConfig.PaintOutlines.Value;

        internal static bool Wants(BuiltKind kind)
        {
            if (!Wanted()) return false;
            switch (kind)
            {
                case BuiltKind.Paved: return TgmConfig.PaintRoads == null || TgmConfig.PaintRoads.Value;
                case BuiltKind.Dirt: return TgmConfig.PaintDirtPaths == null || TgmConfig.PaintDirtPaths.Value;
                case BuiltKind.Farm: return TgmConfig.PaintFarmland == null || TgmConfig.PaintFarmland.Value;
                default: return TgmConfig.PaintBuildings == null || TgmConfig.PaintBuildings.Value;
            }
        }

        internal static Color ColorFor(BuiltKind kind)
        {
            switch (kind)
            {
                case BuiltKind.Wood:  return Cfg(TgmConfig.WoodColor, new Color(0.44f, 0.31f, 0.18f));
                case BuiltKind.Stone: return Cfg(TgmConfig.StoneColor, new Color(0.50f, 0.50f, 0.52f));
                case BuiltKind.Iron:  return Cfg(TgmConfig.IronColor, new Color(0.33f, 0.35f, 0.40f));
                case BuiltKind.Marble: return Cfg(TgmConfig.MarbleColor, new Color(0.07f, 0.07f, 0.09f));
                case BuiltKind.Dirt:  return Cfg(TgmConfig.DirtColor, new Color(0.47f, 0.38f, 0.26f));
                case BuiltKind.Farm:  return Cfg(TgmConfig.FarmColor, new Color(0.38f, 0.33f, 0.19f));
                default:              return Cfg(TgmConfig.PavedColor, new Color(0.62f, 0.60f, 0.55f));
            }
        }

        /// <summary>
        /// The color to stroke an outline of something filled in <paramref name="fill"/>.
        ///
        /// <para>Derived rather than configured, because it is not a choice anybody wants to
        /// make: an outline is the fill in shadow, and ten colors in the config are already more
        /// than enough without ten more beside them for their edges.</para>
        ///
        /// <para>Darker than the fill, except where the fill is already nearly black -- black
        /// marble -- and a darker line would be invisible against it. There the edge goes the
        /// other way and is lighter, which is what a line drawn in chalk rather than ink does
        /// and reads the same way round.</para>
        /// </summary>
        internal static Color Ink(Color fill)
        {
            float light = fill.r * 0.299f + fill.g * 0.587f + fill.b * 0.114f;
            var ink = light > 0.22f
                ? new Color(fill.r * 0.34f, fill.g * 0.34f, fill.b * 0.34f, fill.a)
                : Color.Lerp(fill, new Color(0.62f, 0.60f, 0.56f, fill.a), 0.55f);
            // An edge at the fill's own transparency disappears along with it; the point of the
            // edge is that it is the part you can still see.
            ink.a = Mathf.Max(fill.a, 0.85f);
            return ink;
        }

        /// <summary>The fallback is for the moment before the config is bound, not for a bad value.</summary>
        private static Color Cfg(BepInEx.Configuration.ConfigEntry<Color> entry, Color fallback) =>
            entry != null ? entry.Value : fallback;

        /// <summary>
        /// Rewrite the map: every pixel we want gets its color, its forest cleared and, if it was
        /// under the sea, its height lifted; every pixel we changed before and no longer want gets
        /// all three back exactly as they were; and each picture is uploaded at most once.
        /// </summary>
        private static void Paint(Minimap map, Texture2D tex)
        {
            try
            {
                int size = map.m_textureSize;
                float pixelSize = map.m_pixelSize;
                if (size <= 0 || pixelSize <= 0f) return;

                // Timed because the first paint of a session is the whole picture at once, and
                // this client has been stalled into disconnecting by less. Nothing here waits on
                // it: the number is in the log so that it is a measurement rather than a guess.
                var clock = System.Diagnostics.Stopwatch.StartNew();
                var mask = MaskTexture(map);
                var height = HeightTexture(map);
                bool lift = OverWater();
                float line = WaterLine() + Lift;

                // What stands in each pixel, counted rather than ranked. A pixel is twelve
                // meters across and a cell is four, so nine cells land in one and the honest
                // question is what that square of ground is mostly made of. Taking the hardest
                // material instead -- stone over wood, as this did -- turned a wooden longhouse
                // gray the moment a single stone block stood anywhere inside it, and a Valheim
                // base is wood on stone footings, so that was most pixels of most bases.
                //
                // Halving the cell size is what made it obvious: at eight meters a pixel held
                // about two cells and stone had two chances to win it, at four it holds nine.
                // The rule was always wrong, but it only started showing when the grid got finer
                // than the thing drawing it.
                var tally = new Dictionary<int, Tally>();
                foreach (var kv in _cells)
                {
                    if (!Wants(kv.Value)) continue;
                    foreach (int pixel in PixelsOf(kv.Key, size, pixelSize))
                    {
                        tally.TryGetValue(pixel, out var counts);
                        counts.Add(kv.Value);
                        tally[pixel] = counts;
                    }
                }

                var want = new Dictionary<int, BuiltKind>();
                foreach (var kv in tally)
                {
                    var kind = kv.Value.Winner();
                    if (kind != BuiltKind.None) want[kv.Key] = kind;
                }
                want = Spread(want, size);

                // Worked out once rather than per pixel; a color is parsed from text every time
                // it is asked for.
                //
                // The length comes from the enum and the entries are filled by walking it, because
                // when both were written out by hand they went out of step: two kinds of ground
                // were added, the length stayed at five, and every paint pass threw on the sixth
                // entry before it drew a single pixel. Nothing appeared on the map at all -- not
                // the new ground, not the buildings that had worked for months -- and the only
                // sign of it was one line in the log. Adding a kind now cannot do that again.
                var values = System.Enum.GetValues(typeof(BuiltKind));
                int widest = 0;
                foreach (BuiltKind kind in values) widest = Mathf.Max(widest, (int)kind + 1);
                var paint = new Color32[widest];
                foreach (BuiltKind kind in values) paint[(int)kind] = ColorFor(kind);

                int changed = 0;
                bool colorDirty = false, maskDirty = false, heightDirty = false;
                foreach (var kv in want)
                {
                    var color = paint[(int)kv.Value];
                    // Already standing: the picture is as we want it, so touching it would be a
                    // texture upload for nothing.
                    if (_shown.TryGetValue(kv.Key, out var already) && Same(already, color)) continue;
                    int x = kv.Key % size, y = kv.Key / size;

                    if (!_under.ContainsKey(kv.Key)) _under[kv.Key] = tex.GetPixel(x, y);
                    tex.SetPixel(x, y, color);
                    _shown[kv.Key] = color;
                    colorDirty = true;
                    changed++;

                    // The forest mask drew trees over the color, which in the black forest is
                    // everywhere: the game's own map has no notion of a clearing, so we make one.
                    if (ClearForest(mask, kv.Key, x, y)) maskDirty = true;

                    // And the height is why nothing showed over water: the sea is drawn from the
                    // heights, not the colors, so a dock below the water line was painted and then
                    // the sea was painted over it.
                    if (lift && LiftAboveWater(height, kv.Key, x, y, line)) heightDirty = true;
                }

                // Switched the lifting off: the water comes back although the paint stays.
                if (!lift && height != null && _underHeight.Count > 0)
                {
                    foreach (var kv in _underHeight)
                        height.SetPixel(kv.Key % size, kv.Key / size, new Color(kv.Value, 0f, 0f, 0f));
                    _underHeight.Clear();
                    heightDirty = true;
                }

                var stale = new List<int>();
                foreach (var kv in _under) if (!want.ContainsKey(kv.Key)) stale.Add(kv.Key);
                foreach (int pixel in stale)
                {
                    int x = pixel % size, y = pixel / size;
                    tex.SetPixel(x, y, _under[pixel]);
                    _under.Remove(pixel);
                    _shown.Remove(pixel);
                    colorDirty = true;
                    changed++;
                    if (_underMask.TryGetValue(pixel, out var wasMask))
                    {
                        if (mask != null) { mask.SetPixel(x, y, wasMask); maskDirty = true; }
                        _underMask.Remove(pixel);
                    }
                    if (_underHeight.TryGetValue(pixel, out var wasHeight))
                    {
                        if (height != null) { height.SetPixel(x, y, new Color(wasHeight, 0f, 0f, 0f)); heightDirty = true; }
                        _underHeight.Remove(pixel);
                    }
                }

                if (colorDirty) tex.Apply();
                if (maskDirty && mask != null) mask.Apply();
                if (heightDirty && height != null) height.Apply();
                if (changed > 0)
                {
                    _lastPaintMs = clock.ElapsedMilliseconds;
                    _lastPaintPixels = changed;
                    TheGreatestMapMod.Log.LogDebug(
                        $"[TheGreatestMap] Painted {changed} map pixel(s) in {_lastPaintMs} ms; " +
                        $"{_under.Count} standing.");
                }
            }
            catch (System.Exception e)
            {
                TheGreatestMapMod.Log.LogWarning("[TheGreatestMap] Could not paint the map: " + e.Message);
            }
        }

        /// <summary>The map has no alpha, so two colors are the same when they look the same.</summary>
        private static bool Same(Color32 a, Color32 b) => a.r == b.r && a.g == b.g && a.b == b.b;

        /// <summary>
        /// Grow every painted pixel into the pixels around it.
        ///
        /// <para>A map pixel is about ten meters of world, so a shed is one pixel out of two
        /// thousand, and the game draws that map scaled down and smoothed: a lone pixel is
        /// averaged with the terrain around it until it is a smudge of the wrong color rather than
        /// a building. Painting the ring around it as well costs nothing anybody can see -- the
        /// map is already coarser than the thing it is drawing -- and makes a small place read as
        /// a small place instead of as a stain.</para>
        /// </summary>
        private static Dictionary<int, BuiltKind> Spread(Dictionary<int, BuiltKind> want, int size)
        {
            int reach = SpreadPixels();
            if (reach <= 0 || want.Count == 0) return want;
            var grown = new Dictionary<int, BuiltKind>(want);
            foreach (var kv in want)
            {
                int x = kv.Key % size, y = kv.Key / size;
                for (int dy = -reach; dy <= reach; dy++)
                {
                    int ny = y + dy;
                    if (ny < 0 || ny >= size) continue;
                    for (int dx = -reach; dx <= reach; dx++)
                    {
                        int nx = x + dx;
                        if (nx < 0 || nx >= size) continue;
                        int pixel = ny * size + nx;
                        // What really stands on a pixel beats what spread onto it from next door.
                        if (want.ContainsKey(pixel)) continue;
                        if (grown.TryGetValue(pixel, out var had) && Rank(had) >= Rank(kv.Value)) continue;
                        grown[pixel] = kv.Value;
                    }
                }
            }
            return grown;
        }

        /// <summary>Which wins when two kinds land on one pixel. Nothing subtle: the heavier material.</summary>
        internal static int Rank(BuiltKind kind)
        {
            switch (kind)
            {
                // Marble above stone, because it is the rarer material and a tie between them
                // says more if it is drawn as the one you see less often.
                case BuiltKind.Marble: return 5;
                case BuiltKind.Stone: return 4;
                case BuiltKind.Iron: return 3;
                case BuiltKind.Wood: return 2;
                case BuiltKind.Paved: return 1;
                case BuiltKind.Dirt: return 1;
                case BuiltKind.Farm: return 1;
                default: return 0;
            }
        }

        private static readonly List<int> _pixels = new List<int>();

        /// <summary>
        /// The map pixels a cell covers. Usually one, because a map pixel is larger than a cell,
        /// but the loop is written for either so a finer map would fill in rather than speckle.
        /// </summary>
        private static List<int> PixelsOf(int key, int size, float pixelSize)
        {
            _pixels.Clear();
            var center = Center(key);
            float half = size * 0.5f;
            float x0 = (center.x - CellSize * 0.5f) / pixelSize + half;
            float x1 = (center.x + CellSize * 0.5f) / pixelSize + half;
            float y0 = (center.z - CellSize * 0.5f) / pixelSize + half;
            float y1 = (center.z + CellSize * 0.5f) / pixelSize + half;
            int px0 = Mathf.Clamp(Mathf.FloorToInt(x0), 0, size - 1);
            int px1 = Mathf.Clamp(Mathf.FloorToInt(x1), 0, size - 1);
            int py0 = Mathf.Clamp(Mathf.FloorToInt(y0), 0, size - 1);
            int py1 = Mathf.Clamp(Mathf.FloorToInt(y1), 0, size - 1);
            for (int y = py0; y <= py1; y++)
                for (int x = px0; x <= px1; x++)
                    _pixels.Add(y * size + x);
            return _pixels;
        }

        /// <summary>What the painting knows, for tgm_paint.</summary>
        internal static List<string> Describe()
        {
            var lines = new List<string>();
            var map = Minimap.instance;
            if (map == null) { lines.Add("No map yet."); return lines; }
            lines.Add($"map texture {map.m_textureSize} px across, {map.m_pixelSize:0.#} m per pixel " +
                      $"(a cell is {CellSize:0} m, so about {Mathf.Max(1f, CellSize / map.m_pixelSize):0.0} cell(s) per pixel)");
            lines.Add($"drawing: {(!On() ? "OFF (Server Shares Player Developments, the server's)" : Wanted() ? "on" : "OFF (Show Player Developments)")}, " +
                      $"buildings {(Wants(BuiltKind.Wood) ? "on" : "off")}, roads {(Wants(BuiltKind.Paved) ? "on" : "off")}");
            var counted = new Tally();
            foreach (var kind in _cells.Values) counted.Add(kind);
            lines.Add($"cells from the server: {_cells.Count} (wood {counted.Wood}, stone {counted.Stone}, " +
                      $"iron {counted.Iron}, marble {counted.Marble}, paved {counted.Paved}, " +
                      $"dirt {counted.Dirt}, farm {counted.Farm})");
            lines.Add($"map pixels painted: {_under.Count}, spread {SpreadPixels()} pixel(s) around each" +
                      (_lastPaintPixels > 0 ? $"; last repaint {_lastPaintPixels} pixel(s) in {_lastPaintMs} ms" : ""));
            FindFields();
            lines.Add("forest cleared under the paint: " +
                      (MaskTexture(map) != null && !_maskRefused
                         ? "yes" : "NO -- the map's forest picture could not be written to"));
            lines.Add("lifted above water: " + (!OverWater() ? "off (Paint Over Water)"
                      : HeightTexture(map) != null && !_heightRefused
                        ? $"yes, to {WaterLine() + Lift:0.#} m, for {_underHeight.Count} pixel(s)"
                        : "NO -- the map's height picture could not be written to"));
            return lines;
        }
    }
}
