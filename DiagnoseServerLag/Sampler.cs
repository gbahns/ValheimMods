using System;
using System.Collections.Generic;
using UnityEngine;

namespace DiagnoseServerLag
{
    /// <summary>What one connected player looked like from the server's side during a second.</summary>
    internal struct PeerSample
    {
        internal long Uid;
        internal string Name;
        internal int Ping;
        internal bool HasPing;
        internal float Quality;
        internal int SendQueue;
        internal int SendRate;
        /// <summary>The ping came from the mod's own round trip, not from the socket.</summary>
        internal bool PingFromRoundTrip;
        /// <summary>How far this player is from the world center; useful for spotting who is loading what.</summary>
        internal float DistanceFromCenter;
    }

    /// <summary>
    /// Takes the measurements, once a second, on whichever end of the connection it is running.
    ///
    /// The same code runs on the client and on the dedicated server because the questions are the
    /// same on both ends - how long did the frames take, how much is queued, how many objects are
    /// moving - and only the interpretation differs. Which end this is decides what is reachable:
    /// a headless server instantiates nothing so it has no ZNetScene instance count, and a client
    /// has exactly one peer, the server, so its per-peer table has one row.
    ///
    /// Sampling is cheap on purpose. Every call in here is a counter read or a dictionary Count;
    /// nothing walks the object graph, because a diagnostic that costs a millisecond a frame would
    /// become a cause of the thing it is trying to explain.
    /// </summary>
    internal static class Sampler
    {
        /// <summary>
        /// The rolling history, one sample a second.
        ///
        /// Not readonly, because the length is configurable and the config is not bound yet when
        /// this type is initialised - ApplyCapacity replaces it once it is. A sample is about 110
        /// bytes, so an hour costs roughly 400 KB and the three-hour maximum about 1.2 MB, which is
        /// why the ceiling is set by what is useful to capture rather than by what it costs.
        /// </summary>
        internal static Ring History { get; private set; } = new Ring(3600);   // one hour at 1 Hz

        /// <summary>
        /// Resizes the history to the configured length. Called once, after the config is bound.
        ///
        /// Anything already recorded is dropped rather than copied across: this runs at startup
        /// before a world exists, so there is nothing worth keeping, and carrying samples from one
        /// ring to another is machinery with no caller.
        /// </summary>
        internal static void ApplyCapacity(int seconds)
        {
            seconds = Math.Max(60, seconds);
            if (History.Capacity == seconds) return;
            History = new Ring(seconds);
            _started = false;
        }

        /// <summary>The per-peer detail behind the newest sample. Server-side only; empty on a client.</summary>
        internal static readonly List<PeerSample> Peers = new List<PeerSample>();

        // Accumulated across the frames of the second currently being measured.
        private static float _bucketStart;
        private static float _bucketTotalMs;
        private static float _bucketMaxMs;
        private static int _bucketFrames;
        private static int _bucketStalls;

        private static bool _started;

        /// <summary>One line per session when a socket cannot be read; see NoteSocketUnreadable.</summary>
        private static bool _socketWarningLogged;

        /// <summary>
        /// True while the game is paused and the history is deliberately standing still.
        /// The report says so, because a frozen measurement that looked live would be a lie.
        /// </summary>
        internal static bool Frozen { get; private set; }

        internal static bool IsServerHere => ZNet.instance != null && ZNet.instance.IsServer();
        internal static bool IsDedicatedHere => ZNet.instance != null && ZNet.instance.IsDedicated();

        /// <summary>Whether there is anything to measure yet: no world, nothing to say.</summary>
        internal static bool Live => ZNet.instance != null;

        internal static void Reset()
        {
            History.Clear();
            Peers.Clear();
            _started = false;
            Frozen = false;
            Machine.Reset();
            _bucketTotalMs = 0f;
            _bucketMaxMs = 0f;
            _bucketFrames = 0;
            _bucketStalls = 0;
        }

        /// <summary>Call once per frame from the plugin's Update.</summary>
        internal static void Tick()
        {
            if (!Live) { if (_started) Reset(); return; }

            // Unscaled: a paused game still renders, and the ESC menu freezing the simulation must
            // not read as the machine having stopped keeping up.
            float now = Time.unscaledTime;
            if (!_started)
            {
                _started = true;
                _bucketStart = now;
                return;     // the first frame after loading is always long; it measures the load, not the game
            }

            // Nothing is recorded while the game is paused, and this is the most important line in
            // the file for a mod that offers to pause itself.
            //
            // A paused world simulates nothing, so frames get cheap, object traffic stops and the
            // link goes quiet. Recorded, those seconds would flow into the same window and baseline
            // the verdict is computed from, and the report would talk itself round to "nothing wrong
            // right now" while its reader sat looking at it - worst of all for someone who paused
            // precisely to read why the last minute was bad. Holding the history still instead means
            // a pause freezes the evidence, which is what makes pausing worth offering here at all.
            //
            // The partial second in progress is thrown away rather than committed, so a second that
            // was half real play and half frozen never becomes a data point.
            if (Game.IsPaused())
            {
                Frozen = true;
                _bucketStart = now;
                _bucketTotalMs = 0f;
                _bucketMaxMs = 0f;
                _bucketFrames = 0;
                _bucketStalls = 0;
                return;
            }
            Frozen = false;

            float frameMs = Time.unscaledDeltaTime * 1000f;
            _bucketFrames++;
            _bucketTotalMs += frameMs;
            if (frameMs > _bucketMaxMs) _bucketMaxMs = frameMs;
            if (frameMs >= DslConfig.StallMs.Value) _bucketStalls++;

            if (now - _bucketStart < 1f) return;
            Commit(now);
            _bucketStart = now;
            _bucketTotalMs = 0f;
            _bucketMaxMs = 0f;
            _bucketFrames = 0;
            _bucketStalls = 0;
        }

