namespace DudeWhatAreMyStats
{
    /// <summary>
    /// Pauses the game while the stats panel is open, the way the ESC menu does.
    ///
    /// It goes through vanilla's own Game.Pause / Game.Unpause, the two calls the menu makes, so
    /// the game itself decides whether the pause takes: alone in a solo or hosted game it freezes,
    /// and with other players connected vanilla refuses, which is what you want on a shared server.
    /// If Pause My Server is installed it hooks those same two calls and may grant the pause anyway.
    /// Nothing here knows about that mod.
    /// </summary>
    internal static class StatsPause
    {
        private static bool _holding;

        internal static bool Holding => _holding;

        /// <summary>Hold the pause exactly while the option is on, the panel is open and there is a player.</summary>
        internal static void Refresh()
        {
            bool want = DwamsConfig.PauseWhileOpen != null && DwamsConfig.PauseWhileOpen.Value
                && StatsPanel.IsOpen && Player.m_localPlayer != null;
            if (want == _holding) return;
            _holding = want;
            if (want) Game.Pause();
            else Game.Unpause();
        }

        /// <summary>Drops the pause, whatever the panel is doing. Used when the world or the mod goes away.</summary>
        internal static void Release()
        {
            if (!_holding) return;
            _holding = false;
            Game.Unpause();
        }
    }
}
