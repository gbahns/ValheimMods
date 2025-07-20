using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;
using HarmonyLib;

namespace GrabMaterials
{
	internal class Boxes
	{
		internal static readonly List<Container> Containers = new List<Container>();
		private static readonly List<Container> ContainersToAdd = new List<Container>();
		private static readonly List<Container> ContainersToRemove = new List<Container>();
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
			// Debug.Log($"Container {trigger}: {container.name} {container.GetType()} {container.GetInstanceID()}");

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
				//Debug.Log($"adding container {container.name}");
				Boxes.AddContainer(container);
			}
		}

		internal static List<Container> GetNearbyContainers(float radius)
		{
			List<Container> nearbyContainers = new List<Container>();
			if (!Player.m_localPlayer)
				return nearbyContainers;
			Vector3 playerPosition = Player.m_localPlayer.transform.position;
			//Debug.Log($"player position: {playerPosition}");
			Debug.Log($"checking distance of {Boxes.Containers.Count} containers");
			foreach (var container in Boxes.Containers)
			{
				if (container == null)
				{
					Debug.LogWarning("Found null container in list");
					ContainersToRemove.Add(container);
					continue;
				}
				if (container.transform == null)
				{
					Debug.LogWarning($"Found container {container.name} with null transform in list");
					ContainersToRemove.Add(container);
					continue;
				}
				var distance = Vector3.Distance(playerPosition, container.transform.position);
				//Debug.Log($"Checking distance of {container.name} {container.GetInstanceID()} {container.transform.position}: {distance} meters from player");
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
