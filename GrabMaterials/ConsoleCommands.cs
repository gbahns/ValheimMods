using HarmonyLib;
using Jotunn.Extensions;
using Jotunn.Managers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using UnityEngine;
using UnityEngine.UI;
using static MeleeWeaponTrail;

namespace GrabMaterials
{
	internal static class ConsoleCommands
	{
		struct ItemToGrab
		{
			public string Name;
			public int Count;
			public ItemToGrab(string name, int count) { Name = name.Replace("$item_",""); Count = count; }
			public string FullName { get { return $"$item_{Name}"; } }
		}

		private static Dictionary<string, Piece> pieceLookup = new Dictionary<string, Piece>();
		private static Dictionary<string, GameObject> itemLookup = new Dictionary<string, GameObject>();

		public static void GrabMaterialsForPiece(this Terminal.ConsoleEventArgs args)
		{
			Debug.Log($"GrabMaterialsForPiece({args.FullLine})");

			if (args.Length <= 1)
			{
				var msg = "usage: /grabpiece  <name>, e.g. /grab workbench";
				Chat.instance.SendMessage(msg);
				Debug.Log(msg);
				return;
			}

			var name = args[1];
			Debug.Log($"name of piece to grab materials for: {name}");

			//GrabMaterialsForPiece($"$item_{name}");
			GrabMaterialsForPiece(name);
		}

		public static void GrabMaterialsForPack(GrabMaterialsMod.GrabMaterialsMod.GrabPackConfig grabPack)
		{
			GrabMaterialsForPack(grabPack.Name.Value, grabPack.Items.Value, grabPack.GrabDelta.Value);
		}

