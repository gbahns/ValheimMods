using System;
using System.Collections.Generic;
using UnityEngine;

namespace DudeWhatAreMyStats
{
    /// <summary>One share of a fight: a player by name, or one of the sentinel buckets.</summary>
    internal sealed class FightShare
    {
        internal string Key = "";
        internal float Damage;

        internal string Display => FightReport.NameOf(Key);
        internal bool IsPlayer => Key.Length > 0 && Key[0] != '#';
    }

    /// <summary>
    /// Who did what to one creature over one fight.
    ///
    /// Built by whichever client owned the creature, because that is the only one that saw every
    /// blow: <c>Character.Damage</c> does nothing but invoke an RPC that returns immediately unless
    /// <c>m_nview.IsOwner()</c>, so all damage from all players funnels through a single game. See
    /// <see cref="FightWatch"/> for how it survives the creature changing hands mid-fight.
    ///
    /// The damage figures are health actually lost, measured as a delta across
    /// <c>Character.ApplyDamage</c> rather than read off the HitData. By the time a hit reaches
    /// ApplyDamage the game has already applied resistances, weak spots and armor to it, and
    /// ApplyDamage then applies the difficulty scale and <c>Game.m_playerDamageRate</c> on top, so
    /// the swing and the wound are different numbers. Measuring the wound also handles overkill,
    /// immunity and the sub-0.1 damage floor without a special case for any of them.
    /// </summary>
    internal sealed class FightReport
    {
        /// <summary>This message's own version, so it can gain fields without partitioning anyone.</summary>
        internal const int Wire = 1;

        /// <summary>Damage dealt by somebody's pets, which the game never attributes to an owner.</summary>
        internal const string Tames = "#tames";
        /// <summary>Damage dealt by creatures fighting each other.</summary>
        internal const string Creatures = "#creatures";
        /// <summary>Fall damage, drowning, fire it walked into: harm with no author.</summary>
        internal const string World = "#world";

        internal string CreatureKey = "";
        internal int Level = 1;
        internal bool Boss;
        internal float MaxHealth;
        internal float Seconds;
        internal long WhenUtc;

        /// <summary>
        /// Every point of health the creature lost, including the parts nobody is named for. Kept
        /// separately from the sum of the shares so the report can say how much it cannot account
        /// for rather than quietly scaling the percentages up to a hundred.
        /// </summary>
        internal float TotalDamage;

        internal readonly List<FightShare> Shares = new List<FightShare>();

        internal string Display => DeathReport.CauseName(CreatureKey);

        /// <summary>The shares that have a name on them, which is less than the whole in a fight somebody left.</summary>
        internal float Accounted
        {
            get
            {
                float sum = 0f;
                foreach (var s in Shares) sum += s.Damage;
                return sum;
            }
        }

        internal float Unaccounted => Mathf.Max(0f, TotalDamage - Accounted);

        internal float DamageFor(string key)
        {
            if (string.IsNullOrEmpty(key)) return 0f;
            foreach (var s in Shares)
                if (string.Equals(s.Key, key, StringComparison.Ordinal)) return s.Damage;
            return 0f;
        }

        /// <summary>A share of the whole fight, 0 to 1. Of the damage dealt, never of max health:
        /// a creature that heals can lose more health than it ever had at once.</summary>
        internal float ShareFor(string key)
            => TotalDamage > 0f ? DamageFor(key) / TotalDamage : 0f;

        /// <summary>Sorts the shares highest first, which is the only order anyone reads them in.</summary>
        internal void Sort() => Shares.Sort((a, b) => b.Damage.CompareTo(a.Damage));

        // ── words ───────────────────────────────────────────────────────────────────

        internal static string NameOf(string key)
        {
            if (string.IsNullOrEmpty(key)) return "(unknown)";
            switch (key)
            {
                case Tames:     return "Tamed animals";
                case Creatures: return "Other creatures";
                case World:     return "The world";
                default:        return key;   // a player name, which needs no localizing
            }
        }

        internal static string Duration(float seconds)
        {
            if (seconds < 60f) return seconds.ToString("0") + "s";
            int whole = Mathf.RoundToInt(seconds);
            return whole / 60 + "m " + whole % 60 + "s";
        }

        /// <summary>One line for the player reading it: what died and what their part in it was.</summary>
        internal string Headline(string viewer)
        {
            string what = (Level > 1 ? Level + "-star " : "") + Display;
            if (string.IsNullOrEmpty(viewer) || DamageFor(viewer) <= 0f)
                return what + " down (" + Duration(Seconds) + ")";
            return what + " down - you did " + (ShareFor(viewer) * 100f).ToString("0") + "% (" +
                   StatGroups.Count(DamageFor(viewer)) + ") in " + Duration(Seconds);
        }

        /// <summary>The whole thing, a line at a time, for the console and the log.</summary>
        internal List<string> Lines()
        {
            var lines = new List<string>();
            string what = (Level > 1 ? Level + "-star " : "") + Display;
            lines.Add(what + " - " + Duration(Seconds) + ", " + StatGroups.Count(TotalDamage) + " damage, " +
                      StatGroups.Count(MaxHealth) + " hp" + (Boss ? " (boss)" : ""));
            foreach (var s in Shares)
            {
                if (s.Damage <= 0f) continue;
                lines.Add("  " + s.Display + ": " + StatGroups.Count(s.Damage) +
                          " (" + (ShareFor(s.Key) * 100f).ToString("0") + "%)");
            }
            float rest = Unaccounted;
            // Damage by somebody who logged out before it died has nobody left to credit. Saying so
            // beats a set of percentages that silently do not add up.
            if (rest > 0.5f)
                lines.Add("  nobody still here: " + StatGroups.Count(rest) +
                          " (" + (rest / Mathf.Max(1f, TotalDamage) * 100f).ToString("0") + "%)");
            return lines;
        }

        // ── the wire, and the file ──────────────────────────────────────────────────

        internal ZPackage Pack()
        {
            var pkg = new ZPackage();
            pkg.Write(Wire);
            pkg.Write(CreatureKey ?? "");
            pkg.Write(Level);
            pkg.Write(Boss);
            pkg.Write(MaxHealth);
            pkg.Write(Seconds);
            pkg.Write(WhenUtc);
            pkg.Write(TotalDamage);
            pkg.Write(Shares.Count);
            foreach (var s in Shares)
            {
                pkg.Write(s.Key ?? "");
                pkg.Write(s.Damage);
            }
            return pkg;
        }

        internal static FightReport Unpack(ZPackage pkg)
        {
            if (pkg == null) return null;
            pkg.SetPos(0);
            int wire = pkg.ReadInt();
            if (wire < 1) return null;
            var report = new FightReport
            {
                CreatureKey = pkg.ReadString(),
                Level = pkg.ReadInt(),
                Boss = pkg.ReadBool(),
                MaxHealth = pkg.ReadSingle(),
                Seconds = pkg.ReadSingle(),
                WhenUtc = pkg.ReadLong(),
                TotalDamage = pkg.ReadSingle(),
            };
            int count = pkg.ReadInt();
            for (int i = 0; i < count; i++)
                report.Shares.Add(new FightShare { Key = pkg.ReadString(), Damage = pkg.ReadSingle() });
            // Anything a later version appended is left unread on purpose, the same contract the
            // stats message keeps.
            return report;
        }
    }
}
