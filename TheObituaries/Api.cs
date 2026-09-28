using UnityEngine;

namespace TheObituaries
{
    /// <summary>
    /// How another mod says what a death of its own making should read as.
    ///
    /// Valheim records only the blow, so a mod that kills a player itself leaves a HitData with
    /// no attacker and no cause, and the obituary for it can only be the blank "{v} died". Such a
    /// mod calls <see cref="NextDeath"/> with the line it wants just before dealing the blow, and
    /// the obituary for that death reads as asked instead.
    ///
    /// Public on purpose: this is the one thing here another assembly is meant to call, and it is
    /// meant to be called by reflection (AccessTools.TypeByName("TheObituaries.Api")), so neither
    /// mod needs a reference to the other and either one can be absent.
    /// </summary>
    public static class Api
    {
        private const float Grace = 3f;   // how long a claim stays good, in seconds

        private static string _line;
        private static float _claimedAt;

        /// <summary>
        /// Claims the local player's next death. <paramref name="line"/> is a template: {v} is
        /// the victim's name and {k} a killer, if it names one; everything else reads as written.
        /// A claim is used once and expires after a few seconds, so a death that never arrives
        /// (the blow was survived, or refused) leaves nothing behind to surprise the next one.
        /// Passing an empty line drops a claim already made.
        /// </summary>
        public static void NextDeath(string line)
        {
            _line = string.IsNullOrEmpty(line) ? null : line;
            _claimedAt = Time.time;
        }

        /// <summary>The claimed line if one is still good, forgetting it either way.</summary>
        internal static string Take()
        {
            string line = _line;
            _line = null;
            if (line == null) return null;
            return Time.time - _claimedAt <= Grace ? line : null;
        }
    }
}
