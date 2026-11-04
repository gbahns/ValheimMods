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
    /// area over cell area, so what a crowded map costs stops following how crowded it is.</para>
    ///
    /// <para><b>Only markers that are actually under each other.</b> The distance is the drawn
    /// width of that kind's marker, so "too close" means the pictures are touching and nothing
    /// looser. Two things this got wrong when it first shipped, both of which thinned markers
    /// with daylight between them:</para>
    ///
    /// <para>The width is the width this kind is <i>drawn</i> at, not the map's nominal pin size.
    /// Berries, mushrooms, herbs, seeds and plants are drawn at 60% and ore at 80% (see
    /// <c>Catalog.DefaultSize</c>), so measuring in nominal widths made the cell 1.67x too big
    /// for exactly the kinds that come in clumps -- and those are the ones anyone notices.</para>
    ///
    /// <para>And the cell is the distance over root two, floor-snapped to the ladder and never
    /// rounded up, because two markers in one cell can be a diagonal apart. A cell equal to the
    /// distance held markers back at up to 1.41x it; one rounded up the ladder, up to 1.22x again
    /// on top. The two together reached 5.7 drawn widths at the old default -- markers a clear
    /// five icons apart, one of them gone. Erring small instead means some touching pairs in
    /// neighboring cells both draw, which is the harmless direction: the cap bounds the cost, and
    /// nothing is ever held back that is not genuinely underneath something.</para>
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

        private const float Sqrt2 = 1.41421356f;

        private static readonly Dictionary<Spot, int> _taken = new Dictionary<Spot, int>();
        private static readonly Dictionary<int, List<int>> _byKind = new Dictionary<int, List<int>>();
        private static readonly Dictionary<int, float> _cellByKind = new Dictionary<int, float>();

        /// <summary>What the last pass did, for tgm_status. Nothing reads these but the report.</summary>
        internal static float LastCell { get; private set; }
        internal static int LastConsidered { get; private set; }
        internal static int LastHeld { get; private set; }
        internal static int LastCapped { get; private set; }
        internal static int LastStep { get; private set; }

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

            float width = MetersPerWidth(map);
            if (width <= 0f) return 0;

            // Nothing exempt is ever held back, so what is left standing is simple subtraction.
            int most = TgmConfig.MostMarkersDrawn != null ? TgmConfig.MostMarkersDrawn.Value : 0;
            int step = 0;
            int held = Sweep(shown, width, step);
            // Too many still standing: widen the patch and sweep again, rather than picking off
            // markers one at a time. Every patch keeps one of each kind in it however wide it
            // gets, so a lone stand of four mushrooms in a swamp keeps its marker no matter how
            // hard the limit bites -- where giving up individuals would take all four and leave
            // the swamp looking as though nobody had ever found a mushroom in it.
            //
            // The width is reckoned rather than climbed to. A sweep costs about a microsecond a
            // marker, and a ladder of twenty-seven steps climbed one at a time is a stall on a
            // map that can never come under the limit at all -- which is any map carrying more
            // markers nothing may thin than the limit allows. Each step is half again as wide,
            // so it thins by about the square of that; guess from how far over we are, then
            // correct, and give up after a few corrections and leave the rest to the backstop.
            if (most > 0 && shown.Count - held > most)
            {
                step = Mathf.Clamp(
                    Mathf.CeilToInt(Mathf.Log((float)(shown.Count - held) / most) / Mathf.Log(2.25f)),
                    1, Steps.Length - 1);
                held = Sweep(shown, width, step);
                for (int tries = 0; tries < 3 && shown.Count - held > most && step < Steps.Length - 1; tries++)
                {
                    step++;
                    held = Sweep(shown, width, step);
                }
            }
            LastStep = step;
            LastCell = CellFor(width, 1f, step);
            LastHeld = held;
            // The backstop's backstop: markers nothing may hold back -- near you, just recorded,
            // asked for by name -- can outnumber the limit on their own, and no width fixes that.
            LastCapped = Cap(shown, shown.Count - held);
            return held + LastCapped;
        }

        /// <summary>
        /// One pass of the patch rule at <paramref name="stepUp"/> steps wider than the markers
        /// themselves ask for. Returns how many were held back.
        /// </summary>
        private static int Sweep(List<Drawn> shown, float width, int stepUp)
        {
            _taken.Clear();
            _cellByKind.Clear();
            int held = 0;
            for (int i = 0; i < shown.Count; i++)
            {
                var mine = shown[i];
                // A wider sweep starts from a clean slate, since a marker held back by a narrow
                // patch may well be the one that wins a wide one.
                if (mine.Held) { mine.Held = false; shown[i] = mine; }
                if (mine.Always) continue;
                // Each kind gets its own cell, sized to the width that kind is drawn at. The
                // grid is already per kind, so this costs nothing structurally.
                float cell = CellOf(mine.Kind, width, stepUp);
                if (cell <= 0f) continue;
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
            return held;
        }

        /// <summary>
        /// How many world meters one full-size marker covers at the current zoom. Zero when there
        /// is nothing to work from.
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
        internal static float MetersPerWidth(Minimap map)
        {
            if (map == null) return 0f;
            bool large = map.m_mode == Minimap.MapMode.Large;
            var img = large ? map.m_mapImageLarge : map.m_mapImageSmall;
            if (img == null || img.rectTransform == null) return 0f;
            float across = img.rectTransform.rect.width;
            float uv = img.uvRect.width;
            if (across <= 1f || uv <= 0f) return 0f;
            float metersPerUnit = map.m_pixelSize * map.m_textureSize * uv / across;
            float marker = large ? map.m_pinSizeLarge : map.m_pinSizeSmall;
            if (marker <= 0f) marker = 48f;
            return marker * metersPerUnit;
        }

        /// <summary>The cell for one kind, worked out once per kind per pass.</summary>
        private static float CellOf(Category? kind, float width, int stepUp)
        {
            int key = kind.HasValue ? (int)kind.Value : -1;
            if (_cellByKind.TryGetValue(key, out float known)) return known;
            float cell = CellFor(width, KindScale(kind), stepUp);
            _cellByKind[key] = cell;
            return cell;
        }

        /// <summary>
        /// The cell in world meters for a marker drawn at <paramref name="scale"/> of full size:
        /// the spacing in that marker's own widths, over root two so a cell's diagonal -- the
        /// farthest apart two markers in one cell can be -- is the spacing itself and not more.
        /// </summary>
        private static float CellFor(float width, float scale, int stepUp)
        {
            float widths = TgmConfig.ThinSpacing != null ? TgmConfig.ThinSpacing.Value : 1f;
            if (width <= 0f || widths <= 0f || scale <= 0f) return 0f;
            return Floor(widths * width * scale / Sqrt2, stepUp);
        }

        /// <summary>
        /// How large this kind's markers are drawn, as a fraction of full size. The per-kind
        /// setting only: the per-icon tweak varies within a kind, and a grid whose cell changed
        /// from one marker to the next would not be a grid.
        /// </summary>
        private static float KindScale(Category? kind)
        {
            if (!kind.HasValue || TgmConfig.MarkerSize == null) return 1f;
            return TgmConfig.MarkerSize.TryGetValue(kind.Value, out var size)
                ? Mathf.Clamp(size.Value, 20, 100) / 100f
                : 1f;
        }

        /// <summary>
        /// The largest step at or below <paramref name="want"/>. Down and never up, so a snapped
        /// cell can only hold markers back that are closer than the spacing asked for, never
        /// farther. The ladder is there so zooming changes who survives in jumps rather than
        /// continuously.
        /// </summary>
        private static float Floor(float want, int stepUp)
        {
            int at = 0;
            for (int i = Steps.Length - 1; i >= 0; i--) if (Steps[i] <= want) { at = i; break; }
            return Steps[Mathf.Min(at + stepUp, Steps.Length - 1)];
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
        private static int Cap(List<Drawn> shown, int drawn)
        {
            int most = TgmConfig.MostMarkersDrawn != null ? TgmConfig.MostMarkersDrawn.Value : 0;
            if (most <= 0 || drawn <= most) return 0;

            // The lists are kept and emptied rather than made again: this runs on every layout of
            // the map, which on the minimap is every step the player takes.
            foreach (var kv in _byKind) kv.Value.Clear();
            for (int i = 0; i < shown.Count; i++)
            {
                var one = shown[i];
                if (one.Held) continue;
                if (one.Always) continue;        // counts toward the total, cannot be given up
                int kind = one.Kind.HasValue ? (int)one.Kind.Value : -1;
                List<int> bucket;
                if (!_byKind.TryGetValue(kind, out bucket)) _byKind[kind] = bucket = new List<int>();
                bucket.Add(i);
            }

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
        /// and a scatter of the key decides the rest. It has to be decided the same way every time
        /// the map is laid out, or a cluster would shimmer between its members as the map moved,
        /// and nothing else about two berry bushes distinguishes them.
        ///
        /// <para><b>Not the key itself.</b> <c>PinKey.CompareTo</c> orders by owner and then by
        /// x, so comparing keys meant the westernmost marker won every cell and the easternmost
        /// was the first the cap gave up. Every survivor then sat against the west wall of its
        /// cell, and with a large cell a whole kind could disappear from the part of the map you
        /// were looking at while the same kind drew normally a cell to the west. Mixing the hash
        /// keeps the decision fixed per marker and makes it independent of where the marker is,
        /// so what survives is spread over the ground rather than lined up along it.</para>
        /// </summary>
        private static int Compare(Drawn a, Drawn b)
        {
            bool aDone = a.Shared.Checked || a.Pin.m_checked;
            bool bDone = b.Shared.Checked || b.Pin.m_checked;
            if (aDone != bDone) return aDone ? 1 : -1;
            int ha = Scatter(a.Shared), hb = Scatter(b.Shared);
            if (ha != hb) return ha < hb ? -1 : 1;
            // Two keys that scatter alike still have to be ordered, or neither sort nor cell is
            // stable. Vanishingly rare, and position is a fine tiebreak once it is not the rule.
            return a.Shared.Key.CompareTo(b.Shared.Key);
        }

        /// <summary>
        /// A marker's place in the pecking order: its key's hash, stirred. The key's own hash is
        /// precomputed and free to read, but neighboring markers can land in neighboring buckets,
        /// and the stirring is what makes "lowest wins" mean nothing about where a marker is.
        /// </summary>
        private static int Scatter(SharedPin pin)
        {
            unchecked
            {
                int h = pin.Key.GetHashCode();
                h ^= h >> 16; h *= (int)0x7feb352d;
                h ^= h >> 15; h *= (int)0x846ca68b;
                h ^= h >> 16;
                return h;
            }
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
            return $"Thinning overlapping markers: {LastCell:0.#} m cells at full marker size " +
                   $"(smaller for the kinds drawn smaller" +
                   (LastStep > 0 ? $", widened {LastStep} step(s) to fit the limit of " +
                                   $"{TgmConfig.MostMarkersDrawn.Value}" : "") + "), " +
                   $"{drawn} of {LastConsidered} drawn " +
                   $"({LastHeld} standing on one of their own kind" +
                   (LastCapped > 0 ? $", {LastCapped} over the limit of {TgmConfig.MostMarkersDrawn.Value}" : "") + ").";
        }
    }
}
