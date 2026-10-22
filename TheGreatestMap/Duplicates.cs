using System.Collections.Generic;
using UnityEngine;

namespace TheGreatestMap
{
    /// <summary>
    /// Two markers on one thing.
    ///
    /// Recording keeps a marker from being written where one of the same icon already stands, but
    /// that test is made against your own map at the moment of writing. It cannot speak for a map
    /// you have never seen: if another player recorded the same frost cave before your maps met,
    /// you each wrote one honestly, and the merge keeps both -- they are different markers by id,
    /// and nothing downstream looks at where they are. The pair then sits one on top of the other,
    /// and a player crossing one off is left with a bright marker wearing a red cross, because the
    /// cross belongs to the one underneath.
    ///
    /// Folding them has to reach the same answer on every map, or two clients each keep a
    /// different one and erase the other's, and the pair becomes none. So the survivor is chosen
    /// by something every map agrees on -- the lowest id -- rather than by anything local like
    /// which arrived first. What the losers knew is carried over first: crossed off if any of them
    /// was, and a name if the survivor has none.
    /// </summary>
    internal static class Duplicates
    {
        /// <summary>
        /// Markers of the same icon closer together than that kind's own spacing -- the same
        /// measure recording uses to decide a marker is already there.
        /// </summary>
        private static float SpacingFor(SharedPin pin)
        {
            var kind = ClientPins.KindOf(pin);
            if (kind.HasValue && TgmConfig.MarkerSpacing.TryGetValue(kind.Value, out var entry))
                return Mathf.Max(1f, entry.Value);
            return 5f;
        }

        internal sealed class Group
        {
            internal SharedPin Keep;
            internal readonly List<SharedPin> Drop = new List<SharedPin>();

            /// <summary>The whole cluster, survivor first.</summary>
            internal List<SharedPin> Members
            {
                get { var all = new List<SharedPin> { Keep }; all.AddRange(Drop); return all; }
            }
        }

