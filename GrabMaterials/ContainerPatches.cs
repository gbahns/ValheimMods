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
			//Debug.Log($"*** CONTAINER AWAKE POSTFIX GOT CALLED *** {__instance.name} {__instance.GetInstanceID()}");
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
			// Drop containers that have been destroyed or lost their inventory.  The old LINQ
			// selected the dead ones and then filtered the nulls back out, so it never removed
			// anything (#36).
			foreach (var container in Boxes.Containers.ToList())
			{
				if (container == null || container.transform == null || container.GetInventory() == null)
					Boxes.RemoveContainer(container);
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
