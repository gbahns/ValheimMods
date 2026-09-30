using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpreadTheLoad
{
    /// <summary>
    /// Where fighting is happening, so ownership can be left alone there.
    ///
    /// Moving a creature between machines mid-fight is the one transfer that visibly costs
    /// something. A tree's whole state is its health in the ZDO and moves intact; a creature's
    /// target, path and alert timers are not all replicated, so the new owner restarts its AI from
    /// what the ZDO happens to hold. What that looks like in play is a boss that stops attacking
    /// and stands about - which is what Greg reported after a Gerhaffa fight in the Howling Cavern.
    ///
    /// The attacker rule already refused to move anything living for exactly this reason. The yield
    /// rule did not, which was an inconsistency rather than a decision: it works through a predicate
    /// that is handed a position and an owner and never learns which object it is being asked
    /// about, so it could not tell a troll from a fence post even if it wanted to.
    ///
    /// Since the object cannot be identified, the guard is spatial instead. Every damage RPC the
    /// server relays is a fight happening at a known place and time, so ownership is simply frozen
    /// near anywhere blows have recently landed. It is coarse - it protects the fence posts in the
    /// fight as well as the troll - but it errs in the safe direction, and a fight is the one moment
    /// when rebalancing has nothing to offer anyway.
    /// </summary>
    internal static class Combat
    {
        private struct Blow
        {
            internal Vector3 Where;
            internal float When;
        }

        private static readonly List<Blow> _blows = new List<Blow>();

        /// <summary>
        /// How far from a blow counts as "in the fight". Generous: a fight is not a point, and the
        /// creatures circling the edge of one are the ones most likely to be mid-path.
        /// </summary>
        private const float RadiusMeters = 40f;

        /// <summary>
        /// How long a place stays frozen after the last blow lands. Long enough to cover the pauses
        /// in a fight, short enough that a cleared area rebalances soon after.
        /// </summary>
        private const float QuietSeconds = 15f;

        /// <summary>Bounded so a long fight cannot grow this without limit.</summary>
        private const int MaxBlows = 64;

        /// <summary>Called for each damage RPC the server relays, from Attackers.</summary>
        internal static void Note(Vector3 where)
        {
            float now = Time.unscaledTime;
            // Collapse blows that land near an existing one rather than adding a row each swing:
            // a fight is a place, not a list of hits.
            for (int i = 0; i < _blows.Count; i++)
            {
                if ((_blows[i].Where - where).sqrMagnitude > RadiusMeters * RadiusMeters * 0.25f) continue;
                _blows[i] = new Blow { Where = _blows[i].Where, When = now };
                return;
            }
            if (_blows.Count >= MaxBlows) _blows.RemoveAt(0);
            _blows.Add(new Blow { Where = where, When = now });
        }

        /// <summary>True when blows have landed near here recently, so ownership should not move.</summary>
        internal static bool Nearby(Vector3 point)
        {
            if (_blows.Count == 0) return false;
            float now = Time.unscaledTime;
            float r2 = RadiusMeters * RadiusMeters;
            for (int i = _blows.Count - 1; i >= 0; i--)
            {
                if (now - _blows[i].When > QuietSeconds) { _blows.RemoveAt(i); continue; }
                if ((_blows[i].Where - point).sqrMagnitude <= r2) return true;
            }
            return false;
        }

        internal static void Clear() => _blows.Clear();
    }
}