        /// <summary>
        /// Two markers that are one thing. The same icon, close enough together, is not quite
        /// enough: a name is something a player wrote by hand, and two names side by side are two
        /// things somebody meant to tell apart, however near they sit. A name against a blank is
        /// still one thing -- one of them was recorded and the other written about the same spot,
        /// and folding carries the name onto the survivor rather than losing it.
        /// </summary>
        private static bool SameThing(SharedPin a, SharedPin b)
        {
            string x = (a.Name ?? "").Trim();
            string y = (b.Name ?? "").Trim();
            if (x.Length == 0 || y.Length == 0) return true;
            return string.Equals(x, y, System.StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Markers of one icon, filed by where they are, so a marker is only ever compared with
        /// the handful standing near it.
        ///
        /// Comparing every marker with every other marker of the same icon is what this used to
        /// do, and on a map that has been shared for a month it is ruinous: seven hundred
        /// blueberry bushes is half a million comparisons, each one resolving a marker's kind to
        /// ask how close is too close, and every merge ran the lot. Two players comparing maps in
        /// the field watched the game stop for fifteen seconds.
        ///
        /// The cells are as wide as the furthest two markers of this icon could be and still
        /// count as one thing, so everything near enough to matter is in this cell or one of the
        /// eight around it, and nothing else need be looked at.
        /// </summary>
        private sealed class Grid
        {
            internal readonly Dictionary<long, List<SharedPin>> Cells = new Dictionary<long, List<SharedPin>>();
            internal float Size = 5f;

            internal long KeyOf(Vector3 pos) =>
                ((long)Mathf.FloorToInt(pos.x / Size) << 32) ^ (uint)Mathf.FloorToInt(pos.z / Size);

            internal void Add(SharedPin pin)
            {
                long key = KeyOf(pin.Pos);
                if (!Cells.TryGetValue(key, out var list)) Cells[key] = list = new List<SharedPin>();
                list.Add(pin);
            }

            /// <summary>Everything in this marker's cell and the eight around it.</summary>
            internal IEnumerable<SharedPin> Near(Vector3 pos)
            {
                int cx = Mathf.FloorToInt(pos.x / Size), cz = Mathf.FloorToInt(pos.z / Size);
                for (int dx = -1; dx <= 1; dx++)
                    for (int dz = -1; dz <= 1; dz++)
                        if (Cells.TryGetValue(((long)(cx + dx) << 32) ^ (uint)(cz + dz), out var list))
                            foreach (var pin in list) yield return pin;
            }
        }

        /// <summary>Every cluster of markers standing on one another, without changing anything.</summary>
        internal static List<Group> Find()
        {
            var groups = new List<Group>();
            var byIcon = new Dictionary<string, List<SharedPin>>(System.StringComparer.OrdinalIgnoreCase);
            var spacingOf = new Dictionary<PinKey, float>();
            foreach (var pin in ClientPins.All)
            {
                string icon = IconRegistry.Normalize(ClientPins.IconOf(pin)) ?? "";
                if (!byIcon.TryGetValue(icon, out var list)) byIcon[icon] = list = new List<SharedPin>();
                list.Add(pin);
                // Once per marker, not once per comparison: working out a marker's kind is a
                // dictionary lookup and an inference, and it was being done inside the inner loop.
                spacingOf[pin.Key] = SpacingFor(pin);
            }

            foreach (var kv in byIcon)
            {
                var pins = kv.Value;
                if (pins.Count < 2) continue;
                var grid = new Grid();
                foreach (var pin in pins) { float s = spacingOf[pin.Key]; if (s > grid.Size) grid.Size = s; }
                foreach (var pin in pins) grid.Add(pin);
                var taken = new HashSet<PinKey>();
                foreach (var pin in pins)
                {
                    if (!taken.Add(pin.Key)) continue;
                    Group group = null;
                    float spacing = spacingOf[pin.Key];
                    foreach (var other in grid.Near(pin.Pos))
                    {
                        if (other == pin || taken.Contains(other.Key)) continue;
                        if (Geo.FlatDistance(pin.Pos, other.Pos) > Mathf.Max(spacing, spacingOf[other.Key])) continue;
                        if (!SameThing(pin, other)) continue;
                        // Everything in a cluster has to be one thing with everything else in it,
                        // not merely with the first of them: an unnamed marker between two
                        // differently named ones must not quietly join them to each other.
                        if (group != null && !group.Drop.TrueForAll(m => SameThing(m, other))) continue;
                        if (group == null) group = new Group { Keep = pin };
                        group.Drop.Add(other);
                        taken.Add(other.Key);
                    }
                    if (group == null) continue;
                    // The copy that knows what it is survives, and the lowest key settles it
                    // when they know the same amount, so every map folds the pair the same way.
                    //
                    // It used to be the lowest key alone, which was right while a key was a GUID
                    // made up on the spot: an arbitrary choice, but the same arbitrary choice
                    // everywhere, which is all that was needed. Since 4.0.0 a key ends in what
                    // the marker stands for, and the blank that means "nobody has worked this
                    // out yet" sorts before every real answer -- so a rule that used to pick at
                    // random became a rule that always threw away the copy that knew. One
                    // player who had never identified their map, syncing in, was enough: their
                    // blank copy of a house stood at the same point with the same icon, the two
                    // folded, and the answer lost. 325 structures went that way in one evening.
                    var best = group.Keep;
                    foreach (var member in group.Drop) if (Better(member, best)) best = member;
                    if (!ReferenceEquals(best, group.Keep))
                    {
                        group.Drop.Add(group.Keep);
                        group.Drop.Remove(best);
                        group.Keep = best;
                    }
                    groups.Add(group);
                }
            }
            return groups;
        }

        /// <summary>
        /// Which of two copies of one thing to keep: the one that knows what it is, then the one
        /// that was found first, then -- only to break a tie that cannot otherwise be broken --
        /// the lower key, so every machine folds the group the same way round.
        ///
        /// The key is last on purpose. Within a group the positions differ a little and the
        /// owners usually do not, so ordering by key resolves to ordering by owner id: the same
        /// player would win every fold on the server, for good. That is the same fault as
        /// keeping the copy that knew less, only quieter, because nothing about the surviving
        /// marker looks wrong -- it is simply always the same person's.
        ///
        /// Found first is the honest answer for a shared map. It is also stable: Created never
        /// changes, and is identical on every copy of a marker, so two players comparing maps
        /// reach the same verdict without having to agree on anything else first.
        /// </summary>
        private static bool Better(SharedPin a, SharedPin b)
        {
            bool knowsA = !string.IsNullOrEmpty(a.Source), knowsB = !string.IsNullOrEmpty(b.Source);
            if (knowsA != knowsB) return knowsA;
            if (a.Created != b.Created) return a.Created < b.Created;
            return a.Key.CompareTo(b.Key) < 0;
        }

        /// <summary>Fold them, carrying what the losers knew onto the survivor. Returns how many went.</summary>
        internal static int Fold()
        {
            int dropped = 0;
            foreach (var group in Find())
            {
                bool changed = false;
                foreach (var loser in group.Drop)
                {
                    if (loser.Checked && !group.Keep.Checked) { group.Keep.Checked = true; changed = true; }
                    if (string.IsNullOrEmpty(group.Keep.Name) && !string.IsNullOrEmpty(loser.Name))
                    {
                        group.Keep.Name = loser.Name;
                        changed = true;
                    }
                }
                if (changed) ClientPins.UpdateMarker(group.Keep);
                foreach (var loser in group.Drop)
                {
                    // Erased, not suppressed: nothing is being rejected here, and the spot must stay
                    // recordable -- the survivor is standing on it.
                    ClientPins.RemoveStale(loser.Key);
                    dropped++;
                }
            }
            if (dropped > 0) ClientPins.Restyle();
            return dropped;
        }
    }
}
