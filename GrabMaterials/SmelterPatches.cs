using HarmonyLib;

namespace GrabMaterials
{
	[HarmonyPatch(typeof(Smelter), "Awake")]
	internal static class SmelterAwakePatch
	{
		private static void Postfix(Smelter __instance)
		{
			Boxes.ConditionallyAddSmelter(__instance);
		}
	}

	[HarmonyPatch(typeof(Smelter), "OnDestroyed")]
	internal static class SmelterOnDestroyedPatch
	{
		private static void Postfix(Smelter __instance)
		{
			Boxes.RemoveSmelter(__instance);
		}
	}
}