        private static void Commit(float now)
        {
            var s = new Sample
            {
                At = now,
                Frames = _bucketFrames,
                FrameMsAvg = _bucketFrames > 0 ? _bucketTotalMs / _bucketFrames : 0f,
                FrameMsMax = _bucketMaxMs,
                Stalls = _bucketStalls,
            };

            var znet = ZNet.instance;
            if (znet != null)
            {
                // On a client this reads the server connection. On a server it averages every
                // peer, which is the right headline number there: one player on a bad line should
                // not be reported as the server having a bad line.
                znet.GetNetStats(out float localQ, out float remoteQ, out int ping, out float outBps, out float inBps);
                s.Ping = ping;
                s.LocalQuality = localQ;
                s.RemoteQuality = remoteQ;
                s.OutByteSec = outBps;
                s.InByteSec = inBps;
                // Zero ping means "the socket cannot tell us", not "instant". Only ZSteamSocket
                // fills these in; the plain TCP path in ZNetStats hardcodes ping and quality to
                // zero, so a real reading has to be distinguished from an absent one or the
                // verdict would congratulate a broken link on its latency.
                s.HasPing = ping > 0 || localQ > 0f;

                CollectSockets(znet, ref s);
            }

            var zdoMan = ZDOMan.instance;
            if (zdoMan != null)
            {
                s.Zdos = zdoMan.NrOfObjects();
                // ZDOMan refreshes these once a second from its own timer, so they are already
                // "per second" and reading them costs nothing and resets nothing.
                s.ZdosSent = zdoMan.GetSentZDOs();
                s.ZdosRecv = zdoMan.GetRecvZDOs();
                s.ChangeQueue = zdoMan.GetClientChangeQueue();
            }

            // A dedicated server never instantiates prefabs, so this stays zero there and the
            // verdict rules that use it are client-only by construction.
            var scene = ZNetScene.instance;
            if (scene != null) s.Instances = scene.NrOfInstances();

            // The machine-level half: CPU, memory and collections. This is the part that still
            // means something on a frame-capped server, where tick time is constant by
            // construction and says nothing about how much room is left.
            Machine.Fill(ref s, now);

            History.Add(s);
        }

