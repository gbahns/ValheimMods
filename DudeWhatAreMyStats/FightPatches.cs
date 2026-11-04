using HarmonyLib;

namespace DudeWhatAreMyStats
{
    // The hooks that feed FightWatch. All of them run on whichever game owns the creature, which is
    // the only one that sees every blow -- see the class comment on FightWatch.

    /// <summary>
    /// What a blow actually cost the creature, measured as a health delta.
    ///
    /// The number is deliberately not read off the HitData. By the time a hit reaches ApplyDamage
    /// the game has applied resistances, the weak spot and armor to it, and ApplyDamage then
    /// applies the difficulty scale and Game.m_playerDamageRate on top, so the swing and the wound
    /// are different figures. A delta also gives overkill, immunity, god mode and the sub-0.1
    /// damage floor for free: each of those shows up as a smaller delta or none at all, with no
    /// special case to keep in step with the game.
    /// </summary>
    [HarmonyPatch(typeof(Character), nameof(Character.ApplyDamage))]
    internal static class Character_ApplyDamage_FightWatch_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(Character __instance, ref float __state)
        {
            __state = -1f;
            if (__instance == null || !FightWatch.Enabled) return;
            // Players have their own ledger in DeathWatch; this one is about what you fought.
            if (__instance.IsPlayer()) return;
            var nview = __instance.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid() || !nview.IsOwner()) return;
            __state = __instance.GetHealth();
        }

        [HarmonyPostfix]
        private static void Postfix(Character __instance, HitData hit, float __state)
        {
            if (__state < 0f || __instance == null) return;
            // Healing arrives here as negative damage, so only a drop counts.
            float lost = __state - __instance.GetHealth();
            if (lost <= 0f) return;
            FightWatch.Record(__instance, hit, lost);
        }
    }

    /// <summary>
    /// The blow being processed, so the three methods below can learn who struck it.
    ///
    /// RPC_Damage is where the attacker is still on the hit. It is private, and it is also the only
    /// place the elemental damage gets stripped off a hit and handed to a status effect, so it is
    /// the right bracket to hold.
    /// </summary>
    [HarmonyPatch(typeof(Character), "RPC_Damage")]
    internal static class Character_RPCDamage_FightWatch_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(Character __instance, HitData hit)
        {
            if (__instance == null || hit == null || !FightWatch.Enabled) return;
            if (__instance.IsPlayer()) return;
            Character attacker = null;
            try { attacker = hit.HaveAttacker() ? hit.GetAttacker() : null; }
            catch { /* the damage still counts, just not against a name */ }
            FightWatch.BeginHit(__instance, attacker);
        }

        [HarmonyPostfix]
        private static void Postfix()
        {
            FightWatch.EndHit();
        }
    }

    // Poison, fire and spirit ticks carry no attacker: SE_Poison and SE_Burning build a fresh
    // HitData with nothing but a hit type and call ApplyDamage directly, and
    // StatusEffect.SetAttacker is an empty virtual that neither of them overrides. So the only
    // moment the author is knowable is when the element is applied.
    //
    // These three private methods are the right moment rather than the hit itself, because each is
    // called from exactly one place -- RPC_Damage, after the early exits and after the element has
    // been stripped off the hit. A blow that was dodged, discarded or aimed at an immune target
    // never reaches them, so a dodge cannot steal the credit for somebody else's poison.

    [HarmonyPatch(typeof(Character), "AddPoisonDamage")]
    internal static class Character_AddPoisonDamage_FightWatch_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(Character __instance, float damage)
        {
            if (damage > 0f) FightWatch.NoteElement(__instance, 'p');
        }
    }

    [HarmonyPatch(typeof(Character), "AddFireDamage")]
    internal static class Character_AddFireDamage_FightWatch_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(Character __instance, float damage)
        {
            if (damage > 0f) FightWatch.NoteElement(__instance, 'f');
        }
    }

    [HarmonyPatch(typeof(Character), "AddSpiritDamage")]
    internal static class Character_AddSpiritDamage_FightWatch_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(Character __instance, float damage)
        {
            if (damage > 0f) FightWatch.NoteElement(__instance, 's');
        }
    }

    /// <summary>
    /// The end of the fight. Same gate vanilla puts on its own kill crediting a few lines further
    /// down: the owner reports, and nobody else, or every game holding the creature would.
    /// </summary>
    [HarmonyPatch(typeof(Character), nameof(Character.OnDeath))]
    internal static class Character_OnDeath_FightWatch_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(Character __instance)
        {
            if (__instance == null || !FightWatch.Enabled) return;
            if (__instance.IsPlayer()) return;
            var nview = __instance.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid() || !nview.IsOwner()) return;
            FightWatch.Seal(__instance);
        }
    }
}
