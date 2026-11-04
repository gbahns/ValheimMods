using System.Collections.Generic;
using UnityEngine;

namespace TheGreatestMap
{
    /// <summary>
    /// One marker the styling pass has decided to draw, handed to <see cref="Thinning"/> so it can
    /// say which of them are standing on top of each other.
    /// </summary>
    internal struct Drawn
    {
        public Minimap.PinData Pin;
        public SharedPin Shared;
        public Category? Kind;

        /// <summary>
        /// Never thinned, whatever else is near it. These are the markers something asked for by
        /// name: one close enough to the player to be part of the honest near map, one just
        /// recorded, and one answering tgm_locate. Thinning is about redundancy, and a marker
        /// somebody asked to see is not redundant.
        /// </summary>
        public bool Always;

        /// <summary>Set by <see cref="Thinning"/>: another marker of this kind has this patch.</summary>
        public bool Held;
    }

    /// <summary>
    /// Draw one marker of each kind per patch of ground rather than every one of them.
    ///
    /// <para><b>Why.</b> Measured solo in GeniusWorld with 2,939 markers and the large map open:
    /// 13.9 fps with everything shown, 27.1 fps with every marker of ours hidden. 85% of the 35 ms
    /// recovered is in Unity's canvas pass -- layout, batching and draw over the active marker
    /// objects -- which no patch can see or time, and which costs about 10 microseconds per drawn
    /// marker per frame against 1.66 for this mod's styling and 0.54 for its decision chain, both
    /// of which run only when the map is laid out again. So the canvas is twelve times everything
    /// the mod does, and the only input it responds to is how many markers are active. There is no
    /// culling bug behind this and nothing being wasted: vanilla destroys the marker of any pin
    /// outside the visible window, and every marker drawn was genuinely on screen. The map is slow
    /// because it is asked to draw three thousand markers and it draws them.</para>
    ///
    /// <para><b>Why thinning rather than hiding more.</b> Zoomed out, berries and mushrooms and
    /// tin overlap into mush -- the same marker forty times over one meadow. Hiding a kind loses
    /// "there is tin here"; thinning keeps one of each cluster, so only the repetition goes. And
    /// it bounds the cost by screen area instead of by world size: drawn markers become screen
    /// area over cell area, so a map with 7,266 markers on it costs what one with 2,939 costs.
    /// On 5120x1440 with cells two marker widths across that is about 795 markers at the most,
    /// 8 ms of canvas a frame instead of 30 -- and a smaller screen caps lower by itself.</para>
    ///
    /// <para><b>The grid is in world meters, not on the screen.</b> Cell size is worked out from
    /// the zoom, but the cells themselves sit on fixed world lines, so panning does not move them
    /// and survivors do not flicker as the map is dragged. The size is snapped to a ladder of
    /// steps for the same reason in the other direction: zooming changes who survives in jumps
    /// rather than continuously.</para>
    /// </summary>
    internal static class Thinning
    {
        /// <summary>
        /// Cell sizes in meters. Every step is about half again the one before, so a cell is never
        /// more than a quarter off the size the zoom asked for, and there are few enough of them
        /// that zooming settles on one and stays there.
        /// </summary>
        private static readonly float[] Steps =
        {
            1f, 1.5f, 2f, 3f, 4f, 6f, 8f, 12f, 16f, 24f, 32f, 48f, 64f, 96f, 128f, 192f,
            256f, 384f, 512f, 768f, 1024f, 1536f, 2048f, 3072f, 4096f, 6144f, 8192f,
        };

        private static readonly Dictionary<Spot, int> _taken = new Dictionary<Spot, int>();
        private static readonly Dictionary<int, List<int>> _byKind = new Dictionary<int, List<int>>();

        /// <summary>What the last pass did, for tgm_status. Nothing reads these but the report.</summary>
        internal static float LastCell { get; private set; }
        internal static int LastConsidered { get; private set; }
        internal static int LastHeld { get; private set; }
        internal static int LastCapped { get; private set; }

        private static bool On => TgmConfig.ThinMarkers == null || TgmConfig.ThinMarkers.Value;

