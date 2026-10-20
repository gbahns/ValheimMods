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

        /// <summary>Every cluster of markers standing on one another, without changing anything.</summary>
        internal static List<Group> Find()
        {
            var groups = new List<Group>();
            var byIcon = new Dictionary<string, List<SharedPin>>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var pin in ClientPins.All)
            {
                string icon = IconRegistry.Normalize(ClientPins.IconOf(pin)) ?? "";
                if (!byIcon.TryGetValue(icon, out var list)) byIcon[icon] = list = new List<SharedPin>();
                list.Add(pin);
            }

            foreach (var kv in byIcon)
            {
                var pins = kv.Value;
                if (pins.Count < 2) continue;
                var taken = new HashSet<string>();
                foreach (var pin in pins)
                {
                    if (!taken.Add(pin.Id)) continue;
                    Group group = null;
                    float spacing = SpacingFor(pin);
                    foreach (var other in pins)
                    {
                        if (other == pin || taken.Contains(other.Id)) continue;
                        if (Geo.FlatDistance(pin.Pos, other.Pos) > Mathf.Max(spacing, SpacingFor(other))) continue;
                        if (!SameThing(pin, other)) continue;
                        // Everything in a cluster has to be one thing with everything else in it,
                        // not merely with the first of them: an unnamed marker between two
                        // differently named ones must not quietly join them to each other.
                        if (group != null && !group.Drop.TrueForAll(m => SameThing(m, other))) continue;
                        if (group == null) group = new Group { Keep = pin };
                        group.Drop.Add(other);
                        taken.Add(other.Id);
                    }
                    if (group == null) continue;
                    // The lowest id survives, so every map folds the pair the same way round.
                    foreach (var member in group.Drop)
                        if (string.CompareOrdinal(member.Id, group.Keep.Id) < 0)
                        {
                            group.Drop.Add(group.Keep);
                            group.Keep = member;
                            group.Drop.Remove(member);
                            break;
                        }
                    groups.Add(group);
                }
            }
            return groups;
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
                    ClientPins.RemoveStale(loser.Id);
                    dropped++;
                }
            }
            if (dropped > 0) ClientPins.Restyle();
            return dropped;
        }
    }
}
