using Jotunn.Managers;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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

		public static string LocalizedName(this ItemDrop.ItemData self)
		{
			return LocalizationManager.Instance.TryTranslate(self.m_shared.m_name);
		}

		public static bool isMatch(this ItemDrop.ItemData self, string matchString)
		{
			return self.Name().isMatch(matchString) || self.m_shared.m_name.isMatch(matchString) || self.LocalizedName().isMatch(matchString);
		}

		public static bool isMatch(this string self, string s)
		{
			return string.Equals(self, s, StringComparison.CurrentCultureIgnoreCase);
		}

		public static int Count(this ItemDrop.ItemData self)
		{
			return self.m_stack;
		}

		public enum ItemCategory
		{
			None,
			Weapon,
			Armor,
			Tool,
			Shield,
			Arrow,
			Bolt,
			Trophy,
			Utility,
			Mead,
			BalancedFood,
			HealthFood,
			StaminaFood,
			EitrFood,
			RawMeat,
			CookedMeat,
			Summoning,
			Metal,
			Wood,
			Stone,
			Treasure,
			Skin,
			Plant,
			FoodIngredient,
			FishingBait,
			Seed,
			Weed,
			Material,
			Misc,
		}

		// Pre-defined lists of prefab names for name-based checks.
		// Using HashSet for fast lookups (O(1) average time complexity).
		private static readonly HashSet<string> BossSummoningNames = new HashSet<string> { "trophydeer", "ancientseed", "witheredbone", "dragonegg", "goblintotem", "seekerbrood", "trophyfader", "bellfragment", "sealbreaker", "sealbreakerfragment" };
		private static readonly HashSet<string> RawMeatNames = new HashSet<string> { "boar_meat", "wolfmeat", "loxmeat", "deermeat", "serpentmeat", "fishraw", "haremeat", "seekermeat", "chickenmeat", "asksvintail", "bonemawmeat", "chickenegg", "entrails" };
		private static readonly HashSet<string> CookedMeatNames = new HashSet<string> { "cooked_boar_meat", "cookedwolfmeat", "cookedloxmeat", "cookeddeermeat", "serpentmeatcooked", "cookedfish", "cookedharemeat", "cookedseekermeat", "cookedchickenmeat", "cookedbonemawmeat" };
		private static readonly HashSet<string> FoodIngredientNames = new HashSet<string> { "barleyflour", "bloodclot", "royaljelly" };
		private static readonly HashSet<string> FishingBaitNames = new HashSet<string> { "fishingbait", "fishingbaitashlands", "fishingbaitcave", "fishingbaitdeepnorth", "fishingbaitforest", "fishingbaitmistlands", "fishingbaitocean", "fishingbaitplains", "fishingbaitswamp" };
		private static readonly HashSet<string> AmmoNames = new HashSet<string> { "payload_grausten", "payload_explosive" };
		private static readonly HashSet<string> SkinNames = new HashSet<string> { "leatherscraps", "deerhide", "trollhide", "wolfpelt", "loxpelt", "feathers", "chitin", "scalehide", "harepelt", "carapace", "askbladder", "askhide", "bonemawtooth", "celestialfeather", "fenrisclaw", "fenrishair", "mandible", "queendrop", "morgenheart", "morgensinew", "needle", "root", "ooze", "wolf_fang" };
		private static readonly HashSet<string> TreasureNames = new HashSet<string> { "coins", "ruby", "amber", "amberpearl", "silvernecklace" };
		private static readonly HashSet<string> WoodNames = new HashSet<string> { "wood", "finewood", "roundlog", "yggdrasilwood", "elderbark", "blackwood" };
		private static readonly HashSet<string> StoneNames = new HashSet<string> { "stone", "flint", "obsidian", "marble", "blackmarble", "coal", "crystal", "grausten" };
		private static readonly HashSet<string> MetalNames = new HashSet<string> { "iron", "copper", "tin", "silver", "bronze", "blackmetal", "flametal", "ironore", "copperore", "tinore", "silverore", "ironscrap", "blackmetalscrap", "copperscrap", "frometal", "frometalore", "bronzenails", "ironnails", "hildir_ironpit" };
		private static readonly HashSet<string> SeedNames = new HashSet<string> { "carrotseeds", "turnipseeds", "onionseeds", "fircone", "pinecone", "beechseeds", "birchseeds" };
		private static readonly HashSet<string> WeedNames = new HashSet<string> { "dandelion", "thistle", "vine", "guck" };
		private static readonly HashSet<string> PlantNames = new HashSet<string> { "mushroom", "carrot", "turnip", "onion", "raspberry", "blueberries", "cloudberry", "jotunpuffs", "magecap", "sap", "barley", "flax", "fiddlehead", "smokepuff" };
		private static readonly HashSet<ItemDrop.ItemData.ItemType> ArmorTypes = new HashSet<ItemDrop.ItemData.ItemType> { ItemDrop.ItemData.ItemType.Helmet, ItemDrop.ItemData.ItemType.Chest, ItemDrop.ItemData.ItemType.Legs, ItemDrop.ItemData.ItemType.Shoulder };

		//skins: feathers, scales, fur, hide, leather, etal, cloth, silk, Lox Pelt 
		//treasures: coins, gems, runestones, trophies, artifacts, relics

		/// <summary>
		/// Determines the most specific category for a given Valheim item.
		/// The order of checks is important to ensure specificity.
		/// </summary>
		/// <param name="self">The ItemData of the item to categorize.</param>
		/// <returns>A string representing the item's category.</returns>
		public static ItemCategory GetCategory(this ItemDrop.ItemData self)
		{
			var shared = self.m_shared;
			string prefabName = self.m_dropPrefab.name.ToLowerInvariant();

			// 1. Check by ItemType enum for clear-cut categories.
			// These are checked first because they are unambiguous.
			if (shared.m_itemType == ItemDrop.ItemData.ItemType.Trophy) return ItemCategory.Trophy;
			if (shared.m_itemType == ItemDrop.ItemData.ItemType.Tool) return ItemCategory.Tool;
			if (shared.m_itemType == ItemDrop.ItemData.ItemType.Ammo && shared.m_ammoType == "$ammo_arrows") return ItemCategory.Arrow;
			if (shared.m_itemType == ItemDrop.ItemData.ItemType.Ammo && shared.m_ammoType == "$ammo_bolts") return ItemCategory.Bolt;
			if (shared.m_itemType == ItemDrop.ItemData.ItemType.Ammo && shared.m_ammoType == "$item_fishingbait") return ItemCategory.FishingBait;
			if (shared.m_itemType == ItemDrop.ItemData.ItemType.Shield) return ItemCategory.Shield;
			if (shared.m_itemType == ItemDrop.ItemData.ItemType.Utility) return ItemCategory.Utility;
			if (shared.m_isDrink) return ItemCategory.Mead;
			if (self.IsWeapon()) return ItemCategory.Weapon;
			if (ArmorTypes.Contains(shared.m_itemType)) return ItemCategory.Armor;
			//{
			//	// If it's a weapon, we check if it has a specific type.
			//	if (shared.m_itemType == ItemDrop.ItemData.ItemType.OneHandedWeapon) return "One-Handed Weapon";
			//	if (shared.m_itemType == ItemDrop.ItemData.ItemType.TwoHandedWeapon) return "Two-Handed Weapon";
			//	if (shared.m_itemType == ItemDrop.ItemData.ItemType.Shield) return "Shield";
			//}

			if (BossSummoningNames.Contains(prefabName)) return ItemCategory.Summoning;
			if (MetalNames.Contains(prefabName)) return ItemCategory.Metal;
			if (WoodNames.Contains(prefabName)) return ItemCategory.Wood;
			if (StoneNames.Contains(prefabName)) return ItemCategory.Stone;
			if (TreasureNames.Contains(prefabName)) return ItemCategory.Treasure;
			if (SkinNames.Contains(prefabName)) return ItemCategory.Skin;
			if (PlantNames.Contains(prefabName) || prefabName.Contains("Seed")) return ItemCategory.Plant;
			if (FoodIngredientNames.Contains(prefabName)) return ItemCategory.FoodIngredient;
			if (FishingBaitNames.Contains(prefabName)) return ItemCategory.FishingBait;
			if (SeedNames.Contains(prefabName)) return ItemCategory.Seed;
			if (WeedNames.Contains(prefabName)) return ItemCategory.Weed;
			if (AmmoNames.Contains(prefabName)) return ItemCategory.Arrow; // If these are arrows, otherwise create a new enum value

			// Check for food types. This is complex and must be ordered correctly.
			if (shared.m_food > 0)
			{
				// Check for specific meat types first, as they are also food.
				if (RawMeatNames.Contains(prefabName)) return ItemCategory.RawMeat;
				if (CookedMeatNames.Contains(prefabName)) return ItemCategory.CookedMeat;

				// Check for Eitr food first as it's a primary distinguisher.
				if (shared.m_foodEitr > 0) return ItemCategory.EitrFood;

				// Check if it's primarily a health or stamina food.
				if (shared.m_food > shared.m_foodStamina) return ItemCategory.HealthFood;
				if (shared.m_foodStamina > shared.m_food) return ItemCategory.StaminaFood;

				// If it's balanced or has no specific lean, it's generic "Food".
				return ItemCategory.BalancedFood;
			}

			// If no other category matches, return a generic fallback.
			if (shared.m_itemType == ItemDrop.ItemData.ItemType.Material) return ItemCategory.Material;

			return ItemCategory.Misc;
		}

		public static void Highlight(this Container container)
		{
			WearNTear component = container.GetComponent<WearNTear>();

			if (component)
			{
				//component.Highlight();
				component.StartCoroutine(HighlightRoutine(component));
			}
		}

		//private static FieldInfo _highlightField;

		private static IEnumerator HighlightRoutine(WearNTear wearTear)
		{
			//Debug.Log("HighlightRoutine");
			//Debug.Log($"Highlighting {wearTear.name} {wearTear.GetInstanceID()}");
			//if (_highlightField == null)
			//{
			//	// Use reflection to get the private field "m_highlight" from WearNTear
			//	_highlightField = typeof(WearNTear).GetField("m_highlight", BindingFlags.NonPublic | BindingFlags.Instance);
			//	Debug.Log($"Found field: {_highlightField}");
			//	Debug.Log($"Found field: {_highlightField.Name}");
			//}

			//Debug.Log($"Highlighting {wearTear.name} {wearTear.GetInstanceID()} using field {_highlightField.Name}");

			//// Get the actual highlight GameObject using reflection
			//GameObject highlightObject = (GameObject)_highlightField.GetValue(wearTear);

			//Debug.Log($"Highlight object: {highlightObject}");

			//if (highlightObject == null)
			//{
			//	Debug.LogWarning("WearNTear highlight object is null.");
			//	yield break;
			//}
			//highlightObject.SetActive(true);
			//yield return new WaitForSeconds(2.5f);
			//highlightObject.SetActive(false);

			var duration = GrabMaterialsMod.GrabMaterialsMod.Instance.HighlightDuration.Value;
			var iterations = Mathf.CeilToInt(duration / 0.1f);

			for (var i = 0; i < iterations; i++)
			{
				wearTear.Highlight();
				yield return new WaitForSeconds(0.1f);
			}
		}

		public static int GrabItemFromContainer(this Container container, string name, int count)
		{
			//Debug.Log($"looking for {count} {name} in {container} {container.GetInstanceID()}");
			var player = Player.m_localPlayer;
			var playerInventory = player.GetInventory();
			var containerInventory = container.GetInventory();
			int countGrabbed = 0;
			var items = containerInventory.GetAllItems().ToArray();
			for (int i = 0; i < items.Count() && count > 0; i++)
			{
				var item = items[i];
				//Debug.Log($"{item.Name()} {item.LocalizedName()} {item.Count()}");
				if (item.isMatch(name))
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
			//Debug.Log($"grabbed a total of {countGrabbed} {name} from {container} {container.GetInstanceID()}");
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

		public static string GetResourceList(this Piece piece)
		{
			var resources = piece.m_resources;
			var sb = new StringBuilder();
			if (resources != null)
			{
				foreach (var requirement in resources)
				{
					//Debug.Log($"{requirement.m_amount} {requirement.m_resItem.m_itemData.Name()}");
					sb.Append($"{requirement.m_resItem.m_itemData.m_shared.m_name.Replace("$item_", "")}:{requirement.m_amount},");
				}
			}
			return sb.ToString();
		}
	}
}