        /// <summary>
        /// Mark the markers of <paramref name="shown"/> that are standing where one of their own
        /// kind already stands. Returns how many were held back.
        /// </summary>
        internal static int Hold(Minimap map, List<Drawn> shown)
        {
            LastConsidered = shown.Count;
            LastHeld = 0;
            LastCapped = 0;
            LastCell = 0f;
            if (!On || shown.Count == 0) return 0;

            float cell = CellSize(map);
            LastCell = cell;
            int held = 0;
            if (cell > 0f)
            {
                _taken.Clear();
                for (int i = 0; i < shown.Count; i++)
                {
                    var mine = shown[i];
                    if (mine.Always) continue;
                    // Floor, so the lines fall on multiples of the cell size in world space and
                    // stay where they are however the map is panned.
                    var spot = new Spot(mine.Kind,
                                        Mathf.FloorToInt(mine.Shared.Pos.x / cell),
                                        Mathf.FloorToInt(mine.Shared.Pos.z / cell));
                    int sitting;
                    if (!_taken.TryGetValue(spot, out sitting)) { _taken[spot] = i; continue; }
                    // One of the two goes. Not whichever came first: the same marker has to win
                    // every time the map is laid out again, or the cluster would shimmer between
                    // its members as the dictionary happened to be walked.
                    int loser = Compare(mine, shown[sitting]) < 0 ? sitting : i;
                    if (loser == sitting) _taken[spot] = i;
                    Keep(shown, loser);
                    held++;
                }
            }
            LastHeld = held;
            LastCapped = Cap(shown);
            return held + LastCapped;
        }

        /// <summary>
        /// A cell in world meters: the configured spacing in marker widths, turned into meters at
        /// the current zoom, then snapped to the ladder. Zero when there is nothing to work from or
        /// the spacing is off.
        ///
        /// <para>The chain from a marker's position to its place on screen is
        /// <c>WorldToMapPoint</c> (meters to texture coordinates, dividing by
        /// <c>m_pixelSize * m_textureSize</c>) and then <c>MapPointToLocalGuiPos</c> (texture
        /// coordinates to canvas units, multiplying by <c>rect.width / uvRect.width</c>). So
        /// meters per canvas unit is the first over the second, and marker sizes are in those same
        /// canvas units -- which is why the spacing is in marker widths and needs nothing about
        /// the screen's resolution. The same figure holds vertically: <c>CenterMap</c> sets
        /// <c>uvRect.width = LargeZoom * (rect.width / rect.height)</c> against
        /// <c>uvRect.height = LargeZoom</c>, so both axes come to <c>LargeZoom / rect.height</c>.
        /// An ultrawide screen therefore sees twice the world at a given zoom and gets twice the
        /// cells to put markers in, which is the right answer rather than a double helping of
        /// markers in the same space.</para>
        /// </summary>
        internal static float CellSize(Minimap map)
        {
            if (map == null) return 0f;
            float widths = TgmConfig.ThinSpacing != null ? TgmConfig.ThinSpacing.Value : 2f;
            if (widths <= 0f) return 0f;
            bool large = map.m_mode == Minimap.MapMode.Large;
            var img = large ? map.m_mapImageLarge : map.m_mapImageSmall;
            if (img == null || img.rectTransform == null) return 0f;
            float across = img.rectTransform.rect.width;
            float uv = img.uvRect.width;
            if (across <= 1f || uv <= 0f) return 0f;
            float metersPerUnit = map.m_pixelSize * map.m_textureSize * uv / across;
            float marker = large ? map.m_pinSizeLarge : map.m_pinSizeSmall;
            if (marker <= 0f) marker = 48f;
            return Snap(widths * marker * metersPerUnit);
        }

        /// <summary>The nearest step to <paramref name="want"/>, nearest by ratio rather than by
        /// difference, since the steps grow by a ratio.</summary>
        private static float Snap(float want)
        {
            if (want <= Steps[0]) return Steps[0];
            for (int i = 1; i < Steps.Length; i++)
            {
                if (want >= Steps[i]) continue;
                // Below the geometric mean of the two steps, the lower one is the nearer.
                return want * want <= Steps[i - 1] * Steps[i] ? Steps[i - 1] : Steps[i];
            }
            return Steps[Steps.Length - 1];
        }

