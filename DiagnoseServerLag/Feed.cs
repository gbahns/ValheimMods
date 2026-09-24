using HarmonyLib;
using UnityEngine;

namespace DiagnoseServerLag
{
    /// <summary>
    /// How often the server actually tells this client anything.
    ///
    /// The server does not broadcast. It serves one peer per frame behind a 50 ms gate:
    ///
    ///     m_sendTimer += dt;
    ///     if (m_nextSendPeer &lt; 0) {
    ///         if (m_sendTimer &gt; 0.05f) { m_nextSendPeer = 0; m_sendTimer = 0f; }
    ///         return;
    ///     }
    ///     if (m_nextSendPeer &lt; m_peers.Count) SendZDOs(m_peers[m_nextSendPeer], flush: false);
    ///     m_nextSendPeer++;
    ///
    /// so a full cycle is 50 ms plus one server frame per connected player. At the dedicated
    /// server's 30 Hz cap that is 150 ms at three players, 217 ms at five, 317 ms at eight.
    ///
    /// This is the measurement that CPU and tick time cannot make. A server can be 95% idle, hold
    /// a perfect 33.3 ms tick, and still only reach you six times a second - the cost is in the
    /// scheduling, not the load, so every other number on the readout says the server is fine
    /// while this one grows with the size of the group. It is the one remaining candidate for lag
    /// that gets worse the more people are playing, which is the complaint this mod was built for.
    ///
    /// Measured rather than assumed, and reported next to what the formula predicts, so the theory
    /// can be confirmed or dropped rather than believed.
    /// </summary>
    internal static class Feed
    {
        /// <summary>Recent gaps between updates from the server, in milliseconds.</summary>
        private static readonly float[] _gaps = new float[64];
        private static readonly float[] _sorted = new float[64];
        private static int _count, _next;
        private static float _last = -1f;

        /// <summary>
        /// Gaps past this are something other than the send cycle - a zone load, a pause, a
        /// reconnect - and would drag the median somewhere meaningless.
        /// </summary>
        private const float IgnoreAboveMs = 2000f;

        /// <summary>
        /// The typical gap between updates from the server, or 0 before enough have arrived.
        /// The median rather than the mean: one stall should not move it.
        /// </summary>
        internal static float IntervalMs
        {
            get
            {
                if (_count < 8) return 0f;
                System.Array.Copy(_gaps, _sorted, _count);
                System.Array.Sort(_sorted, 0, _count);
                return _sorted[_count / 2];
            }
        }

        /// <summary>
        /// What the send cycle should cost, from the server's own tick and player count: the 50 ms
        /// gate plus one server frame per peer. Zero when the server has not reported yet.
        /// </summary>
        internal static float PredictedMs(ServerReport report)
        {
            // PeerCount rather than Peers.Count: the per-peer table is only populated when the
            // server could read its sockets, which it cannot on a server without Steamworks
            // initialised, while the count itself is always there.
            int peers = report == null ? 0 : report.PeerCount;
            if (peers <= 0) return 0f;
            float frame = Mathf.Max(1f, report.TickMsAvg);
            return 50f + peers * frame;
        }

        internal static void Note()
        {
            // Only meaningful on a client, where every ZDO update comes from the server. On the
            // server the same callback fires once per connected player and the gaps would measure
            // something else entirely.
            var znet = ZNet.instance;
            if (znet == null || znet.IsServer()) return;

            float now = Time.unscaledTime;
            if (_last >= 0f)
            {
                float gap = (now - _last) * 1000f;
                if (gap > 0f && gap < IgnoreAboveMs)
                {
                    _gaps[_next] = gap;
                    _next = (_next + 1) % _gaps.Length;
                    if (_count < _gaps.Length) _count++;
                }
            }
            _last = now;
        }

        internal static void Reset()
        {
            _count = 0; _next = 0; _last = -1f;
        }
    }

    /// <summary>
    /// Times every ZDO update arriving from the server. Registered per peer as "ZDOData", so on a
    /// client this fires exactly once per turn the server gives us.
    /// </summary>
    [HarmonyPatch(typeof(ZDOMan), "RPC_ZDOData")]
    internal static class ZdoFeedPatch
    {
        private static void Postfix()
        {
            try { Feed.Note(); }
            catch { /* a measurement must never break the receive path */ }
        }
    }
}
