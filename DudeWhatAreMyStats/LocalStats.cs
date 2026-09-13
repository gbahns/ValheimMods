using System;
using System.Collections.Generic;

namespace DudeWhatAreMyStats
{
    /// <summary>
    /// Reads the local character's stats out of its profile.
    ///
    /// Valheim 1.0 keeps ten sets of stats per character, one per <see cref="DifficultyRequirement"/>.
    /// Slot 0 (RawStats) is the one that matters here: PlayerProfile.IncrementStat always writes it,
    /// then writes slot 1 and the current difficulty's slot as well when the run is eligible for
    /// achievements. Slot 0 is therefore the complete lifetime tally, and adding the slots together
    /// would count most events two or three times. (PlayerProfile.GetStat is no help either: it
    /// returns the *current difficulty's* slot, which reads zero on a difficulty you just switched to.)
    /// </summary>
    internal static class LocalStats
    {
        private const int RawStats = 0;

        private static bool _warnedLayout;
        private static bool _warnedSkills;
        private static bool _warnedRead;

        /// <summary>
        /// The local character's stats, or null if there is no profile loaded yet. Never throws:
        /// this feeds the panel and the RPC reply, and a surprise from the game's own data should
        /// leave an empty scoreboard row, not tear down whichever one of those called it.
        /// </summary>
        internal static Snapshot Read()
        {
            try
            {
                return ReadInner();
            }
            catch (Exception e)
            {
                if (!_warnedRead)
                {
                    _warnedRead = true;
                    DudeWhatAreMyStatsMod.Log.LogError($"[DudeWhatAreMyStats] Could not read the local character's stats: {e}");
                }
                return null;
            }
        }

        private static Snapshot ReadInner()
        {
            var game = Game.instance;
            var profile = game != null ? game.GetPlayerProfile() : null;
            if (profile == null) return null;

            var snap = new Snapshot
            {
                Name = string.IsNullOrEmpty(profile.GetName()) ? "Viking" : profile.GetName(),
                ProfileId = profile.GetPlayerID(),
                PeerId = ZNet.instance != null ? ZNet.GetUID() : 0L,
                IsLocal = true,
                Online = true,
                ReceivedAt = UnityEngine.Time.unscaledTime,
            };

            var raw = Raw(profile);
            if (raw != null)
            {
                if (raw.m_stats != null)
                    foreach (var kv in raw.m_stats)
                        if (kv.Value != 0f) snap.Stats[kv.Key] = kv.Value;

                // m_enemyStats is indexed by KillModifiers; slot 0 is the total across all of them.
                if (raw.m_enemyStats != null && raw.m_enemyStats.Length > 0 && raw.m_enemyStats[0] != null)
                {
                    foreach (var kv in raw.m_enemyStats[0])
                        if (kv.Value > 0f) snap.Creatures.Add(kv);
                    snap.Creatures.Sort((a, b) => b.Value.CompareTo(a.Value));
                    int keep = DwamsConfig.TopCreatureCount != null ? DwamsConfig.TopCreatureCount.Value : 15;
                    if (keep >= 0 && snap.Creatures.Count > keep) snap.Creatures.RemoveRange(keep, snap.Creatures.Count - keep);
                }
            }

            ReadSkills(snap);
            return snap;
        }

        /// <summary>
        /// Adds the local character's skill levels.
        ///
        /// Through Character.GetSkills(), never Player.m_skills: that field is private, and the
        /// publicized assemblies this project compiles against exist only at build time. The game
        /// loads the real ones, where Mono enforces the access and throws FieldAccessException.
        /// </summary>
        private static void ReadSkills(Snapshot snap)
        {
            try
            {
                var player = Player.m_localPlayer;
                var skills = player != null ? player.GetSkills() : null;
                if (skills == null) return;
                foreach (var s in skills.GetSkillList())
                {
                    if (s == null || s.m_info == null) continue;
                    if (s.m_level <= 0f) continue;
                    snap.Skills.Add(new KeyValuePair<Skills.SkillType, float>(s.m_info.m_skill, s.m_level));
                }
                snap.Skills.Sort((a, b) => b.Value.CompareTo(a.Value));
            }
            catch (Exception e)
            {
                if (!_warnedSkills)
                {
                    _warnedSkills = true;
                    DudeWhatAreMyStatsMod.Log.LogError(
                        $"[DudeWhatAreMyStats] Could not read this character's skills; the rest of the stats still work. {e.Message}");
                }
            }
        }

        /// <summary>Slot 0 of the per-difficulty stats, or null if the layout is not what this build expects.</summary>
        private static PlayerProfile.PlayerStats Raw(PlayerProfile profile)
        {
            try
            {
                var all = profile.m_playerStats;
                if (all != null && all.Length > RawStats && all[RawStats] != null) return all[RawStats];
            }
            catch (Exception e)
            {
                if (!_warnedLayout)
                {
                    _warnedLayout = true;
                    DudeWhatAreMyStatsMod.Log.LogError(
                        "[DudeWhatAreMyStats] The player profile's stats are not laid out the way this build expects, " +
                        $"so the panel will be empty. This usually means a Valheim update moved them. Details: {e.Message}");
                }
                return null;
            }
            if (!_warnedLayout)
            {
                _warnedLayout = true;
                DudeWhatAreMyStatsMod.Log.LogWarning(
                    "[DudeWhatAreMyStats] The player profile has no raw stats slot; the panel will be empty.");
            }
            return null;
        }
    }
}
