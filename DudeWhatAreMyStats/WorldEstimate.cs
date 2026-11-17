using System;
using System.Collections.Generic;

namespace DudeWhatAreMyStats
{
    /// <summary>One species' all-time range, worked out from everyone's own lifetime tallies.</summary>
    internal sealed class WorldEstimateRow
    {
        internal string Key = "";

        /// <summary>The biggest single player's tally, which the group cannot have beaten.</summary>
        internal float Least;

        /// <summary>Everyone's tallies added up, which the group cannot have exceeded.</summary>
        internal float Most;

        /// <summary>How many players have killed one at all, which is what makes the range wide.</summary>
        internal int Players;

        internal bool Exact => Players <= 1 || Least >= Most;
    }

    /// <summary>
    /// What the group has killed all time, as a range, because an exact figure does not exist.
    ///
    /// <para><see cref="WorldKills"/> counts exactly, but only from the day it was installed, and
    /// Valheim keeps no record of what a world has killed before that. The per-player tallies do
    /// survive, so the history is not lost -- only the overlap is. Two bounds fall straight out of
    /// that, per species:</para>
    ///
    /// <list type="bullet">
    /// <item>the biggest single player's count is the fewest the group can have killed, since those
    /// kills all happened whoever else was there; it is the exact answer when every kill was shared
    /// by everybody, which is roughly true of bosses.</item>
    /// <item>everyone's counts added up is the most they can have killed, since the only way the
    /// real number is lower is a kill credited twice; it is the exact answer when nothing was
    /// shared, which is roughly true of greydwarves.</item>
    /// </list>
    ///
    /// <para>So the two are tight in opposite cases and the truth sits between them. Both are shown
    /// for what they are rather than averaged into a single number that would be wrong in a way
    /// nobody could see.</para>
    ///
    /// <para>Two honest caveats, which the panel says out loud. A character's stats follow the
    /// character and not the world, so somebody who has played elsewhere brings those kills with
    /// them. And the server keeps only a short species list per offline player, so a range built
    /// while people are away is narrower than the real one rather than wider.</para>
    /// </summary>
    internal static class WorldEstimate
    {
        /// <summary>
        /// Builds the range for every species anybody has killed.
        /// </summary>
        /// <param name="roster">The panel's roster, live answers and stored records together.</param>
        /// <param name="partial">
        /// True when somebody in the roster is offline, so their species list is the server's
        /// trimmed copy and the totals below are understated.
        /// </param>
        internal static List<WorldEstimateRow> From(List<Snapshot> roster, out bool partial)
        {
            partial = false;
            var rows = new Dictionary<string, WorldEstimateRow>(StringComparer.Ordinal);
            if (roster == null) return new List<WorldEstimateRow>();

            foreach (var snap in roster)
            {
                if (snap == null) continue;
                // A record from the store holds the top few species only, on purpose: the whole
                // store travels in one message. Counting it is still better than leaving the
                // player out, so it is counted and the shortfall is reported.
                if (snap.FromStore && snap.Creatures.Count >= StatsStore.StoredCreatures) partial = true;
                foreach (var kv in snap.Creatures)
                {
                    if (string.IsNullOrEmpty(kv.Key) || kv.Value <= 0f) continue;
                    if (!rows.TryGetValue(kv.Key, out var row))
                        rows[kv.Key] = row = new WorldEstimateRow { Key = kv.Key };
                    row.Most += kv.Value;
                    if (kv.Value > row.Least) row.Least = kv.Value;
                    row.Players++;
                }
            }

            var list = new List<WorldEstimateRow>(rows.Values);
            list.Sort((a, b) =>
            {
                int byCount = b.Most.CompareTo(a.Most);
                return byCount != 0 ? byCount : string.Compare(a.Key, b.Key, StringComparison.Ordinal);
            });
            return list;
        }

        /// <summary>The same two bounds for everything together, so the header can show one line.</summary>
        internal static void Totals(List<WorldEstimateRow> rows, out float least, out float most)
        {
            least = 0f;
            most = 0f;
            if (rows == null) return;
            foreach (var row in rows)
            {
                least += row.Least;
                most += row.Most;
            }
        }
    }
}
