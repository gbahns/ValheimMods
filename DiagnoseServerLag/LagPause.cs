namespace DiagnoseServerLag
{
    /// <summary>
    /// Option: pause the game while the lag report is open, the way the ESC menu does.
    ///
    /// It goes through vanilla's own Game.Pause / Game.Unpause, the two calls the menu makes, so
    /// the game itself decides whether the pause takes: alone in a solo or hosted game it freezes,
    /// and with other players connected vanilla refuses, which is what you want on a shared server.
    /// If Pause My Server is installed it may grant the pause anyway. Nothing here knows about that
    /// mod, and nothing in the UI claims a pause that is not happening - the button reads
    /// Game.IsPaused() and reports what actually took.
    ///
    /// Pausing matters more here than it does on a map or an inventory panel, because this panel is
    /// reporting on the very thing a pause changes. Freezing the world while you read is what stops
    /// the evidence moving underneath you; see Sampler.Tick, which stops recording while paused so
    /// that reading a diagnosis cannot quietly rewrite it into "nothing wrong right now".
    /// </summary>
    internal static class LagPause
    {
        private static bool _holding;

        /// <summary>Whether the game really was paused last time we looked. Drives the re-assert.</summary>
        private static bool _wasPaused;

        internal static bool Holding => _holding;

        /// <summary>
        /// Hold the pause exactly while the option is on, the report is open and there is a player.
        ///
        /// Called every frame rather than only on the open and close, because of a vanilla detail
        /// that the sibling mods get wrong: Game keeps a single pause flag, not a count, so any
        /// other mod calling Game.Unpause() drops the pause for everyone holding one. MapPause and
        /// PanelPause only act on a transition - `if (want == _holding) return;` - so once that
        /// happens they believe they are still holding a pause that is long gone, and the game
        /// stays running with the panel open.
        ///
        /// Re-asserting is deliberately driven by the paused-to-running transition rather than by
        /// "not paused while holding". A pause can be legitimately refused - a dedicated server with
        /// other players, or one without Pause My Server - and in that case Game.IsPaused() is false
        /// forever; re-asserting on that would call Pause() every frame for a pause that is never
        /// coming. Watching for a pause we actually had and then lost fires exactly once per real
        /// event and stays silent when the answer was simply no.
        /// </summary>
        internal static void Refresh()
        {
            bool want = DslConfig.PauseWhileOpen != null && DslConfig.PauseWhileOpen.Value
                && LagPanel.IsOpen && Player.m_localPlayer != null;

            if (want != _holding)
            {
                _holding = want;
                if (want) Game.Pause();
                else Game.Unpause();
                // Read back rather than assume: Pause() sets a flag, and whether it becomes a real
                // pause is decided elsewhere and possibly not until a round trip to the server.
                _wasPaused = Game.IsPaused();
                return;
            }

            if (!_holding) { _wasPaused = false; return; }

            bool paused = Game.IsPaused();
            if (_wasPaused && !paused)
            {
                Game.Pause();
                DiagnoseServerLagMod.Log.LogInfo("[DiagnoseServerLag] Something else lifted the pause while the report was open; asked again.");
                paused = Game.IsPaused();
            }
            _wasPaused = paused;
        }

        /// <summary>Drops the pause, whatever the report is doing. Used when the world or the mod goes away.</summary>
        internal static void Release()
        {
            if (!_holding) return;
            _holding = false;
            _wasPaused = false;
            Game.Unpause();
        }
    }
}
