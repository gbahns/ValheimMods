using BepInEx;
using BepInEx.Configuration;
using GrabMaterials;
using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Managers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using UnityEngine;

namespace GrabMaterialsMod
{

	[BepInPlugin(GrabMaterialsMod.ModGuid, "Automatically Grab Materials", "1.0.0")]
	[BepInProcess("valheim.exe")]
	public class GrabMaterialsMod : BaseUnityPlugin
	{
		const string ModGuid = "DeathMonger.GrabMaterialsMod";
		private readonly Harmony harmony = new Harmony(ModGuid);

		//private ButtonConfig GrabPortalMatsButton;
		private ButtonConfig GrabSelectedPieceMatsButton;
		//private ConfigEntry<KeyCode> GrabPortalMatsKeyboardConfig;
		//private ConfigEntry<InputManager.GamepadButton> GrabPortalMatsGamepadConfig;
		private ConfigEntry<KeyCode> GrabSelectedPieceMatsKeyboardConfig;

		public class GrabPackConfig
		{
			//public string Name;
			//public KeyCode Key;
			//public string Items;
			//public bool GrabDelta;
			public ConfigEntry<string> Name;
			public ConfigEntry<KeyboardShortcut> Key;
			public ConfigEntry<string> Items;
			public ConfigEntry<bool> GrabDelta;
			public ButtonConfig Button;

			public GrabPackConfig(ConfigFile config, string section, KeyboardShortcut keyboardShortcut, string items)
			{
				Name = config.Bind(section, section+" Name", section, new ConfigDescription("Name of the grab pack"));
				Key = config.Bind(section, section+" Key", keyboardShortcut, new ConfigDescription("Key to grab materials for the grab pack"));
				Items = config.Bind(section, section+" Items", items, new ConfigDescription("Items to grab for the grab pack"));
				GrabDelta = config.Bind(section, section+" Grab Delta", false, new ConfigDescription("NOT YET SUPPORTED (Grab the delta of items needed for the grab pack)"));
				Button = new ButtonConfig()
				{
					Name = Name.Value,
					ShortcutConfig = Key
				};
			}
		}

		GrabPackConfig GrabPack1;
		GrabPackConfig GrabPack2;
		GrabPackConfig GrabPack3;

		private void Awake()
		{
			harmony.PatchAll();
			InitConfig();
			InitCommands();
			InitButtons();
		}

		private void InitConfig()
		{
			var configDescription = new ConfigDescription("Key to grab portal materials");
			//GrabPortalMatsKeyboardConfig = Config.Bind("Client config", "GrabPortalMaterialsKey", KeyCode.I, configDescription);
			//GrabPortalMatsGamepadConfig = Config.Bind("Client config", "GrabPortalMaterialsButton", InputManager.GamepadButton.ButtonSouth, configDescription);
			//new KeyboardShortcut(KeyCode.G, KeyCode.LeftShift, KeyCode.RightShift)
			GrabSelectedPieceMatsKeyboardConfig = Config.Bind("Grab Selected Piece", "GrabSelectedPieceMatsKey", KeyCode.J, new ConfigDescription("Key to grab materials for the currently selectede build piece"));

			GrabPack1 = new GrabPackConfig(Config, "Grab Pack 1", new KeyboardShortcut(KeyCode.G), "wood:10,finewood:20,greydwarfeye:10,surtlingcore:2");
			GrabPack2 = new GrabPackConfig(Config, "Grab Pack 2", new KeyboardShortcut(KeyCode.G, KeyCode.LeftShift), "wood:10,finewood:40,ancientbark:40,ironnails:100,deeerhide:20");
			GrabPack3 = new GrabPackConfig(Config, "Grab Pack 3", new KeyboardShortcut(KeyCode.G, KeyCode.LeftAlt), "wood:12,stone:5");
		}

		private static void InitCommands()
		{
			//grab materials from nearby containers
			new Terminal.ConsoleCommand("grab", "[items] - grab items from nearby containers - 'help' to see supported options", (args) => { args.GrabItemsFromNearbyContainers(); });
			new Terminal.ConsoleCommand("grabselected", "", (args) => { ConsoleCommands.GrabMaterialsForSelectedPiece(); });
			new Terminal.ConsoleCommand("grabpiece", "grab materials for named build piece, e.g. workbench or portal", (args) => { args.GrabMaterialsForPiece(); });

			//view container info
			new Terminal.ConsoleCommand("listcontainers", "list all known containers", (args) => { ListKnownContainers(); });
			new Terminal.ConsoleCommand("listlocalcontainers", "[radius] - Finds containers within the radius.", (args) => { ListLocalContainers(args); });
			new Terminal.ConsoleCommand("listcontents", "[radius] - Finds containers within the radius and lists their contents.", (args) => { ListLocalContainerContents(args); });

			//for testing/learning
			new Terminal.ConsoleCommand("search", "[search-text] - search for items matching this string in nearby containers", (args) => { FindContainersWithMatchingItems(args); });
			new Terminal.ConsoleCommand("store", "[items] - grab items from nearby containers - 'help' to see supported options", (args) => { StoreItemsInNearbyContainers(); });
			new Terminal.ConsoleCommand("count", "[name of item to count] - omit to count everything", (args) => { CountInventory(args); });
			new Terminal.ConsoleCommand("listpieces", "", (args) => { ListAllPieces(); });
		}

