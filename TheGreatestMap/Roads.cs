using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TheGreatestMap
{
    /// <summary>
    /// Finding the roads players have paved.
    ///
    /// <para>There is no list of roads to ask the world for. A paved road is paint on the
    /// terrain, and terrain only exists where it is loaded, so the only honest way to learn
    /// about one is for somebody to be standing near it. That is the same bargain the crop sweep
    /// makes, and it works for the same reason: a road exists to be walked on, so a road anybody
    /// uses is found the first time they use it, and a road nobody uses was never worth drawing.
    /// </para>
    ///
    /// <para>Each client samples the ground around itself a few dozen cells at a time -- reading
    /// terrain is the expensive part, which is why it is rationed rather than done in one sweep
    /// -- and tells the server about the paved ones. The server keeps them for everybody and
    /// saves them beside the map, so a road is learned once by whoever walked it and is on
    /// everyone else's map from then on.</para>
    ///
    /// <para>Cells already asked about are not asked again for a while, because the answer
    /// almost never changes; the "while" exists so that paving a new stretch shows up without
    /// anyone relogging.</para>
    /// </summary>
    internal static class Roads
    {
        /// <summary>How far around the player the ground is sampled. Terrain is loaded well past this.</summary>
        private const float Reach = 64f;

        private const float EverySeconds = 2f;

        /// <summary>Terrain readings per pass. Each one is a heightmap lookup and a texture read.</summary>
        private const int PerPass = 48;

        /// <summary>How long before ground already asked about is asked again, so new paving appears.</summary>
        private const float ForgetAfterSeconds = 300f;

        private static readonly HashSet<int> _asked = new HashSet<int>();
        private static readonly List<int> _found = new List<int>();
        private static float _next;
        private static float _forget;
        private static int _cursor;
        private static int _paved;

        /// <summary>The ring of cell offsets to try, nearest first, built once.</summary>
        private static int[] _offsets;

        internal static void Reset()
        {
            _asked.Clear();
            _found.Clear();
            _next = 0f;
            _forget = 0f;
            _cursor = 0;
            _paved = 0;
        }

        private static int[] Offsets()
        {
            if (_offsets != null) return _offsets;
            int span = Mathf.CeilToInt(Reach / MapPaint.CellSize);
            var list = new List<int>();
            for (int dz = -span; dz <= span; dz++)
                for (int dx = -span; dx <= span; dx++)
                    if (dx * dx + dz * dz <= span * span)
                        list.Add((dx << 16) | (dz & 0xFFFF));
            // Nearest first, so walking a road finds the road rather than the field beside it.
            list.Sort((a, b) => Near(a).CompareTo(Near(b)));
            _offsets = list.ToArray();
            return _offsets;
        }

        private static int Near(int packed)
        {
            int dx = packed >> 16, dz = (short)(packed & 0xFFFF);
            return dx * dx + dz * dz;
        }

        internal static void Update(Action<List<int>> report)
        {
            if (TgmConfig.PaintRoads != null && !TgmConfig.PaintRoads.Value) return;
            if (Time.time < _next) return;
            _next = Time.time + EverySeconds;

            var player = Player.m_localPlayer;
            if (player == null || player.InInterior()) return;

            if (Time.time >= _forget)
            {
                _forget = Time.time + ForgetAfterSeconds;
                _asked.Clear();
            }

            var here = player.transform.position;
            int baseX = Mathf.FloorToInt(here.x / MapPaint.CellSize);
            int baseZ = Mathf.FloorToInt(here.z / MapPaint.CellSize);
            var offsets = Offsets();
            _found.Clear();

            for (int done = 0; done < PerPass && offsets.Length > 0; done++)
            {
                int packed = offsets[_cursor % offsets.Length];
                _cursor++;
                int dx = packed >> 16, dz = (short)(packed & 0xFFFF);
                var at = new Vector3((baseX + dx + 0.5f) * MapPaint.CellSize, here.y,
                                     (baseZ + dz + 0.5f) * MapPaint.CellSize);
                int key = MapPaint.Key(at);
                if (!_asked.Add(key)) continue;
                if (IsPaved(at)) { _found.Add(key); _paved++; }
            }

            if (_found.Count > 0 && report != null) report(new List<int>(_found));
        }

        /// <summary>
        /// Is this ground paved? The game paints terrain with one of six colors and paved is the
        /// blue one, so the mask answers directly -- no guessing from what is built nearby, which
        /// is how a mod ends up calling somebody's courtyard a road.
        /// </summary>
        private static bool IsPaved(Vector3 at)
        {
            try
            {
                var ground = Heightmap.FindHeightmap(at);
                if (ground == null) return false;
                var paint = ground.GetPaintMask(at);
                return paint.b > 0.5f && paint.b > paint.g && paint.b > paint.r;
            }
            catch (Exception) { return false; }
        }

        internal static List<string> Describe()
        {
            var lines = new List<string>();
            if (TgmConfig.PaintRoads != null && !TgmConfig.PaintRoads.Value)
            {
                lines.Add("roads: off (Paint Paved Roads)");
                return lines;
            }
            var player = Player.m_localPlayer;
            lines.Add($"roads: sampling {PerPass} cell(s) every {EverySeconds:0.#} s out to {Reach:0} m; " +
                      $"{_asked.Count} asked about since the last forget, {_paved} paved found this session");
            if (player != null)
                lines.Add("  ground under you: " + (IsPaved(player.transform.position) ? "PAVED" : "not paved"));
            return lines;
        }
    }

    /// <summary>
    /// The paved ground the server has been told about, kept beside the map.
    ///
    /// <para>A file of its own rather than part of the map, for the reason the acknowledgement
    /// ledger has one: no client ever receives this file, nothing merges it, and a map carried
    /// to another server should not bring another world's roads. Losing it costs nothing but the
    /// walking it took to learn, which happens again by itself.</para>
    /// </summary>
    internal static class PavedStore
    {
        internal static bool Dirty;

        private static string _path;
        private static float _nextSave;

        internal static void Load(string mapPath)
        {
            _path = null;
            Dirty = false;
            if (string.IsNullOrEmpty(mapPath)) return;
            // Only the server keeps this. A client has a personal map file in the same folder,
            // and writing a roads file beside it would be a second copy of something the client
            // never reads -- its roads come down the wire with everything else.
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            _path = mapPath + ".roads";
            if (!File.Exists(_path)) return;
            try
            {
                var bytes = File.ReadAllBytes(_path);
                var cells = new List<int>(bytes.Length / 4);
                for (int i = 0; i + 3 < bytes.Length; i += 4) cells.Add(BitConverter.ToInt32(bytes, i));
                Builds.LoadPaved(cells);
                TheGreatestMapMod.Log.LogInfo($"[TheGreatestMap] {cells.Count} paved cell(s) of road read from {_path}.");
            }
            catch (Exception e)
            {
                TheGreatestMapMod.Log.LogWarning(
                    "[TheGreatestMap] Could not read the paved ground (" + e.Message +
                    "); it will be learned again as people walk. No markers are affected.");
            }
        }

        internal static void Update()
        {
            if (!Dirty || string.IsNullOrEmpty(_path) || Time.unscaledTime < _nextSave) return;
            _nextSave = Time.unscaledTime + 30f;
            Save();
        }

        internal static void Save()
        {
            if (!Dirty || string.IsNullOrEmpty(_path)) return;
            try
            {
                var cells = Builds.PavedCells;
                var bytes = new byte[cells.Count * 4];
                int i = 0;
                foreach (int key in cells) { Buffer.BlockCopy(BitConverter.GetBytes(key), 0, bytes, i, 4); i += 4; }
                File.WriteAllBytes(_path, bytes);
                Dirty = false;
            }
            catch (Exception e)
            {
                TheGreatestMapMod.Log.LogWarning("[TheGreatestMap] Could not write " + _path + ": " + e.Message);
            }
        }
    }
}
