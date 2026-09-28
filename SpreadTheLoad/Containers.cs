using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpreadTheLoad
{
    /// <summary>
    /// Leaves a chest alone while the player steered away from it has it open.
    ///
    /// Vanilla's container code assumes the person with the window open owns the ZDO, because in
    /// vanilla they always do - opening a chest claims it, and the handout never takes an object
    /// from an owner standing next to it. Both halves of the container break without that:
    ///
    ///     private void OnContainerChanged() { if (!m_loading &amp;&amp; IsOwner()) Save(); }
    ///     private bool Load() { if (DataRevision == m_lastRevision) return false; ... }
    ///
    /// A non-owner's drag changes only their own copy, and the next revision bump - which an
    /// ownership handoff is - repaints the panel from the network copy. The stack they just pulled
    /// reappears in the chest while the one they took sits in their inventory. It is only a
    /// display fault, and closing the chest clears it, but it looks exactly like item duplication
    /// to the person it happens to.
    ///
    /// So a container in use is held with its user, the way a ship is held with its captain.
    ///
    /// Two sources decide the hold, because neither is enough alone. The open request names the
    /// container: it is a routed RPC, so the server sees it go past without any client's help, and
    /// it only travels at all when somebody else owns the chest - which is precisely the case this
    /// mod creates. The container's own InUse flag then says when to let go: there is no closing
    /// RPC to watch for, and a timer alone would expire in the middle of a long sorting session.
    /// InUse is session-only (ZDOVars.s_sessionHashes), which strips it from the save file but not
    /// from the network, so the server has it.
    /// </summary>
    internal static class Containers
    {
        private static readonly int OpenHash = "RPC_RequestOpen".GetStableHashCode();
        private static readonly int StackHash = "RPC_RequestStack".GetStableHashCode();
        private static readonly int TakeAllHash = "RPC_RequestTakeAll".GetStableHashCode();

        private struct Held
        {
            internal Vector3 Position;
            internal float Noted;
        }

        private static readonly Dictionary<ZDOID, Held> _held = new Dictionary<ZDOID, Held>();

        /// <summary>
        /// How long a hold survives before InUse has to justify it. The flag is written by the
        /// owner a frame or so after the chest opens and travels with the next ZDO update, so
        /// reading it immediately would drop the hold just as it starts to matter.
        /// </summary>
        private const float GraceSeconds = 15f;

        /// <summary>A chest whose user disconnected mid-window keeps InUse set. Nothing is held forever.</summary>
        private const float MaxHoldSeconds = 600f;

        /// <summary>The handout asks about a position, not a ZDO, so a hold is matched by position.</summary>
        private const float SameSpotSqr = 0.01f;

        private const int MaxHeld = 32;

        private static int _heldSinceReport;
        private static float _nextReport;

        /// <summary>Called for every routed RPC the server relays. Must stay cheap.</summary>
        internal static void Note(ZRoutedRpc.RoutedRPCData data)
        {
            try
            {
                if (data == null) return;
                int method = data.m_methodHash;
                if (method != OpenHash && method != StackHash && method != TakeAllHash) return;

                var znet = ZNet.instance;
                if (znet == null || !znet.IsServer()) return;

                // Only the players being steered away from can lose a chest they are using, so
                // only their windows are worth remembering.
                long opener = data.m_senderPeerID;
                if (opener == 0L || !Ownership.IsYielding(opener)) return;

                var zdoMan = ZDOMan.instance;
                if (zdoMan == null) return;
                var zdo = zdoMan.GetZDO(data.m_targetZDO);
                if (zdo == null || !zdo.IsValid()) return;

                if (_held.Count >= MaxHeld && !_held.ContainsKey(data.m_targetZDO)) return;
                if (!_held.ContainsKey(data.m_targetZDO)) _heldSinceReport++;
                _held[data.m_targetZDO] = new Held { Position = zdo.GetPosition(), Noted = Time.unscaledTime };
            }
            catch { /* a missed open is one chest that behaves the way it did before */ }
        }

        /// <summary>Whether the object at this point is a container somebody has open.</summary>
        internal static bool IsHeld(Vector3 point)
        {
            // The common case by far, and this is asked thousands of times a second.
            if (_held.Count == 0) return false;

            foreach (var kv in _held)
                if ((kv.Value.Position - point).sqrMagnitude <= SameSpotSqr) return true;
            return false;
        }

        /// <summary>Drops holds whose chest has been closed, destroyed, or held too long.</summary>
        internal static void Tick(float now)
        {
            try
            {
                if (_held.Count == 0) { Report(now); return; }

                var zdoMan = ZDOMan.instance;
                if (zdoMan == null) { _held.Clear(); return; }

                List<ZDOID> done = null;
                var refreshed = new List<KeyValuePair<ZDOID, Held>>();
                foreach (var kv in _held)
                {
                    float age = now - kv.Value.Noted;
                    if (age > MaxHoldSeconds) { (done ?? (done = new List<ZDOID>())).Add(kv.Key); continue; }
                    if (age < GraceSeconds) continue;

                    var zdo = zdoMan.GetZDO(kv.Key);
                    if (zdo == null || !zdo.IsValid() || zdo.GetInt(ZDOVars.s_inUse, 0) != 1)
                    {
                        (done ?? (done = new List<ZDOID>())).Add(kv.Key);
                        continue;
                    }

                    // A cart or a ship's hold moves while it is open, and the match is by position.
                    Vector3 where = zdo.GetPosition();
                    if ((where - kv.Value.Position).sqrMagnitude > SameSpotSqr)
                        refreshed.Add(new KeyValuePair<ZDOID, Held>(kv.Key, new Held { Position = where, Noted = kv.Value.Noted }));
                }
                if (done != null) foreach (var id in done) _held.Remove(id);
                foreach (var kv in refreshed) _held[kv.Key] = kv.Value;

                Report(now);
            }
            catch (Exception e)
            {
                _held.Clear();
                SpreadTheLoadMod.Log.LogWarning($"[SpreadTheLoad] container pass failed: {e.Message}");
            }
        }

        private static void Report(float now)
        {
            if (_heldSinceReport == 0) return;
            if (SpreadTheLoadMod.LogActivity == null || !SpreadTheLoadMod.LogActivity.Value) return;
            if (now < _nextReport) return;
            _nextReport = now + 60f;
            SpreadTheLoadMod.Log.LogInfo(
                $"[SpreadTheLoad] left {_heldSinceReport} open container(s) with the player using them in the last minute.");
            _heldSinceReport = 0;
        }
    }
}