		private void InitButtons()
		{
			//GrabPortalMatsButton = new ButtonConfig()
			//{
			//	Name = "GrabPortalMaterials",
			//	Config = GrabPortalMatsKeyboardConfig,
			//	GamepadConfig = GrabPortalMatsGamepadConfig,
			//};
			//InputManager.Instance.AddButton(ModGuid, GrabPortalMatsButton);

			GrabSelectedPieceMatsButton = new ButtonConfig()
			{
				Name = "GrabSelectedPieceMaterials",
				Config = GrabSelectedPieceMatsKeyboardConfig,
			};
			InputManager.Instance.AddButton(ModGuid, GrabSelectedPieceMatsButton);

			InputManager.Instance.AddButton(ModGuid, GrabPack1.Button);
			InputManager.Instance.AddButton(ModGuid, GrabPack2.Button);
			InputManager.Instance.AddButton(ModGuid, GrabPack3.Button);
		}

		private void InitButton(string name, KeyCode key)
		{
			var button = new ButtonConfig()
			{
				Name = name,
				Config = Config.Bind("", name, key),
			};
			InputManager.Instance.AddButton(ModGuid, button);
		}

		private void Update()
		{
			if (Player.m_localPlayer && Chat.instance && !Chat.instance.IsChatDialogWindowVisible())
			{
				//if (ZInput.GetButtonDown(GrabPortalMatsButton.Name))
				//{
				//	ConsoleCommands.GrabItemsFromNearbyContainers("explore");
				//}

				if (ZInput.GetButtonDown(GrabSelectedPieceMatsButton.Name))
				{
					//var recipe = ItemManager.Instance.GetRecipe("wood");
					ConsoleCommands.GrabMaterialsForSelectedPiece();
				}

				if (ZInput.GetButtonDown(GrabPack1.Button.Name))
				{
					ConsoleCommands.GrabMaterialsForPack(GrabPack1.Name.Value, GrabPack1.Items.Value);
				}

				if (ZInput.GetButtonDown(GrabPack2.Button.Name))
				{
					ConsoleCommands.GrabMaterialsForPack(GrabPack2.Name.Value, GrabPack2.Items.Value);
				}

				if (ZInput.GetButtonDown(GrabPack3.Button.Name))
				{
					ConsoleCommands.GrabMaterialsForPack(GrabPack3.Name.Value, GrabPack3.Items.Value);
				}
			}
		}



		void OnDestroy()
		{
			harmony.UnpatchSelf();
		}

		private static void ListAllPieces()
		{

			if (!ZNetScene.instance)
			{
				Debug.LogWarning("Cannot index: ZNetScene.instance is null");
				return;
			}

			Debug.LogWarning("listing prefabs");
			foreach (var prefab in ZNetScene.instance.m_prefabs)
			{
				Debug.Log($"Prefab: {prefab.name}");
			}

			//Jotunn.Managers.PieceManager.Instance.GetPiece().Pieces.ForEach(piece =>
			//{
			//	Debug.Log($"{piece.name}");
			//});

			////this just list the build pieces available on the currently selected workbench category
			//var pieces = player.GetBuildPieces();
			//if (pieces == null)
			//{
			//	Debug.Log("No build pieces found");
			//	return;
			//}
			//Debug.Log($"listing {pieces.Count()} pieces");
			//foreach (var piece in pieces)
			//{
			//	Debug.Log($"{piece.name}");
			//}

			////this seems to only give the players base recipes without even a hammer
			//var recipes = new List<Recipe>();
			//player.GetAvailableRecipes(ref recipes);
			//Debug.Log($"listing {recipes.Count()} recipes");
			//foreach (var recipe in recipes)
			//{
			//	Debug.Log($"{recipe}");
			//}

			//var objectDB = ObjectDB.instance;
			//if (objectDB == null)
			//{
			//	Debug.LogError("ObjectDB instance is null");
			//	return;
			//}

			//foreach (var prefab in objectDB.m_items)
			//{
			//	Debug.Log($"Prefab: {prefab.name}");
			//}

			//Debug.LogWarning("listing named prefabs");
			//foreach (var prefab in ZNetScene.instance.m_namedPrefabs.Values)
			//{
			//	Debug.Log($"Named Prefab: {prefab.name}");
			//}

			//PieceTable pieceTable = GetPieceTable();
			//Jotunn.Utils.ModRegistry.GetPieces().ForEach(piece =>
			//{
			//	Debug.Log($"{piece.name}");
			//});
		}

		//private static List<GameObject> GetPrefabs()
		//{
		//	HashSet<GameObject> prefabs = new HashSet<GameObject>(ZNetScene.instance.m_prefabs);
		//	HashSet<GameObject> namedPrefabs = new HashSet<GameObject>(ZNetScene.instance.m_namedPrefabs.Values);

		//	List<GameObject> combinedPrefabs = prefabs.Union(namedPrefabs).ToList();
		//	combinedPrefabs.RemoveAll(prefab => !prefab);

		//	return combinedPrefabs;
		//}

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
						container.Highlight();
					}
				}
				if (inventory.ContainsItemByName(text))
				{
					Debug.Log($"it contains {text}!");
					container.Highlight();
				}
			}
		}

		static void StoreItemsInNearbyContainers()
		{
			var radius = 50f; // Default radius
			var nearbyContainers = Boxes.GetNearbyContainers(radius);
			var player = Player.m_localPlayer;
			var playerInventory = player.GetInventory();
			var itemsToMove = new List<ItemDrop.ItemData>();

			//this one works
			player.Message(MessageHud.MessageType.Center, $"Storing items in {nearbyContainers.Count} containers within {radius} meters");

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
				nearbyContainers[currentContainer].StoreItemInContainer(itemsToMove[0]);
				currentContainer++;
				if (currentContainer == nearbyContainers.Count)
					currentContainer = 0;
				itemsToMove.RemoveAt(0);
			}
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
				count = playerInventory.CountItems(itemName);
				Debug.Log($"{count} {itemName} in inventory");
			}
			else
			{
				count = playerInventory.CountItems();
				Debug.Log($"{count} items in inventory");
			}
		}
	}
}