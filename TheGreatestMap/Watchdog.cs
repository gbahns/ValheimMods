using System;
using UnityEngine;

namespace TheGreatestMap
{
    /// <summary>
    /// Why the connection dropped, written down while it happens.
    ///
    /// A player thrown off a server sees "Lost connection", and the server says only "ZRpc
    /// timeout detected": it stopped hearing from that client for thirty seconds. Three very
    /// different faults look identical from there -- the client froze, the client was paused
    /// (a frozen client sends nothing, because ZNet's heartbeat counts in scaled time), or the
    /// packets simply stopped arriving. Guessing between them from the outside is how an evening
    /// disappears.
    ///
    /// So this watches how long a frame took and says something only when one is alarming. The
    /// line carries the wall clock, because the server's log has timestamps and this mod's does
    /// not, and lining the two up is the whole point.
    ///
    /// It used to report long pauses too, and that half has done its work and gone. It answered
    /// the question it was asked -- a pause cannot cost a connection, because the pong that
    /// resets the server's timeout is sent from the receive handler whenever a package arrives
    /// rather than on a timer, so even a frozen client still replies. Once that was known the
    /// lines said nothing, and with the map pausing the game by design they said it every time
    /// anybody opened the map.
    ///
    /// Quiet when nothing is wrong: no line is written for a healthy frame, and the cost is two
    /// comparisons.
    /// </summary>
    internal static class Watchdog
    {
        /// <summary>
        /// A frame this long is a stall a player would notice. Half a second rather than a whole
        /// one: the point is to catch the thing before it grows into a disconnect, and a report
        /// that only fires at the catastrophic end tells you nothing about what led there.
        /// </summary>
        private const float StallSeconds = 0.5f;

        private static float _lastFrame;
        private static float _worst;

        internal static void Reset()
        {
            _lastFrame = 0f;
            _worst = 0f;
        }

        private static string Now => DateTime.Now.ToString("HH:mm:ss");

        internal static void Update()
        {
            float now = Time.unscaledTime;

            if (_lastFrame > 0f)
            {
                float gap = now - _lastFrame;
                if (gap >= StallSeconds)
                {
                    if (gap > _worst) _worst = gap;
                    TheGreatestMapMod.Log.LogWarning(
                        $"[TheGreatestMap] {Now} the game stopped for {gap:0.0} s (worst so far {_worst:0.0} s). " +
                        "A stop of 30 s is long enough for the server to close the connection.");
                }
            }
            _lastFrame = now;
        }
    }
}
