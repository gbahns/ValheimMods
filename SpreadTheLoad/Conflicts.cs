using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace SpreadTheLoad
{
    /// <summary>
    /// Notices when another mod has taken over the method this one depends on.
    ///
    /// This mod works by answering ZDOMan.IsInPeerActiveArea, which vanilla consults from exactly
    /// one place: the ownership handout in ReleaseNearbyZDOS. That is a strength - one caller, no
    /// surprises - and a single point of failure. ValheimPerformanceOptimizations, which is
    /// popular, replaces ReleaseNearbyZDOS with a Harmony prefix and calls its own private copy of
    /// the active-area check:
    ///
    ///     [HarmonyPatch(typeof(ZDOMan), "ReleaseNearbyZDOS")]
    ///     [HarmonyPrefix]
    ///     ...
    ///     private static bool IsInPeerActiveArea(
    ///
    /// On a server running it, vanilla's method never executes, our answer is never asked for, and
    /// this mod does nothing at all while reporting itself loaded. A mod that silently does
    /// nothing is worse than one that fails, because the admin keeps believing the problem is
    /// handled. This project has shipped that bug before - a whole feature unreachable behind a
    /// patch that never applied - and the lesson was to make silence impossible.
    ///
    /// So there are two independent checks, because either alone can be fooled:
    ///
    ///   By inspection - ask Harmony who else has patched ReleaseNearbyZDOS. Names the culprit,
    ///   but only catches mods that patch the method we happen to know about.
    ///
    ///   By evidence - count how many times our answer was actually asked for. If the server has
    ///   had players aboard for a minute and nobody has asked once, something upstream is bypassing
    ///   us, whoever it is and however they did it.
    ///
    /// Modelled on balrond_core_optimizer, which checks Harmony ownership of its exact target and
    /// stands down rather than trusting patch order.
    /// </summary>
    internal static class Conflicts
    {
        /// <summary>How many times our postfix has been consulted. Zero is the interesting value.</summary>
        internal static long Consultations;

        private static bool _inspected;
        private static bool _reportedSilence;
        private static float _serverUpSince = -1f;
        private static float _peersSince = -1f;

        /// <summary>
        /// Long enough that a mod loading after us has certainly finished patching, short enough
        /// that the warning is in the log before the admin stops reading it.
        /// </summary>
        private const float InspectAfterSeconds = 15f;

        /// <summary>
        /// A minute of at least one connected player without a single consultation. Generous on
        /// purpose: the handout only runs every two seconds and only over loaded objects, so a
        /// quiet server legitimately asks nothing for a while.
        /// </summary>
        private const float SilenceSeconds = 60f;

        internal static void Tick(float now, bool isServer, int peerCount)
        {
            if (!isServer) { _serverUpSince = -1f; _peersSince = -1f; return; }
            if (_serverUpSince < 0f) _serverUpSince = now;

            if (!_inspected && now - _serverUpSince > InspectAfterSeconds)
            {
                _inspected = true;
                Inspect();
            }

            if (peerCount <= 0) { _peersSince = -1f; return; }
            if (_peersSince < 0f) _peersSince = now;

            if (_reportedSilence || Consultations > 0) return;
            if (now - _peersSince < SilenceSeconds) return;
            _reportedSilence = true;
            SpreadTheLoadMod.Log.LogError(
                "[SpreadTheLoad] Yield Players IS NOT WORKING: players have been connected for a minute " +
                "and the ownership check has never been consulted once. Another mod has almost certainly " +
                "replaced ZDOMan.ReleaseNearbyZDOS - ValheimPerformanceOptimizations does exactly that. " +
                "Remove one of the two. Helm ownership is unaffected and still working; it does not go " +
                "through that method.");
        }

        private static void Inspect()
        {
            try
            {
                var target = AccessTools.Method(typeof(ZDOMan), "ReleaseNearbyZDOS");
                if (target == null)
                {
                    SpreadTheLoadMod.Log.LogWarning(
                        "[SpreadTheLoad] could not find ZDOMan.ReleaseNearbyZDOS to check for conflicts. " +
                        "The game has probably changed; this mod may no longer do anything.");
                    return;
                }

                var others = Foreign(target);
                if (others.Count == 0) return;

                SpreadTheLoadMod.Log.LogWarning(
                    "[SpreadTheLoad] another mod patches the ownership handout this mod relies on: " +
                    string.Join(", ", others.ToArray()) +
                    ". If it replaces the method rather than adding to it, this mod will have no effect - " +
                    "watch for the NOT WORKING line a minute after somebody joins.");
            }
            catch (Exception e)
            {
                SpreadTheLoadMod.Log.LogWarning($"[SpreadTheLoad] conflict check failed: {e.Message}");
            }
        }

        /// <summary>Patch owners other than us. Prefixes and transpilers are the ones that can hide a method.</summary>
        private static List<string> Foreign(MethodBase target)
        {
            var names = new List<string>();
            var info = Harmony.GetPatchInfo(target);
            if (info == null) return names;

            Collect(info.Prefixes, "prefix", names);
            Collect(info.Transpilers, "transpiler", names);
            return names;
        }

        private static void Collect(IEnumerable<Patch> patches, string kind, List<string> into)
        {
            if (patches == null) return;
            foreach (var p in patches)
            {
                if (p.owner == SpreadTheLoadMod.ModGuid) continue;
                string entry = $"{p.owner} ({kind})";
                if (!into.Contains(entry)) into.Add(entry);
            }
        }
    }
}