        /// <summary>
        /// Reads the send queues, and on a server builds the per-peer table.
        ///
        /// The send queue is the measurement this whole mod is built around, so it is worth being
        /// precise about which queue is being read. On a client there is one socket, the one to
        /// the server, and its queue is what this machine has failed to upload. On a server there
        /// is a socket per player and each queue is what that player has failed to download - so
        /// the server's headline figure is the worst of them, because one saturated player is a
        /// real problem even when the other five are fine.
        /// </summary>
        private static void CollectSockets(ZNet znet, ref Sample s)
        {
            Peers.Clear();

            // Only peers that have finished handshaking count as connected. Everything in m_peers
            // is returned by GetConnectedPeers, including sockets still being set up or torn down,
            // and reporting those as players put "2 connected" above an empty table.
            int ready = 0;
            foreach (var p in znet.GetConnectedPeers())
                if (p != null && p.IsReady()) ready++;
            s.Peers = ready;

            if (!znet.IsServer())
            {
                var server = znet.GetServerPeer();
                var socket = server?.m_socket;
                if (socket == null) return;
                // Separately, for the same reason as the per-peer reads below: on a PlayFab socket
                // the queue size is real and the send rate throws NotImplementedException, and one
                // try around both would throw the usable number away with the missing one.
                //
                // Clamped, because the game hands back negative queue sizes. ZSteamSocket's
                // GetSendQueueSize sums its own queued byte arrays and Steam's pending counters,
                // none of which can be negative on their own, yet a real session reported
                // "-21294 B queued". Whatever Steam is reporting through that struct, a negative
                // backlog is not a measurement, and letting it through both printed nonsense and
                // fed the saturation rule a number it would silently read as healthy.
                try { s.SendQueue = Mathf.Max(0, socket.GetSendQueueSize()); }
                catch (Exception e) { NoteSocketUnreadable(e); }
                try { s.SendRate = Mathf.Max(0, socket.GetCurrentSendRate()); }
                catch (Exception e) { NoteSocketUnreadable(e); }

                // The server connection cannot report latency on a PlayFab or plain socket, so use
                // the round trip of the mod's own request instead. Flagged, so the report can say
                // which it is rather than passing one off as the other.
                if (!s.HasPing && LagNetwork.RoundTripMs > 0f)
                {
                    s.Ping = Mathf.RoundToInt(LagNetwork.RoundTripMs);
                    s.HasPing = true;
                    s.PingFromRoundTrip = true;
                }
                return;
            }

            int worstQueue = 0;
            int totalRate = 0;
            foreach (var peer in znet.GetConnectedPeers())
            {
                // Vanilla's own GetNetStats walks the same list and touches a socket only when
                // IsReady() - which is simply "has a uid yet" - and that guard is the entire reason
                // vanilla does not throw here. Without it, a peer that is mid-handshake or whose
                // socket is being disposed throws "Steamworks is not initialized" on the first
                // socket call, every second, for as long as it sits in the list. On the real server
                // that filled 452 of the last 600 console lines with one warning, with nobody
                // connected - a diagnostic mod making the server harder to diagnose.
                //
                // Such a peer has no uid and no name, so there is nothing to show for it either.
                if (peer == null || !peer.IsReady() || peer.m_socket == null) continue;

                // Identity first, and kept whatever the socket does next. Building the row after
                // the socket call was the second half of the bug: every peer that threw was dropped
                // before it was ever added, so the per-player table - one of the things this mod
                // exists to show - came out empty, and the worst-queue figure sat at a reassuring
                // 0 B that nothing had actually measured.
                var ps = new PeerSample
                {
                    Uid = peer.m_uid,
                    Name = string.IsNullOrEmpty(peer.m_playerName) ? "(connecting)" : peer.m_playerName,
                    DistanceFromCenter = peer.m_refPos.magnitude,
                };

                // Each figure is read on its own, because the three are not equally available and
                // failing together wastes the ones that work. On this server's PlayFab peers,
                // GetConnectionQuality is inherited from ZNetStats and returns hardcoded zeros,
                // GetSendQueueSize returns a real in-flight byte count, and GetCurrentSendRate
                // throws NotImplementedException outright. Reading all three in one try meant that
                // last throw discarded the queue size - the one per-peer number genuinely
                // measurable here, and the one that detects saturation.
                try
                {
                    peer.m_socket.GetConnectionQuality(out float localQ, out _, out int ping, out _, out _);
                    ps.Quality = localQ;
                    if (ping > 0 || localQ > 0f) { ps.Ping = ping; ps.HasPing = true; }
                }
                catch (Exception e) { NoteSocketUnreadable(e); }

                try
                {
                    ps.SendQueue = Mathf.Max(0, peer.m_socket.GetSendQueueSize());
                    if (ps.SendQueue > worstQueue) worstQueue = ps.SendQueue;
                }
                catch (Exception e) { NoteSocketUnreadable(e); }

                try
                {
                    ps.SendRate = Mathf.Max(0, peer.m_socket.GetCurrentSendRate());
                    totalRate += ps.SendRate;
                }
                catch (Exception e) { NoteSocketUnreadable(e); }

                // Nothing on this server's sockets can answer for latency, so fall back to what the
                // mod measured itself: the round trip of its own request to this player. Only
                // players running the mod have one, which is honest rather than absent.
                if (!ps.HasPing)
                {
                    float rtt = LagNetwork.PeerRoundTripMs(peer.m_uid);
                    if (rtt > 0f) { ps.Ping = Mathf.RoundToInt(rtt); ps.HasPing = true; ps.PingFromRoundTrip = true; }
                }

                Peers.Add(ps);
            }

            s.SendQueue = worstQueue;
            s.SendRate = totalRate;
        }

        /// <summary>
        /// Says once that a socket could not be read, and then stops saying it.
        ///
        /// Deliberately not a latch that switches the reading off: the cause is per-peer and
        /// transient - a connection still being set up, or one being torn down - so refusing to read
        /// sockets ever again because one stale peer threw would trade a noisy bug for a silent one
        /// and cost the per-player table for everybody else.
        ///
        /// What is latched is the logging. A condition that repeats once a second per peer is worth
        /// exactly one line; the alternative is what the server console looked like before this.
        /// </summary>
        private static void NoteSocketUnreadable(Exception e)
        {
            if (_socketWarningLogged) return;
            _socketWarningLogged = true;
            DiagnoseServerLagMod.Log.LogInfo(
                $"[DiagnoseServerLag] A socket could not be read ({e.Message}). Ping, connection quality " +
                "and queue size are left as not measurable for that peer; everything else still works. " +
                "Said once per session, not once a second.");
        }

        /// <summary>The most recent completed second, if there is one.</summary>
        internal static bool TryNewest(out Sample s) => History.TryNewest(out s);

        /// <summary>The baseline window used to judge what "normal" looks like right now.</summary>
        internal static List<Sample> BaselineWindow() => History.Recent(DslConfig.BaselineSeconds.Value);
    }
}
