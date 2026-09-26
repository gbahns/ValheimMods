using HarmonyLib;
using UnityEngine;

namespace Comfortometer
{
    // Arrange mode borrows the way vanilla treats its own inventory screen: the cursor is free,
    // the mouse stops turning the camera, and clicks are not attacks or blocks, while walking
    // and every hotkey keep working. Outside arrange mode neither patch does anything.

    /// <summary>Free the cursor while arranging. Vanilla locks it again on the first frame after, so there is nothing to undo.</summary>
    [HarmonyPatch(typeof(GameCamera), nameof(GameCamera.UpdateMouseCapture))]
    internal static class GameCamera_UpdateMouseCapture_Arrange
    {
        private static void Postfix()
        {
            if (!ComfortPanel.WantsPointer) return;
            ZCursor.LockState = CursorLockMode.None;
            ZCursor.Show();
        }
    }

    /// <summary>Vanilla's "an inventory-like screen is open" test: no mouse look, no attack on click.</summary>
    [HarmonyPatch(typeof(PlayerController), "InInventoryEtc")]
    internal static class PlayerController_InInventoryEtc_Arrange
    {
        private static void Postfix(ref bool __result)
        {
            if (ComfortPanel.WantsPointer) __result = true;
        }
    }
}