        /// <summary>
        /// Hold markers back until no more than the configured number are drawn, taking them from
        /// whichever kind has the most still standing.
        ///
        /// <para>A cell holds one of <i>each</i> kind, which is the point -- "tin and berries are
        /// both here" is worth more than either on its own. The cost is that enough co-located
        /// kinds can multiply the cell count back up, and this is the backstop for that. Taking
        /// from the largest kind each time is the same idea as the cell rule: the more plentiful a
        /// kind, the less any one of its locations says, and evening the kinds out this way keeps
        /// some of all of them rather than erasing the commonest outright.</para>
        /// </summary>
        private static int Cap(List<Drawn> shown)
        {
            int most = TgmConfig.MostMarkersDrawn != null ? TgmConfig.MostMarkersDrawn.Value : 0;
            if (most <= 0) return 0;

            // The lists are kept and emptied rather than made again: this runs on every layout of
            // the map, which on the minimap is every step the player takes.
            foreach (var kv in _byKind) kv.Value.Clear();
            int drawn = 0;
            for (int i = 0; i < shown.Count; i++)
            {
                var one = shown[i];
                if (one.Held) continue;
                drawn++;
                if (one.Always) continue;        // counts toward the total, cannot be given up
                int kind = one.Kind.HasValue ? (int)one.Kind.Value : -1;
                List<int> bucket;
                if (!_byKind.TryGetValue(kind, out bucket)) _byKind[kind] = bucket = new List<int>();
                bucket.Add(i);
            }
            if (drawn <= most) return 0;

            _sorting = shown;
            foreach (var kv in _byKind) kv.Value.Sort(_bestFirst);

            int over = drawn - most;
            int held = 0;
            while (held < over)
            {
                List<int> biggest = null;
                foreach (var kv in _byKind)
                    if (kv.Value.Count > 0 && (biggest == null || kv.Value.Count > biggest.Count))
                        biggest = kv.Value;
                if (biggest == null) break;      // everything left is exempt
                Keep(shown, biggest[biggest.Count - 1]);
                biggest.RemoveAt(biggest.Count - 1);
                held++;
            }
            _sorting = null;
            return held;
        }

        private static void Keep(List<Drawn> shown, int at)
        {
            var one = shown[at];
            one.Held = true;
            shown[at] = one;
        }

        /// <summary>
        /// Which of two markers is the one worth drawing: one still to do beats one crossed off,
        /// and the key decides the rest. The key because it has to be decided the same way every
        /// time by every machine, and nothing else about two berry bushes distinguishes them.
        /// </summary>
        private static int Compare(Drawn a, Drawn b)
        {
            bool aDone = a.Shared.Checked || a.Pin.m_checked;
            bool bDone = b.Shared.Checked || b.Pin.m_checked;
            if (aDone != bDone) return aDone ? 1 : -1;
            return a.Shared.Key.CompareTo(b.Shared.Key);
        }

        // Held in a field so the comparison can be made once rather than closing over the list on
        // every pass that reaches the cap.
        private static List<Drawn> _sorting;
        private static readonly System.Comparison<int> _bestFirst =
            (x, y) => Compare(_sorting[x], _sorting[y]);

        /// <summary>A patch of ground, for one kind. Equatable so the dictionary does not box it.</summary>
        private struct Spot : System.IEquatable<Spot>
        {
            private readonly int _kind, _x, _z;

            internal Spot(Category? kind, int x, int z)
            {
                _kind = kind.HasValue ? (int)kind.Value : -1;
                _x = x;
                _z = z;
            }

            public bool Equals(Spot other) => _kind == other._kind && _x == other._x && _z == other._z;
            public override bool Equals(object other) => other is Spot && Equals((Spot)other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int h = _kind;
                    h = (h * 397) ^ _x;
                    h = (h * 397) ^ _z;
                    return h;
                }
            }
        }

        /// <summary>What the last pass did, in words, for tgm_status.</summary>
        internal static string Describe()
        {
            if (TgmConfig.ThinMarkers != null && !TgmConfig.ThinMarkers.Value)
                return "Thinning overlapping markers: off.";
            if (LastCell <= 0f)
                return "Thinning overlapping markers: on, but the map has not been drawn yet.";
            int drawn = LastConsidered - LastHeld - LastCapped;
            return $"Thinning overlapping markers: {LastCell:0.#} m cells, {drawn} of {LastConsidered} drawn " +
                   $"({LastHeld} standing on one of their own kind" +
                   (LastCapped > 0 ? $", {LastCapped} over the limit of {TgmConfig.MostMarkersDrawn.Value}" : "") + ").";
        }
    }
}
