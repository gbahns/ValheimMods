using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace SpreadTheLoad
{
    /// <summary>
    /// Hands an object to the player standing on top of it when its owner is nowhere near.
    ///
    /// Greg's rule, and it fixes the case everybody actually feels: shoving a boar toward its pen.
    /// Every push you make travels to whoever owns the boar and the corrected position comes back,
    /// so the animal lurches instead of moving - and the owner may be a hundred metres away with no
    /// interest in it at all. The same applies to opening somebody's chest, or anything you are
    /// standing over.
    ///
    /// The hysteresis is what makes it safe. Taking at two metres while only giving up at five
    /// means an object cannot flit between two people: once the near player owns it they are at
    /// zero distance, and the rule cannot fire again until somebody else is closer than two metres
    /// while they are further than five. It settles by construction rather than by a cooldown.
    ///
    /// Distances come from each player's character ZDO rather than the peer reference position,
    /// which is a zone-grained figure updated for a different purpose. A character's position syncs
    /// at the ordinary ZDO rate, which since the pacing change is every 67 ms - accurate enough to
    /// mean two metres rather than approximately two metres.
    ///
    /// Unlike the yield rule this is not about anybody's machine being slow. It is about the object
    /// being in the wrong hands: whoever is touching it should own it, whatever their frame rate.
    /// </summary>
    internal static class Proximity
    {
        /// <summary>
        /// The peer whose pass is currently running, recorded by the patch below.
        ///
        /// The active-area predicate is told the object's position and its *owner*, never which
        /// candidate is being considered - so without this the rule could say "the owner is far
        /// away" but not "and this particular player is close". Vanilla would then hand the object
        /// to whichever peer happened to be iterating, which might be another distant one.
        /// </summary>
        internal static long Candidate;

        private static readonly Dictionary<long, Vector3> _where = new Dictionary<long, Vector3>();
        private static float _nextRefresh;

        private static int _taken;
        private static float _nextReport;

        internal static void Reset()
        {
            Candidate = 0L; _where.Clear(); _nextRefresh = 0f;
        }

        /// <summary>
        /// True when the candidate is standing on this object and its owner has left it behind.
        /// </summary>
        internal static bool ShouldTake(Vector3 point, long ownerUid)
        {
            if (SpreadTheLoadMod.ProximityTransfer == null || !SpreadTheLoadMod.ProximityTransfer.Value) return false;
            if (Candidate == 0L || Candidate == ownerUid) return false;

            Refresh();
            if (!_where.TryGetValue(Candidate, out Vector3 mine)) return false;
            if (!_where.TryGetValue(ownerUid, out Vector3 theirs)) return false;

            float near = Mathf.Max(0.5f, SpreadTheLoadMod.ProximityNearMeters.Value);
            float far = Mathf.Max(near + 0.5f, SpreadTheLoadMod.ProximityFarMeters.Value);

            // Flat distance: a chest one floor up is not the one you are standing on, but height
            // differences in a base are small enough that including them would refuse legitimate
            // transfers on ramps and stairs more often than it would prevent wrong ones.
            if (Flat(mine, point) > near) return false;
            if (Flat(theirs, point) < far) return false;

            // Never hand work to a machine being steered away from - it would only be taken back.
            if (Ownership.IsYielding(Candidate)) return false;

            _taken++;
            Report();
            return true;
        }

        private static float Flat(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>
        /// Where everybody is, from their character ZDO. Rebuilt once a second: the predicate runs
        /// thousands of times a second and must not walk the player list on each.
        /// </summary>
        private static void Refresh()
        {
            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + 1f;

            _where.Clear();
            try
            {
                var znet = ZNet.instance;
                var zdoMan = ZDOMan.instance;
                if (znet == null || zdoMan == null) return;
                foreach (var info in znet.GetPlayerList())
                {
                    var zdo = zdoMan.GetZDO(info.m_characterID);
                    if (zdo == null || !zdo.IsValid()) continue;
                    _where[info.m_characterID.UserID] = zdo.GetPosition();
                }
            }
            catch { /* a player we cannot place is one this rule leaves alone */ }
        }

        private static void Report()
        {
            if (SpreadTheLoadMod.LogActivity == null || !SpreadTheLoadMod.LogActivity.Value) return;
            if (Time.unscaledTime < _nextReport) return;
            if (_nextReport > 0f)
                SpreadTheLoadMod.Log.LogInfo(
                    $"[SpreadTheLoad] handed {_taken} object(s) to the player standing on them in the last minute.");
            _nextReport = Time.unscaledTime + 60f;
            _taken = 0;
        }
    }

    /// <summary>
    /// Records which peer's pass is running, so the active-area predicate can tell who is being
    /// offered the object. See <see cref="Proximity.Candidate"/>.
    /// </summary>
    [HarmonyPatch(typeof(ZDOMan), "ReleaseNearbyZDOS")]
    internal static class ReleaseNearbyZDOSPatch
    {
        private static void Prefix(long uid) => Proximity.Candidate = uid;
        private static void Postfix() => Proximity.Candidate = 0L;
    }
}
