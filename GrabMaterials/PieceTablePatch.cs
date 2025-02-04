using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;
using UnityEngine;

namespace GrabMaterials
{
	public static class HudTranspilers
	{
	//	[HarmonyPatch(typeof(Hud), nameof(Hud.GetSelectedGrid))]
	//	public static class Hud_GetSelectedGrid_Transpiler
	//	{
	//		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
	//		{
	//			//if (BuildExpansionMod.isEnabled.Value)
	//			//{
	//			//	var codes = new List<CodeInstruction>(instructions);
	//			//	codes[0].operand = BuildExpansionMod.newGridWidth.Value;
	//			//	codes[2].opcode = OpCodes.Ldc_I4_S;
	//			//	codes[2].operand = BuildExpansionMod.maxGridHeight.Value;
	//			//	return codes;
	//			//}
	//			return instructions;
	//		}
	//	}

	//	[HarmonyPatch(typeof(Hud), nameof(Hud.UpdatePieceList))]
	//	public static class Hud_UpdatePieceList_Transpiler
	//	{
	//		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
	//		{
	//			//if (BuildExpansionMod.isEnabled.Value)
	//			//{
	//			//	var codes = new List<CodeInstruction>(instructions);
	//			//	codes[3].operand = BuildExpansionMod.newGridWidth.Value;
	//			//	codes[5].opcode = OpCodes.Ldc_I4_S;
	//			//	codes[5].operand = HudPatches.calculatedRows;
	//			//	return codes;
	//			//}
	//			return instructions;
	//		}
	//	}
	//}


	[HarmonyPatch(typeof(Hud), "Awake")]
	public static class PieceTablePatch
	{
		static void Postfix(Hud __instance)
		{
			Debug.Log($"Hud active {__instance.name}");
				// Add your logic here for when the PieceTable is opened
		}
	}   
	//[HarmonyPatch(typeof(BuildMenu), "OnItemClicked")]
		//public class BuildMenuPatch
		//{
		//	static bool Prefix(BuildMenu __instance, int itemIndex)
		//	{
		//		// Check if the right mouse button was clicked
		//		if (ZInput.GetMouseButtonDown(1))
		//		{
		//			// Get the item that was right-clicked
		//			var item = __instance.GetItem(itemIndex);

	//			// Implement your custom functionality here
	//			Debug.Log($"Right-clicked on item: {item}");
	//			//Player.m_localPlayer.Message(MessageHud.MessageType.Center, $"Right-clicked on item: {item.}");

	//			// Return false to prevent the original method from executing if desired
	//			return false;
	//		}

	//		// Allow the original method to execute if it was not a right-click
	//		return true;
	//	}
	//}
}
