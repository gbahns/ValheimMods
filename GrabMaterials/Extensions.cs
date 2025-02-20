using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace GrabMaterials
{
	internal static class Extensions
	{
		public static string Name(this ItemDrop.ItemData self)
		{
			return self.m_shared.m_name.Substring(6);
		}

		public static int Count(this ItemDrop.ItemData self)
		{
			return self.m_stack;
		}

		public static void Highlight(this Container container)
		{
			WearNTear component = container.GetComponent<WearNTear>();

			if (component)
			{
				component.Highlight();
			}
		}

		public static int GrabItemFromContainer(this Container container, string name, int count)
		{
			Debug.Log($"looking for {count} {name} in {container} {container.GetInstanceID()}");
			var player = Player.m_localPlayer;
			var playerInventory = player.GetInventory();
			var containerInventory = container.GetInventory();
			int countGrabbed = 0;
			var items = containerInventory.GetAllItems().ToArray();
			for (int i = 0; i < items.Count() && count > 0; i++)
			{
				var item = items[i];
				Debug.Log($"{item.Name()} {item.Count()}");
				if (item.Name() == name)
				{
					int numberToGrab = count > item.Count() ? item.Count() : count;
					Debug.Log($"grabbing {numberToGrab} of {item.Count()} {name} from {container} {container.GetInstanceID()}");
					var newItem = item.Clone();
					newItem.m_stack = numberToGrab;
					containerInventory.RemoveItem(item, numberToGrab);
					playerInventory.AddItem(newItem);
					countGrabbed += numberToGrab;
					count -= numberToGrab;
					//Debug.Log($"grabbed {numberToGrab} {name} from {container} {container.GetInstanceID()}");
				}
			}
			Debug.Log($"grabbed a total of {countGrabbed} {name} from {container} {container.GetInstanceID()}");
			if (countGrabbed > 0)
			{
				container.Highlight();
			}
			return countGrabbed;
		}

		public static bool StoreItemInContainer(this Container container, ItemDrop.ItemData item)
		{
			var player = Player.m_localPlayer;
			var playerInventory = player.GetInventory();
			var containerInventory = container.GetInventory();
			if (!containerInventory.CanAddItem(item))
			{
				Debug.LogWarning("Container's inventory full.");
				return false;
			}
			Debug.Log($"moving {item.Name()} {item.Count()}");
			//playerInventory.RemoveItem(item);
			//containerInventory.AddItem(item);
			containerInventory.MoveItemToThis(playerInventory, item);
			Debug.Log($"moved {item.Name()} {item.Count()}");
			container.Highlight();
			return true;
		}

		public static int CountItems(this Inventory inventory)
		{
			var count = 0;
			var player = Player.m_localPlayer;
			var playerInventory = player.GetInventory();
			foreach (var item in playerInventory.GetAllItems())
			{
				count += item.Count();
			}
			return count;
		}

		public static int CountItems(this Inventory inventory, string name)
		{
			var count = 0;
			var player = Player.m_localPlayer;
			var playerInventory = player.GetInventory();
			foreach (var item in playerInventory.GetAllItems())
			{
				if (item.Name() == name)
					count += item.Count();
			}
			return count;
		}

	}
}
