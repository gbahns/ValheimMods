using System;
using System.Collections.Generic;
using UnityEngine;

namespace DudeWhatAreMyStats
{
    /// <summary>A character's kills, sorted into what they actually were.</summary>
    internal sealed class KillSplitResult
    {
        internal float Hostile;
        internal float Tame;
        internal float Critters;
        internal float Neutral;
        internal float Bosses;
        internal float Players;
        internal float Unknown;

        /// <summary>How much of <see cref="Tame"/> is assumption rather than something watched.</summary>
        internal float Assumed;

        /// <summary>Base health of everything killed that was not a pet, added up.</summary>
        internal float Toughness;

        /// <summary>The toughest thing killed, by base health, and what it was.</summary>
        internal float Hardest;
        internal string HardestKey = "";

        /// <summary>Kills watched carrying at least one star. Only ever what this mod has seen.</summary>
        internal float Starred;

        /// <summary>Kills this mod watched, as against the game's own count of them.</summary>
        internal float Seen;
        internal float Total;

        internal float Unaccounted => Mathf.Max(0f, Total - Seen);
    }

    /// <summary>
    /// Turns the game's per-species kill tallies into hostile, tame, critters and bosses.
    ///
    /// The whole problem is that Valheim records a kill as a species and nothing else, so a tame
    /// wolf and a wild one are the same row. Three things are combined to get past that:
    ///
    ///  * <see cref="CreatureCatalog"/> says what a species is, which is enough for hostile against
    ///    harmless and is true of the whole history, because it is a fact about the species.
    ///  * <see cref="KillLog"/> says what this mod actually watched, pet or not, which is exact but
    ///    only covers kills made since it was installed.
    ///  * The config says what to assume about the rest, per species.
    ///
    /// The assumption applies **only** to the kills nothing was watching -- the game's count minus
    /// what the log saw. So a kill made today is never guessed at, the guessed share shrinks every
    /// session, and a world that has slaughtered hundreds of its own wolves can say so without
    /// rewriting what happens from here.
    /// </summary>
    internal static class KillSplit
    {
        private static readonly Dictionary<string, float> _shares = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        private static string _parsedFrom;

        /// <summary>
        /// What fraction of this species' unwatched kills to count as pets, 0 to 1. Zero unless the
        /// config says otherwise, so nothing is assumed about a world nobody has described.
        /// </summary>
        internal static float AssumedTameShare(string key)
        {
            EnsureParsed();
            if (string.IsNullOrEmpty(key)) return 0f;
            // The config wins where it speaks, so a world that wants its hens counted as wild can
            // still say so.
            if (_shares.TryGetValue(key, out float exact)) return exact;

            // Not every ambiguous species is actually ambiguous. A creature the world never spawns
            // and a player can tame exists only because somebody made it, so every one that died
            // was a pet, and no setting should be needed to say so. In vanilla that is the hen and
            // the chicken: they come from an egg and nowhere else.
            var info = CreatureCatalog.Find(key);
            if (info != null && info.OnlyEverTame) return 1f;

            if (_shares.Count == 0) return 0f;
            // "$enemy_boar" should match a config that just says "Boar", which is what anyone would
            // write and what the dump command shows alongside the full key.
            int underscore = key.LastIndexOf('_');
            if (underscore >= 0 && underscore + 1 < key.Length && _shares.TryGetValue(key.Substring(underscore + 1), out float suffix))
                return suffix;
            return 0f;
        }

        private static void EnsureParsed()
        {
            string raw = DwamsConfig.AssumeTameShare != null ? DwamsConfig.AssumeTameShare.Value : "";
            if (raw == _parsedFrom) return;
            _parsedFrom = raw;
            _shares.Clear();
            if (string.IsNullOrEmpty(raw)) return;
            foreach (string part in raw.Split(','))
            {
                string entry = part.Trim();
                if (entry.Length == 0) continue;
                // "Boar:100", or bare "Boar" for all of them.
                int colon = entry.IndexOf(':');
                string name = colon < 0 ? entry : entry.Substring(0, colon).Trim();
                string value = colon < 0 ? "100" : entry.Substring(colon + 1).Trim();
                if (name.Length == 0) continue;
                if (!float.TryParse(value, out float percent))
                {
                    DudeWhatAreMyStatsMod.Log.LogWarning(
                        $"[DudeWhatAreMyStats] Could not read \"{entry}\" in Assume Tame Share; expected something like Boar:100.");
                    continue;
                }
                _shares[name] = Mathf.Clamp01(percent / 100f);
            }
        }

        /// <summary>
        /// Sorts a character's whole kill table. <paramref name="totals"/> is the game's own
        /// per-species tally, slot 0 of m_enemyStats, and must be the untrimmed one: a table cut
        /// down to the top fifteen would leave most of the kills out of the arithmetic.
        /// </summary>
        internal static KillSplitResult Compute(Dictionary<string, float> totals)
        {
            var result = new KillSplitResult();
            if (totals == null) return result;

            foreach (var kv in totals)
            {
                string key = kv.Key;
                float count = kv.Value;
                if (count <= 0f) continue;
                result.Total += count;

                var info = CreatureCatalog.Find(key);
                var tally = KillLog.For(key);

                // The game's count is the authority on how many. The log can only ever have seen
                // some of them, and a log that somehow claims more is clamped rather than trusted.
                float seen = tally != null ? Mathf.Min(tally.Seen, count) : 0f;
                float seenTame = tally != null ? Mathf.Min(tally.Tame, seen) : 0f;
                result.Seen += seen;
                if (tally != null) result.Starred += tally.Starred;

                float unwatched = Mathf.Max(0f, count - seen);
                float assumed = unwatched * AssumedTameShare(key);
                float tame = seenTame + assumed;
                float rest = Mathf.Max(0f, count - tame);

                result.Tame += tame;
                result.Assumed += assumed;

                switch (info != null ? info.Kind : CreatureCatalog.KindOf(key))
                {
                    case CreatureKind.Boss:    result.Bosses += rest;   break;
                    case CreatureKind.Critter: result.Critters += rest; break;
                    case CreatureKind.Neutral: result.Neutral += rest;  break;
                    case CreatureKind.Player:  result.Players += rest;  break;
                    case CreatureKind.Hostile: result.Hostile += rest;  break;
                    default:                   result.Unknown += rest;  break;
                }

                // Toughness counts what you actually fought. Killing your own pig is not an
                // achievement however much the pig weighed.
                if (info == null || rest <= 0f) continue;
                result.Toughness += rest * info.Health;
                if (info.Health > result.Hardest)
                {
                    result.Hardest = info.Health;
                    result.HardestKey = key;
                }
            }
            return result;
        }
    }
}
