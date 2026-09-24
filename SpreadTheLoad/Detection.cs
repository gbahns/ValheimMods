using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace SpreadTheLoad
{
    /// <summary>
    /// Works out for itself which machines are struggling, so the admin does not have to name them.
    ///
    /// The obvious measurement is the wrong one. Ping, connection quality, send rate - everything
    /// the server can read from a socket - describes the *link*, and the client this mod was
    /// written for had a perfectly good link: 24 ms ping, better than one of the healthy players.
    /// What was wrong with it was 16 frames a second, and no network measurement can see that.
    ///
    /// What the server can see is *timing*. A client's outgoing ZDO updates are produced from its
    /// own update loop, so they arrive at whatever pace that machine is managing. Two things fall
    /// out of the arrival times, and only one of them is useful:
    ///
    ///   Rate saturates. The sender is gated to roughly 20 Hz, so a client at 30 fps and one at
    ///   200 fps look identical, and only a machine below about 20 fps registers at all. Too blunt
    ///   to judge anyone on.
    ///
    ///   Gaps do not. A frame that takes 479 ms - the worst one measured on the machine this was
    ///   built for - is a 479 ms hole in the stream, and no healthy client produces one. Stalls are
    ///   also the thing that actually hurts: a stall past Unity's catch-up limit loses simulated
    ///   time outright, which is what makes a hull lurch and a creature teleport.
    ///
    /// So detection is stall-based, and honest about what it knows: a gap means that peer stopped
    /// delivering, not *why*. A frozen machine and a hiccuping connection look the same from here.
    /// That conflation is acceptable, because routing other players' interactions through either
    /// one is a bad idea for the same reason.
    ///
    /// Off by default. Deciding on its own to move work away from a named person is a judgement
    /// the admin should opt into, not inherit.
    /// </summary>
    internal static class Detection
    {
        /// <summary>What one peer has been doing lately.</summary>
        private sealed class Peer
        {
            internal float LastArrival;
            internal readonly Queue<float> Stalls = new Queue<float>();   // when each stall was seen
            internal float QuietSince = -1f;
            internal bool Flagged;
            internal string Name = "";
        }

        private static readonly Dictionary<long, Peer> _peers = new Dictionary<long, Peer>();

        /// <summary>Auto-flagged ids. Deliberately separate from the configured list and never persisted.</summary>
        private static readonly HashSet<long> _flagged = new HashSet<long>();

        /// <summary>
        /// Arrivals further apart than this count as a stall. Well clear of the ~50 ms a healthy
        /// client manages and of the ~63 ms a 16 fps one does, so ordinary slowness is not a stall;
        /// this is looking for the machine stopping, not being slow.
        /// </summary>
        private const float StallGapSeconds = 0.30f;

        /// <summary>
        /// A gap this long is something else - a player loading a zone, a connection dropping, a
        /// world save - and counting it would flag people for things that are not their machine.
        /// </summary>
        private const float IgnoreGapSeconds = 5f;

        /// <summary>How far back the stall count looks.</summary>
        private const float WindowSeconds = 120f;

        /// <summary>
        /// Clearing the flag needs a longer clean run than setting it needed stalls, because
        /// ownership moving back and forth costs more than leaving it where it is. Asymmetry here
        /// is what stops a borderline machine flapping.
        /// </summary>
        private const float RecoverySeconds = 300f;

        internal static bool IsFlagged(long uid) => _flagged.Contains(uid);

        /// <summary>Called for every ZDO update the server receives, from the Harmony patch below.</summary>
        internal static void NoteArrival(long uid)
        {
            if (SpreadTheLoadMod.AutoDetect == null || !SpreadTheLoadMod.AutoDetect.Value) return;

            float now = Time.unscaledTime;
            if (!_peers.TryGetValue(uid, out var p)) _peers[uid] = p = new Peer { LastArrival = now };

            float gap = now - p.LastArrival;
            p.LastArrival = now;
            if (gap > StallGapSeconds && gap < IgnoreGapSeconds) p.Stalls.Enqueue(now);
        }

        /// <summary>Re-judges everyone. Called once a second from the plugin's update.</summary>
        internal static void Tick(float now, ZNet znet)
        {
            if (SpreadTheLoadMod.AutoDetect == null || !SpreadTheLoadMod.AutoDetect.Value)
            {
                if (_flagged.Count > 0) { _flagged.Clear(); _peers.Clear(); }
                return;
            }

            int threshold = Mathf.Max(1, SpreadTheLoadMod.StallsPerMinute.Value);
            var connected = new HashSet<long>();

            foreach (var peer in znet.GetConnectedPeers())
            {
                if (peer == null || !peer.IsReady()) continue;
                connected.Add(peer.m_uid);
                if (!_peers.TryGetValue(peer.m_uid, out var p)) continue;
                p.Name = peer.m_playerName ?? "";

                while (p.Stalls.Count > 0 && now - p.Stalls.Peek() > WindowSeconds) p.Stalls.Dequeue();

                // Per minute, from a two-minute window, so one bad moment does not flag anybody.
                float perMinute = p.Stalls.Count / (WindowSeconds / 60f);

                if (!p.Flagged)
                {
                    if (perMinute < threshold) continue;
                    if (!WouldLeaveSomebodyCapable(znet, peer.m_uid)) continue;
                    p.Flagged = true;
                    p.QuietSince = -1f;
                    _flagged.Add(peer.m_uid);
                    SpreadTheLoadMod.Log.LogInfo(
                        $"[SpreadTheLoad] {Describe(p, peer.m_uid)} is stalling ({perMinute:0.0}/min, threshold {threshold}); " +
                        "steering shared objects away from them until it settles.");
                    continue;
                }

                // Recovering: a clean run, not merely a quieter one.
                if (perMinute >= threshold) { p.QuietSince = -1f; continue; }
                if (p.QuietSince < 0f) p.QuietSince = now;
                if (now - p.QuietSince < RecoverySeconds) continue;

                p.Flagged = false;
                p.QuietSince = -1f;
                _flagged.Remove(peer.m_uid);
                SpreadTheLoadMod.Log.LogInfo(
                    $"[SpreadTheLoad] {Describe(p, peer.m_uid)} has run clean for {RecoverySeconds / 60f:0} minutes; " +
                    "giving them a normal share again.");
            }

            // Somebody who left stops being flagged; nothing is carried across sessions.
            if (_flagged.Count == 0) return;
            var gone = new List<long>();
            foreach (var uid in _flagged) if (!connected.Contains(uid)) gone.Add(uid);
            foreach (var uid in gone) { _flagged.Remove(uid); _peers.Remove(uid); }
        }

        private static string Describe(Peer p, long uid) =>
            string.IsNullOrEmpty(p.Name) ? uid.ToString() : p.Name;

        /// <summary>
        /// Refuses to flag the last healthy player. If everybody is struggling there is nobody to
        /// hand work to, and flagging the whole server would only churn ownership for no gain.
        /// </summary>
        private static bool WouldLeaveSomebodyCapable(ZNet znet, long candidate)
        {
            foreach (var peer in znet.GetConnectedPeers())
            {
                if (peer == null || !peer.IsReady()) continue;
                if (peer.m_uid == candidate) continue;
                if (_flagged.Contains(peer.m_uid)) continue;
                if (Ownership.IsYielding(peer.m_uid)) continue;
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Timestamps every ZDO update the server receives, per peer. This is the only place the server
    /// learns anything about how a client's own update loop is doing.
    /// </summary>
    [HarmonyPatch(typeof(ZDOMan), "RPC_ZDOData")]
    internal static class ZDODataPatch
    {
        private static void Postfix(ZRpc rpc)
        {
            try
            {
                var znet = ZNet.instance;
                if (znet == null || !znet.IsServer() || rpc == null) return;
                foreach (var peer in znet.GetPeers())
                {
                    if (peer == null || peer.m_rpc != rpc) continue;
                    Detection.NoteArrival(peer.m_uid);
                    return;
                }
            }
            catch { /* measurement must never break the receive path */ }
        }
    }
}
