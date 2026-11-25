using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace GrabMaterials
{
	// Dropping a stack of eggs puts one pile on the ground, and a pile never hatches: EggGrow
	// only grows an item whose stack is exactly one, and its hover text says "stacked" to
	// explain the cold eggs.  So a stack dropped by the local player goes down as that many
	// single eggs instead, fanned out in front of the player so they do not land on one point.
	// Chicken eggs, asksvin eggs and any modded egg qualify, because the test is the EggGrow
	// component on the item's prefab, not a name.
	//
	// Every way of dropping from the inventory (drag outside the panel, the drop modifier)
	// funnels through Humanoid.DropItem, so one prefix covers them all.  The body repeats
	// vanilla's: take the eggs out of the inventory, spawn the drops, play the interact
	// animation and the drop effect, show one "Dropped" message with the whole count.
	//
	// Vanilla only merges items lying on the ground when more than 200 ItemDrops exist, and
	// then only onto a drop that has not stacked yet; each spawned egg is marked as already
	// stacked so that pass never pulls the eggs back into a pile.
	[HarmonyPatch(typeof(Humanoid), nameof(Humanoid.DropItem), typeof(Inventory), typeof(ItemDrop.ItemData), typeof(int))]
	internal static class EggDropPatch
	{
		private const float FanDegrees = 60f;     // the eggs spread over this arc in front of the player
		private const float ThrowSpeed = 5f;      // vanilla's speed for anything under 300 weight

		private static readonly Dictionary<string, bool> isEggByPrefab = new Dictionary<string, bool>();
		private static readonly FieldInfo zanimField = AccessTools.Field(typeof(Character), "m_zanim");
		private static readonly FieldInfo haveAutoStackedField = AccessTools.Field(typeof(ItemDrop), "m_haveAutoStacked");

		private static bool Prefix(Humanoid __instance, Inventory inventory, ItemDrop.ItemData item, int amount, ref bool __result)
		{
			var mod = GrabMaterialsMod.GrabMaterialsMod.Instance;
			if (mod == null || !(mod.DropEggsSingly?.Value ?? false)) return true;
			if (item == null || amount <= 1 || item.m_stack <= 1) return true;
			if (__instance == null || __instance != Player.m_localPlayer) return true;
			if (item.m_shared.m_questItem || !IsEgg(item)) return true;
			if (inventory == null) inventory = __instance.GetInventory();
			if (inventory == null) return true;

			amount = Math.Min(amount, item.m_stack);
			if (amount == item.m_stack)
			{
				if (!inventory.RemoveItem(item))
				{
					__result = false;
					return false;
				}
			}
			else
			{
				inventory.RemoveItem(item, amount);
			}

			var t = __instance.transform;
			for (int i = 0; i < amount; i++)
			{
				// Across the arc from left to right, each one a little faster or slower than the
				// last, so they land in a loose scatter rather than a row or a heap.
				var angle = Mathf.Lerp(-FanDegrees / 2f, FanDegrees / 2f, (i + 0.5f) / amount);
				var dir = Quaternion.AngleAxis(angle, Vector3.up) * t.forward;
				var speed = ThrowSpeed * (0.8f + 0.1f * (i % 5));
				var drop = ItemDrop.DropItem(item, 1, t.position + dir + t.up, t.rotation);
				if (drop == null) continue;
				drop.OnPlayerDrop();
				haveAutoStackedField?.SetValue(drop, true);
				var body = drop.GetComponent<Rigidbody>();
				if (body != null) body.linearVelocity = (dir + Vector3.up) * speed;
			}

			(zanimField?.GetValue(__instance) as ZSyncAnimation)?.SetTrigger("interact");
			__instance.m_dropEffects.Create(t.position, Quaternion.identity, null, 1f, -1, __instance.GetZDOID());
			__instance.Message(MessageHud.MessageType.TopLeft, "$msg_dropped " + item.m_shared.m_name, amount, item.GetIcon());
			__result = true;
			return false;
		}

		private static bool IsEgg(ItemDrop.ItemData item)
		{
			var prefab = item.m_dropPrefab;
			if (prefab == null) return false;
			if (!isEggByPrefab.TryGetValue(prefab.name, out var egg))
			{
				egg = prefab.GetComponent<EggGrow>() != null;
				isEggByPrefab[prefab.name] = egg;
			}
			return egg;
		}
	}
}
