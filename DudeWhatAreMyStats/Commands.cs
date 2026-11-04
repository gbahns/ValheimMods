using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace DudeWhatAreMyStats
{
    /// <summary>Console commands (F5 in game). Prefixed dwams_.</summary>
    internal static class Commands
    {
        internal static void Register()
        {
            new Terminal.ConsoleCommand("dwams", "Dude What Are My Stats: open or close the stats panel",
                (Terminal.ConsoleEvent)(args =>
                {
                    if (Player.m_localPlayer == null) { args.Context?.AddString("No character loaded."); return; }
                    StatsPanel.Toggle();
                }));

            new Terminal.ConsoleCommand("dwams_hud", "Dude What Are My Stats: show or hide the always-on player list",
                (Terminal.ConsoleEvent)(args =>
                {
                    PlayerListHud.Toggle();
                    args.Context?.AddString("Player list " + (DwamsConfig.ShowPlayerList.Value ? "on." : "off."));
                }));

            new Terminal.ConsoleCommand("dwams_refresh", "Dude What Are My Stats: ask the other players for their stats again",
                (Terminal.ConsoleEvent)(args =>
                {
                    StatsNetwork.RequestNow();
                    args.Context?.AddString("Asked everyone online, and the server, for stats.");
                }));

            new Terminal.ConsoleCommand("dwams_deaths", "Dude What Are My Stats: what has killed you, and the story of the last few",
                (Terminal.ConsoleEvent)(args =>
                {
                    args.Context?.AddString(Deaths(args.Length > 1 ? args[1] : null));
                }));

            new Terminal.ConsoleCommand("dwams_fights", "Dude What Are My Stats: who did what in the fights worth remembering",
                (Terminal.ConsoleEvent)(args =>
                {
                    args.Context?.AddString(Fights(args.Length > 1 ? args[1] : null));
                }));

            new Terminal.ConsoleCommand("dwams_creatures", "Dude What Are My Stats: how each creature is classified for the kill breakdown",
                (Terminal.ConsoleEvent)(args =>
                {
                    args.Context?.AddString(Creatures(args.Length > 1 ? args[1] : null));
                }));

            new Terminal.ConsoleCommand("dwams_status", "Dude What Are My Stats: what the scoreboard currently knows",
                (Terminal.ConsoleEvent)(args =>
                {
                    var sb = new StringBuilder();
                    sb.AppendLine($"panel: {(StatsPanel.IsOpen ? "open" : "closed")} | holding pause: {StatsPause.Holding} | answered live: {StatsNetwork.KnownCount + 1}");
                    if (StatsNetwork.OtherVersionCount > 0)
                        sb.AppendLine("version: " + StatsNetwork.OtherVersionText);
                    if (StatsNetwork.StoredUnreadable > 0)
                        sb.AppendLine($"version: the server holds {StatsNetwork.StoredUnreadable} record(s) this build cannot read, so nobody offline is shown");
                    sb.AppendLine(StatsNetwork.ServerHasStore
                        ? $"server store: answering, {StatsNetwork.StoredCount} character(s) remembered"
                        : "server store: no answer yet (the server may not run this mod, which only costs you offline players)");
                    sb.AppendLine(KillLog.Loaded
                        ? $"kill log: {KillLog.Species} species watched, {CreatureCatalog.Count} creature(s) catalogued"
                        : "kill log: not open yet");
                    int progress = Progression.Tier();
                    sb.AppendLine(progress > 0
                        ? $"progress: tier {progress}, {CreatureCatalog.TierName(progress)} ({Progression.Describe(progress)})"
                        : "progress: no world loaded, so kills cannot be ranked");
                    if (DwamsConfig.RecordDeaths.Value)
                        sb.AppendLine(DeathLog.Loaded
                            ? $"death log: {DeathLog.Recorded} recorded, {DeathLog.ReportCount} kept in full" +
                              (DeathWatch.InFight ? $", in a fight with {DeathWatch.LiveCount} creature(s) right now" : "")
                            : "death log: not open yet");
                    else
                        sb.AppendLine("death log: off (Record Deaths)");
                    if (DwamsConfig.RecordFights.Value)
                        sb.AppendLine(FightLog.Loaded
                            ? $"fight log: {FightLog.BossCount} boss fight(s), {FightLog.RecentCount} other(s)" +
                              (FightWatch.LiveCount > 0 ? $", following {FightWatch.LiveCount} creature(s) right now" : "")
                            : "fight log: not open yet");
                    else
                        sb.AppendLine("fight log: off (Record Fights)");
                    if (StatsStore.IsServer)
                        sb.AppendLine(StatsStore.Loaded
                            ? $"this game is the server: {StatsStore.Count} character(s) in {StatsStore.Path}"
                            : "this game is the server, but the store is off or has no world yet");
                    foreach (var snap in StatsNetwork.Roster())
                    {
                        string age = snap.LastSeenText;
                        string who = snap.IsLocal ? "you" : snap.Online ? "online" : age.Length > 0 ? "last seen " + age : "offline";
                        sb.AppendLine($"{snap.Name} ({who}): {StatGroups.Count(snap.KillsShown)} kills, {StatGroups.Count(snap.Deaths)} deaths, " +
                                      $"{StatGroups.Count(snap.Bosses)} bosses, {StatGroups.Duration(snap.Played)} played, best {snap.BestSkillText}");
                    }
                    args.Context?.AddString(sb.ToString().TrimEnd());
                }));
        }

        /// <summary>
        /// How every creature in this world is classified, which is what the kill breakdown rests
        /// on. With no argument it prints the summary and the rows worth checking by eye -- the
        /// tameable ones, the ones that leave you alone, and anything it could not place. "all"
        /// prints everything and copies it to the log, and any other word filters by name.
        /// </summary>
        private static string Creatures(string filter)
        {
            if (!CreatureCatalog.Built) CreatureCatalog.Build();
            var all = CreatureCatalog.All();
            if (all.Count == 0)
                return "No creatures catalogued. This needs a world loaded.";

            bool everything = string.Equals(filter, "all", System.StringComparison.OrdinalIgnoreCase);
            bool filtered = !everything && !string.IsNullOrEmpty(filter);

            int hostile = 0, critter = 0, neutral = 0, boss = 0, other = 0, tameable = 0;
            foreach (var c in all)
            {
                if (c.Tameable) tameable++;
                switch (c.Kind)
                {
                    case CreatureKind.Hostile: hostile++; break;
                    case CreatureKind.Critter: critter++; break;
                    case CreatureKind.Neutral: neutral++; break;
                    case CreatureKind.Boss:    boss++;    break;
                    default:                   other++;   break;
                }
            }

            var sb = new StringBuilder();
            string summary = $"{all.Count} creature(s): {hostile} hostile, {critter} critters, {neutral} leave you alone, " +
                             $"{boss} bosses, {other} unplaced; {tameable} can be tamed.";
            sb.AppendLine(summary);
            DudeWhatAreMyStatsMod.Log.LogInfo("[DudeWhatAreMyStats] " + summary);
            if (!everything && !filtered)
                sb.AppendLine("Showing the ones worth checking. Use dwams_creatures all for every row, or a word to filter.");

            // The one place this mod overrules the game's own grouping, and therefore the one place
            // a mistake in the armament test would hide. Listed by name so it can be read at a
            // glance rather than hunted for among a hundred rows.
            var overruled = new List<string>();
            foreach (var c in all)
                if (c.OverruledFaction) overruled.Add(c.Display);
            if (overruled.Count > 0)
            {
                string line = $"Counted harmless despite a monster faction ({overruled.Count}): " +
                              string.Join(", ", overruled.ToArray()) + ". Anything in here that can hurt you is a bug.";
                sb.AppendLine(line);
                DudeWhatAreMyStatsMod.Log.LogInfo("[DudeWhatAreMyStats] " + line);
            }

            int shown = 0;
            foreach (var c in all)
            {
                // Critters are in here too: there are only a handful of them and they are the rows
                // most likely to be wrong, since "cannot fight back" is inferred rather than stated.
                bool interesting = c.Tameable || c.Kind == CreatureKind.Critter ||
                                   c.Kind == CreatureKind.Neutral || c.Kind == CreatureKind.Unknown;
                if (filtered)
                {
                    if (c.Key.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) < 0 &&
                        c.Display.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) < 0 &&
                        c.Prefab.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                }
                else if (!everything && !interesting) continue;

                string line = Row(c);
                sb.AppendLine(line);
                // Always to the log as well: the console holds a couple of dozen lines and will not
                // scroll back, so a hundred-row answer is unreadable there by design.
                DudeWhatAreMyStatsMod.Log.LogInfo("[DudeWhatAreMyStats] " + line);
                shown++;
            }
            if (shown == 0) sb.AppendLine("Nothing matched.");
            else sb.AppendLine($"All {shown} row(s) also written to the BepInEx log, which does scroll.");
            return sb.ToString().TrimEnd();
        }

        private static string Row(CreatureInfo c)
        {
            var sb = new StringBuilder();
            sb.Append(CreatureCatalog.TierName(c.Tier).PadRight(12)).Append(' ');
            sb.Append(c.Display).Append(" (").Append(c.Key).Append(") ");
            sb.Append(c.Kind.ToString().ToLowerInvariant());
            sb.Append(", ").Append(c.Faction);
            sb.Append(", ").Append(Mathf.RoundToInt(c.Health)).Append(" hp");
            if (c.OnlyEverTame) sb.Append(", only ever tame");
            else if (c.Tameable) sb.Append(", tameable");
            // The evidence behind the classification, so a wrong call can be argued with rather
            // than just disbelieved.
            sb.Append(c.Armed ? ", can attack" : ", NO ATTACK");
            if (c.Flees) sb.Append(", flees");
            if (c.Overridden) sb.Append(" <set by config>");
            else if (c.Reason.Length > 0) sb.Append(" <").Append(c.Reason).Append(">");
            if (c.Attacks.Length > 0) sb.Append(" [").Append(c.Attacks).Append("]");
            float share = KillSplit.AssumedTameShare(c.Key);
            if (share > 0f) sb.Append(", old kills counted ").Append(Mathf.RoundToInt(share * 100f)).Append("% tame");
            var tally = KillLog.For(c.Key);
            if (tally != null)
                sb.Append(", watched ").Append(Mathf.RoundToInt(tally.Seen))
                  .Append(" (").Append(Mathf.RoundToInt(tally.Tame)).Append(" tame)");
            return sb.ToString();
        }

        /// <summary>
        /// The death log as text. With no argument, the totals and a line per recent death; with a
        /// number, that one death in full, counting 1 as the most recent.
        /// </summary>
        /// <summary>
        /// Every fight report kept, bosses first. Written to the log as well as the console: a long
        /// list scrolls off the top of the console and cannot be scrolled back.
        /// </summary>
        private static string Fights(string which)
        {
            if (DwamsConfig.RecordFights != null && !DwamsConfig.RecordFights.Value)
                return "Record Fights is off in the config, so nothing is being recorded.";
            if (!FightLog.Loaded)
                return "No fight log loaded yet. It opens once a character is in a world.";

            var all = new List<FightReport>();
            all.AddRange(FightLog.Bosses());
            all.AddRange(FightLog.Recent());
            if (all.Count == 0)
                return "No fights recorded yet. Boss fights always count; others have to clear Fight Seconds and " +
                       "Fight Health, and only the game that owns the creature can report one.";

            var sb = new StringBuilder();
            string filter = string.IsNullOrEmpty(which) ? null : which.Trim();
            sb.AppendLine($"{FightLog.BossCount} boss fight(s) and {FightLog.RecentCount} other(s) recorded." +
                          (filter == null ? " Add a name to narrow it." : $" Showing those matching \"{filter}\"."));
            int shown = 0;
            foreach (var r in all)
            {
                if (filter != null && r.Display.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 &&
                    r.CreatureKey.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                shown++;
                foreach (string line in r.Lines()) sb.AppendLine(line);
            }
            if (shown == 0) sb.AppendLine("Nothing matched.");

            string text = sb.ToString().TrimEnd();
            foreach (string line in text.Split('\n'))
                DudeWhatAreMyStatsMod.Log.LogInfo("[DudeWhatAreMyStats] " + line.TrimEnd());
            return text;
        }

        private static string Deaths(string which)
        {
            var sb = new StringBuilder();
            if (DwamsConfig.RecordDeaths != null && !DwamsConfig.RecordDeaths.Value)
                return "Record Deaths is off in the config, so nothing is being recorded.";
            if (!DeathLog.Loaded)
                return "No death log loaded yet. It opens once a character is in a world.";

            var reports = DeathLog.Reports();

            if (!string.IsNullOrEmpty(which))
            {
                if (!int.TryParse(which, out int index) || index < 1 || index > reports.Count)
                    return $"Pick a death between 1 and {reports.Count}, 1 being the most recent.";
                foreach (string line in reports[index - 1].Lines(true)) sb.AppendLine(line);
                return sb.ToString().TrimEnd();
            }

            sb.AppendLine($"{DeathLog.Recorded} death(s) recorded, {reports.Count} kept in full. Add a number for one in detail.");

            var killedBy = DeathLog.KilledBy(0);
            if (killedBy.Count > 0)
            {
                sb.Append("killed by:");
                foreach (var kv in killedBy) sb.Append(' ').Append(DeathReport.CauseName(kv.Key)).Append(" x").Append(Mathf.FloorToInt(kv.Value));
                sb.AppendLine();
            }
            var foughtWith = DeathLog.FoughtWith(0);
            if (foughtWith.Count > 0)
            {
                sb.Append("in the fight:");
                foreach (var kv in foughtWith) sb.Append(' ').Append(DeathReport.CauseName(kv.Key)).Append(" x").Append(Mathf.FloorToInt(kv.Value));
                sb.AppendLine();
            }
            if (reports.Count == 0) sb.AppendLine("No deaths recorded yet. Give it time.");
            for (int i = 0; i < reports.Count; i++)
                sb.AppendLine($"{i + 1}. {reports[i].Lines(false)[0]}");
            return sb.ToString().TrimEnd();
        }
    }
}
