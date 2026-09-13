using System;
using System.Collections.Generic;
using System.Text;

namespace DudeWhatAreMyStats
{
    /// <summary>
    /// Turns Valheim's flat list of 200-odd <see cref="PlayerStatType"/> values into something a
    /// person can read: which section a stat belongs in, what its number means (a count, a span of
    /// time, a distance) and a spaced-out label.
    /// </summary>
    internal static class StatGroups
    {
        /// <summary>Section names, in the order the Details tab shows them.</summary>
        internal static readonly string[] Order =
        {
            "Combat", "Deaths", "Exploration", "Building", "Crafting", "Gathering",
            "Creatures", "Forsaken powers", "Other",
        };

        private enum Unit { Count, Seconds, Meters }

        // Stats measured in seconds of play.
        private static readonly HashSet<string> TimeStats = new HashSet<string>(StringComparer.Ordinal)
        {
            "TimeInBase", "TimeOutOfBase",
        };

        // Stats measured in meters: distances covered, how far out you have been, how high you built.
        private static readonly HashSet<string> MeterStats = new HashSet<string>(StringComparer.Ordinal)
        {
            "DistanceTraveled", "DistanceWalk", "DistanceRun", "DistanceSail", "DistanceAir", "DistanceSailHelm",
            "ExploreNorth", "ExploreSouth", "ExploreEast", "ExploreWest",
            "ExploreNorthNoMap", "ExploreSouthNoMap", "ExploreEastNoMap", "ExploreWestNoMap",
            "MaxBuildingHeight", "MaxBuildingHeightWorld", "DeepestDungeon",
        };

        private static readonly HashSet<string> Combat = new HashSet<string>(StringComparer.Ordinal)
        {
            "EnemyHits", "EnemyKills", "EnemyKillsLastHits", "PlayerHits", "PlayerKills",
            "HitsTakenEnemies", "HitsTakenPlayers", "ArrowsShot", "SkeletonSummons",
            "BossKills", "BossLastHits", "BossKillMultiplayer", "BossKillSolo",
        };

        private static readonly HashSet<string> Exploration = new HashSet<string>(StringComparer.Ordinal)
        {
            "PortalsUsed", "PortalDungeonIn", "PortalDungeonOut", "LeviathanSink", "LavaLeviathanSink",
            "DeepestDungeon", "TimeInBase", "TimeOutOfBase", "Sleep", "WorldLoads", "PlayerSpawn",
            "ConsecutiveDaysSurvived", "ConsecutiveDaysSurvivedMax",
        };

        private static readonly HashSet<string> Building = new HashSet<string>(StringComparer.Ordinal)
        {
            "Builds", "PlaceStacks", "BuildPiecesRemoved", "MaxBuildingHeight", "MaxBuildingHeightWorld",
            "MaxComfort", "VillagePointsMax",
        };

        private static readonly HashSet<string> Gathering = new HashSet<string>(StringComparer.Ordinal)
        {
            "TreeChops", "Tree", "LogChops", "Logs", "MineHits", "Mines", "BeesHarvested", "SapHarvested",
            "ItemsPickedUp", "FishHooked", "FishLost", "FishCaught",
            "TreeFir", "TreeOak", "TreePine", "TreeAshlands", "TreeYggdrasilShoot", "TreeSwamp",
            "TreeBeech", "TreeBirch", "TreeSnowFir", "TreeSnowPine",
        };

        private static readonly HashSet<string> Creatures = new HashSet<string>(StringComparer.Ordinal)
        {
            "CreatureTamed", "TamedPetting", "TamedCommand", "RavenHits", "RavenTalk", "RavenAppear",
        };

        private static readonly Dictionary<PlayerStatType, string> SectionCache = new Dictionary<PlayerStatType, string>();
        private static readonly Dictionary<PlayerStatType, string> LabelCache = new Dictionary<PlayerStatType, string>();

        /// <summary>The section heading a stat is listed under. Never null.</summary>
        internal static string Section(PlayerStatType stat)
        {
            if (SectionCache.TryGetValue(stat, out var cached)) return cached;
            string s = Classify(stat.ToString());
            SectionCache[stat] = s;
            return s;
        }

