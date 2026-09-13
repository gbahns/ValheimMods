using System.Text;

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

            new Terminal.ConsoleCommand("dwams_refresh", "Dude What Are My Stats: ask the other players for their stats again",
                (Terminal.ConsoleEvent)(args =>
                {
                    StatsNetwork.Request();
                    args.Context?.AddString("Asked everyone online for their stats.");
                }));

            new Terminal.ConsoleCommand("dwams_status", "Dude What Are My Stats: what the scoreboard currently knows",
                (Terminal.ConsoleEvent)(args =>
                {
                    var sb = new StringBuilder();
                    sb.AppendLine($"panel: {(StatsPanel.IsOpen ? "open" : "closed")} | holding pause: {StatsPause.Holding} | players known: {StatsNetwork.KnownCount + 1}");
                    foreach (var snap in StatsNetwork.Roster())
                    {
                        string who = snap.IsLocal ? "you" : (snap.Online ? "online" : "offline");
                        sb.AppendLine($"{snap.Name} ({who}): {StatGroups.Count(snap.Kills)} kills, {StatGroups.Count(snap.Deaths)} deaths, " +
                                      $"{StatGroups.Count(snap.Bosses)} bosses, {StatGroups.Duration(snap.Played)} played, best {snap.BestSkillText}");
                    }
                    args.Context?.AddString(sb.ToString().TrimEnd());
                }));
        }
    }
}
