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
    /// So this keeps two numbers and says something only when one of them is alarming: how long
    /// the last frame took, and how long the game has been paused. Each line carries the wall
    /// clock, because the server's log has timestamps and this mod's does not, and lining the
    /// two up is the whole point.
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

        /// <summary>A pause worth noting in the log when reading one back.</summary>
        private const float PauseWarnSeconds = 20f;

        private static float _lastFrame;
        private static float _pausedSince;
        private static bool _warnedPause;
        private static float _worst;

        internal static void Reset()
        {
            _lastFrame = 0f;
            _pausedSince = 0f;
            _warnedPause = false;
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

            // Paused is not stopped, and it does not cost a connection: the pong that resets the
            // server's timeout is sent from the receive handler, which answers whenever a package
            // arrives rather than on a timer, so a frozen client still replies. Worth logging all
            // the same, because a long pause in the log explains a long gap in everything else.
            bool paused = Time.timeScale == 0f;
            if (paused)
            {
                if (_pausedSince == 0f) { _pausedSince = now; _warnedPause = false; }
                else if (!_warnedPause && now - _pausedSince >= PauseWarnSeconds)
                {
                    _warnedPause = true;
                    TheGreatestMapMod.Log.LogInfo(
                        $"[TheGreatestMap] {Now} paused for {now - _pausedSince:0} s and counting.");
                }
            }
            else if (_pausedSince > 0f)
            {
                float held = now - _pausedSince;
                _pausedSince = 0f;
                if (held >= PauseWarnSeconds)
                    TheGreatestMapMod.Log.LogInfo($"[TheGreatestMap] {Now} was paused for {held:0} s.");
            }
        }
    }
}
