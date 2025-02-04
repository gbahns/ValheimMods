using System.Linq;
using System.Runtime.InteropServices;
using HarmonyLib;
using UnityEngine;

namespace GrabMaterials
{
	[HarmonyPatch(typeof(Container), "Load")]
	internal class ContainerLoadPatch
	{
		private static void Postfix(Container __instance)
		{
			Boxes.ConditionallyAddContainer(__instance, "Load");
		}
	}

	[HarmonyPatch(typeof(Container), "Awake")]
	internal static class ContainerAwakePatch
	{
		private static void Postfix(Container __instance)
		{
			Debug.Log($"*** CONTAINER AWAKE POSTFIX GOT CALLED *** {__instance.name} {__instance.GetInstanceID()}");
			Boxes.ConditionallyAddContainer(__instance, "Awake");
		}
	}

	[HarmonyPatch(typeof(Container), "OnDestroyed")]
	internal static class ContainerOnDestroyedPatch
	{
		private static void Postfix(Container __instance)
		{
			Boxes.RemoveContainer(__instance);
		}
	}

	[HarmonyPatch(typeof(Player), "UpdateTeleport")]
	public static class PlayerUpdateTeleportPatchCleanupContainers
	{
		public static void Prefix(float dt)
		{
			var player = Player.m_localPlayer;
			if (player == null || player.IsTeleporting())
			{
				return;
			}
			foreach (Container item in from container in Boxes.Containers.ToList()
									   where !((Object)(object)container != (Object)null) || !((Object)(object)((Component)container).transform != (Object)null) || container.GetInventory() == null
									   where (Object)(object)container != (Object)null
									   select container)
			{
				Boxes.RemoveContainer(item);
			}
		}
	}

	[HarmonyPatch(typeof(WearNTear), "OnDestroy")]
	internal static class WearNTearOnDestroyPatch
	{
		private static void Prefix(WearNTear __instance)
		{
			Container[] componentsInChildren = ((Component)__instance).GetComponentsInChildren<Container>();
			Container[] componentsInParent = ((Component)__instance).GetComponentsInParent<Container>();
			if (componentsInChildren.Length != 0)
			{
				Container[] array = componentsInChildren;
				for (int i = 0; i < array.Length; i++)
				{
					Boxes.RemoveContainer(array[i]);
				}
			}
			if (componentsInParent.Length != 0)
			{
				Container[] array = componentsInParent;
				for (int i = 0; i < array.Length; i++)
				{
					Boxes.RemoveContainer(array[i]);
				}
			}
		}
	}

}
