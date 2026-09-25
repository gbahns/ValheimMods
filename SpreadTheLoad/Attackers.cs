using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace SpreadTheLoad
{
    /// <summary>
    /// Gives a tree, rock or ore vein to whoever is hitting it.
    ///
    /// Vanilla never does this. TreeBase, TreeLog, Destructible and MineRock5 all open their damage
    /// handler with `if (!m_nview.IsOwner()) return;` and nothing anywhere calls ClaimOwnership, so
    /// the machine that loaded a tree keeps it and every axe swing anyone else makes travels to
    /// that machine and back - for the whole tree, and the next one, indefinitely. A chopping
    /// session is hundreds of interactions against a handful of objects, which makes this the
    /// commonest way a group feels somebody else's frame time.
    ///
    /// The server can see the swings because it relays them:
    ///
    ///     if (m_server &amp;&amp; routedRPCData.m_targetPeerID != m_id) RouteRPC(routedRPCData);
    ///
    /// so a server-only mod can watch damage go past without any client needing to help.
    ///
    /// Resources only, deliberately. A tree's entire state is its health in the ZDO, so handing it
    /// over costs one owner revision and loses nothing. A creature carries live local state - its
    /// target, its path, its alert timers - that is not all replicated, so moving one mid-fight can
    /// make it re-acquire its target or re-path. That is a real hitch in exactly the moment it
    /// would be least welcome, so creatures are left alone until it can be measured rather than
    /// reasoned about.
    /// </summary>
    internal static class Attackers
    {
        private static readonly int DamageHash = "RPC_Damage".GetStableHashCode();

        /// <summary>A swing seen, waiting to be acted on.</summary>
        private struct Pending
        {
            internal long Attacker;
            internal float At;
        }

        private static readonly Dictionary<ZDOID, Pending> _pending = new Dictionary<ZDOID, Pending>();
        private static readonly Dictionary<ZDOID, float> _lastMoved = new Dictionary<ZDOID, float>();

        /// <summary>Prefab hash -> whether it is a creature, so the test is done once per type.</summary>
        private static readonly Dictionary<int, bool> _isCreature = new Dictionary<int, bool>();

        /// <summary>
        /// How long a swing waits before the object moves.
        ///
        /// Not zero, and that matters. The transfer happens while the damage is still in flight to
        /// the current owner, and their handler begins by checking they are still the owner - so
        /// changing it immediately can make them drop the very hit that triggered the change.
        /// Letting the swing land first costs nothing and cannot lose damage.
        /// </summary>
        private const float SettleSeconds = 0.4f;

        /// <summary>Keeps the pending and cooldown tables from growing without bound.</summary>
        private const int MaxTracked = 512;

        private static int _movedSinceReport;
        private static float _nextReport;

        /// <summary>Called for every routed RPC the server relays. Must stay cheap.</summary>
        internal static void Note(ZRoutedRpc.RoutedRPCData data)
        {
            try
            {
                if (data == null || data.m_methodHash != DamageHash) return;
                if (SpreadTheLoadMod.OwnershipFollowsAttacker == null
                    || !SpreadTheLoadMod.OwnershipFollowsAttacker.Value) return;

                var znet = ZNet.instance;
                if (znet == null || !znet.IsServer()) return;

                long attacker = data.m_senderPeerID;
                if (attacker == 0L || attacker == data.m_targetPeerID) return;

                // Never hand work to a machine work is being steered away from: the yield pass
                // would take it back within two seconds and the next swing would move it again,
                // which is worse for everyone than leaving it where it is.
                if (Ownership.IsYielding(attacker)) return;

                if (_pending.Count < MaxTracked)
                    _pending[data.m_targetZDO] = new Pending { Attacker = attacker, At = Time.unscaledTime };
            }
            catch { /* a missed swing is one object left where it was */ }
        }

        /// <summary>Applies the swings that have had time to land. Called once a pass.</summary>
        internal static void Tick(float now)
        {
            try
            {
                if (_pending.Count == 0) return;
                var znet = ZNet.instance;
                var zdoMan = ZDOMan.instance;
                if (znet == null || zdoMan == null || !znet.IsServer()) { _pending.Clear(); return; }

                float dwell = Mathf.Max(0f, SpreadTheLoadMod.AttackerDwellSeconds.Value);
                List<ZDOID> done = null;
                foreach (var kv in _pending)
                {
                    if (now - kv.Value.At < SettleSeconds) continue;
                    (done ?? (done = new List<ZDOID>())).Add(kv.Key);

                    if (_lastMoved.TryGetValue(kv.Key, out float moved) && now - moved < dwell) continue;

                    var zdo = zdoMan.GetZDO(kv.Key);
                    if (zdo == null || !zdo.IsValid()) continue;
                    if (zdo.GetOwner() == kv.Value.Attacker) continue;
                    if (IsCreature(zdo)) continue;

                    zdo.SetOwner(kv.Value.Attacker);
                    if (_lastMoved.Count < MaxTracked) _lastMoved[kv.Key] = now;
                    _movedSinceReport++;
                }
                if (done != null) foreach (var id in done) _pending.Remove(id);

                if (_lastMoved.Count >= MaxTracked) _lastMoved.Clear();   // cheapest possible prune
                Report(now);
            }
            catch (Exception e)
            {
                _pending.Clear();
                SpreadTheLoadMod.Log.LogWarning($"[SpreadTheLoad] attacker pass failed: {e.Message}");
            }
        }

        /// <summary>
        /// Whether a ZDO is a creature, from its prefab rather than its instance.
        ///
        /// It has to come from the prefab: the object being chopped is usually nowhere near the
        /// server's own active area, so there is no instantiated GameObject to ask. ZNetScene keeps
        /// every prefab regardless, and the answer is the same for every object of a type, so it is
        /// worked out once and remembered.
        /// </summary>
        private static bool IsCreature(ZDO zdo)
        {
            int hash = zdo.GetPrefab();
            if (_isCreature.TryGetValue(hash, out bool known)) return known;

            bool creature = true;      // unknown means leave it alone
            try
            {
                var prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(hash) : null;
                if (prefab != null) creature = prefab.GetComponent<BaseAI>() != null;
            }
            catch { }
            _isCreature[hash] = creature;
            return creature;
        }

        private static void Report(float now)
        {
            if (_movedSinceReport == 0) return;
            if (SpreadTheLoadMod.LogActivity == null || !SpreadTheLoadMod.LogActivity.Value) return;
            if (now < _nextReport) return;
            _nextReport = now + 60f;
            SpreadTheLoadMod.Log.LogInfo(
                $"[SpreadTheLoad] moved {_movedSinceReport} object(s) to the player hitting them in the last minute.");
            _movedSinceReport = 0;
        }
    }

    /// <summary>
    /// Watches damage go past on its way to the object's owner. See <see cref="Attackers"/>.
    /// A prefix rather than a postfix only so the timestamp is the moment the swing was relayed.
    /// </summary>
    [HarmonyPatch(typeof(ZRoutedRpc), "RouteRPC")]
    internal static class RouteRpcPatch
    {
        private static void Prefix(ZRoutedRpc.RoutedRPCData rpcData) => Attackers.Note(rpcData);
    }
}
