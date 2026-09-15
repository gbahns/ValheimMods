namespace Armory
{
    /// <summary>
    /// Option: pause the game while the Armory Rack panel is up, the way the ESC menu does.
    /// It goes through vanilla's own Game.Pause / Game.Unpause, the two calls the menu makes, so
    /// alone in a solo or hosted game the game freezes by itself, and on a dedicated server it is
    /// Pause My Server, if installed, that decides. Nothing here knows about that mod.
    ///
    /// This replaced a direct Time.timeScale write. Game.UpdatePause() assigns Time.timeScale
    /// every frame from Game.Update, so setting it ourselves was a per-frame tug of war that only
    /// came out our way on script execution order, and it stopped the local clock even in
    /// multiplayer, where the server keeps running regardless.
    /// </summary>
    internal static class ArmoryPause
    {
        private static bool _holding;

        internal static bool Holding => _holding;

        /// <summary>Hold the pause exactly while the option is on and the rack panel is up; let go otherwise.</summary>
        internal static void Refresh()
        {
            bool want = ArmoryMod.PauseGameWhileOpen != null && ArmoryMod.PauseGameWhileOpen.Value
                     && ArmoryUI.IsOpen
                     && Player.m_localPlayer != null;

            if (want == _holding)
            {
                // Re-assert instead of returning early. Vanilla keeps one pause flag, not a
                // count, so anything else calling Game.Unpause() over our panel — the ESC menu
                // closing, a cinematic ending — drops our request with nothing left to notice.
                // Game.Pause() only sets that flag, and Pause My Server sends its RPC only when
                // the value changes, so holding it down every frame costs nothing.
                if (want && !Game.IsPaused()) Game.Pause();
                return;
            }

            _holding = want;
            if (want) Game.Pause();
            else      Game.Unpause();

            // The inventory's slide has to come off game time before the clock stops, and go
            // back on it once the clock runs again.  See ArmoryUI.SetInventoryGuiUnscaled.
            ArmoryUI.SetInventoryGuiUnscaled(want);

            // Only ask whether it was granted on the way in. Game.CanPause() dereferences
            // ZNet.instance before its own null check on it, so it throws once the player is
            // gone — which is exactly when the release path runs.
            Jotunn.Logger.LogInfo(want
                ? $"[Armory] Pause: requested, granted={Game.IsPaused()}"
                : "[Armory] Pause: released");
        }

        /// <summary>Let go at once rather than waiting for the next Refresh, so closing the panel resumes on the same frame.</summary>
        internal static void Release()
        {
            if (!_holding) return;
            _holding = false;
            Game.Unpause();
            ArmoryUI.SetInventoryGuiUnscaled(false);
        }
    }
}
