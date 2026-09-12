namespace PauseMyServer
{
    /// <summary>Console commands (F5 in game, or the dedicated server's console). Prefixed pms_.</summary>
    internal static class Commands
    {
        internal static void Register()
        {
            new Terminal.ConsoleCommand("pms_pause",
                "Pause My Server: toggle the server-wide admin pause (same as the pause key). " +
                "On the dedicated server's own console it toggles directly, which is the way out if no admin is online.",
                (Terminal.ConsoleEvent)(args =>
                {
                    var znet = ZNet.instance;
                    if (znet == null) { args.Context?.AddString("Not in a game."); return; }
                    if (znet.IsServer() && Player.m_localPlayer == null)
                    {
                        // Dedicated server console: no player, no admin list needed.
                        PauseSync.SetAdminPause(!PauseSync.AdminPaused, "the server console");
                        args.Context?.AddString(PauseSync.AdminPaused ? "Server paused." : "Server resumed.");
                        return;
                    }
                    PauseSync.SendAdminToggle();
                    args.Context?.AddString("Pause toggle requested.");
                }));

            new Terminal.ConsoleCommand("pms_status", "Pause My Server: current pause state",
                (Terminal.ConsoleEvent)(args => args.Context?.AddString(PauseSync.Status())));
        }
    }
}
