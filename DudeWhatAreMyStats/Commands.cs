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
                    StatsNetwork.RequestNow();
                    args.Context?.AddString("Asked everyone online, and the server, for stats.");
                }));

            new Terminal.ConsoleCommand("dwams_status", "Dude What Are My Stats: what the scoreboard currently knows",
                (Terminal.ConsoleEvent)(args =>
                {
                    var sb = new StringBuilder();
                    sb.AppendLine($"panel: {(StatsPanel.IsOpen ? "open" : "closed")} | holding pause: {StatsPause.Holding} | answered live: {StatsNetwork.KnownCount + 1}");
                    sb.AppendLine(StatsNetwork.ServerHasStore
                        ? $"server store: answering, {StatsNetwork.StoredCount} character(s) remembered"
                        : "server store: no answer yet (the server may not run this mod, which only costs you offline players)");
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
    }
}
