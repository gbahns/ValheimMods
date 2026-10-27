using System.Collections.Generic;
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
    /// </summary>
    internal static class MapPaint
    {
        /// <summary>
        /// How large a piece of world one recorded cell is, in meters. Smaller than a map pixel
        /// on purpose: the cell grid is what travels over the wire and what the server keeps, and
        /// it should not have to be rebuilt the day the game ships a finer map.
        /// </summary>
        internal const float CellSize = 8f;

        /// <summary>Offset that keeps a cell index positive, so two of them pack into one int.</summary>
        private const int CellBias = 16384;

        internal static int Key(Vector3 world)
        {
            int cx = Mathf.FloorToInt(world.x / CellSize) + CellBias;
            int cz = Mathf.FloorToInt(world.z / CellSize) + CellBias;
            return (cx << 16) | (cz & 0xFFFF);
        }

        internal static Vector3 Center(int key)
        {
            int cx = (key >> 16) - CellBias;
            int cz = (key & 0xFFFF) - CellBias;
            return new Vector3((cx + 0.5f) * CellSize, 0f, (cz + 0.5f) * CellSize);
        }

        /// <summary>
        /// What stands on each cell. Replaced wholesale by the server's picture; nothing here is
        /// saved on the client, because the server is the one that knows the whole world.
        /// </summary>
        private static readonly Dictionary<int, BuiltKind> _cells = new Dictionary<int, BuiltKind>();

        /// <summary>Every map pixel we have changed, and the color it had before we touched it.</summary>
        private static readonly Dictionary<int, Color32> _under = new Dictionary<int, Color32>();

        private static Texture2D _painted;      // the texture _under belongs to
        private static bool _dirty;
        private static float _nextApply;
        private static string _lastColors = "";

        /// <summary>How long after a change before the texture is rewritten. One upload, not one per cell.</summary>
        private const float Settle = 1.5f;

        internal static int CellCount => _cells.Count;
        internal static int PaintedPixels => _under.Count;

        internal static void Reset()
        {
            // Not a restore: on a world change the texture is gone anyway, and reaching into a
            // dead one would be the error this guards against.
            _cells.Clear();
            _under.Clear();
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
            foreach (var kv in cells) _cells[kv.Key] = kv.Value;
            _dirty = true;
            return true;
        }

        internal static void Update()
        {
            var map = Minimap.instance;
            if (map == null) return;
            var tex = map.m_mapTexture;
            if (tex == null) return;

            // The game rebuilt its world map (a world load): whatever we remembered was underneath
            // belongs to a texture that no longer exists, and the new one is clean.
            if (!ReferenceEquals(tex, _painted))
            {
                _under.Clear();
                _painted = tex;
                _dirty = true;
            }

            // A color change is as much a change as a new cell, and costs the same repaint.
            string colors = ColorSignature();
            if (colors != _lastColors) { _lastColors = colors; _dirty = true; }

            if (!_dirty || Time.unscaledTime < _nextApply) return;
            _dirty = false;
            _nextApply = Time.unscaledTime + Settle;
            Paint(map, tex);
        }

        private static string ColorSignature()
        {
            return (Wanted() ? "1" : "0") + "|" + Show(BuiltKind.Wood) + Show(BuiltKind.Stone)
                   + Show(BuiltKind.Iron) + Show(BuiltKind.Paved);
        }

        private static string Show(BuiltKind kind) => Wants(kind) ? ColorFor(kind).ToString() : "-";

        /// <summary>Is any of this to be drawn on this player's map at all?</summary>
        private static bool Wanted() =>
            TgmConfig.ShowMapPaint == null || TgmConfig.ShowMapPaint.Value;

        private static bool Wants(BuiltKind kind)
        {
            if (!Wanted()) return false;
            if (kind == BuiltKind.Paved)
                return TgmConfig.PaintRoads == null || TgmConfig.PaintRoads.Value;
            return TgmConfig.PaintBuildings == null || TgmConfig.PaintBuildings.Value;
        }

        internal static Color ColorFor(BuiltKind kind)
        {
            switch (kind)
            {
                case BuiltKind.Wood:  return Portals.ParseColor(Cfg(TgmConfig.WoodColor), new Color(0.44f, 0.31f, 0.18f));
                case BuiltKind.Stone: return Portals.ParseColor(Cfg(TgmConfig.StoneColor), new Color(0.50f, 0.50f, 0.52f));
                case BuiltKind.Iron:  return Portals.ParseColor(Cfg(TgmConfig.IronColor), new Color(0.33f, 0.35f, 0.40f));
                default:              return Portals.ParseColor(Cfg(TgmConfig.PavedColor), new Color(0.62f, 0.60f, 0.55f));
            }
        }

        private static string Cfg(BepInEx.Configuration.ConfigEntry<string> entry) => entry != null ? entry.Value : null;

        /// <summary>
        /// Rewrite the map texture: every pixel we want colored gets colored, every pixel we
        /// colored before and no longer want gets the color it had back, and the whole thing is
        /// uploaded once.
        /// </summary>
        private static void Paint(Minimap map, Texture2D tex)
        {
            try
            {
                int size = map.m_textureSize;
                float pixelSize = map.m_pixelSize;
                if (size <= 0 || pixelSize <= 0f) return;

                var want = new Dictionary<int, Color>();
                foreach (var kv in _cells)
                {
                    if (!Wants(kv.Value)) continue;
                    var color = ColorFor(kv.Value);
                    foreach (int pixel in PixelsOf(kv.Key, size, pixelSize))
                    {
                        // A building beats a road it stands on, and stone beats the wood beside
                        // it, so one pixel holding several cells says the most solid thing in it.
                        if (want.TryGetValue(pixel, out var had) && Rank(had) >= Rank(color)) continue;
                        want[pixel] = color;
                    }
                }

                int changed = 0;
                foreach (var kv in want)
                {
                    int x = kv.Key % size, y = kv.Key / size;
                    if (!_under.ContainsKey(kv.Key)) _under[kv.Key] = tex.GetPixel(x, y);
                    tex.SetPixel(x, y, kv.Value);
                    changed++;
                }

                var stale = new List<int>();
                foreach (var kv in _under) if (!want.ContainsKey(kv.Key)) stale.Add(kv.Key);
                foreach (int pixel in stale)
                {
                    tex.SetPixel(pixel % size, pixel / size, _under[pixel]);
                    _under.Remove(pixel);
                    changed++;
                }

                if (changed > 0) tex.Apply();
            }
            catch (System.Exception e)
            {
                TheGreatestMapMod.Log.LogWarning("[TheGreatestMap] Could not paint the map: " + e.Message);
            }
        }

        /// <summary>Which wins when two kinds land on one pixel. Nothing subtle: the heavier material.</summary>
        private static int Rank(Color c)
        {
            if (c == ColorFor(BuiltKind.Stone)) return 4;
            if (c == ColorFor(BuiltKind.Iron)) return 3;
            if (c == ColorFor(BuiltKind.Wood)) return 2;
            return 1;
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
            lines.Add($"drawing: {(Wanted() ? "on" : "OFF (Show Map Painting)")}, " +
                      $"buildings {(Wants(BuiltKind.Wood) ? "on" : "off")}, roads {(Wants(BuiltKind.Paved) ? "on" : "off")}");
            int wood = 0, stone = 0, iron = 0, paved = 0;
            foreach (var kind in _cells.Values)
            {
                if (kind == BuiltKind.Wood) wood++;
                else if (kind == BuiltKind.Stone) stone++;
                else if (kind == BuiltKind.Iron) iron++;
                else if (kind == BuiltKind.Paved) paved++;
            }
            lines.Add($"cells from the server: {_cells.Count} (wood {wood}, stone {stone}, iron {iron}, paved {paved})");
            lines.Add($"map pixels painted: {_under.Count}");
            return lines;
        }
    }
}
