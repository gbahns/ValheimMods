using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace GrabMaterials
{
	/// <summary>
	/// Lets the player point at and click an interactive panel (inventory, packs, pack editor)
	/// without taking the keyboard away, which is how vanilla treats its own inventory screen:
	/// the cursor is free, the mouse stops turning the camera, clicks are not attacks, and
	/// scrolling is not zoom, but movement and every hotkey keep working.
	///
	/// This replaces Jotunn's GUIManager.BlockInput, which also stopped all player input and
	/// made the game believe a text box was open, so everything but Escape went dead while a
	/// panel was up.
	/// </summary>
	internal static class PanelInput
	{
		internal static bool PanelWantsPointer =>
			MaterialsPanel.InteractivePanelVisible && Player.m_localPlayer != null;
	}

	// Free the cursor while a panel is up. Vanilla locks it again on the first frame after the
	// panel closes, so there is nothing to undo.
	[HarmonyPatch(typeof(GameCamera), nameof(GameCamera.UpdateMouseCapture))]
	internal static class GameCamera_UpdateMouseCapture_PanelCursor
	{
		private static void Postfix()
		{
			if (!PanelInput.PanelWantsPointer) return;
			ZCursor.LockState = CursorLockMode.None;
			ZCursor.Show();
		}
	}

	// Vanilla's "an inventory-like screen is open" test. While it is true the mouse no longer
	// turns the camera and clicks are not attacks or blocks, while movement, jumping and
	// hotkeys still work.
	[HarmonyPatch(typeof(PlayerController), "InInventoryEtc")]
	internal static class PlayerController_InInventoryEtc_Panel
	{
		private static void Postfix(ref bool __result)
		{
			if (PanelInput.PanelWantsPointer) __result = true;
		}
	}

	// Typing into one of the pack editor's fields must not also walk the character or fire
	// hotkeys. Reporting a text input as open is how the game keeps keys out of play while you
	// type, and this mod's own hotkeys check the same thing.
	[HarmonyPatch(typeof(TextInput), nameof(TextInput.IsVisible))]
	internal static class TextInput_IsVisible_PanelTyping
	{
		private static void Postfix(ref bool __result)
		{
			if (!__result && MaterialsPanel.TypingInPanel) __result = true;
		}
	}

	// Scrolling a panel's list must not zoom the camera. Vanilla skips its zoom block while its
	// own screens are open, and this adds ours to that condition by branching past the block
	// straight after its Menu.IsVisible() test, the same spot Jotunn's input block used. If the
	// method ever changes shape, the patch leaves it alone and logs a warning.
	[HarmonyPatch(typeof(GameCamera), "UpdateCamera")]
	internal static class GameCamera_UpdateCamera_PanelNoZoom
	{
		private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var codes = new List<CodeInstruction>(instructions);
			var menuIsVisible = AccessTools.Method(typeof(Menu), nameof(Menu.IsVisible));
			var wantsPointer = AccessTools.PropertyGetter(typeof(PanelInput), nameof(PanelInput.PanelWantsPointer));

			for (var i = 0; i < codes.Count - 1; i++)
			{
				if (!codes[i].Calls(menuIsVisible)) continue;
				var branch = codes[i + 1];
				if (branch.opcode != OpCodes.Brtrue && branch.opcode != OpCodes.Brtrue_S) break;

				codes.InsertRange(i + 2, new[]
				{
					new CodeInstruction(OpCodes.Call, wantsPointer),
					new CodeInstruction(OpCodes.Brtrue, branch.operand),
				});
				return codes;
			}

			GrabMaterialsMod.GrabMaterialsMod.Log.LogWarning(
				"GameCamera.UpdateCamera has changed shape; scrolling a Grab Materials panel may zoom the camera");
			return codes;
		}
	}
}
