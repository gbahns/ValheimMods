namespace SpreadTheLoad
{
    /// <summary>
    /// The one thing this mod exposes to others, deliberately small.
    ///
    /// DiagnoseServerLag greys a player's row when work is being steered away from them. It cannot
    /// work that out alone: this mod is server-only and that readout is drawn on the client, so the
    /// server half of DSL asks here and passes the answer down with its report.
    ///
    /// Public and stable so the caller can reach it by reflection without a hard reference. DSL has
    /// to keep working on a server where this mod is not installed at all, which is the normal case
    /// for everybody else's server, so the dependency only ever runs in one direction and only when
    /// the type happens to be there.
    ///
    /// Nothing here throws. Every answer is "no" when the mod is off, nobody is configured, or
    /// anything at all goes wrong - a display hint is never worth a failure.
    /// </summary>
    public static class SpreadTheLoadApi
    {
        /// <summary>True when the mod is loaded, enabled, and actually steering somebody.</summary>
        public static bool Active
        {
            get
            {
                try
                {
                    return SpreadTheLoadMod.Enabled != null
                           && SpreadTheLoadMod.Enabled.Value
                           && Ownership.AnyYielding;
                }
                catch { return false; }
            }
        }

        /// <summary>True when work is being steered away from this peer right now.</summary>
        public static bool IsYielding(long uid)
        {
            try { return Ownership.IsYielding(uid); }
            catch { return false; }
        }
    }
}
