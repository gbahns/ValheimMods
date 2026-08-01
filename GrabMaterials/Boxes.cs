using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;
using HarmonyLib;
using static GrabMaterialsMod.GrabMaterialsMod;

namespace GrabMaterials
{
	internal class Boxes
	{
		internal static readonly List<Container> Containers = new List<Container>();
		private static readonly List<Container> ContainersToAdd = new List<Container>();
		private static readonly List<Container> ContainersToRemove = new List<Container>();

		internal static readonly List<Smelter> Smelters = new List<Smelter>();
		private static readonly List<Smelter> SmeltersToAdd = new List<Smelter>();
		private static readonly List<Smelter> SmeltersToRemove = new List<Smelter>();
		//private static ConcurrentDictionary<float, Stopwatch> stopwatches = new ConcurrentDictionary<float, Stopwatch>();

		internal static void AddContainer(Container container)
		{
			if (!Containers.Contains(container))
			{
				ContainersToAdd.Add(container);
				Jotunn.Logger.LogDebug($"Added container {container.name} ({container.GetType()} {container.GetInstanceID()}) to list");
			}
			UpdateContainers();
		}
		internal static void RemoveContainer(Container container)
		{
			if (Containers.Contains(container))
			{
				ContainersToRemove.Add(container);
				Jotunn.Logger.LogDebug($"Removed container {container.name} ({container.GetType()} {container.GetInstanceID()}) from list");
			}
			UpdateContainers();
		}

		internal static void UpdateContainers()
		{
			foreach (Container item in ContainersToAdd)
			{
				Containers.Add(item);
			}
			ContainersToAdd.Clear();
			foreach (Container item2 in ContainersToRemove)
			{
				Containers.Remove(item2);
			}
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
			if (!Smelters.Contains(smelter))
			{
				SmeltersToAdd.Add(smelter);
				Jotunn.Logger.LogDebug($"Added smelter {smelter.name} ({smelter.GetType()} {smelter.GetInstanceID()}) to list");
			}
			UpdateSmelters();
		}

		internal static void RemoveSmelter(Smelter smelter)
		{
			if (Smelters.Contains(smelter))
			{
				SmeltersToRemove.Add(smelter);
				Jotunn.Logger.LogDebug($"Removed smelter {smelter.name} ({smelter.GetType()} {smelter.GetInstanceID()}) from list");
			}
			UpdateSmelters();
		}

		internal static void UpdateSmelters()
		{
			foreach (var s in SmeltersToAdd) Smelters.Add(s);
			SmeltersToAdd.Clear();
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
