using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace TheObituaries
{
    /// <summary>
    /// Who has hit the local player lately, by attacker id.
    ///
    /// A hit only carries its attacker as a ZDOID, and at death the game looks that id up in
    /// the local scene. When the creature has despawned or been killed in the meantime the
    /// lookup fails and the obituary would have no name to give. The attacker is nearly always
    /// present when the hit lands, so its description is taken then and kept for a while.
    /// </summary>
    internal static class AttackerMemory
    {
        internal sealed class Entry
        {
            public string Description;   // "a 2-star Troll", "Marco", ...
            public string Prefab;        // for the creature table
            public bool   IsPlayer;
            public float  Time;
        }

        private const float Retention = 120f;   // seconds; a death is rarely that long after the last hit

        private static readonly Dictionary<ZDOID, Entry> Recent = new Dictionary<ZDOID, Entry>();
        private static float _lastPrune;

        internal static void Clear() => Recent.Clear();

        internal static void Record(Character attacker)
        {
            if (attacker == null) return;
            ZDOID id = attacker.GetZDOID();
            if (id == ZDOID.None) return;
            Recent[id] = new Entry
            {
                Description = Obituary.Describe(attacker),
                Prefab      = Obituary.PrefabName(attacker),
                IsPlayer    = attacker is Player,
                Time        = UnityEngine.Time.time,
            };
            Prune();
        }

        internal static Entry Lookup(ZDOID id)
        {
            if (id == ZDOID.None || !Recent.TryGetValue(id, out var e)) return null;
            return UnityEngine.Time.time - e.Time <= Retention ? e : null;
        }

        private static void Prune()
        {
            float now = UnityEngine.Time.time;
            if (now - _lastPrune < 30f) return;
            _lastPrune = now;
            var stale = new List<ZDOID>();
            foreach (var kv in Recent)
                if (now - kv.Value.Time > Retention) stale.Add(kv.Key);
            foreach (var id in stale) Recent.Remove(id);
        }
    }

    // Every hit the local player takes goes through ApplyDamage on its own client (RPC_Damage
    // and the burning/poison ticks all end up here), and the attacker is still around then.
    [HarmonyPatch(typeof(Character), nameof(Character.ApplyDamage))]
    internal static class Character_ApplyDamage_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(Character __instance, HitData hit)
        {
            if (__instance == null || __instance != Player.m_localPlayer || hit == null) return;
            if (!hit.HaveAttacker()) return;
            try
            {
                Character attacker = hit.GetAttacker();
                if (attacker != null && attacker != __instance) AttackerMemory.Record(attacker);
            }
            catch (System.Exception e)
            {
                TheObituariesMod.Log.LogDebug("Could not remember an attacker: " + e.Message);
            }
        }
    }
}
