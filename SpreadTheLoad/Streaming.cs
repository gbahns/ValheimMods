using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace SpreadTheLoad
{
    /// <summary>
    /// Which clients are still being sent the world, so the detector does not mistake that for a
    /// machine that cannot keep up.
    ///
    /// A client loading a region stops sending its own updates for stretches at a time. To the
    /// server that is indistinguishable from a struggling machine, and the detector counted it:
    /// a capture on 2026-10-04 had the admin's own login produce 68 stalls in six minutes - 11.3 a
    /// minute against a threshold of 6 - which flagged the fastest machine on the server as the
    /// one to steer work away from, during the one period when it was not even playing yet.
    /// Detection already ignored single gaps over five seconds for exactly this reason; what it
    /// missed is that loading is mostly made of the shorter gaps, dozens of them.
    ///
    /// A fixed grace period after joining was the obvious fix and the wrong one. That same capture
    /// streamed for five minutes - the client's ZDO count climbed from 7,000 to 30,000 - so a
    /// grace period long enough to cover it would be long enough to miss a genuinely bad machine,
    /// and still would not cover a portal into unexplored ground.
    ///
    /// So this measures the thing itself. The server tracks, per peer, how many ZDOs that peer has
    /// been told about; while that number is climbing quickly the server is still filling them in,
    /// and the client's silences are the cost of receiving rather than evidence about its hardware.
    /// It is self-limiting: it lasts exactly as long as the streaming does, whether that is twenty
    /// seconds through a portal or five minutes at login.
    ///
    /// Reaching it needs ZDOMan's private m_peers and the nested ZDOPeer's m_zdos, as
    /// <see cref="Pacing"/> already does for SendZDOs. A miss leaves this reporting "nobody is
    /// streaming", which returns the detector to its previous behavior rather than breaking it.
    /// </summary>
    internal static class Streaming
    {
        /// <summary>
        /// New ZDOs a second above which the peer is taken to be loading rather than playing.
        ///
        /// A client at rest is told about a trickle - things changing near it. One receiving a
        /// region gets thousands a second; the captures that prompted this showed 433 to 3,351 in
        /// single seconds while regions arrived, against medians near zero once settled. A few
        /// hundred is clear of the trickle and well under the burst.
        /// </summary>
        private const int StreamingZdosPerSecond = 150;

        /// <summary>
        /// Kept on a little after the streaming stops, because the client is still working through
        /// what it received after the server has finished sending it.
        /// </summary>
        private const float TailSeconds = 15f;

        private static FieldInfo _peersField, _zdosField, _peerField;
        private static bool _looked, _usable;

        private sealed class Seen
        {
            internal int Zdos;
            internal float At;
            internal float StreamingUntil = -1f;
        }

        private static readonly Dictionary<long, Seen> _seen = new Dictionary<long, Seen>();

        private static bool Usable()
        {
            if (_looked) return _usable;
            _looked = true;
            try
            {
                var peerType = AccessTools.Inner(typeof(ZDOMan), "ZDOPeer");
                _peersField = AccessTools.Field(typeof(ZDOMan), "m_peers");
                if (peerType != null)
                {
                    _zdosField = AccessTools.Field(peerType, "m_zdos");
                    _peerField = AccessTools.Field(peerType, "m_peer");
                }
                _usable = _peersField != null && _zdosField != null && _peerField != null;
                if (!_usable)
                    SpreadTheLoadMod.Log.LogWarning(
                        "[SpreadTheLoad] could not reach ZDOMan's per-peer ZDO sets; a loading client may " +
                        "be mistaken for a stalling one.");
            }
            catch (Exception e)
            {
                _usable = false;
                SpreadTheLoadMod.Log.LogWarning($"[SpreadTheLoad] streaming detection disabled: {e.Message}");
            }
            return _usable;
        }

        /// <summary>Re-reads every peer's ZDO count. Called once a second, before the detector judges.</summary>
        internal static void Tick(float now)
        {
            if (!Usable()) return;
            try
            {
                var man = ZDOMan.instance;
                if (man == null) return;
                var peers = _peersField.GetValue(man) as IList;
                if (peers == null) return;

                var present = new HashSet<long>();
                for (int i = 0; i < peers.Count; i++)
                {
                    object zp = peers[i];
                    if (zp == null) continue;
                    var netPeer = _peerField.GetValue(zp) as ZNetPeer;
                    if (netPeer == null) continue;
                    long uid = netPeer.m_uid;
                    present.Add(uid);

                    var known = _zdosField.GetValue(zp) as ICollection;
                    int count = known != null ? known.Count : 0;

                    if (!_seen.TryGetValue(uid, out var s))
                    {
                        // First sight. Everything it holds arrived before we were looking, so take
                        // it as streaming: a peer we have only just noticed is one that just joined.
                        _seen[uid] = new Seen { Zdos = count, At = now, StreamingUntil = now + TailSeconds };
                        continue;
                    }

                    float elapsed = now - s.At;
                    if (elapsed < 0.5f) continue;              // too short a gap to rate reliably
                    float perSecond = (count - s.Zdos) / elapsed;
                    s.Zdos = count;
                    s.At = now;
                    if (perSecond >= StreamingZdosPerSecond) s.StreamingUntil = now + TailSeconds;
                }

                if (_seen.Count > present.Count)
                {
                    var gone = new List<long>();
                    foreach (var kv in _seen) if (!present.Contains(kv.Key)) gone.Add(kv.Key);
                    foreach (long uid in gone) _seen.Remove(uid);
                }
            }
            catch (Exception e)
            {
                SpreadTheLoadMod.Log.LogWarning($"[SpreadTheLoad] could not read per-peer ZDO counts: {e.Message}");
                _usable = false;
            }
        }

        /// <summary>
        /// Whether the server is still filling this client in, in which case its silences say
        /// nothing about its machine. False whenever this cannot be measured, so an unreadable
        /// build judges exactly as it did before.
        /// </summary>
        internal static bool IsLoading(long uid) =>
            _seen.TryGetValue(uid, out var s) && s.StreamingUntil >= 0f &&
            UnityEngine.Time.unscaledTime <= s.StreamingUntil;
    }
}
