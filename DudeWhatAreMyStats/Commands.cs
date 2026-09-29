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

            new Terminal.ConsoleCommand("dwams_status", "Dude What Are My Stats: what the scoreboard currently knows",
                (Terminal.ConsoleEvent)(args =>
                {
                    var sb = new StringBuilder();
                    sb.AppendLine($"panel: {(StatsPanel.IsOpen ? "open" : "closed")} | holding pause: {StatsPause.Holding} | answered live: {StatsNetwork.KnownCount + 1}");
                    sb.AppendLine(StatsNetwork.ServerHasStore
                        ? $"server store: answering, {StatsNetwork.StoredCount} character(s) remembered"
                        : "server store: no answer yet (the server may not run this mod, which only costs you offline players)");
                    if (DwamsConfig.RecordDeaths.Value)
                        sb.AppendLine(DeathLog.Loaded
                            ? $"death log: {DeathLog.Recorded} recorded, {DeathLog.ReportCount} kept in full" +
                              (DeathWatch.InFight ? $", in a fight with {DeathWatch.LiveCount} creature(s) right now" : "")
                            : "death log: not open yet");
                    else
                        sb.AppendLine("death log: off (Record Deaths)");
                    if (StatsStore.IsServer)
                        sb.AppendLine(StatsStore.Loaded
                            ? $"this game is the server: {StatsStore.Count} character(s) in {StatsStore.Path}"
                            : "this game is the server, but the store is off or has no world yet");
                    foreach (var snap in StatsNetwork.Roster())
                    {
                        string age = snap.LastSeenText;
                        string who = snap.IsLocal ? "you" : snap.Online ? "online" : age.Length > 0 ? "last seen " + age : "offline";
                        sb.AppendLine($"{snap.Name} ({who}): {StatGroups.Count(snap.Kills)} kills, {StatGroups.Count(snap.Deaths)} deaths, " +
                                      $"{StatGroups.Count(snap.Bosses)} bosses, {StatGroups.Duration(snap.Played)} played, best {snap.BestSkillText}");
                    }
                    args.Context?.AddString(sb.ToString().TrimEnd());
                }));
        }

        /// <summary>
        /// The death log as text. With no argument, the totals and a line per recent death; with a
        /// number, that one death in full, counting 1 as the most recent.
        /// </summary>
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
