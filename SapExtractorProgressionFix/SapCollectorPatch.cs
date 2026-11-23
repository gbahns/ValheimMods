using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace SapExtractorProgressionFix
{
    /// <summary>
    /// Replaces SapCollector.UpdateTick, the 5 second tick that turns elapsed world time into sap.
    ///
    /// Vanilla reads the time since the last update, stamps "now" over it, and only THEN checks
    /// whether it may produce: is a root connected, and does the root hold more than 1. When the
    /// check fails the time is already spent. A beehive checks first and reads the clock second,
    /// which is why hives catch up and extractors do not. Three things go wrong on returning:
    ///
    ///   1. The extractor's first tick comes 0-2 s after it loads, and it finds its root by a
    ///      physics overlap at its own position. If the root object does not exist yet the
    ///      extractor is "not connected" and the whole absence is thrown away.
    ///   2. The root regenerates on its own tick, first due 0-10 s after it loads - usually after
    ///      the extractor's. A root left drained still reads as drained, so the absence is thrown
    ///      away; a root left nearly empty caps the catch-up at the whole units in it.
    ///   3. A catch-up that does work drains the root by everything the extractor earned, not by
    ///      what fitted in it, so one extractor back from a long absence empties the root for
    ///      every other extractor on it and sets up case 2 for the next visit.
    ///
    /// Here the time is not spent until a root is found (for a short grace period, so an extractor
    /// that really has no root does not bank time forever), the root's own regeneration is run
    /// first, and the root is drained by what was actually added. The ordinary 5 s tick is
    /// otherwise left as the game has it, including dropping the remainder after each unit.
    /// </summary>
    [HarmonyPatch(typeof(SapCollector), "UpdateTick")]
    internal static class SapCollectorPatch
    {
        /// <summary>How long after loading an extractor may hold its elapsed time waiting for its root.</summary>
        private const float RootGraceSeconds = 30f;

        /// <summary>An elapsed time this long is a return from somewhere, not an ordinary tick.</summary>
        private const double AbsenceSeconds = 60.0;

        /// <summary>What is remembered about one loaded extractor. A reload makes a new component, so a new one of these.</summary>
        private sealed class State
        {
            public float FirstTick = -1f;
            public bool HeldForRoot;
            public bool HoldLogged;
        }

        private static readonly ConditionalWeakTable<SapCollector, State> States =
            new ConditionalWeakTable<SapCollector, State>();

        // ResourceRoot.UpdateTick is private: it credits the root's regeneration for the time
        // since its own last update, and recolors the root.
        private static readonly Action<ResourceRoot> RootTick = BindRootTick();

        private static Action<ResourceRoot> BindRootTick()
        {
            var method = AccessTools.Method(typeof(ResourceRoot), "UpdateTick");
            if (method == null)
            {
                SapExtractorProgressionFixMod.Log?.LogWarning(
                    "ResourceRoot.UpdateTick not found; roots will not be regenerated ahead of their extractors.");
                return null;
            }
            return AccessTools.MethodDelegate<Action<ResourceRoot>>(method);
        }

        private static bool Prefix(SapCollector __instance, ZNetView ___m_nview, ref ResourceRoot ___m_root)
        {
            if (!SapExtractorProgressionFixMod.ModEnabled.Value) return true;
            if (___m_nview == null || !___m_nview.IsValid() || ZNet.instance == null) return true;

            ZDO zdo = ___m_nview.GetZDO();
            State state = States.GetOrCreateValue(__instance);
            if (state.FirstTick < 0f) state.FirstTick = Time.time;

            if (__instance.m_mustConnectTo && !___m_root)
                ___m_root = FindRoot(__instance.transform.position);
            ResourceRoot root = ___m_root;

            if (___m_nview.IsOwner())
                OwnerTick(__instance, zdo, root, state);

            int level = zdo.GetInt(ZDOVars.s_level);
            bool working = level < __instance.m_maxLevel && root && root.CanDrain(1f);
            if (__instance.m_notEmptyEffect) __instance.m_notEmptyEffect.SetActive(level > 0);
            if (__instance.m_workingEffect) __instance.m_workingEffect.SetActive(working);
            return false;
        }

        private static ResourceRoot FindRoot(Vector3 position)
        {
            foreach (Collider collider in Physics.OverlapSphere(position, 0.2f))
            {
                ResourceRoot found = collider.GetComponentInParent<ResourceRoot>();
                if ((object)found != null) return found;
            }
            return null;
        }

        private static void OwnerTick(SapCollector collector, ZDO zdo, ResourceRoot root, State state)
        {
            DateTime now = ZNet.instance.GetTime();
            long last = zdo.GetLong(ZDOVars.s_lastTime, now.Ticks);
            double elapsed = Math.Max(0.0, (now - new DateTime(last)).TotalSeconds);
            bool absence = elapsed >= AbsenceSeconds;
            float waited = Time.time - state.FirstTick;

            // Trigger 1. No root yet: leave the clock alone and look again next tick.
            if (!root && collector.m_mustConnectTo && waited < RootGraceSeconds)
            {
                state.HeldForRoot = true;
                if (absence && !state.HoldLogged)
                {
                    state.HoldLogged = true;
                    Report($"{Where(collector)}: away {elapsed:F0} s, root not loaded yet. " +
                           "Vanilla discards the time here (trigger 1). Holding it until the root appears.");
                }
                return;
            }

            // Trigger 2. Let the root catch up on its own regeneration before it is asked.
            float rootBefore = root ? root.GetLevel() : 0f;
            bool rootOwned = false;
            if (root)
            {
                ZNetView rootView = root.GetComponent<ZNetView>();
                rootOwned = rootView && rootView.IsValid() && rootView.IsOwner();
                if (rootOwned && RootTick != null) RootTick(root);
            }
            float rootNow = root ? root.GetLevel() : 0f;

            zdo.Set(ZDOVars.s_lastTime, now.Ticks);

            int max = collector.m_maxLevel;
            float secPerUnit = collector.m_secPerUnit;
            int level = zdo.GetInt(ZDOVars.s_level);
            float banked = zdo.GetFloat(ZDOVars.s_product);
            int earned = secPerUnit > 0f && banked + (float)elapsed > secPerUnit
                ? (int)((banked + (float)elapsed) / secPerUnit)
                : 0;
            int added = 0;

            if (level < max && root && root.CanDrain(1f))
            {
                float product = banked + (float)elapsed;
                if (earned > 0)
                {
                    // Trigger 3. Take only what fits, and only what the root will hand over:
                    // Drain refuses unless the root holds strictly more than the amount.
                    added = Mathf.Min(earned, max - level, Mathf.CeilToInt(rootNow) - 1);
                    if (added > 0)
                    {
                        zdo.Set(ZDOVars.s_level, level + added);
                        root.Drain(added);
                    }
                    product = 0f;
                }
                zdo.Set(ZDOVars.s_product, product);
            }

            if (!absence) return;

            string vanilla;
            if (state.HeldForRoot)
                vanilla = "discards it all, root not loaded on the extractor's first tick (trigger 1)";
            else if (!root)
                vanilla = "discards it all, no root connected";
            else if (level >= max)
                vanilla = "credits nothing, already full";
            else if (!(rootBefore > 1f))
                vanilla = $"discards it all, root not regenerated yet at {rootBefore:F1} (trigger 2)";
            else
            {
                // Vanilla drains what the extractor earned (capped only by the root), then
                // clamps what it adds to the room left.
                int vanillaDrained = Mathf.Min((int)rootBefore, earned);
                int vanillaAdded = Mathf.Min(vanillaDrained, max - level);
                vanilla = (int)rootBefore < Mathf.Min(earned, max - level)
                    ? $"credits {vanillaAdded}, capped by a root not regenerated yet at {rootBefore:F1} (trigger 2)"
                    : $"credits {vanillaAdded}";
                if (vanillaDrained > vanillaAdded)
                    vanilla += $" but drains the root by {vanillaDrained} (trigger 3)";
            }

            string rootText = !root
                ? "no root"
                : $"root {rootBefore:F1} -> {rootNow:F1} after regen{(rootOwned ? "" : " (root owned by someone else, not regenerated here)")}";
            string held = state.HeldForRoot ? $", root appeared after {waited:F0} s" : "";
            Report($"{Where(collector)}: away {elapsed:F0} s, worth {earned} at {secPerUnit:F0} s each{held}. " +
                   $"Vanilla {vanilla}. Fix: {level} -> {level + added} of {max}, {rootText}, drained {added}.");
        }

        private static string Where(SapCollector collector)
        {
            Vector3 p = collector.transform.position;
            return $"Sap extractor at ({p.x:F0}, {p.z:F0})";
        }

        private static void Report(string line)
        {
            if (SapExtractorProgressionFixMod.LogCatchUp.Value)
                SapExtractorProgressionFixMod.Log.LogInfo(line);
        }
    }
}
