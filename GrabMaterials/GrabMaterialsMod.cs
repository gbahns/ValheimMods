using BepInEx;
using BepInEx.Configuration;
using GrabMaterials;
using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Managers;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GrabMaterialsMod
{

	[BepInPlugin(GrabMaterialsMod.ModGuid, "Automatically Grab Materials", "1.0.0")]
	[BepInProcess("valheim.exe")]
	public class GrabMaterialsMod : BaseUnityPlugin
	{
		const string ModGuid = "DeathMonger.GrabMaterialsMod";
		private readonly Harmony harmony = new Harmony(ModGuid);

		private ButtonConfig GrabPortalMatsButton;
		private ConfigEntry<KeyCode> GrabPortalMatsKeyboardConfig;
		private ConfigEntry<InputManager.GamepadButton> GrabPortalMatsGamepadConfig;

		private void Awake()
		{
			harmony.PatchAll();
			InitCommands();
			InitInputs();
		}

		private void InitInputs()
		{
			var configDescription = new ConfigDescription("Key to grab portal materials");
			GrabPortalMatsKeyboardConfig = Config.Bind("Client config", "GrabPortalMaterialsKey", KeyCode.G, configDescription);
			GrabPortalMatsGamepadConfig = Config.Bind("Client config", "GrabPortalMaterialsButton", InputManager.GamepadButton.ButtonSouth, configDescription);

			GrabPortalMatsButton = new ButtonConfig()
			{
				Name = "GrabPortalMaterials",
				Config = GrabPortalMatsKeyboardConfig,
				GamepadConfig = GrabPortalMatsGamepadConfig,
			};
			InputManager.Instance.AddButton(ModGuid, GrabPortalMatsButton);
		}

		private void Update()
		{
			if (Player.m_localPlayer && Chat.instance && !Chat.instance.IsChatDialogWindowVisible())
			{
				//IsBuildMenuOpen();
				//BuildMenu.instance.IsVisible();
				if (ZInput.GetButtonDown(GrabPortalMatsButton.Name))
				{
					GrabItemsFromNearbyContainers("explore");
				}
			}
		}

		void OnDestroy()
		{
			harmony.UnpatchSelf();
		}

		private static void InitCommands()
		{
			new Terminal.ConsoleCommand("list", "list all known containers", (args) => { ListKnownContainers(); });
			new Terminal.ConsoleCommand("listlocal", "[radius] - Finds containers within the radius.", (args) => { ListLocalContainers(args); });
			new Terminal.ConsoleCommand("listcontents", "[radius] - Finds containers within the radius and lists their contents.", (args) => { ListLocalContainerContents(args); });
			new Terminal.ConsoleCommand("search", "[search-text] - search for items matching this string in nearby containers", (args) => { FindContainersWithMatchingItems(args); });
			new Terminal.ConsoleCommand("grab", "[items] - grab items from nearby containers - 'help' to see supported options", (args) => { GrabItemsFromNearbyContainers(args); });
			new Terminal.ConsoleCommand("store", "[items] - grab items from nearby containers - 'help' to see supported options", (args) => { StoreItemsInNearbyContainers(); });
			new Terminal.ConsoleCommand("count", "[name of item to count] - omit to count everything", (args) => { CountInventory(args); });
		}

		private static void ListKnownContainers()
		{
			int i = 0;
			Debug.Log($"listing {Boxes.Containers.Count} known containers");
			foreach (var container in Boxes.Containers)
				Debug.Log($"{++i}. {container.name} {container.m_name}  ({container.GetType()} {container.GetInstanceID()})");
		}

		private static void ListLocalContainers(Terminal.ConsoleEventArgs args)
		{
			int i = 0;
			var radius = 10f; // Default radius
			if (args.Length > 1)
				float.TryParse(args[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out radius);
			var nearbyContainers = Boxes.GetNearbyContainers(radius);
			Debug.Log($"listing {nearbyContainers.Count} containers within {radius} meters out of {Boxes.Containers.Count} known containers");
			foreach (var container in nearbyContainers)
				Debug.Log($"{++i}. {container.name} {container.m_name}  ({container.GetType()} {container.GetInstanceID()})");
			Player.m_localPlayer.Message(MessageHud.MessageType.Center, $"Found {nearbyContainers.Count} containers within {radius} meters");
			//MessageHud.BiomeMessage("Found " + nearbyContainers.Count + " containers within " + radius + " meters", 0, null);
			MessageHud.instance.SendMessage("Found " + nearbyContainers.Count + " containers within " + radius + " meters");
		}

		private static void ListLocalContainerContents(Terminal.ConsoleEventArgs args)
		{
			var radius = 10f; // Default radius
			if (args.Length > 1)
				float.TryParse(args[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out radius);
			var nearbyContainers = Boxes.GetNearbyContainers(radius);
			Debug.Log($"listing {nearbyContainers.Count} containers within {radius} meters out of {Boxes.Containers.Count} known containers");
			Debug.Log($"showing the contents of {nearbyContainers.Count} nearby containers");
			foreach (var container in nearbyContainers)
			{
				Debug.Log($"contents of {container.name} {container.GetInstanceID()}:");
				var inventory = container.GetInventory();
				var items = inventory.GetAllItems();
				foreach (var item in items)
				{
					Debug.Log($"{item.Name()} {item.Count()}");
				}
			}
		}

		private static void FindContainersWithMatchingItems(Terminal.ConsoleEventArgs args)
		{
			if (args.Length <= 1)
			{
				var msg = "Please specify the text you want to search for in nearby containers";
				Chat.instance.SendMessage(msg);
				Debug.Log(msg);
				return;
			}

			var radius = 50f; // Default radius
			var text = args[1];

			var nearbyContainers = Boxes.GetNearbyContainers(radius);
			Debug.Log($"searching for {text} in {nearbyContainers.Count} containers within {radius} meters");
			foreach (var container in nearbyContainers)
			{
				var inventory = container.GetInventory();
				var items = inventory.GetAllItems();
				foreach (var item in items)
				{
					if (item.Name().Contains(text))
					{
						Debug.Log($"it contains {text}!");
						HighlightContainer(container);
					}
				}
				if (inventory.ContainsItemByName(text))
				{
					Debug.Log($"it contains {text}!");
					HighlightContainer(container);
				}
			}
		}

		struct ItemToGrab
		{
			public string Name;
			public int Count;
			public ItemToGrab(string name, int count) { Name = name; Count = count; }
			public string FullName { get { return $"$item_{Name}"; } }
		}

		/// <summary>
		/// /grab all
		/// /grab wood
		/// /grab wood 10
		/// /grab workbench
		/// /grab portal
		/// </summary>
		/// <param name="args"></param>
		static void GrabItemsFromNearbyContainers(Terminal.ConsoleEventArgs args)
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

		static void GrabItemsFromNearbyContainers(string name, int count = 1)
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

			var nearbyContainers = Boxes.GetNearbyContainers(radius);
			//Debug.Log($"grabbing items for {name} from {nearbyContainers.Count} containers within {radius} meters");
			for (int i = 0; i < itemsToGrab.Count; i++)
			{
				var itemToGrab = itemsToGrab[i];
				Debug.Log($"grabbing {itemToGrab.Count} {itemToGrab.Name} from {nearbyContainers.Count} containers within {radius} meters");
				for (int j = 0; j < nearbyContainers.Count && itemToGrab.Count > 0; j++)
				{
					var container = nearbyContainers[j];
					int countGrabbed = GrabItemFromContainer(container, itemToGrab.Name, itemToGrab.Count);
					itemToGrab.Count -= countGrabbed;
				}
			}
		}

		public static int GrabItemFromContainer(Container container, string name, int count)
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
			return countGrabbed;
		}

		static void StoreItemsInNearbyContainers()
		{
			var radius = 50f; // Default radius
			var nearbyContainers = Boxes.GetNearbyContainers(radius);
			var player = Player.m_localPlayer;
			var playerInventory = player.GetInventory();
			var itemsToMove = new List<ItemDrop.ItemData>();

			for (int y = 1; y < playerInventory.GetHeight(); y++)
			{
				for (int x = 0; x < playerInventory.GetWidth(); x++)
				{
					var item = playerInventory.GetItemAt(x, y);
					if (item != null)
					{
						itemsToMove.Add(item);
					}
				}
			}

			Debug.Log($"storing {itemsToMove.Count()} items in {nearbyContainers.Count} containers within {radius} meters");

			int currentContainer = 0;
			while (itemsToMove.Count > 0)
			{
				StoreItemInContainer(nearbyContainers[currentContainer], itemsToMove[0]);
				currentContainer++;
				if (currentContainer == nearbyContainers.Count)
					currentContainer = 0;
				itemsToMove.RemoveAt(0);
			}
		}

		static bool StoreItemInContainer(Container container, ItemDrop.ItemData item)
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
			return true;
		}

		static void CountInventory(Terminal.ConsoleEventArgs args)
		{
			var itemName = "";
			var count = 0;
			var player = Player.m_localPlayer;
			var playerInventory = player.GetInventory();
			if (args.Length > 1)
			{
				itemName = args[1];
				count = CountItemsInInventory(playerInventory, itemName);
				Debug.Log($"{count} {itemName} in inventory");
			}
			else
			{
				count = CountItemsInInventory(playerInventory);
				Debug.Log($"{count} items in inventory");
			}
		}

		static int CountItemsInInventory(Inventory inventory)
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

		static int CountItemsInInventory(Inventory inventory, string name)
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

		static void HighlightContainer(Container container)
		{
			WearNTear component = container.GetComponent<WearNTear>();

			if (component)
			{
				component.Highlight();
			}
		}

		//[HarmonyPatch(typeof(CraftingStation), "CheckResources")]
		//static bool GrabMaterials(CraftingStation __instance, ref bool __result)
		//{
		//    // Find nearby chests
		//    Chest[] nearbyChests = GameObject.FindObjectsOfType<Chest>();

		//    foreach (Recipe recipe in __instance.m_recipes)
		//    {
		//        // Check if player has enough materials
		//        foreach (Piece.Requirement requirement in recipe.m_resources)
		//        {
		//            int requiredAmount = requirement.m_amount;

		//            // Search chests for required materials
		//            foreach (Chest chest in nearbyChests)
		//            {
		//                // Logic to transfer materials from chest to crafting station
		//                // Implement inventory management and material transfer
		//            }
		//        }
		//    }

		//    return true;
		//}
	}
}