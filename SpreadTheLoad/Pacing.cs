using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace SpreadTheLoad
{
    /// <summary>
    /// Sends every player their world update each cycle, instead of one player per frame.
    ///
    /// Vanilla serves exactly one peer per server frame:
    ///
    ///     m_sendTimer += dt;
    ///     if (m_nextSendPeer &lt; 0) {
    ///         if (m_sendTimer &gt; 0.05f) { m_nextSendPeer = 0; m_sendTimer = 0f; }
    ///         return;
    ///     }
    ///     if (m_nextSendPeer &lt; m_peers.Count) SendZDOs(m_peers[m_nextSendPeer], flush: false);
    ///     m_nextSendPeer++;
    ///
    /// so a full cycle costs one gate frame plus one frame per connected player, and each player
    /// hears from the server every (players + 1) frames. The 50 ms gate never signifies: m_sendTimer
    /// keeps accumulating during the serving frames, so by the end of a cycle it is far past 0.05
    /// and the gate opens on the very next frame.
    ///
    /// Measured on bahnsheim at the server's 30 Hz cap, and the model is exact at both ends:
    ///
    ///     1 player   (1+1) x 33.3 = 67 ms    measured 67
    ///     5 players  (5+1) x 33.3 = 200 ms   measured 199, 200, 201, 204
    ///
    /// Five updates a second, with four other players in the world. Everything a player does not
    /// own - where everyone else is, what their creatures are doing, the ship they are standing on -
    /// arrives at that rate and is interpolated in between. It is the one cost that grows with the
    /// size of the group, and it is invisible to every other measurement: the server holding a
    /// perfect 33.3 ms tick on 17% of one core is doing this the whole time.
    ///
    /// The 50 ms gate reads like an intended 20 updates a second. The per-frame loop quietly turns
    /// that into 20/N. This restores the apparent intent by serving everybody on each cycle, so the
    /// rate no longer depends on how many people are playing.
    ///
    /// Safe because the real flow control is elsewhere and untouched: SendZDOs refuses outright
    /// when the socket's send queue is backed up, and caps each package at what is left of 10 KB.
    /// The per-frame round robin is a second, cruder limiter on top of that. Removing it lets the
    /// socket's actual capacity decide, which is why the gain is self-limiting rather than a flood -
    /// bahnsheim's queue already sits at a median 3.7 KB against a refusal threshold near 8 KB.
    /// </summary>
    internal static class Pacing
    {
        private static FieldInfo _peersField;
        private static MethodInfo _sendZDOs;
        private static bool _looked;
        private static bool _usable;

        private static readonly object[] _args = new object[2];

        private static float _timer;
        private static int _served;
        private static float _nextReport;

        /// <summary>
        /// True once the private members are in hand. A miss is cached: without them the patch
        /// stands aside and vanilla keeps running, which is the right outcome and must not cost a
        /// reflection search every frame.
        /// </summary>
        private static bool Usable()
        {
            if (_looked) return _usable;
            _looked = true;
            try
            {
                var peerType = AccessTools.Inner(typeof(ZDOMan), "ZDOPeer");
                _peersField = AccessTools.Field(typeof(ZDOMan), "m_peers");
                if (peerType != null)
                    _sendZDOs = AccessTools.Method(typeof(ZDOMan), "SendZDOs", new[] { peerType, typeof(bool) });
                _usable = _peersField != null && _sendZDOs != null;
                if (!_usable)
                    SpreadTheLoadMod.Log.LogWarning(
                        "[SpreadTheLoad] could not reach ZDOMan.SendZDOs; update pacing is left to vanilla.");
            }
            catch (Exception e)
            {
                _usable = false;
                SpreadTheLoadMod.Log.LogWarning($"[SpreadTheLoad] update pacing disabled: {e.Message}");
            }
            return _usable;
        }

        /// <summary>
        /// Replaces the vanilla pass. Returns true to let vanilla run instead.
        /// </summary>
        internal static bool Run(ZDOMan man, float dt)
        {
            try
            {
                if (SpreadTheLoadMod.SendToEveryPeer == null || !SpreadTheLoadMod.SendToEveryPeer.Value) return true;
                var znet = ZNet.instance;
                if (znet == null || !znet.IsServer()) return true;
                if (!Usable()) return true;

                var peers = _peersField.GetValue(man) as IList;
                if (peers == null || peers.Count == 0) return false;

                // Our own timer, so vanilla's m_sendTimer and m_nextSendPeer are left exactly as
                // they were: switching this off mid-session hands the job back intact.
                _timer += dt;
                float interval = 1f / Mathf.Clamp(SpreadTheLoadMod.UpdatesPerSecond.Value, 1, 60);
                if (_timer < interval) return false;
                _timer = 0f;

                for (int i = 0; i < peers.Count; i++)
                {
                    _args[0] = peers[i];
                    _args[1] = false;              // not a flush: the queue check still applies
                    _sendZDOs.Invoke(man, _args);
                }
                _served++;
                Report();
                return false;
            }
            catch (Exception e)
            {
                // Hand the job back rather than leave the world unsynced. Disabling outright is
                // safer than limping: nobody hearing from the server is worse than hearing slowly.
                _usable = false;
                SpreadTheLoadMod.Log.LogError(
                    $"[SpreadTheLoad] update pacing failed and is now off; vanilla has it back: {e}");
                return true;
            }
        }

        private static void Report()
        {
            if (SpreadTheLoadMod.LogActivity == null || !SpreadTheLoadMod.LogActivity.Value) return;
            if (Time.unscaledTime < _nextReport) return;
            if (_nextReport > 0f)
                SpreadTheLoadMod.Log.LogInfo(
                    $"[SpreadTheLoad] served every player {_served} times in the last minute " +
                    $"({_served / 60f:0.#}/s each; vanilla would manage {_served / 60f:0.#} shared between them).");
            _nextReport = Time.unscaledTime + 60f;
            _served = 0;
        }
    }

    /// <summary>
    /// See <see cref="Pacing"/>. A prefix returning false replaces the vanilla pass entirely; it
    /// returns true whenever the feature is off, this is not a server, or anything went wrong.
    /// </summary>
    [HarmonyPatch(typeof(ZDOMan), "SendZDOToPeers2")]
    internal static class SendZDOToPeers2Patch
    {
        private static bool Prefix(ZDOMan __instance, float dt) => Pacing.Run(__instance, dt);
    }
}
