using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GrabMaterials
{
	// Gather (#47): look at a chest and press the key, and the item that chest mostly holds is
	// pulled into it from every other chest in range, so the same material stops living in five
	// places.  Sources are drained farthest-from-the-target first, so what is left behind, if
	// anything, is next to the target.  When the target fills, the overflow goes to the chest
	// nearest the target with room, then the next nearest, and so on; a chest used that way is
	// no longer drained.  Everything stays within Container Range of the player, the same reach
	// as /g, and only chests the player could open are touched, each owned through the open
	// handshake first (ContainerAccess).
	//
	// `/gather` does the same from the console; `/gather <item>` names the item instead of
	// reading it off the target.
	internal static class ContainerGather
	{
		private sealed class Slot
		{
			public Container Container;
			public Inventory Inventory;
			public float Distance;   // from the target
		}

		private static GrabMaterialsMod.GrabMaterialsMod Mod => GrabMaterialsMod.GrabMaterialsMod.Instance;
		private static float Range => GrabMaterialsMod.GrabMaterialsMod.Range;

		internal static void GatherIntoHovered(string requested = null)
		{
			var player = Player.m_localPlayer;
			if (player == null) return;

			var hover = player.GetHoverObject();
			var target = hover != null ? hover.GetComponentInParent<Container>() : null;
			if (target == null || target.GetInventory() == null)
			{
				player.Message(MessageHud.MessageType.Center, "Look at a chest to gather into");
				return;
			}
			if (!ContainerAccess.CanOpen(target, out var reason))
			{
				player.Message(MessageHud.MessageType.Center, $"Cannot gather into this chest ({reason})");
				return;
			}
			if (!Usable(target, out reason))
			{
				player.Message(MessageHud.MessageType.Center, $"Cannot gather into this chest ({reason})");
				return;
			}

			var material = string.IsNullOrEmpty(requested) ? DominantItem(target.GetInventory()) : ResolveName(requested, target);
			if (material == null)
			{
				player.Message(MessageHud.MessageType.Center, string.IsNullOrEmpty(requested)
					? "Put what you want gathered in the chest first"
					: $"Nothing called \"{requested}\" in the nearby chests");
				return;
			}

			// Every other openable chest in reach: the ones holding the material are sources, and
			// any of them may become an overflow destination, so all are owned up front.
			var others = Boxes.GetOpenableContainers(Range).Where(c => c != target && Usable(c, out _)).ToList();
			var all = new List<Container> { target };
			all.AddRange(others);
			ContainerAccess.Acquire(all, owned => Gather(target, material, owned));
		}

		// Chests whose contents are not ordinary storage are neither drained nor filled.
		private static bool Usable(Container container, out string why)
		{
			why = null;
			if (container.GetInventory() == null) { why = "no inventory"; return false; }
			if (container.GetComponent<TombStone>() != null) { why = "tombstone"; return false; }
			if (container.GetComponent("ArmoryRack") != null) { why = "Armory rack"; return false; }
			return true;
		}

		// The item taking the most slots in the chest; most items breaks a tie.
		private static string DominantItem(Inventory inventory)
		{
			string best = null;
			var bestSlots = 0;
			var bestCount = 0;
			var slots = new Dictionary<string, int>();
			var counts = new Dictionary<string, int>();
			foreach (var item in inventory.GetAllItems())
			{
				if (item?.m_shared == null) continue;
				var name = item.m_shared.m_name;
				slots.TryGetValue(name, out var s);
				counts.TryGetValue(name, out var n);
				slots[name] = s + 1;
				counts[name] = n + item.m_stack;
			}
			foreach (var kv in slots)
			{
				var n = counts[kv.Key];
				if (kv.Value > bestSlots || (kv.Value == bestSlots && n > bestCount))
				{
					best = kv.Key;
					bestSlots = kv.Value;
					bestCount = n;
				}
			}
			return best;
		}

		// `/gather <item>`: the shared name of the first matching item in the target or any chest in reach.
		private static string ResolveName(string requested, Container target)
		{
			foreach (var item in target.GetInventory().GetAllItems())
				if (item != null && item.isMatch(requested)) return item.m_shared.m_name;
			foreach (var container in Boxes.GetNearbyContainers(Range))
			{
				var inventory = container.GetInventory();
				if (inventory == null) continue;
				foreach (var item in inventory.GetAllItems())
					if (item != null && item.isMatch(requested)) return item.m_shared.m_name;
			}
			return null;
		}

		// The move proper, over containers this machine now owns.
		private static void Gather(Container target, string material, List<Container> owned)
		{
			var player = Player.m_localPlayer;
			if (player == null || target == null) return;
			if (!owned.Contains(target))
			{
				player.Message(MessageHud.MessageType.Center, "The chest would not hand itself over");
				return;
			}
			var origin = target.transform.position;
			var name = DisplayName(material, target.GetInventory());

			var sources = new List<Slot>();
			var spare = new List<Slot>();
			foreach (var container in owned)
			{
				if (container == null || container == target) continue;
				var inventory = container.GetInventory();
				if (inventory == null) continue;
				var slot = new Slot { Container = container, Inventory = inventory, Distance = Vector3.Distance(container.transform.position, origin) };
				if (inventory.GetAllItems().Any(item => item?.m_shared != null && item.m_shared.m_name == material)) sources.Add(slot);
				spare.Add(slot);
			}
			if (sources.Count == 0)
			{
				player.Message(MessageHud.MessageType.Center, $"No {name} in any other chest within {Range:0} m");
				return;
			}
			sources.Sort((a, b) => b.Distance.CompareTo(a.Distance));   // farthest first
			spare.Sort((a, b) => a.Distance.CompareTo(b.Distance));     // nearest first

			var destination = new Slot { Container = target, Inventory = target.GetInventory(), Distance = 0f };
			var destinations = new List<Slot> { destination };
			var drained = new HashSet<Container>();
			var moved = 0;
			var leftBehind = 0;
			var outOfRoom = false;

			foreach (var source in sources)
			{
				if (destinations.Contains(source)) continue;   // became an overflow chest; not drained
				var tookFromThis = 0;
				while (!outOfRoom)
				{
					var stack = source.Inventory.GetAllItems().FirstOrDefault(item => item?.m_shared != null && item.m_shared.m_name == material);
					if (stack == null) break;

					var amount = Math.Min(stack.m_stack, RoomFor(destination.Inventory, stack));
					if (amount <= 0)
					{
						// The destination is full for this item: the nearest chest to the target
						// with room takes over, and from then on is a destination, not a source.
						var next = spare.FirstOrDefault(s => !destinations.Contains(s) && RoomFor(s.Inventory, stack) > 0);
						if (next == null) { outOfRoom = true; break; }
						destinations.Add(next);
						destination = next;
						GrabMaterialsMod.GrabMaterialsMod.Log.LogInfo($"gather: {target.name} is full, continuing into {next.Container.name} {next.Distance:0.0} m away");
						continue;
					}

					var clone = stack.Clone();
					clone.m_stack = amount;
					if (!destination.Inventory.AddItem(clone))
					{
						GrabMaterialsMod.GrabMaterialsMod.Log.LogWarning($"gather: {destination.Container.name} refused {amount} {name} it had room for; stopping");
						outOfRoom = true;
						break;
					}
					source.Inventory.RemoveItem(stack, amount);
					moved += amount;
					tookFromThis += amount;
				}
				if (tookFromThis > 0) drained.Add(source.Container);
				if (outOfRoom) break;
			}

			if (outOfRoom)
			{
				foreach (var source in sources)
					if (!destinations.Contains(source))
						leftBehind += source.Inventory.CountItems(material);
			}

			foreach (var d in destinations) d.Container.Highlight();

			var into = destinations.Count == 1 ? "the chest" : $"{destinations.Count} chests";
			var from = drained.Count == 1 ? "1 chest" : $"{drained.Count} chests";
			string message;
			if (moved == 0) message = outOfRoom ? $"No room for {name}: the chest and every chest near it are full" : $"Nothing to gather: no {name} elsewhere";
			else if (leftBehind > 0) message = $"Gathered {moved} {name} from {from} into {into}; {leftBehind} left behind, no room nearby";
			else message = $"Gathered {moved} {name} from {from} into {into}";
			player.Message(MessageHud.MessageType.TopLeft, message);
			GrabMaterialsMod.GrabMaterialsMod.Log.LogInfo($"gather: {message} (target {target.name} at {origin})");
		}

		// How many of this stack the inventory could take: topping up its own partial stacks of
		// the same item, quality and variant, then empty slots.  Computed here rather than
		// trusting AddItem, which fills partial stacks before discovering it has no empty slot
		// for the rest.
		private static int RoomFor(Inventory inventory, ItemDrop.ItemData stack)
		{
			var max = stack.m_shared.m_maxStackSize;
			var room = inventory.GetEmptySlots() * max;
			if (max > 1)
			{
				foreach (var item in inventory.GetAllItems())
				{
					if (item?.m_shared == null || item.m_shared.m_name != stack.m_shared.m_name) continue;
					if (item.m_quality != stack.m_quality || item.m_variant != stack.m_variant) continue;
					room += Math.Max(0, max - item.m_stack);
				}
			}
			return room;
		}

		private static string DisplayName(string sharedName, Inventory inventory)
		{
			var localized = Extensions.Localize(sharedName);
			return string.IsNullOrEmpty(localized) ? sharedName : localized;
		}
	}
}
