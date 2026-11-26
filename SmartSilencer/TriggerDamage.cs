using System;
using System.Collections.Generic;
using HarmonyLib;

namespace SmartSilencer
{
    /// <summary>
    /// Stops the game complaining about a damage path it has itself switched off.
    ///
    /// An Aoe with a collider but with trigger damage disabled logs a warning for every collider
    /// overlapping it, every physics tick:
    ///
    ///     private void CauseTriggerDamage(Collider collider, bool onTriggerEnter)
    ///     {
    ///         if (!(m_triggerEnterOnly &amp;&amp; onTriggerEnter) &amp;&amp; !(m_activationTimer > 0f))
    ///         {
    ///             if (!m_useTriggers)
    ///                 ZLog.LogWarning("AOE got OnTriggerStay but trigger damage is disabled in " + gameObject.name);
    ///             else if (ShouldHit(collider))
    ///                 OnHit(collider, collider.bounds.center);
    ///         }
    ///     }
    ///
    /// The branch is the whole of it: with m_useTriggers off the method logs and does nothing else,
    /// so skipping it is what the game already does, minus the line. Damage is untouched - the
    /// other branch is where hits happen, and it is left alone.
    ///
    /// Valheim ships at least one of these. The pufferfish prefab Fish12 carries, under
    /// attach/attachobj, a mesh child named "default spiky" with an Aoe of 40 poison and a box
    /// collider, and that Aoe has trigger damage off. One pufferfish lying where things touch it
    /// wrote 9,741 lines in four minutes. The fish still poisons whoever handles it: that goes
    /// through the Aoe's own radius, not through this.
    /// </summary>
    internal static class TriggerDamage
    {
        // Names already reported, so a noisy object is mentioned once rather than per tick.
        private static readonly HashSet<string> _reported = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Whether this Aoe's trigger pass should be skipped, because all it can do is log.</summary>
        internal static bool ShouldSkip(Aoe aoe)
        {
            try
            {
                if (SmartSilencerMod.QuietDisabledTriggerDamage == null
                    || !SmartSilencerMod.QuietDisabledTriggerDamage.Value) return false;
                if (aoe == null || aoe.m_useTriggers) return false;

                if (SmartSilencerMod.LogTransitions != null && SmartSilencerMod.LogTransitions.Value)
                {
                    string name = aoe.gameObject != null ? aoe.gameObject.name : "?";
                    if (_reported.Add(name))
                        SmartSilencerMod.Log.LogInfo(
                            $"[SmartSilencer] '{name}' has trigger damage off but a collider others touch; " +
                            "not letting the game warn about it once a tick.");
                }
                return true;
            }
            catch (Exception e)
            {
                // Whatever went wrong here, the game's own behaviour is the safe answer.
                SmartSilencerMod.Log.LogWarning($"[SmartSilencer] Trigger damage check failed: {e.Message}");
                return false;
            }
        }
    }

    [HarmonyPatch(typeof(Aoe), "CauseTriggerDamage")]
    internal static class Aoe_CauseTriggerDamage_Patch
    {
        // Only __instance is taken: naming the Collider parameter would drag
        // UnityEngine.PhysicsModule into this project for nothing.
        private static bool Prefix(Aoe __instance)
        {
            return !TriggerDamage.ShouldSkip(__instance);
        }
    }
}
