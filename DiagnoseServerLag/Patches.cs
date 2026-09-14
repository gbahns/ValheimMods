using HarmonyLib;

namespace DiagnoseServerLag
{
    // ── the cursor, and holding the world's hands off the keyboard ────────────────

    /// <summary>
    /// Makes vanilla treat the report as one of its own screens.
    ///
    /// Valheim frees the mouse for its own UI and nothing else. GameCamera.UpdateMouseCapture
    /// locks the cursor and hides it unless one of a fixed list of screens is up - the inventory,
    /// the menu, the map, the store, a text prompt - so a panel built by a mod gets no pointer at
    /// all: the mouse keeps turning the character behind it and its buttons cannot be clicked.
    /// That is what happened to 0.2.0's pause toggle.
    ///
    /// Reporting the report as a text prompt is the whole fix, because TextInput.IsVisible is one
    /// of the cases on that list, and the same flag is what vanilla already consults to hold back
    /// movement, the hotbar, the map key, the ESC menu and chat. Jotunn's GUIManager.BlockInput
    /// does exactly this underneath; doing it directly keeps the mod free of that dependency, and
    /// is what DudeWhatAreMyStats does for the same reason.
    ///
    /// The frame after closing counts too, so the key that closed the panel does not also act on
    /// the world on its way out.
    /// </summary>
    [HarmonyPatch(typeof(TextInput), nameof(TextInput.IsVisible))]
    internal static class TextInput_IsVisible_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(ref bool __result)
        {
            if (!__result && (LagPanel.IsOpen || LagPanel.JustClosed)) __result = true;
        }
    }

    /// <summary>
    /// The mouse wheel belongs to the report's list while it is open.
    ///
    /// GameCamera's zoom check looks at chat, the console, the inventory, the store, the menu, the
    /// map, piece selection and the radial - but not at text prompts - so without this the camera
    /// zooms in and out behind the panel the whole time you scroll the evidence. The list itself
    /// scrolls through UI pointer events, which this does not touch.
    /// </summary>
    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetMouseScrollWheel))]
    internal static class ZInput_GetMouseScrollWheel_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(ref float __result)
        {
            if (__result != 0f && LagPanel.IsOpen) __result = 0f;
        }
    }

    // ── world lifetime ────────────────────────────────────────────────────────────

    /// <summary>
    /// Leaves the world cleanly rather than a frame late.
    ///
    /// The plugin's Update notices a world going away on its own, but only on the next frame, and
    /// the pause is a global flag: dropping it here means a shutdown can never leave the game
    /// frozen behind a panel that no longer exists. Registration is not repeated - LagNetwork does
    /// that per ZRoutedRpc instance, which is the honest test for a new session.
    /// </summary>
    [HarmonyPatch(typeof(ZNet), nameof(ZNet.Shutdown))]
    internal static class ZNet_Shutdown_Patch
    {
        [HarmonyPrefix]
        private static void Prefix()
        {
            LagPanel.Close();
            LagPause.Release();
            Sampler.Reset();
            LagNetwork.Reset();
        }
    }
}
