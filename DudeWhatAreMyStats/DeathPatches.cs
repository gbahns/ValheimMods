using HarmonyLib;

namespace DudeWhatAreMyStats
{
    // The three hooks that feed DeathWatch, plus the one that seals a report. See the class comment
    // on DeathWatch for why each of these is the right place for the job it does; the short version
    // is that Damage sees the swing and ApplyDamage sees the cost, and neither substitutes for the
    // other.

    /// <summary>
    /// Every blow that reaches the local player, before block, resistance and armor, and before the
    /// early exit that drops a hit they dodged. Records the attempt, not the damage.
    /// </summary>
    [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
    internal static class Character_Damage_DeathWatch_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(Character __instance, HitData hit)
        {
            if (__instance == null || hit == null || !DeathWatch.Enabled) return;
            if (__instance != Player.m_localPlayer) return;

            Character attacker = null;
            try { attacker = hit.HaveAttacker() ? hit.GetAttacker() : null; }
            catch { /* the attempt is still worth counting */ }
            // Your own bomb or staff. It counts as being in a fight but not as somebody else's work.
            if (attacker == __instance) attacker = null;

            bool dodged = false;
            try { dodged = hit.m_dodgeable && __instance.IsDodgeInvincible(); }
            catch { /* not worth losing the row over */ }

            DeathWatch.RecordAttempt(attacker, hit, dodged);
        }
    }

    /// <summary>
    /// What a blow actually cost, after everything that reduces it. A hit blocked to nothing arrives
    /// here worth nothing, which is the distinction the whole ledger rests on.
    /// </summary>
    [HarmonyPatch(typeof(Character), nameof(Character.ApplyDamage))]
    internal static class Character_ApplyDamage_DeathWatch_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(Character __instance, HitData hit)
        {
            if (__instance == null || hit == null || !DeathWatch.Enabled) return;
            if (__instance != Player.m_localPlayer) return;
            // ApplyDamage itself drops these on the floor a few lines further down, so counting them
            // would credit a creature for damage nobody ever took.
            if (__instance.IsDead() || __instance.IsTeleporting() || __instance.InCutscene()) return;
            DeathWatch.RecordDamage(hit);
        }
    }

    /// <summary>
    /// Blocking, which is where a creature that never scratches you still wears you down. This is
    /// the one place the game both spends the stamina and knows whose blow forced it.
    /// </summary>
    [HarmonyPatch(typeof(Humanoid), "BlockAttack")]
    internal static class Humanoid_BlockAttack_DeathWatch_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(Humanoid __instance, ref float __state)
        {
            __state = -1f;
            if (!DeathWatch.Enabled) return;
            var player = __instance as Player;
            if (player == null || player != Player.m_localPlayer) return;
            __state = player.GetStamina();
        }

        [HarmonyPostfix]
        private static void Postfix(Humanoid __instance, Character attacker, bool __result, float __state)
        {
            if (__state < 0f) return;
            var player = __instance as Player;
            if (player == null) return;
            // A perfect block can hand stamina back, so this is a delta and not a cost.
            float spent = __state - player.GetStamina();
            // BlockAttack also returns early when you are facing the wrong way or holding nothing,
            // which is not a block at all. Either a successful block or stamina spent means it was.
            if (!__result && spent <= 0f) return;
            DeathWatch.RecordBlock(attacker, spent > 0f ? spent : 0f);
        }
    }

    /// <summary>
    /// The death itself. OnDeath runs on the dying player's own client and is the last place
    /// m_lastHit -- the blow that finished them -- still exists.
    /// </summary>
    [HarmonyPatch(typeof(Player), "OnDeath")]
    internal static class Player_OnDeath_DeathWatch_Patch
    {
        // m_lastHit is protected. A publicized reference would compile and then throw
        // FieldAccessException in game, since the game loads the real assemblies, so it goes
        // through Harmony's field ref instead.
        private static readonly AccessTools.FieldRef<Character, HitData> LastHit =
            AccessTools.FieldRefAccess<Character, HitData>("m_lastHit");

        [HarmonyPostfix]
        private static void Postfix(Player __instance)
        {
            if (__instance == null || __instance != Player.m_localPlayer) return;
            if (!DeathWatch.Enabled) return;
            HitData hit = null;
            try { hit = LastHit(__instance); }
            catch { /* a report without the killing blow still names who was there */ }
            DeathWatch.Seal(__instance, hit);
        }
    }
}
