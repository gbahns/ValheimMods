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
        private static bool _warnedDeaths;
        private static bool _warnedKills;

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
            ReadDeaths(snap);
            ReadKills(snap, raw);
            return snap;
        }

        /// <summary>
        /// Sorts this character's kills into what they actually were, and reads the kill counts by
        /// weapon class that Valheim has been keeping all along.
        ///
        /// Note the untrimmed table: the breakdown is computed from every species the profile holds,
        /// not from the top fifteen the panel lists, or most of the kills would be left out of it.
        /// Only the totals travel, so this costs the message a few dozen bytes rather than a table.
        /// </summary>
        private static void ReadKills(Snapshot snap, PlayerProfile.PlayerStats raw)
        {
            try
            {
                if (DwamsConfig.RecordKills == null) return;
                if (raw == null || raw.m_enemyStats == null || raw.m_enemyStats.Length == 0) return;

                if (raw.m_enemyStats[0] != null)
                    snap.KillTypes = KillSplit.Compute(raw.m_enemyStats[0]);

                // Slots 1 to 4 are KillModifiers: Unarmed, Magic, Ranged, Melee. The game writes
                // them on every kill and nothing has ever read them.
                AddWeaponClass(snap, raw, (int)KillModifiers.Unarmed, "Unarmed");
                AddWeaponClass(snap, raw, (int)KillModifiers.Magic, "Magic");
                AddWeaponClass(snap, raw, (int)KillModifiers.Ranged, "Ranged");
                AddWeaponClass(snap, raw, (int)KillModifiers.Melee, "Melee");
            }
            catch (Exception e)
            {
                if (!_warnedKills)
                {
                    _warnedKills = true;
                    DudeWhatAreMyStatsMod.Log.LogWarning(
                        $"[DudeWhatAreMyStats] Could not sort this character's kills; the rest of the stats still work. {e.Message}");
                }
            }
        }

        private static void AddWeaponClass(Snapshot snap, PlayerProfile.PlayerStats raw, int slot, string label)
        {
            if (slot < 0 || slot >= raw.m_enemyStats.Length) return;
            var table = raw.m_enemyStats[slot];
            if (table == null) return;
            float total = 0f;
            foreach (var kv in table) total += kv.Value;
            if (total > 0f) snap.ByWeapon.Add(new KeyValuePair<string, float>(label, total));
        }

        /// <summary>
        /// Adds what this mod has watched kill this character, which the game itself does not record.
        ///
        /// Only the top few travel: these ride the same message as everything else, and the store
        /// hands every character it holds to a client in one send. The full reports never leave the
        /// machine that made them.
        /// </summary>
        private static void ReadDeaths(Snapshot snap)
        {
            try
            {
                if (DwamsConfig.RecordDeaths == null || !DwamsConfig.RecordDeaths.Value) return;
                int top = DwamsConfig.TopDeathCauses != null ? DwamsConfig.TopDeathCauses.Value : 10;
                snap.RecordedDeaths = DeathLog.Recorded;
                if (top <= 0) return;
                snap.KilledBy.AddRange(DeathLog.KilledBy(top));
                snap.FoughtWith.AddRange(DeathLog.FoughtWith(top));
            }
            catch (Exception e)
            {
                if (!_warnedDeaths)
                {
                    _warnedDeaths = true;
                    DudeWhatAreMyStatsMod.Log.LogWarning(
                        $"[DudeWhatAreMyStats] Could not read the death log; the rest of the stats still work. {e.Message}");
                }
            }
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
