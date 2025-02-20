using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace GrabMaterials
{
	internal static class ConsoleCommands
	{
		struct ItemToGrab
		{
			public string Name;
			public int Count;
			public ItemToGrab(string name, int count) { Name = name; Count = count; }
			public string FullName { get { return $"$item_{Name}"; } }
		}

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

		public static void GrabMaterialsForPack(string packName, string itemsString)
		{
			Debug.Log($"GrabMaterialsForPack({packName}, {itemsString})");
			var itemsToGrab = new List<ItemToGrab>();
			var entries = itemsString.Replace(" ", "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
			foreach (var entry in entries)
			{
				var parts = entry.Split(new[] { ':' }, StringSplitOptions.RemoveEmptyEntries);
				if (parts.Length != 2)
				{
					Debug.LogError($"Invalid format for {packName} item list: was {itemsString}, expected 'item:quantity,item:quantity,...'");
					return;
				}
				var item = parts[0];
				var amountString = parts[1];
				if (!int.TryParse(amountString, out var amount))
				{
					Debug.LogError($"Invalid format for {packName} item list: was {itemsString}, expected 'item:quantity,item:quantity,...'");
					return;
				}
				Debug.Log($"grabbing {amount} {item}");
				itemsToGrab.Add(new ItemToGrab(item, amount));
			}
			GrabItemsFromNearbyContainers(itemsToGrab, 10f);
		}

		/// <summary>
		/// /grab all
		/// /grab wood
		/// /grab wood 10
		/// /grab workbench
		/// /grab portal
		/// </summary>
		/// <param name="args"></param>
		public static void GrabItemsFromNearbyContainers(this Terminal.ConsoleEventArgs args)
		{
			Debug.Log($"GrabItemsFromNearbyContainers({args.FullLine})");

			if (args.Length <= 1)
			{
				var msg = "usage: /grab <all | name> [count], e.g. /grab wood 10";
				Chat.instance.SendMessage(msg);
				Debug.Log(msg);
				return;
			}

			var name = args[1];
			Debug.Log($"name of materials to grab: {name}");
			var count = 1;
			if (args.Length > 2)
				int.TryParse(args[2], out count);
			Debug.Log($"count to grab: {count}");

			GrabItemsFromNearbyContainers(name, count);
		}

		public static Piece.Requirement[] GetPieceRequirements(string pieceName)
		{
			if (!ZNetScene.instance)
			{
				Debug.LogWarning("Cannot look for prefab: ZNetScene.instance is null");
				return null;
			}
			var prefab = ZNetScene.instance.m_prefabs.Find(_prefab => _prefab.name == pieceName);
			//var requirements = 
			//prefab.gameObject.GetComponent<Piece>().m_resources.ToList().ForEach(requirement =>
			//{
			//	Debug.Log($"{requirement.m_amount} {requirement.m_resItem.m_itemData.m_shared.m_name}");
			//});
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
					Debug.Log($"{requirement.m_amount} {requirement.m_resItem.m_itemData.m_shared.m_name}");
				}
			}

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

		public static void GrabItemsFromNearbyContainers(string name, int count = 1)
		{
			var radius = 50f; // Default radius
			var itemsToGrab = new List<ItemToGrab>();

			switch (name)
			{
				case "workbench":
					itemsToGrab.Add(new ItemToGrab("wood", 10));
					break;

				case "portal":
					itemsToGrab.Add(new ItemToGrab("finewood", 20));
					itemsToGrab.Add(new ItemToGrab("greydwarfeye", 10));
					itemsToGrab.Add(new ItemToGrab("surtlingcore", 2));
					break;

				case "explore":
					itemsToGrab.Add(new ItemToGrab("wood", 10));
					itemsToGrab.Add(new ItemToGrab("finewood", 20));
					itemsToGrab.Add(new ItemToGrab("greydwarfeye", 10));
					itemsToGrab.Add(new ItemToGrab("surtlingcore", 2));
					break;

				case "karve":
					itemsToGrab.Add(new ItemToGrab("finewood", 30));
					itemsToGrab.Add(new ItemToGrab("deerhide", 10));
					itemsToGrab.Add(new ItemToGrab("resin", 20));
					itemsToGrab.Add(new ItemToGrab("bronzenails", 80));
					break;

				case "longship":
					itemsToGrab.Add(new ItemToGrab("finewood", 40));
					itemsToGrab.Add(new ItemToGrab("elderbark", 40));
					itemsToGrab.Add(new ItemToGrab("deerhide", 10));
					itemsToGrab.Add(new ItemToGrab("ironnails", 100));
					break;

				default:
					itemsToGrab.Add(new ItemToGrab(name, count));
					break;
			}

			GrabItemsFromNearbyContainers(itemsToGrab, radius);

		}


	}
}
