using HarmonyLib;

namespace PauseMyServer
{
    // ── client: what the player wants ─────────────────────────────────────────────
    //
    // Menu.Show() calls Game.Pause() and Menu.Hide() calls Game.Unpause(); so does the intro
    // cinematic. Tracking those two public calls mirrors the private Game.m_pause without
    // touching it.

    [HarmonyPatch(typeof(Game), nameof(Game.Pause))]
    internal static class Game_Pause_Patch
    {
        [HarmonyPostfix]
        private static void Postfix() => PauseSync.WantPause = true;
    }

    [HarmonyPatch(typeof(Game), nameof(Game.Unpause))]
    internal static class Game_Unpause_Patch
    {
        [HarmonyPostfix]
        private static void Postfix() => PauseSync.WantPause = false;
    }

    // ── client: the freeze itself ─────────────────────────────────────────────────
    //
    // Vanilla: IsPaused() = m_pause && CanPause(), and CanPause() is false for anyone connected
    // to a server. Game.UpdatePause() (called every frame from Game.Update, which keeps running
    // at time scale 0) sets Time.timeScale = IsPaused() ? 0 : 1. We report paused once the
    // server has confirmed, and either the menu is still open (lone pause) or the pause is an
    // admin pause. Closing the menu during a lone pause resumes locally at once without waiting
    // for the round trip. Never before the player has spawned: loading needs frame time.

    [HarmonyPatch(typeof(Game), nameof(Game.IsPaused))]
    internal static class Game_IsPaused_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(ref bool __result)
        {
            if (__result || Player.m_localPlayer == null) return;
            if (PauseSync.ClientPaused && (PauseSync.ClientForced || PauseSync.WantPause)) __result = true;
        }
    }

    // A player who spawns during an admin pause asks for the state and is frozen too.
    [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
    internal static class Player_OnSpawned_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(Player __instance)
        {
            if (__instance == Player.m_localPlayer) PauseSync.RequestState();
        }
    }

    // ── server: what still runs on a dedicated server while its clients are frozen ──
    //
    // Everything near a player is owned and simulated by that player's client, so frozen
    // clients freeze monsters, ships, physics, smelters and the rest. Three things run on the
    // server itself and must be held too. The server's own frame time is deliberately left
    // alone: with Time.timeScale = 0 the server would stop sending player lists and ZDO data,
    // and a joining player could never load in.
    //
    // ServerPaused is only ever true on the server, so these prefixes are no-ops on clients.

    // The world clock (ZNet.FixedUpdate -> UpdateNetTime). Day/night, weather, plant growth,
    // fermenters, beehives and respawn timers all derive from it.
    [HarmonyPatch(typeof(ZNet), "UpdateNetTime")]
    internal static class ZNet_UpdateNetTime_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix() => !PauseSync.ServerPaused;
    }

    // Raids and other random events: the server picks them and runs their timers.
    [HarmonyPatch(typeof(RandEventSystem), "FixedUpdate")]
    internal static class RandEventSystem_FixedUpdate_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix() => !PauseSync.ServerPaused;
    }

    // A sleep time-skip in progress advances the clock on the server; it waits too.
    [HarmonyPatch(typeof(EnvMan), "UpdateTimeSkip")]
    internal static class EnvMan_UpdateTimeSkip_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix() => !PauseSync.ServerPaused;
    }

    // ── lifecycle ─────────────────────────────────────────────────────────────────

    // Game.Start runs once per session on both the server and the client, after ZNet has
    // created ZRoutedRpc (a fresh instance per session, so registration repeats on every login).
    [HarmonyPatch(typeof(Game), "Start")]
    internal static class Game_Start_Patch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            PauseSync.Reset();
            PauseSync.Register();
        }
    }

    [HarmonyPatch(typeof(ZNet), nameof(ZNet.Shutdown))]
    internal static class ZNet_Shutdown_Patch
    {
        [HarmonyPrefix]
        private static void Prefix() => PauseSync.Reset();
    }
}
