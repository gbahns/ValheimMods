using System;
using System.Collections.Generic;
using UnityEngine;

namespace DudeWhatAreMyStats
{
    /// <summary>
    /// One player's stats as of the moment they were read. The local player's snapshot is read
    /// straight out of the profile; everyone else's arrives over the wire, so this is also the
    /// message format. Only non-zero stats travel, which keeps a snapshot at a couple of kilobytes.
    /// </summary>
    internal sealed class Snapshot
    {
        /// <summary>Bumped when the fields below change shape. Mismatched versions are dropped, not guessed at.</summary>
        internal const int Schema = 2;

        internal string Name = "";
        internal long ProfileId;
        internal long PeerId;
        internal bool IsLocal;
        internal bool Online = true;
        internal float ReceivedAt;

        /// <summary>
        /// When the server last heard from this character, as Unix seconds, stamped by the server on
        /// arrival rather than taken from the sender. Every stored row is therefore measured against
        /// the same clock, though the age is worked out on the reader's, so a badly wrong clock at
        /// either end skews what is shown. Zero means nobody stamped it, as with a live answer.
        /// </summary>
        internal long LastSeenUtc;

        /// <summary>True when this came out of the server's store rather than from the player just now.</summary>
        internal bool FromStore;

        internal readonly Dictionary<PlayerStatType, float> Stats = new Dictionary<PlayerStatType, float>();
        internal readonly List<KeyValuePair<Skills.SkillType, float>> Skills = new List<KeyValuePair<Skills.SkillType, float>>();
        internal readonly List<KeyValuePair<string, float>> Creatures = new List<KeyValuePair<string, float>>();

        /// <summary>
        /// What identifies this player in the roster. The character's profile id normally, which is
        /// stable across reconnects; a profile restored from an old save can carry a zero id, so the
        /// connection falls in behind it rather than letting two zeroes collapse into one row.
        /// </summary>
        internal long Identity => ProfileId != 0L ? ProfileId : PeerId;

        // ── derived values the scoreboard sorts on ──────────────────────────────────

        internal float Stat(PlayerStatType stat) => Stats.TryGetValue(stat, out var v) ? v : 0f;

        internal float Kills  => Stat(PlayerStatType.EnemyKills);
        internal float Deaths => Stat(PlayerStatType.Deaths);
        internal float Bosses => Stat(PlayerStatType.BossKills);
        internal float Played => Stat(PlayerStatType.TimeInBase) + Stat(PlayerStatType.TimeOutOfBase);

        /// <summary>Kills per death. With no deaths yet the ratio is the kill count, as scoreboards usually show it.</summary>
        internal float KillDeath => Deaths >= 1f ? Kills / Deaths : Kills;

        /// <summary>The highest skill, or None at level 0 for a fresh character.</summary>
        internal KeyValuePair<Skills.SkillType, float> BestSkill
        {
            get
            {
                var best = new KeyValuePair<Skills.SkillType, float>(global::Skills.SkillType.None, 0f);
                foreach (var s in Skills)
                    if (s.Value > best.Value) best = s;
                return best;
            }
        }

        internal float BestSkillLevel => BestSkill.Value;

        /// <summary>"2d ago" for a stored row; empty while the player is online to answer for themselves.</summary>
        internal string LastSeenText
        {
            get
            {
                if (Online || LastSeenUtc <= 0L) return "";
                // Negative means the stamping clock is ahead of this one. Saying "just now" is
                // closer to the truth than saying nothing and falling back to a bare "offline".
                long seconds = NowUtc() - LastSeenUtc;
                if (seconds < 0L) return "just now";
                if (seconds < 90L) return "just now";
                if (seconds < 3600L) return (seconds / 60L) + "m ago";
                if (seconds < 86400L) return (seconds / 3600L) + "h ago";
                return (seconds / 86400L) + "d ago";
            }
        }

        /// <summary>Unix seconds. One clock for every age this mod shows, wherever it is computed.</summary>
        internal static long NowUtc()
        {
            return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        }

