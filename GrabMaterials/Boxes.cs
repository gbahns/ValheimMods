using System.Collections.Generic;
using UnityEngine;
using HarmonyLib;
using static GrabMaterialsMod.GrabMaterialsMod;

namespace GrabMaterials
{
	internal class Boxes
	{
		internal static readonly List<Container> Containers = new List<Container>();
		// Containers found dead while GetNearbyContainers walks the list; removed once the walk
		// is over, since the list cannot change under its own foreach.  Add/Remove below write
		// the list directly (#37).
		private static readonly List<Container> ContainersToRemove = new List<Container>();

		internal static readonly List<Smelter> Smelters = new List<Smelter>();
		private static readonly List<Smelter> SmeltersToRemove = new List<Smelter>();

		internal static void AddContainer(Container container)
		{
			if (container == null || Containers.Contains(container)) return;
			Containers.Add(container);
			Jotunn.Logger.LogDebug($"Added container {container.name} ({container.GetInstanceID()}) to list");
		}
		internal static void RemoveContainer(Container container)
		{
			// A destroyed Unity object is "== null" but still a list entry; Remove finds it by Equals.
			if (Containers.Remove(container) && container != null)
				Jotunn.Logger.LogDebug($"Removed container {container.name} ({container.GetInstanceID()}) from list");
		}

		// Flush the dead-container list after a walk of Containers.
		internal static void UpdateContainers()
		{
			foreach (var container in ContainersToRemove) Containers.Remove(container);
			ContainersToRemove.Clear();
		}

		internal static void ConditionallyAddContainer(Container container, string trigger)
		{
			// Log.LogInfo($"Container {trigger}: {container.name} {container.GetType()} {container.GetInstanceID()}");

			if (container.GetInventory() == null)
			{
				return;
			}
			//if ((Object)(object)((Component)container).GetComponentInParent<Player>() != (Object)null && (Object)(object)((Component)container).GetComponentInParent<Player>() != (Object)(object)Player.m_localPlayer)
			//{
			//    return;
			//}
			long playerID = Game.instance.GetPlayerProfile().GetPlayerID();
			//if (container.CheckAccess(playerID) && PrivateArea.CheckAccess(((Component)container).transform.position, 0f, false, true))
			{
				//Log.LogInfo($"adding container {container.name}");
				Boxes.AddContainer(container);
			}
		}

		internal static void AddSmelter(Smelter smelter)
		{
			if (smelter == null || Smelters.Contains(smelter)) return;
			Smelters.Add(smelter);
			Jotunn.Logger.LogDebug($"Added smelter {smelter.name} ({smelter.GetInstanceID()}) to list");
		}

		internal static void RemoveSmelter(Smelter smelter)
		{
			if (Smelters.Remove(smelter) && smelter != null)
				Jotunn.Logger.LogDebug($"Removed smelter {smelter.name} ({smelter.GetInstanceID()}) from list");
		}

		internal static void UpdateSmelters()
		{
			foreach (var s in SmeltersToRemove) Smelters.Remove(s);
			SmeltersToRemove.Clear();
		}

		internal static void ConditionallyAddSmelter(Smelter smelter)
		{
			if (smelter == null) return;
			AddSmelter(smelter);
		}

		internal static List<Smelter> GetNearbySmelters(float radius)
		{
			var nearby = new List<Smelter>();
			if (!Player.m_localPlayer) return nearby;
			var playerPosition = Player.m_localPlayer.transform.position;
			foreach (var smelter in Smelters)
			{
				if (smelter == null || smelter.transform == null)
				{
					SmeltersToRemove.Add(smelter);
					continue;
				}
				var distance = Vector3.Distance(playerPosition, smelter.transform.position);
				if (distance < radius) nearby.Add(smelter);
			}
			UpdateSmelters();
			return nearby;
		}

		// The nearby containers the local player may actually take from or put into: the ones
		// vanilla would open.  Reads (/i, /search, highlighting) keep using GetNearbyContainers;
		// anything that changes a container's contents goes through this.
		internal static List<Container> GetOpenableContainers(float radius)
		{
			var nearby = GetNearbyContainers(radius);
			var openable = new List<Container>(nearby.Count);
			var skipped = new Dictionary<string, int>();
			foreach (var container in nearby)
			{
				if (ContainerAccess.CanOpen(container, out var reason))
				{
					openable.Add(container);
					continue;
				}
				skipped.TryGetValue(reason, out var n);
				skipped[reason] = n + 1;
			}
			foreach (var kv in skipped)
				Log.LogInfo($"skipping {kv.Value} {kv.Key} container(s) within {radius} m");
			return openable;
		}

		internal static List<Container> GetNearbyContainers(float radius)
		{
			List<Container> nearbyContainers = new List<Container>();
			if (!Player.m_localPlayer)
				return nearbyContainers;
			Vector3 playerPosition = Player.m_localPlayer.transform.position;
			//Log.LogInfo($"player position: {playerPosition}");
			Log.LogInfo($"checking distance of {Boxes.Containers.Count} containers");
			foreach (var container in Boxes.Containers)
			{
				if (container == null)
				{
					Log.LogWarning("Found null container in list");
					ContainersToRemove.Add(container);
					continue;
				}
				if (container.transform == null)
				{
					Log.LogWarning($"Found container {container.name} with null transform in list");
					ContainersToRemove.Add(container);
					continue;
				}
				var distance = Vector3.Distance(playerPosition, container.transform.position);
				//Log.LogInfo($"Checking distance of {container.name} {container.GetInstanceID()} {container.transform.position}: {distance} meters from player");
				if (distance < radius)
				{
					nearbyContainers.Add(container);
				}
			}
			UpdateContainers();
			return nearbyContainers;
		}

	}
}