        private static string Classify(string n)
        {
            // Deaths first: DeathByTreeTier0 is a death, not a felled tree.
            if (n == "Deaths" || n.StartsWith("DeathBy", StringComparison.Ordinal)) return "Deaths";
            if (Combat.Contains(n)) return "Combat";
            if (n.StartsWith("SetPower", StringComparison.Ordinal) || n.StartsWith("UsePower", StringComparison.Ordinal)
                || n == "SetGuardianPower" || n == "UseGuardianPower") return "Forsaken powers";
            if (Building.Contains(n) || n.StartsWith("BuiltPieces", StringComparison.Ordinal)
                || n.StartsWith("BuildCluster", StringComparison.Ordinal)) return "Building";
            if (n == "CraftsOrUpgrades" || n == "Crafts" || n == "Upgrades"
                || n.StartsWith("Craft", StringComparison.Ordinal)) return "Crafting";
            if (Gathering.Contains(n) || n.StartsWith("TreeTier", StringComparison.Ordinal)
                || n.StartsWith("MineTier", StringComparison.Ordinal) || n.StartsWith("Harvest", StringComparison.Ordinal)
                || n.StartsWith("FishCaughtTier", StringComparison.Ordinal)) return "Gathering";
            if (Exploration.Contains(n) || n.StartsWith("Distance", StringComparison.Ordinal)
                || n.StartsWith("Explore", StringComparison.Ordinal)
                || n.StartsWith("Treasure", StringComparison.Ordinal)) return "Exploration";
            if (Creatures.Contains(n)) return "Creatures";
            return "Other";
        }

        /// <summary>"EnemyKillsLastHits" becomes "Enemy Kills Last Hits".</summary>
        internal static string Label(PlayerStatType stat)
        {
            if (LabelCache.TryGetValue(stat, out var cached)) return cached;
            string label = Spaced(stat.ToString());
            LabelCache[stat] = label;
            return label;
        }

        private static string Spaced(string name)
        {
            var sb = new StringBuilder(name.Length + 8);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                // A space before a capital or a digit that starts a new word, but not inside a run
                // of capitals and not before the digits of "Tier0".
                if (i > 0 && char.IsUpper(c) && !char.IsUpper(name[i - 1])) sb.Append(' ');
                else if (i > 0 && char.IsDigit(c) && !char.IsDigit(name[i - 1])) sb.Append(' ');
                sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>The stat's value written the way its unit reads: 3h 12m, 4.2 km, or a plain count.</summary>
        internal static string Format(PlayerStatType stat, float value)
        {
            string n = stat.ToString();
            if (TimeStats.Contains(n)) return Duration(value);
            if (MeterStats.Contains(n)) return Distance(value);
            return Count(value);
        }

        /// <summary>A whole number with thousands separators; fractions keep one decimal.</summary>
        internal static string Count(float value)
        {
            if (Math.Abs(value - (float)Math.Round(value)) < 0.05f) return ((long)Math.Round(value)).ToString("N0");
            return value.ToString("N1");
        }

        /// <summary>Seconds as 2d 4h, 3h 12m, 5m 20s or 12s.</summary>
        internal static string Duration(float seconds)
        {
            if (seconds <= 0f) return "0s";
            var t = TimeSpan.FromSeconds(seconds);
            if (t.TotalDays >= 1d) return $"{(int)t.TotalDays}d {t.Hours}h";
            if (t.TotalHours >= 1d) return $"{(int)t.TotalHours}h {t.Minutes}m";
            if (t.TotalMinutes >= 1d) return $"{(int)t.TotalMinutes}m {t.Seconds}s";
            return $"{(int)t.TotalSeconds}s";
        }

        /// <summary>Meters, switching to kilometers past 1000.</summary>
        internal static string Distance(float meters)
        {
            if (Math.Abs(meters) >= 1000f) return (meters / 1000f).ToString("N1") + " km";
            return Math.Round(meters).ToString("N0") + " m";
        }
    }
}
