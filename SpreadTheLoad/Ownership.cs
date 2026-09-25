using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace SpreadTheLoad
{
    /// <summary>
    /// Decides whether a given owner should give an object up to somebody standing nearby.
    ///
    /// The whole mod is one answer to one question, because vanilla asks that question in exactly
    /// one place. ZDOMan hands out ownership every two seconds, on the server, like this:
    ///
    ///     else if ((!zdo.HasOwner() || !IsInPeerActiveArea(position, zdo.GetOwner()))
    ///              &amp;&amp; ZNetScene.InActiveArea(position, zone))
    ///         zdo.SetOwner(uid);
    ///
    /// An object moves only when its current owner has walked out of range of it. So rather than
    /// reimplementing the handout - reordering peers, calling SetOwner directly, keeping our own
    /// two-second timer - this answers "is the owner still in range" with a no for the players we
    /// want to steer away from, whenever somebody else is in range to take over. Vanilla then
    /// performs the transfer itself, through its own code path, at its own pace.
    ///
    /// That matters for safety as much as for brevity. The mod never assigns ownership, never
    /// leaves an object ownerless, and cannot strand a creature with nobody simulating it: the
    /// answer is only ever a no when a specific other player is already close enough to be given
    /// it, and vanilla decides whether to act on that. With the mod disabled mid-session the
    /// behaviour reverts exactly, because nothing was reimplemented to drift.
    ///
    /// IsInPeerActiveArea has precisely one caller - the line above - which is what makes this
    /// safe to answer dishonestly. It is not a general-purpose predicate other systems rely on.
    /// </summary>
    internal static class Ownership
    {
        /// <summary>A player who can be given work: connected, ready, and not being steered away from.</summary>
        private struct Candidate
        {
            internal long Uid;
            internal Vector3 RefPos;
        }

        private static readonly List<Candidate> _capable = new List<Candidate>();
        private static readonly Dictionary<long, string> _yielding = new Dictionary<long, string>();

        /// <summary>
        /// How often the peer list is re-read. The question is asked once per nearby object per
        /// peer per pass, which is thousands of times a second on a busy server, so the answer
        /// leans on a snapshot rather than walking the peer list every time. A second of staleness
        /// costs nothing: vanilla only acts on the answer every two seconds anyway.
        /// </summary>
        private const float RefreshSeconds = 1f;

        private static float _nextRefresh;

        // Activity is summarised rather than reported per object: one pass over a loaded zone can
        // answer yes hundreds of times, and a line each would bury the log the way the socket
        // warning once did.
        private static readonly Dictionary<string, int> _yielded = new Dictionary<string, int>();
        private static float _nextLog;
        private const float LogEverySeconds = 60f;

        /// <summary>
        /// True when this owner should be treated as out of range, because somebody else can take
        /// the object instead.
        /// </summary>
        internal static bool ShouldYield(Vector3 point, long ownerUid)
        {
            try
            {
                // Counted first, unconditionally: the point of the number is to prove vanilla still
                // reaches us, which is true even when the answer is no.
                Conflicts.Consultations++;
                if (SpreadTheLoadMod.Enabled == null || !SpreadTheLoadMod.Enabled.Value) return false;

                Refresh();
                if (_yielding.Count == 0) return false;
                if (!_yielding.TryGetValue(ownerUid, out string who)) return false;

                // Only ever a no when somebody else is genuinely in range. Without this the object
                // would be left for whoever happened to be iterated next, or for nobody at all,
                // and a creature with no owner runs no AI.
                for (int i = 0; i < _capable.Count; i++)
                {
                    if (!ZNetScene.InActiveArea(point, _capable[i].RefPos)) continue;
                    Note(who);
                    return true;
                }
                return false;
            }
            catch
            {
                // A question that cannot be answered is answered the vanilla way. This runs inside
                // the server's object loop; throwing here would break ownership for everybody.
                return false;
            }
        }

        /// <summary>Whether anybody at all is being steered away from, named or auto-detected.</summary>
        internal static bool AnyYielding
        {
            get { Refresh(); return _yielding.Count > 0; }
        }

        /// <summary>
        /// Whether this peer is one work is being steered away from right now.
        ///
        /// Honours Enabled, unlike the first version: with the mod switched off nobody is being
        /// steered anywhere, and a caller asking - the ship pass, or another mod wanting to show
        /// it - would otherwise be told yes about a rule that is not in force.
        /// </summary>
        internal static bool IsYielding(long uid)
        {
            if (SpreadTheLoadMod.Enabled == null || !SpreadTheLoadMod.Enabled.Value) return false;
            Refresh();
            return _yielding.ContainsKey(uid);
        }

        private static void Refresh()
        {
            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + RefreshSeconds;

            _capable.Clear();
            _yielding.Clear();

            var znet = ZNet.instance;
            if (znet == null || !znet.IsServer()) return;

            PlayerIds.Configure(SpreadTheLoadMod.YieldPlayers != null ? SpreadTheLoadMod.YieldPlayers.Value : "");

            // Named and auto-detected players are the same thing from here on; only how they got
            // on the list differs. Building one list rather than consulting two also keeps the
            // capable side honest - an auto-flagged machine must not be offered work, which an
            // earlier version got wrong by filtering only on the configured names.
            bool watching = !PlayerIds.Empty ||
                            (SpreadTheLoadMod.AutoDetect != null && SpreadTheLoadMod.AutoDetect.Value);
            if (!watching) return;

            foreach (var peer in znet.GetConnectedPeers())
            {
                if (peer == null || !peer.IsReady()) continue;
                string name = peer.m_playerName ?? "";
                bool yields = PlayerIds.Matches(peer.m_uid, name, NetworkId(peer))
                              || Detection.IsFlagged(peer.m_uid);
                if (yields)
                    _yielding[peer.m_uid] = name.Length > 0 ? name : peer.m_uid.ToString();
                else
                    _capable.Add(new Candidate { Uid = peer.m_uid, RefPos = peer.GetRefPos() });
            }

            ReportActivity();
        }

        /// <summary>
        /// The stable identity behind a peer: the Steam id on a Steam server, whatever the socket
        /// calls the far end otherwise. Read defensively - a socket that cannot answer is a peer we
        /// simply cannot identify by id, not a failure - because Valheim's non-Steam sockets throw
        /// NotImplementedException from some of these accessors rather than returning nothing.
        /// </summary>
        private static string NetworkId(ZNetPeer peer)
        {
            try { return peer.m_socket != null ? peer.m_socket.GetHostName() : null; }
            catch { return null; }
        }

        private static void Note(string who)
        {
            if (SpreadTheLoadMod.LogActivity == null || !SpreadTheLoadMod.LogActivity.Value) return;
            _yielded.TryGetValue(who, out int n);
            _yielded[who] = n + 1;
        }

        private static void ReportActivity()
        {
            if (SpreadTheLoadMod.LogActivity == null || !SpreadTheLoadMod.LogActivity.Value) return;
            if (_yielded.Count == 0) return;
            if (Time.unscaledTime < _nextLog) { return; }
            _nextLog = Time.unscaledTime + LogEverySeconds;

            var parts = new List<string>();
            foreach (var kv in _yielded) parts.Add($"{kv.Key} {kv.Value}");
            _yielded.Clear();
            SpreadTheLoadMod.Log.LogInfo(
                "[SpreadTheLoad] offered to nearby players in the last minute: " + string.Join(", ", parts.ToArray()) +
                " (counts are chances offered, not objects moved; vanilla decides)");
        }
    }

    /// <summary>
    /// The one patch. See <see cref="Ownership"/> for why answering this particular question is
    /// enough, and why it is answered rather than the handout being rewritten.
    /// </summary>
    [HarmonyPatch(typeof(ZDOMan), "IsInPeerActiveArea")]
    internal static class IsInPeerActiveAreaPatch
    {
        private static void Postfix(Vector3 point, long uid, ref bool __result)
        {
            if (!__result) return;                       // already out of range; vanilla moves it anyway
            if (Ownership.ShouldYield(point, uid)) __result = false;
        }
    }
}
