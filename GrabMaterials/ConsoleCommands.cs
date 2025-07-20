using Jotunn.Extensions;
using Jotunn.Managers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using UnityEngine;
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
			GrabMaterialsForPack(grabPack.Name.Value, grabPack.Items.Value);
		}

		public static void GrabMaterialsForPack(string packName, string itemsString)
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
			GrabItemsFromNearbyContainers(itemsToGrab, 50f);
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
				foreach (var requirement in requirements)
				{
					Debug.Log($"Grabbing for {pieceName}: {requirement.m_amount} {requirement.m_resItem.m_itemData.m_shared.m_name}");
					GrabItemsFromNearbyContainers(requirement.m_resItem.m_itemData.m_shared.m_name, requirement.m_amount);
				}
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

		static void GrabItemsFromNearbyContainers(List<ItemToGrab> itemsToGrab, float radius)
		{
			var nearbyContainers = Boxes.GetNearbyContainers(radius);
			var player = Player.m_localPlayer;
			//Debug.Log($"grabbing items for {name} from {nearbyContainers.Count} containers within {radius} meters");
			var msg = new StringBuilder();
			for (int i = 0; i < itemsToGrab.Count; i++)
			{
				var itemToGrab = itemsToGrab[i];
				var totalItemsToGrab = itemToGrab.Count;
				Debug.Log($"grabbing {itemToGrab.Count} {itemToGrab.Name} from {nearbyContainers.Count} containers within {radius} meters");
				for (int j = 0; j < nearbyContainers.Count && itemToGrab.Count > 0; j++)
				{
					var container = nearbyContainers[j];
					int countGrabbed = container.GrabItemFromContainer(itemToGrab.Name, itemToGrab.Count);
					itemToGrab.Count -= countGrabbed;
				}
				msg.AppendLine($"Grabbed {totalItemsToGrab - itemToGrab.Count} of {totalItemsToGrab} {itemToGrab.Name}");
			}
			player.Message(MessageHud.MessageType.Center, msg.ToString());
		}

		public static void GrabMaterialsForSelectedPiece()
		{
			var player = Player.m_localPlayer;
			var piece = player.GetSelectedPiece();
			if (!piece)
			{
				var msg = "No build piece selected";
				Debug.Log(msg);
				player.Message(MessageHud.MessageType.Center, msg);
				return;
			}
			GrabMaterialsForPiece(piece);
		}

		private static void GrabMaterialsForPiece(Piece piece)
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
				GrabItemsFromNearbyContainers(itemsToGrab, 10f);
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
			GrabItemsFromNearbyContainers(itemsToGrab, radius);
		}
	}
}
