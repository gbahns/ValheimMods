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
        internal static readonly Ring History = new Ring(600);          // ten minutes at 1 Hz

        /// <summary>The per-peer detail behind the newest sample. Server-side only; empty on a client.</summary>
        internal static readonly List<PeerSample> Peers = new List<PeerSample>();

        // Accumulated across the frames of the second currently being measured.
        private static float _bucketStart;
        private static float _bucketTotalMs;
        private static float _bucketMaxMs;
        private static int _bucketFrames;
        private static int _bucketStalls;

        private static bool _started;

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
                s.Peers = znet.GetConnectedPeers().Count;

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

            if (!znet.IsServer())
            {
                var server = znet.GetServerPeer();
                var socket = server?.m_socket;
                if (socket == null) return;
                try
                {
                    s.SendQueue = socket.GetSendQueueSize();
                    s.SendRate = socket.GetCurrentSendRate();
                }
                catch (Exception e)
                {
                    DiagnoseServerLagMod.Log.LogWarning($"[DiagnoseServerLag] Could not read the server socket: {e.Message}");
                }
                return;
            }

            int worstQueue = 0;
            int totalRate = 0;
            foreach (var peer in znet.GetConnectedPeers())
            {
                if (peer?.m_socket == null) continue;
                try
                {
                    peer.m_socket.GetConnectionQuality(out float localQ, out _, out int ping, out _, out _);
                    var ps = new PeerSample
                    {
                        Uid = peer.m_uid,
                        Name = string.IsNullOrEmpty(peer.m_playerName) ? "(connecting)" : peer.m_playerName,
                        Ping = ping,
                        HasPing = ping > 0 || localQ > 0f,
                        Quality = localQ,
                        SendQueue = peer.m_socket.GetSendQueueSize(),
                        SendRate = peer.m_socket.GetCurrentSendRate(),
                        DistanceFromCenter = peer.m_refPos.magnitude,
                    };
                    if (ps.SendQueue > worstQueue) worstQueue = ps.SendQueue;
                    totalRate += ps.SendRate;
                    Peers.Add(ps);
                }
                catch (Exception e)
                {
                    DiagnoseServerLagMod.Log.LogWarning($"[DiagnoseServerLag] Could not read a peer socket: {e.Message}");
                }
            }
            s.SendQueue = worstQueue;
            s.SendRate = totalRate;
        }

        /// <summary>The most recent completed second, if there is one.</summary>
        internal static bool TryNewest(out Sample s) => History.TryNewest(out s);

        /// <summary>The baseline window used to judge what "normal" looks like right now.</summary>
        internal static List<Sample> BaselineWindow() => History.Recent(DslConfig.BaselineSeconds.Value);
    }
}
