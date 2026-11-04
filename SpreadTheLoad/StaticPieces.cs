using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace SpreadTheLoad
{
    /// <summary>
    /// Keeps the proximity rule off walls, floors and beams.
    ///
    /// The proximity rule exists for things that misbehave while somebody else owns them: a boar you
    /// shove, a cart you pull, a chest you open. Each of those is something you change and then
    /// watch, so the round trip to a distant owner shows up as a lurch or a pause. A floor tile is
    /// not. Nothing about standing on one is improved by owning it, and the single case that would
    /// be - hitting it - is already covered by Ownership Follows Attacker, which hands over whatever
    /// you swing at. So transferring inert pieces was all cost and no benefit.
    ///
    /// In a built-up base that cost is not small. Measured 2026-11-02, five players, in the admin's
    /// own base, comparing his stalling seconds against his calm ones with location held constant -
    /// 6,266 nearby pieces either way, 11,600 instances either way, the same 31 terrain-modified
    /// zones, the same 6,046 objects owned by somebody else:
    ///
    ///     frame time       130.4 ms  against  37.7 ms
    ///     owned objects       5,176  against    1,185
    ///     owned creatures        49  against        9
    ///     change queue           63  against       12
    ///
    /// Nothing about where he stood differed. Only what he was holding. And this mod's own log said
    /// where it came from: "handed 275 object(s) to the player standing on them in the last minute",
    /// minute after minute, while he walked around his own base.
    ///
    /// A base is the worst case by construction. The rule fires when a player is within Near while
    /// the owner is beyond Far, and a base is thousands of persistent objects packed within a few
    /// metres of one another - so every step through it is another handful of transfers, and they
    /// accumulate for as long as you stay. The hysteresis that stops one object flitting between two
    /// people does nothing to stop one person collecting thousands of different objects.
    ///
    /// What counts as inert is deliberately narrow: a Piece carrying nothing that interacts, ticks
    /// or moves. Anything this cannot classify keeps vanilla's behavior, because the worst a
    /// misclassification can do in that direction is leave a transfer that used to happen still
    /// happening.
    /// </summary>
    internal static class StaticPieces
    {
        /// <summary>
        /// Prefab hash to inert-or-not, worked out once each.
        ///
        /// Keyed on the prefab rather than the object because the answer is a property of the
        /// prefab, and this is asked inside the server's object loop - a GetComponent per call would
        /// cost more than the transfers it is trying to avoid. The mapping cannot change while the
        /// process runs, so there is nothing to invalidate.
        /// </summary>
        private static readonly Dictionary<int, bool> _inert = new Dictionary<int, bool>();

        private static int _skipped;
        private static float _nextReport;
        private static bool _saidItWorks;

        /// <summary>
        /// Whether owning this object could matter to anybody standing next to it.
        ///
        /// Each test names something that makes ownership visible. Interactable covers chests,
        /// doors, fires, crafting stations and carts - anything with a use key. The hover-menu
        /// interfaces catch the ones that take input without being Interactable, smelters and kilns
        /// among them, which matters because adding ore is an RPC to the owner and that round trip
        /// is exactly what this rule exists to remove. IMonoUpdater means the thing ticks. A
        /// Rigidbody means it moves, which is the original boar case.
        ///
        /// A plain Piece is StaticTarget and IPlaced and nothing else, so walls, floors, roofs,
        /// beams, stairs and fences - the bulk of any base - fall through every test and come back
        /// inert.
        /// </summary>
        private static bool Classify(GameObject prefab)
        {
            if (prefab.GetComponent<Piece>() == null) return false;     // not built; not ours to judge
            if (prefab.GetComponent<Interactable>() != null) return false;
            if (prefab.GetComponent<IHasHoverMenu>() != null) return false;
            if (prefab.GetComponent<IHasHoverMenuExtended>() != null) return false;
            if (prefab.GetComponent<IMonoUpdater>() != null) return false;
            if (prefab.GetComponent<Rigidbody>() != null) return false;
            return true;
        }

        /// <summary>
        /// True for a piece nobody would notice the ownership of.
        ///
        /// An unanswerable question is answered no, and - importantly - not cached: ZNetScene holds
        /// no prefabs before the world loads, and caching that emptiness would mark every piece in
        /// the world transferable for the life of the process.
        /// </summary>
        internal static bool IsInert(ZDO zdo)
        {
            if (zdo == null) return false;
            try
            {
                int hash = zdo.GetPrefab();
                bool known;
                if (_inert.TryGetValue(hash, out known)) return known;

                var scene = ZNetScene.instance;
                if (scene == null) return false;
                var prefab = scene.GetPrefab(hash);
                if (prefab == null) return false;

                bool answer = Classify(prefab);
                _inert[hash] = answer;
                return answer;
            }
            catch
            {
                return false;
            }
        }

        internal static void Note()
        {
            _skipped++;
            Proximity.Unhand();

            // Said once, the first time, whatever Log Activity is set to. A veto that silently does
            // nothing and a veto that silently works look identical from outside the process, and
            // this one hangs off a patch on ZDO.SetOwner that a future runtime could inline.
            if (!_saidItWorks)
            {
                _saidItWorks = true;
                SpreadTheLoadMod.Log.LogInfo(
                    "[SpreadTheLoad] keeping inert building pieces out of the proximity rule; " +
                    "the first one just stayed with its owner.");
            }

            if (SpreadTheLoadMod.LogActivity == null || !SpreadTheLoadMod.LogActivity.Value) return;
            if (Time.unscaledTime < _nextReport) return;
            if (_nextReport > 0f)
                SpreadTheLoadMod.Log.LogInfo(
                    $"[SpreadTheLoad] left {_skipped} inert building piece(s) with their owner in the last minute.");
            _nextReport = Time.unscaledTime + 60f;
            _skipped = 0;
        }
    }

    /// <summary>
    /// Takes back the one transfer this mod asked for, once the object is finally in hand.
    ///
    /// The rule is decided in a predicate that is told a position and an owner and never which
    /// object it is being asked about - see <see cref="Ownership.ShouldYield"/>. That is enough for
    /// every other rule here and is not enough for this one, so the object has to be caught later.
    /// ZDOMan.ReleaseNearbyZDOS calls SetOwner immediately after the predicate answers, on the same
    /// ZDO, which makes this the first point in the pass where the two facts exist together.
    ///
    /// Scoped as tightly as it can be: it does nothing unless a release pass is running, unless this
    /// is the claim that pass was about to make, and unless the proximity rule is what let it
    /// through. Vanilla's own handovers, the yield rule's, and every SetOwner anywhere else in the
    /// game are left alone.
    /// </summary>
    [HarmonyPatch(typeof(ZDO), nameof(ZDO.SetOwner))]
    internal static class SetOwnerPatch
    {
        private static bool Prefix(ZDO __instance, long uid)
        {
            try
            {
                if (!Proximity.Approved) return true;        // nothing this mod asked for
                if (Proximity.Candidate == 0L) return true;  // not inside a release pass
                if (uid != Proximity.Candidate) return true; // the release half, not the claim

                Proximity.Approved = false;                  // one claim per decision
                if (SpreadTheLoadMod.KeepInertPieces == null ||
                    !SpreadTheLoadMod.KeepInertPieces.Value) return true;
                if (!StaticPieces.IsInert(__instance)) return true;

                StaticPieces.Note();
                return false;                                // leave it where it was
            }
            catch
            {
                // A question that cannot be answered is answered the vanilla way. This runs inside
                // the server's object loop; throwing here would break ownership for everybody.
                return true;
            }
        }
    }
}