		public static void GrabMaterialsForPack(string packName, string itemsString, bool grabDelta = false)
		{
			Debug.Log($"GrabMaterialsForPack({packName}, {itemsString})");
			var itemsToGrab = new List<ItemToGrab>();
			var entries = itemsString.Replace(" ", "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
			foreach (var entry in entries)
			{
				var parts = entry.Split(new[] { ':' }, StringSplitOptions.RemoveEmptyEntries);
				if (parts.Length > 2)
				{
					Debug.LogError($"Invalid format for {packName} item list: was {itemsString}, expected 'item[:quantity],item[:quantity],...'");
					return;
				}
				var item = parts[0];
				var amount = 1;
				if (parts.Length == 2)
				{
					if (!int.TryParse(parts[1], out amount))
					{
						Debug.LogError($"Invalid format for {packName} item list: was {itemsString}, expected 'item[:quantity],item[:quantity],...'");
						return;
					}
				}
				Debug.Log($"grabbing {amount} {item}");
				//itemsToGrab.Add(new ItemToGrab(item, amount));
				foreach (var itemToGrab in GetItemsToGrab(item, amount))
				{
					itemsToGrab.Add(itemToGrab);
				}
				//GrabItemsFromNearbyContainers(item, amount);
			}
			GrabItemsFromNearbyContainers(itemsToGrab, 50f, packName, grabDelta);
		}

		/// <summary>
		/// /grab all
		/// /grab wood
		/// /grab wood 10
		/// /graab 10 wood
		/// /grab workbench
		/// /grab portal
		/// /grab pack <pack number>
		/// /grab <pack name>
		/// </summary>
		/// <param name="args"></param>
		public static void GrabItemsFromNearbyContainers(this Terminal.ConsoleEventArgs args)
		{
			Debug.Log($"GrabItemsFromNearbyContainers('{args.FullLine})' args.Length={args.Length}");

			if (args.Length > 1)
			{
				if (args[1] == "pack")
				{
					int packNumber = int.Parse(args[2]) - 1;
					GrabMaterialsForPack(GrabMaterialsMod.GrabMaterialsMod.Instance.GrabPacks[packNumber]);
					return;
				}

				foreach (var pack in GrabMaterialsMod.GrabMaterialsMod.Instance.GrabPacks)
				{
					var packName = args.ArgsAll;
					Debug.Log($"Checking pack {pack.Name.Value} against args.ArgsAll='{packName}'");
					if (pack.Name.Value == args.ArgsAll)
					{
						GrabMaterialsForPack(pack);
						return;
					}
				}

				var n = args.Length - 1; // Index of the last argument

				var count = 0;
				var nameStartingArg = int.TryParse(args[1], out count) ? 2 : 1;
				var nameEndingArg = n;

				// if the first argument is a number, it is the count of items to grab
				// if not, check to see if the count is in the last argument
				// if so, adjust the ending argument index to exclude it from the name
				// if the count wasn't specified, default to 1
				if (count == 0)
				{
					nameEndingArg = int.TryParse(args[n], out count) ? n - 1 : n;
					if (count == 0) count = 1;
				}
				Debug.Log($"count={count}, n={n}, nameStartingArg={nameStartingArg}, nameEndingArg={nameEndingArg}");

				var sb = new StringBuilder();
				for (int i = nameStartingArg; i <= nameEndingArg; i++)
				{
					if (sb.Length > 0) sb.Append(" "); // Add a space between words
					sb.Append(args[i]);
				}
				var name = sb.ToString();

				if (name != "")
				{
					Debug.Log($"grabbing {count} '{name}'");
					GrabItemsFromNearbyContainers(name, count);
					return;
				}
			}

			var msg = "usage: /grab <all | name> [count], e.g. /grab 10 wood";
			Chat.instance.SendMessage(msg);
			Debug.Log(msg);
		}

		public static Piece.Requirement[] GetPieceRequirements(string pieceName)
		{
			Debug.Log($"GetPieceRequirements({pieceName})");
			if (!ZNetScene.instance)
			{
				Debug.LogWarning("Cannot look for prefab: ZNetScene.instance is null");
				return null;
			}
			Debug.Log("looking for prefab...");
			Debug.Log($"ZNetScene.instance has {ZNetScene.instance.m_prefabs.Count} prefabs");
			var prefab = ZNetScene.instance.m_prefabs.Find(_prefab => _prefab.name == pieceName);
			if (prefab == null)
			{
				Debug.LogError($"No prefab found for {pieceName}");
				return null;
			}
			//var requirements = 
			//prefab.gameObject.GetComponent<Piece>().m_resources.ToList().ForEach(requirement =>
			//{
			//	Debug.Log($"{requirement.m_amount} {requirement.m_resItem.m_itemData.m_shared.m_name}");
			//});
			Debug.Log($"Prefab found: {prefab.name} {prefab.gameObject.name} {prefab.gameObject.tag} {prefab.gameObject.GetComponent<Piece>().m_name}");
			return prefab.gameObject.GetComponent<Piece>().m_resources;


			//var prefabManager = PrefabManager.Instance;
			//var prefab = prefabManager.GetPrefab(pieceName);
			//if (prefab == null)
			//{
			//	Debug.LogError($"No prefab found for {pieceName}");
			//	return null;
			//}

			//var piece = prefab.GetComponent<Piece>();
			//if (piece == null)
			//{
			//	Debug.LogError($"No Piece component found on prefab for {pieceName}");
			//	return null;
			//}

			//return piece.m_resources;
		}

		private static void GrabMaterialsForPiece(string pieceName)
		{

			var requirements = GetPieceRequirements(pieceName);
			if (requirements != null)
			{
				var itemsToGrab = new List<ItemToGrab>();
				foreach (var requirement in requirements)
				{
					Debug.Log($"Grabbing for {pieceName}: {requirement.m_amount} {requirement.m_resItem.m_itemData.m_shared.m_name}");
					itemsToGrab.Add(new ItemToGrab(requirement.m_resItem.m_itemData.m_shared.m_name, requirement.m_amount));
				}
				GrabItemsFromNearbyContainers(itemsToGrab, 50f, pieceName);
			}

			/*
			piece_workbench
			piece_workbench_ext1
			piece_workbench_ext2
			piece_workbench_ext3
			piece_workbench_ext4

			forge
			forge_ext1
			forge_ext2
			forge_ext3
			forge_ext4
			forge_ext5
			forge_ext6
			*/

			//var piece = Jotunn.Managers.PieceManager.Instance.GetPiece(pieceName);
			//piece.

			//var player = Player.m_localPlayer;
			//if (player == null)
			//{
			//	Debug.Log("No local player found");
			//	return;
			//}
			//foreach (var recipe in player.m_knownRecipes)
			//{
			//	Debug.Log($"{recipe}");
			//}


			//var prefabManager = PrefabManager.Instance;
			////prefabManager.Ge
			//var prefab = prefabManager.GetPrefab(pieceName);
			//if (prefab == null)
			//{
			//	var msg = $"No prefab found for {pieceName}";
			//	Debug.Log(msg);
			//	pieceName = $"$item_{pieceName}";
			//	prefab = prefabManager.GetPrefab(pieceName);
			//	if (prefab == null)
			//	{
			//		msg = $"No recipe found for {pieceName}";
			//		Debug.Log(msg);
			//		return;
			//	}
			//}
			////var recipe = ItemManager.Instance.GetRecipe(pieceName);

			//var resources = prefab.GetComponent<Piece>().m_resources;
			//resources.get


			//var recipe = ItemManager.Instance.GetRecipe(pieceName);
			//if (recipe == null)
			//{
			//	var msg = $"No recipe found for {pieceName}";
			//	Debug.Log(msg);
			//	pieceName = $"$item_{pieceName}";
			//	recipe = ItemManager.Instance.GetRecipe(pieceName);
			//	if (recipe == null)
			//	{
			//		msg = $"No recipe found for {pieceName}";
			//		Debug.Log(msg);
			//		return;
			//	}
			//}
			//var resources = recipe.Recipe.m_resources;
			//if (resources == null)
			//{
			//	var msg = $"No resources found for {pieceName}";
			//	Debug.Log(msg);
			//	Chat.instance.SendMessage(msg);
			//	return;
			//}
			//foreach (var requirement in resources)
			//{
			//	Debug.Log($"{requirement.m_amount} {requirement.m_resItem.m_itemData.Name()}");
			//}

			//var player = Player.m_localPlayer;
			//Piece piece = null;
			//= player.GetPiece(pieceName);
			//Piece.s_allPieces.Find(Piece)
			//if (!piece)
			//{
			//	var msg = $"No build piece named {pieceName} selected";
			//	Debug.Log(msg);
			//	player.Message(MessageHud.MessageType.Center, msg);
			//	return;
			//}
			//GrabMaterialsForPiece(piece);
		}

		static void GrabItemsFromNearbyContainers(List<ItemToGrab> itemsToGrab, float radius, string requestLabel = null, bool grabDelta = false)
		{
			var nearbyContainers = Boxes.GetNearbyContainers(radius);
			var player = Player.m_localPlayer;

			// Aggregate duplicate requests so a pack with several pieces sharing
			// a material (e.g. wood) is checked against the combined total.
			var aggregated = new List<ItemToGrab>();
			foreach (var item in itemsToGrab)
			{
				var idx = aggregated.FindIndex(a => string.Equals(a.Name, item.Name, StringComparison.OrdinalIgnoreCase));
				if (idx >= 0)
				{
					var existing = aggregated[idx];
					existing.Count += item.Count;
					aggregated[idx] = existing;
				}
				else
				{
					aggregated.Add(item);
				}
			}

			// GrabDelta: figure out what's already in the player's inventory so we
			// can subtract it from the per-container request (but still report the
			// original Needed in the panel).
			var had = new int[aggregated.Count];
			var effectiveNeed = new int[aggregated.Count];
			for (int i = 0; i < aggregated.Count; i++)
			{
				if (grabDelta && player != null)
				{
					var playerInv = player.GetInventory();
					if (playerInv != null)
					{
						foreach (var owned in playerInv.GetAllItems())
						{
							if (owned.isMatch(aggregated[i].Name)) had[i] += owned.Count();
						}
					}
				}
				effectiveNeed[i] = Math.Max(0, aggregated[i].Count - had[i]);
			}

			// Whether the player already has everything (drives the success-panel title).
			var allCovered = true;
			for (int i = 0; i < aggregated.Count; i++)
			{
				if (effectiveNeed[i] > 0) { allCovered = false; break; }
			}

			// Pre-flight: total available across nearby containers per requested item.
			var available = new int[aggregated.Count];
			foreach (var container in nearbyContainers)
			{
				var inventory = container.GetInventory();
				if (inventory == null) continue;
				foreach (var item in inventory.GetAllItems())
				{
					for (int i = 0; i < aggregated.Count; i++)
					{
						if (effectiveNeed[i] == 0) continue;
						if (item.isMatch(aggregated[i].Name))
						{
							available[i] += item.Count();
							break;
						}
					}
				}
			}

			// Abort if any item's container availability is short of the (delta-adjusted) need.
			var anyShort = false;
			for (int i = 0; i < aggregated.Count; i++)
			{
				if (effectiveNeed[i] > 0 && available[i] < effectiveNeed[i]) { anyShort = true; break; }
			}
			if (anyShort)
			{
				var statuses = new List<MaterialsPanel.ItemStatus>(aggregated.Count);
				var debugShortages = new List<string>();
				for (int i = 0; i < aggregated.Count; i++)
				{
					statuses.Add(new MaterialsPanel.ItemStatus
					{
						Name = LocalizeItemName(aggregated[i]),
						Needed = aggregated[i].Count,
						Had = had[i],
						Available = available[i],
					});
					if (effectiveNeed[i] > available[i])
					{
						debugShortages.Add($"{effectiveNeed[i] - available[i]} of {aggregated[i].Count} {aggregated[i].Name}");
					}
				}
				Debug.Log($"Cannot grab{(string.IsNullOrEmpty(requestLabel) ? "" : $" for {requestLabel}")} - missing: {string.Join(", ", debugShortages)}");
				var failTitle = string.IsNullOrEmpty(requestLabel) ? "Missing materials" : $"Missing materials for {requestLabel}";
				MaterialsPanel.Show(failTitle, statuses);
				return;
			}

			// Everything available — perform the grab.
			var grabbed = new List<MaterialsPanel.ItemStatus>(aggregated.Count);
			for (int i = 0; i < aggregated.Count; i++)
			{
				var itemToGrab = aggregated[i];
				var remaining = effectiveNeed[i];
				if (remaining > 0)
				{
					Debug.Log($"grabbing {remaining} {itemToGrab.Name} from {nearbyContainers.Count} containers within {radius} meters");
					for (int j = 0; j < nearbyContainers.Count && remaining > 0; j++)
					{
						int countGrabbed = nearbyContainers[j].GrabItemFromContainer(itemToGrab.Name, remaining);
						remaining -= countGrabbed;
					}
				}
				grabbed.Add(new MaterialsPanel.ItemStatus
				{
					Name = LocalizeItemName(itemToGrab),
					Needed = itemToGrab.Count,
					Had = had[i],
					Available = effectiveNeed[i] - remaining,
				});
			}
			string successTitle;
			if (allCovered)
			{
				successTitle = string.IsNullOrEmpty(requestLabel) ? "Already have everything" : $"Already have everything for {requestLabel}";
				Debug.Log(successTitle);
			}
			else
			{
				successTitle = string.IsNullOrEmpty(requestLabel) ? "Grabbed materials" : $"Grabbed materials for {requestLabel}";
			}
			MaterialsPanel.Show(successTitle, grabbed);
		}

		private static string LocalizeItemName(ItemToGrab item)
		{
			var translated = LocalizationManager.Instance.TryTranslate(item.FullName);
			return string.IsNullOrEmpty(translated) || translated == item.FullName ? item.Name : translated;
		}

		private static string LocalizePieceName(Piece piece)
		{
			if (piece == null) return null;
			if (!piece.m_name.StartsWith("$")) return piece.m_name;
			try
			{
				var translated = LocalizationManager.Instance.TryTranslate(piece.m_name);
				return string.IsNullOrEmpty(translated) || translated == piece.m_name ? piece.m_name : translated;
			}
			catch
			{
				return piece.m_name;
			}
		}

		public static void GrabMaterialsForSelectedPiece()
		{
			var player = Player.m_localPlayer;
			if (player == null) return;
			var piece = GetHoveredBuildPiece() ?? player.GetSelectedPiece();
			if (!piece)
			{
				var msg = "No build piece selected or hovered";
				Debug.Log(msg);
				player.Message(MessageHud.MessageType.Center, msg);
				return;
			}
			GrabMaterialsForPiece(piece);
		}

		// The publicized Valheim assembly fixes compile-time access, but the runtime
		// DLL still has m_pieceIcons as private; AccessTools bypasses that.
		private static AccessTools.FieldRef<Hud, List<Hud.PieceIconData>> _pieceIconsAccessor;

		// Highlight every nearby container that holds at least one item whose
		// m_shared.m_name matches. Powers the click-to-highlight behavior on
		// inventory-panel rows (#40).
		public static void HighlightContainersHolding(string sharedName)
		{
			if (string.IsNullOrEmpty(sharedName)) return;
			var nearbyContainers = Boxes.GetNearbyContainers(50f);
			foreach (var container in nearbyContainers)
			{
				var inventory = container.GetInventory();
				if (inventory == null) continue;
				foreach (var item in inventory.GetAllItems())
				{
					if (item.m_shared.m_name == sharedName)
					{
						container.Highlight();
						break;
					}
				}
			}
		}

		// Same as above but for a set of names — used by category headers in
		// the inventory panel (clicking the header highlights every container
		// holding any item in the category).
		public static void HighlightContainersHoldingAny(ICollection<string> sharedNames)
		{
			if (sharedNames == null || sharedNames.Count == 0) return;
			var nameSet = sharedNames as HashSet<string> ?? new HashSet<string>(sharedNames);
			var nearbyContainers = Boxes.GetNearbyContainers(50f);
			foreach (var container in nearbyContainers)
			{
				var inventory = container.GetInventory();
				if (inventory == null) continue;
				foreach (var item in inventory.GetAllItems())
				{
					if (nameSet.Contains(item.m_shared.m_name))
					{
						container.Highlight();
						break;
					}
				}
			}
		}

		public static Piece GetHoveredBuildPiece()
		{
			var hud = Hud.instance;
			var player = Player.m_localPlayer;
			if (hud == null || player == null) return null;

			if (_pieceIconsAccessor == null)
			{
				try
				{
					_pieceIconsAccessor = AccessTools.FieldRefAccess<Hud, List<Hud.PieceIconData>>("m_pieceIcons");
				}
				catch (Exception e)
				{
					Debug.LogWarning($"GrabMaterials: cannot reflect Hud.m_pieceIcons - {e.Message}");
					return null;
				}
			}
			var pieceIcons = _pieceIconsAccessor(hud);
			if (pieceIcons == null) return null;

			foreach (var iconData in pieceIcons)
			{
				if (iconData == null) continue;
				var icon = iconData.m_icon;
				if (icon == null || !icon.gameObject.activeInHierarchy) continue;
				if (!RectTransformUtility.RectangleContainsScreenPoint(icon.rectTransform, Input.mousePosition)) continue;
				// PieceIconData doesn't expose the Piece directly, so match the
				// icon's sprite against the player's available build pieces.
				var sprite = icon.sprite;
				if (sprite == null) return null;
				var pieces = player.GetBuildPieces();
				if (pieces == null) return null;
				foreach (var piece in pieces)
				{
					if (piece != null && piece.m_icon == sprite) return piece;
				}
				return null;
			}
			return null;
		}

		public static void GrabMaterialsForPiece(Piece piece)
		{
			var resources = piece.m_resources;
			Debug.Log($"grabbing materials for selected piece {piece.name} - requires {resources.Count()} resources");
			if (resources != null)
			{
				List<ItemToGrab> itemsToGrab = new List<ItemToGrab>();
				foreach (var requirement in resources)
				{
					Debug.Log($"{requirement.m_amount} {requirement.m_resItem.m_itemData.Name()}");
					itemsToGrab.Add(new ItemToGrab(requirement.m_resItem.m_itemData.Name(), requirement.m_amount));
					//GrabItemsFromNearbyContainers(requirement.m_resItem.m_itemData.m_shared.m_name, requirement.m_amount);
				}
				GrabItemsFromNearbyContainers(itemsToGrab, 10f, LocalizePieceName(piece));
			}
		}

		private static void BuildPieceLookUp()
		{
			// ObjectDB contains all prefabs, including pieces.
			if (ObjectDB.instance == null) return;

			if (!ZNetScene.instance)
			{
				Debug.LogWarning("Cannot index: ZNetScene.instance is null");
				return;
			}

			Debug.Log("building lookup table for all build pieces");
			foreach (var prefab in ZNetScene.instance.m_prefabs)
			{
				if (prefab.TryGetComponent<Piece>(out var piece))
				{
					if (prefab.name.ContainsAny("loot_chest", "TreasureChest"))
					{
						//Debug.Log($"Not adding {prefab.name} to build piece lookup table");
						continue;
					}

					// Get the localized, user-facing name (e.g., "Campfire")
					var localizedName = "";
					try
					{
						if (piece.m_name.StartsWith("$"))
						{ // if the name starts with $, it is a localization key
							localizedName = LocalizationManager.Instance.TryTranslate(piece.m_name);
						}
						else
						{
							localizedName = piece.m_name;
						}
					}
					catch (Exception e)
					{
						Debug.LogError($"Error translating piece name {piece.m_name}: {e.Message}");
						localizedName = piece.m_name;
					}
					//Debug.Log($"{prefab.name} \"{localizedName}\" {GetPieceResourceList(piece)}");
					pieceLookup[localizedName.ToLowerInvariant()] = piece;
					//Debug.Log($"Piece: {piece.name} ({prefab.name})");
				}
				else
				{
					//Debug.Log($"Prefab {prefab.name} does not have a Piece component");
				}
			}
		}

		// In your GrabMaterialsPlugin class
		private static void BuildItemLookUp()
		{
			if (ObjectDB.instance == null) return;

			Jotunn.Logger.LogInfo("Building item lookup...");

			foreach (GameObject itemPrefab in ObjectDB.instance.m_items)
			{
				if (itemPrefab == null) continue;

				// The internal prefab name (e.g., "BoarMeat") is the most reliable key.
				string prefabName = itemPrefab.name;
				itemLookup[prefabName] = itemPrefab;
				//Debug.Log($"added item to list: {prefabName}");
			}

			Jotunn.Logger.LogInfo($"Built a lookup table with {itemLookup.Count} items.");
		}

		private static List<ItemToGrab> GetItemsToGrab (string name, int count = 1)
		{
			var itemsToGrab = new List<ItemToGrab>();
			if (pieceLookup.Count == 0)
				BuildPieceLookUp();
			if (itemLookup.Count == 0)
				BuildItemLookUp();
			if (pieceLookup.ContainsKey(name.ToLowerInvariant()))
			{
				var piece = pieceLookup[name.ToLowerInvariant()];
				Debug.Log($"Found piece {name} in lookup table, grabbing materials for it");
				var resources = piece.m_resources;
				if (resources != null)
				{
					foreach (var requirement in resources)
					{
						Debug.Log($"{requirement.m_amount} {requirement.m_resItem.m_itemData.Name()}");
						itemsToGrab.Add(new ItemToGrab(requirement.m_resItem.m_itemData.Name(), requirement.m_amount * count));
					}
				}
			}
			else
			{
				Debug.Log($"No piece found for {name}, looking for material by this name instead");
				itemsToGrab.Add(new ItemToGrab(name, count));
			}
			return itemsToGrab;
		}

		public static void GrabItemsFromNearbyContainers(string name, int count = 1)
		{
			var radius = 50f; // Default radius
			var itemsToGrab = GetItemsToGrab(name, count);
			// GetItemsToGrab populates pieceLookup on first use; check after.
			var label = pieceLookup.ContainsKey(name.ToLowerInvariant()) ? name : null;
			GrabItemsFromNearbyContainers(itemsToGrab, radius, label);
		}
	}
}
