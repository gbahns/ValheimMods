using System;
using HarmonyLib;

namespace TheObituaries
{
    // Game.Start runs once per session, after ZNet has created ZRoutedRpc (a fresh instance per
    // session, so registration repeats on every login).
    [HarmonyPatch(typeof(Game), "Start")]
    internal static class Game_Start_Patch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            AttackerMemory.Clear();
            DeathNetwork.Register();
        }
    }

    // Player.OnDeath runs on the dying player's own client (it returns at once for anyone
    // else's character) and is the one place that still has m_lastHit: the hit that killed.
    // The player object is not destroyed by it - the visual is hidden and a respawn requested -
    // so the name is still readable afterwards.
    [HarmonyPatch(typeof(Player), "OnDeath")]
    internal static class Player_OnDeath_Patch
    {
        // Character.m_lastHit is protected; a publicized reference would compile and then throw
        // FieldAccessException in game, so it goes through Harmony's field ref.
        private static readonly AccessTools.FieldRef<Character, HitData> LastHit =
            AccessTools.FieldRefAccess<Character, HitData>("m_lastHit");

        [HarmonyPostfix]
        private static void Postfix(Player __instance)
        {
            if (__instance == null || __instance != Player.m_localPlayer) return;
            if (!TheObituariesMod.ModEnabled.Value) return;
            try
            {
                HitData hit = LastHit(__instance);
                Notice notice = Obituary.Compose(__instance, hit);
                DeathNetwork.Send(notice);
            }
            catch (Exception e)
            {
                TheObituariesMod.Log.LogWarning("Could not announce the death: " + e);
            }
        }
    }
}
