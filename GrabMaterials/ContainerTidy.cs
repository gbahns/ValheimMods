using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace GrabMaterials
{
	// Tidies a container when you open it: partial stacks of the same item are merged, then
	// the items are laid out in a chosen order (#80).  This is what SimpleSort did before it
	// stopped working with 0.217.
	//
	// Vanilla hands the opener ZDO ownership as part of the open handshake, and only the owner's
	// Container.Save runs, so a rearrangement made right after the chest opens is the one case
	// where this mod can write to a container without its own Acquire step.  The ownership
	// change travels separately from the open reply, so the tidy waits for it, briefly.
	public static class ContainerTidy
	{
		public enum Order
		{
			Category,   // the /i panel's categories, then name
			Name,
			Weight,
			Value,
		}

		private const float OwnershipWait = 1f;

		// The container whose UI is open, for /sort; null when none is.
		internal static Container Open { get; private set; }

		private static GrabMaterialsMod.GrabMaterialsMod Mod => GrabMaterialsMod.GrabMaterialsMod.Instance;
		private static bool  Enabled   => Mod?.TidyOnOpen?.Value ?? true;
		private static Order SortOrder => Mod?.TidySortOrder?.Value ?? Order.Category;
		private static string Skipped  => Mod?.TidySkipContainers?.Value ?? "";

		internal static void OnOpened(Container container)
		{
			Open = container;
			if (!Enabled || container == null) return;
			if (!ShouldTidy(container, out var why))
			{
				GrabMaterialsMod.GrabMaterialsMod.Log.LogInfo($"not tidying {container.name}: {why}");
				return;
			}
			Mod?.StartCoroutine(TidyWhenOwned(container));
		}

		internal static void OnClosed()
		{
			Open = null;
		}

		// `/sort`: tidy the container that is open right now, whatever the automatic setting says.
		internal static void SortOpenContainer()
		{
			var player = Player.m_localPlayer;
			var container = Open;
			if (container == null || !InventoryGui.IsVisible())
			{
				player?.Message(MessageHud.MessageType.Center, "Open a container first");
				return;
			}
			if (!ShouldTidy(container, out var why))
			{
				player?.Message(MessageHud.MessageType.Center, $"Not sorting this container ({why})");
				return;
			}
			var nview = ContainerAccess.NetView(container);
			if (nview == null || !nview.IsValid() || !nview.IsOwner())
			{
				player?.Message(MessageHud.MessageType.Center, "This container is not yours to change right now");
				return;
			}
			var changed = Tidy(container.GetInventory());
			player?.Message(MessageHud.MessageType.TopLeft, changed ? "Sorted" : "Already tidy");
		}

		// Containers whose slot positions mean something are left alone.
		private static bool ShouldTidy(Container container, out string why)
		{
			why = null;
			if (container.GetInventory() == null) { why = "no inventory"; return false; }
			if (container.GetType() != typeof(Container)) { why = "a mod's own container type"; return false; }
			if (container.GetComponent<TombStone>() != null) { why = "tombstone"; return false; }
			if (container.GetComponent("ArmoryRack") != null) { why = "Armory rack"; return false; }

			var prefab = container.name.Replace("(Clone)", "").Trim();
			foreach (var entry in Skipped.Split(','))
			{
				var name = entry.Trim();
				if (name.Length > 0 && string.Equals(name, prefab, StringComparison.OrdinalIgnoreCase)) { why = "listed in Skip Containers"; return false; }
			}
			return true;
		}

		private static IEnumerator TidyWhenOwned(Container container)
		{
			var deadline = Time.time + OwnershipWait;
			while (Time.time < deadline)
			{
				if (container == null || Open != container) yield break;
				var nview = ContainerAccess.NetView(container);
				if (nview == null || !nview.IsValid()) yield break;
				if (nview.IsOwner())
				{
					Tidy(container.GetInventory());
					yield break;
				}
				yield return null;
			}
			GrabMaterialsMod.GrabMaterialsMod.Log.LogInfo($"not tidying {container?.name}: ownership did not arrive");
		}

		// Merge, sort, lay out.  Returns whether anything moved.
		internal static bool Tidy(Inventory inventory)
		{
			if (inventory == null) return false;
			var items = new List<ItemDrop.ItemData>(inventory.GetAllItems());
			if (items.Count == 0) return false;

			var changed = MergeStacks(inventory, items);

			var ordered = Sorted(items);
			var width = inventory.GetWidth();
			for (var i = 0; i < ordered.Count; i++)
			{
				var pos = new Vector2i(i % width, i / width);
				if (ordered[i].m_gridPos != pos)
				{
					ordered[i].m_gridPos = pos;
					changed = true;
				}
			}

			if (changed) inventory.Changed();
			return changed;
		}

		// Pour partial stacks of the same item together, biggest first, up to the stack limit.
		// Only true stacks: same item, quality and variant, no per-item custom data.
		private static bool MergeStacks(Inventory inventory, List<ItemDrop.ItemData> items)
		{
			var changed = false;
			var groups = items
				.Where(item => item.m_shared.m_maxStackSize > 1 && (item.m_customData == null || item.m_customData.Count == 0))
				.GroupBy(item => (item.m_shared.m_name, item.m_quality, item.m_variant));
			var emptied = new List<ItemDrop.ItemData>();
			foreach (var group in groups)
			{
				var stacks = group.OrderByDescending(item => item.m_stack).ToList();
				if (stacks.Count < 2) continue;
				var max = stacks[0].m_shared.m_maxStackSize;
				var into = 0;
				for (var from = stacks.Count - 1; from > into; from--)
				{
					while (stacks[from].m_stack > 0 && into < from)
					{
						var room = max - stacks[into].m_stack;
						if (room <= 0) { into++; continue; }
						var moved = Math.Min(room, stacks[from].m_stack);
						stacks[into].m_stack += moved;
						stacks[from].m_stack -= moved;
						changed = true;
					}
					if (stacks[from].m_stack == 0) emptied.Add(stacks[from]);
				}
			}
			foreach (var item in emptied)
			{
				inventory.RemoveItem(item);
				items.Remove(item);
			}
			return changed;
		}

		private static List<ItemDrop.ItemData> Sorted(List<ItemDrop.ItemData> items)
		{
			IOrderedEnumerable<ItemDrop.ItemData> ordered;
			switch (SortOrder)
			{
				case Order.Name:
					ordered = items.OrderBy(DisplayName, StringComparer.CurrentCultureIgnoreCase);
					break;
				case Order.Weight:
					ordered = items.OrderBy(item => item.m_shared.m_weight).ThenBy(DisplayName, StringComparer.CurrentCultureIgnoreCase);
					break;
				case Order.Value:
					ordered = items.OrderBy(item => item.m_shared.m_value).ThenBy(DisplayName, StringComparer.CurrentCultureIgnoreCase);
					break;
				default:
					ordered = items.OrderBy(item => (int)item.GetCategory()).ThenBy(DisplayName, StringComparer.CurrentCultureIgnoreCase);
					break;
			}
			// Same item: fuller stacks first, then better quality first, so what you reach for is on the left.
			return ordered.ThenByDescending(item => item.m_stack).ThenByDescending(item => item.m_quality).ToList();
		}

		private static string DisplayName(ItemDrop.ItemData item)
		{
			var name = Extensions.Localize(item.m_shared.m_name);
			return string.IsNullOrEmpty(name) ? item.m_shared.m_name : name;
		}
	}

	// Vanilla opens the chest UI from here, once the owner has granted the open.
	[HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Show))]
	internal static class InventoryGui_Show_Tidy
	{
		private static void Postfix(Container container)
		{
			if (container != null) ContainerTidy.OnOpened(container);
		}
	}

	[HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Hide))]
	internal static class InventoryGui_Hide_Tidy
	{
		private static void Postfix()
		{
			ContainerTidy.OnClosed();
		}
	}
}
