using HarmonyLib;

namespace DudeWhatAreMyStats
{
    // ── world lifetime ────────────────────────────────────────────────────────────

    [HarmonyPatch(typeof(Game), "Start")]
    internal static class Game_Start_Patch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            // Reset here as well as on shutdown: not every way out of a world goes through
            // ZNet.Shutdown (ShutdownWithoutSave does not), and a roster carried into the next
            // world would list the last one's players.
            StatsNetwork.Reset();
            StatsNetwork.Register();
        }
    }

    [HarmonyPatch(typeof(ZNet), nameof(ZNet.Shutdown))]
    internal static class ZNet_Shutdown_Patch
    {
        [HarmonyPrefix]
        private static void Prefix()
        {
            StatsPanel.Close();
            StatsNetwork.Reset();
        }
    }

    // ── input blocking while the panel is up ──────────────────────────────────────

    // Everything vanilla holds back for its own text prompt (movement, hotbar, the map key, the
    // ESC menu, chat, mouse capture) it holds back for this mod's panel, and for the frame after
    // the panel closes, so the key that closed it does not also act on the world.
    [HarmonyPatch(typeof(TextInput), nameof(TextInput.IsVisible))]
    internal static class TextInput_IsVisible_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(ref bool __result)
        {
            if (!__result && (StatsPanel.IsOpen || StatsPanel.JustClosed)) __result = true;
        }
    }

    // The mouse wheel belongs to the stats list while the panel is open. GameCamera's zoom check
    // looks at chat, the console, the inventory, the store, the menu, the map, piece selection and
    // the radial, but not at text prompts, so without this the camera zooms in and out behind the
    // panel the whole time you scroll a long list. The list itself scrolls through UI pointer
    // events, which this does not touch.
    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetMouseScrollWheel))]
    internal static class ZInput_GetMouseScrollWheel_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(ref float __result)
        {
            if (__result != 0f && StatsPanel.IsOpen) __result = 0f;
        }
    }
}