        internal string BestSkillText
        {
            get
            {
                var best = BestSkill;
                if (best.Key == global::Skills.SkillType.None || best.Value <= 0f) return "-";
                return SkillName(best.Key) + " " + Mathf.FloorToInt(best.Value);
            }
        }

        /// <summary>Total of every skill level, the usual "overall progress" number.</summary>
        internal float SkillTotal
        {
            get
            {
                float total = 0f;
                foreach (var s in Skills) total += s.Value;
                return total;
            }
        }

        /// <summary>
        /// The game's own name for a skill. Valheim keys these as "$skill_" plus the lower-cased
        /// enum name, the same way its skills dialog does; if that misses, the enum name is spaced
        /// out instead, so "ElementalMagic" still reads as "Elemental Magic".
        /// </summary>
        internal static string SkillName(Skills.SkillType skill)
        {
            string key = "$skill_" + skill.ToString().ToLowerInvariant();
            string localized = Localization.instance != null ? Localization.instance.Localize(key) : key;
            if (!string.IsNullOrEmpty(localized) && localized != key && !localized.StartsWith("[", StringComparison.Ordinal))
                return localized;
            return Spaced(skill.ToString());
        }

        private static string Spaced(string name)
        {
            var sb = new System.Text.StringBuilder(name.Length + 4);
            for (int i = 0; i < name.Length; i++)
            {
                if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1])) sb.Append(' ');
                sb.Append(name[i]);
            }
            return sb.ToString();
        }

        // ── the wire format ─────────────────────────────────────────────────────────

        internal ZPackage Pack()
        {
            var pkg = new ZPackage();
            pkg.Write(Schema);
            pkg.Write(Name ?? "");
            pkg.Write(ProfileId);
            pkg.Write(LastSeenUtc);

            pkg.Write(Stats.Count);
            foreach (var kv in Stats)
            {
                pkg.Write((int)kv.Key);
                pkg.Write(kv.Value);
            }

            pkg.Write(Skills.Count);
            foreach (var kv in Skills)
            {
                pkg.Write((int)kv.Key);
                pkg.Write(kv.Value);
            }

            pkg.Write(Creatures.Count);
            foreach (var kv in Creatures)
            {
                pkg.Write(kv.Key ?? "");
                pkg.Write(kv.Value);
            }
            return pkg;
        }

        /// <summary>Reads a snapshot off the wire. Returns null for a version this build does not speak.</summary>
        internal static Snapshot Unpack(ZPackage pkg)
        {
            if (pkg == null) return null;
            try
            {
                pkg.SetPos(0);
                int schema = pkg.ReadInt();
                if (schema != Schema) return null;

                var snap = new Snapshot
                {
                    Name = pkg.ReadString(),
                    ProfileId = pkg.ReadLong(),
                    LastSeenUtc = pkg.ReadLong(),
                };

                int stats = pkg.ReadInt();
                for (int i = 0; i < stats; i++)
                {
                    var key = (PlayerStatType)pkg.ReadInt();
                    float value = pkg.ReadSingle();
                    snap.Stats[key] = value;
                }

                int skills = pkg.ReadInt();
                for (int i = 0; i < skills; i++)
                {
                    var key = (Skills.SkillType)pkg.ReadInt();
                    float value = pkg.ReadSingle();
                    snap.Skills.Add(new KeyValuePair<Skills.SkillType, float>(key, value));
                }

                int creatures = pkg.ReadInt();
                for (int i = 0; i < creatures; i++)
                {
                    string key = pkg.ReadString();
                    float value = pkg.ReadSingle();
                    snap.Creatures.Add(new KeyValuePair<string, float>(key, value));
                }
                return snap;
            }
            catch (Exception e)
            {
                DudeWhatAreMyStatsMod.Log.LogWarning($"[DudeWhatAreMyStats] Could not read a stats snapshot: {e.Message}");
                return null;
            }
        }
    }
}
