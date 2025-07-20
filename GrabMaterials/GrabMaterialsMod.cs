using BepInEx;
using BepInEx.Configuration;
using GrabMaterials;
using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Managers;
using Jotunn.Utils;
using Jotunn.Entities;
using Jotunn;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using UnityEngine;

namespace GrabMaterialsMod
{

	[BepInPlugin(GrabMaterialsMod.ModGuid, "Grab Materials", "1.0.0")]
	[BepInProcess("valheim.exe")]
	public class GrabMaterialsMod : BaseUnityPlugin
	{
		const string ModGuid = "DeathMonger.GrabMaterialsMod";
		private readonly Harmony harmony = new Harmony(ModGuid);
		public static GrabMaterialsMod Instance;

		//private ButtonConfig GrabPortalMatsButton;
		public ConfigEntry<float> HighlightDuration;
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

			public GrabPackConfig(ConfigFile config, string section, string name, KeyboardShortcut keyboardShortcut, string items)
			{
				Name = config.Bind(section, section+" Name", name, new ConfigDescription("Name of the grab pack"));
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

		//GrabPackConfig GrabPack1;
		//GrabPackConfig GrabPack2;
		//GrabPackConfig GrabPack3;
		public GrabPackConfig[] GrabPacks;


		private void Awake()
		{
			if (Instance == null)
			{
				Debug.Log("GrabMaterialsMod instance created and Awake called for the first time");
				Instance = this;
			}
			else if (Instance == this)
			{
				Debug.LogWarning("GrabMaterialsMod Awake called an additional time");
			}
			else 
			{
				Debug.LogError("GrabMaterialsMod instance already exists, additional one created, should it be destroyed?");
				Instance = this;
				//Destroy(this);
				//return;
			}
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
			HighlightDuration = Config.Bind("Client config", "Highlight Duration", 2f, new ConfigDescription("Duration in seconds to highlight containers when grabbing materials"));

			//GrabPack1 = new GrabPackConfig(Config, "Grab Pack 1", new KeyboardShortcut(KeyCode.G), "wood:10,finewood:20,greydwarfeye:10,surtlingcore:2");
			//GrabPack2 = new GrabPackConfig(Config, "Grab Pack 2", new KeyboardShortcut(KeyCode.G, KeyCode.LeftShift), "wood:10,finewood:40,ancientbark:40,ironnails:100,deeerhide:20");
			//GrabPack3 = new GrabPackConfig(Config, "Grab Pack 3", new KeyboardShortcut(KeyCode.G, KeyCode.LeftAlt), "wood:12,stone:5");

			GrabPacks = new GrabPackConfig[]
			{
				new GrabPackConfig(Config, "Grab Pack 1", "Explore", new KeyboardShortcut(KeyCode.G), "Workbench,Chest,Portal"),
				new GrabPackConfig(Config, "Grab Pack 2", "Karve Explore", new KeyboardShortcut(KeyCode.G, KeyCode.LeftShift), "Workbench,Chest,Portal,Karve"),
				new GrabPackConfig(Config, "Grab Pack 3", "Longship Explore", new KeyboardShortcut(KeyCode.G, KeyCode.LeftControl), "Workbench,Chest,Portal,Longship"),
				new GrabPackConfig(Config, "Grab Pack 4", "Swamp Explore", new KeyboardShortcut(KeyCode.G, KeyCode.LeftAlt), "Workbench,Chest,Portal,Campfire"),
				new GrabPackConfig(Config, "Grab Pack 5", "Ashlands Explore", new KeyboardShortcut(KeyCode.Y), "Workbench,Portal,Campfire:10"),
				new GrabPackConfig(Config, "Grab Pack 6", "Ashlands Flametal", new KeyboardShortcut(KeyCode.Y, KeyCode.LeftShift), "Workbench,Stone Cutter,Stone Portal,Shield Generator,Bones:10"),
				new GrabPackConfig(Config, "Grab Pack 7", "Grab Pack 7", new KeyboardShortcut(KeyCode.Y, KeyCode.LeftControl), ""),
				new GrabPackConfig(Config, "Grab Pack 8", "Grab Pack 8", new KeyboardShortcut(KeyCode.Y, KeyCode.LeftAlt), ""),
				new GrabPackConfig(Config, "Grab Pack 9", "Grab Pack 9", new KeyboardShortcut(KeyCode.U), ""),
				new GrabPackConfig(Config, "Grab Pack 10", "Grab Pack 10", new KeyboardShortcut(KeyCode.U, KeyCode.LeftShift), ""),
			};

			GrabPacks[8].Name.SettingChanged += (sender, e) =>
			{
				Debug.Log($"Grabpack renamed {sender.ToString()} {e.ToString()}");
			};

			new ConfigFileWatcher(Config);
		}

		private static void InitCommands()
		{
			//grab materials from nearby containers
			new Terminal.ConsoleCommand("grab", "[items] - grab items from nearby containers", (args) => { args.GrabItemsFromNearbyContainers(); });
			new Terminal.ConsoleCommand("g", "[items] - grab items from nearby containers", (args) => { args.GrabItemsFromNearbyContainers(); });
			new Terminal.ConsoleCommand("grabselected", "", (args) => { ConsoleCommands.GrabMaterialsForSelectedPiece(); });
			new Terminal.ConsoleCommand("grabpiece", "grab materials for named build piece, e.g. workbench or portal", (args) => { args.GrabMaterialsForPiece(); });

			//view container info
			new Terminal.ConsoleCommand("listcontainers", "list all known containers", (args) => { ListKnownContainers(); });
			new Terminal.ConsoleCommand("listlocalcontainers", "[radius] - Finds containers within the radius.", (args) => { ListLocalContainers(args); });
			new Terminal.ConsoleCommand("listcontents", "[radius] - Finds containers within the radius and lists their contents.", (args) => { ListLocalContainerContents(args); });
			new Terminal.ConsoleCommand("listpacks", "Lists your configured grab packs.", (args) => { ListGrabPacks(); });
			new Terminal.ConsoleCommand("inventory", "Displays counts of materials in containers in range.", (args) => { ListLocalInventory(args); });
			new Terminal.ConsoleCommand("i", "Displays counts of materials in containers in range.", (args) => { ListLocalInventory(args); });

			//for testing/learning
			new Terminal.ConsoleCommand("search", "[search-text] - search for items matching this string in nearby containers", (args) => { FindContainersWithMatchingItems(args); });
			new Terminal.ConsoleCommand("s", "[search-text] - search for items matching this string in nearby containers", (args) => { FindContainersWithMatchingItems(args); });
			new Terminal.ConsoleCommand("store", "[items] - stores items randomly nearby containers", (args) => { StoreItemsInNearbyContainers(); });
			new Terminal.ConsoleCommand("count", "[name of item to count] - omit to count everything", (args) => { CountInventory(args); });
			new Terminal.ConsoleCommand("listpieces", "", (args) => { ListAllPieces(); });
			//new Terminal.ConsoleCommand("buildpiecelookup", "", (args) => { BuildPieceLookup(); });
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

			//InputManager.Instance.AddButton(ModGuid, GrabPack1.Button);
			//InputManager.Instance.AddButton(ModGuid, GrabPack2.Button);
			//InputManager.Instance.AddButton(ModGuid, GrabPack3.Button);
			foreach (var grabPack in GrabPacks)
			{
				//InitButton(grabPack.Name.Value, grabPack.Key.Value.MainKey);
				//grabPack.Button = new ButtonConfig()
				//{
				//	Name = grabPack.Name.Value,
				//	Config = grabPack.Key,
				//	ShortcutConfig = grabPack.Key
				//};
				InputManager.Instance.AddButton(ModGuid, grabPack.Button);
			}
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

				//if (ZInput.GetButtonDown(GrabPack1.Button.Name))
				//{
				//	ConsoleCommands.GrabMaterialsForPack(GrabPack1.Name.Value, GrabPack1.Items.Value);
				//}

				//if (ZInput.GetButtonDown(GrabPack2.Button.Name))
				//{
				//	ConsoleCommands.GrabMaterialsForPack(GrabPack2.Name.Value, GrabPack2.Items.Value);
				//}

				//if (ZInput.GetButtonDown(GrabPack3.Button.Name))
				//{
				//	ConsoleCommands.GrabMaterialsForPack(GrabPack3.Name.Value, GrabPack3.Items.Value);
				//}

				foreach (var grabPack in GrabPacks)
				{
					if (ZInput.GetButtonDown(grabPack.Button.Name))
					{
						ConsoleCommands.GrabMaterialsForPack(grabPack);
					}
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

			Debug.LogWarning("listing build pieces (prefabs with an associated component)");
			foreach (var prefab in ZNetScene.instance.m_prefabs)
			{
				if (prefab.TryGetComponent<Piece>(out var piece))
				{
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
							localizedName = $"{piece.m_name}";
						}
					}
					catch (Exception e)
					{
						Debug.LogError($"Error translating piece name {piece.m_name}: {e.Message}");
						localizedName = $"translation failed";
					}
					if (prefab.name == piece.name)
					{
						//Debug.Log($"{prefab.name} \"{localizedName}\" {GetPieceResourceList(piece)}");
						Debug.Log($"{prefab.name} \"{localizedName}\"");
					}
					else
					{
						Debug.LogError($"DIFFERENT NAMES Piece: {prefab.name} {piece.name} {localizedName}");
					}
					//Debug.Log($"Piece: {piece.name} ({prefab.name})");
				}
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
			var radius = 50f; // Default radius
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
					var localizedName = "";
					localizedName = LocalizationManager.Instance.TryTranslate(item.m_shared.m_name);
					//Debug.Log($"{item.Name()} ({item.m_shared.m_name}) [{localizedName}] {item.Count()} crafted by '{item.m_crafterName}'\ntooltip: {item.GetTooltip()}\nname: {item.Name()}\n{item.ToString()}");
					Debug.Log($"{item.Count()},{localizedName},{item.Name()},{item.GetCategory()},{item.m_shared.m_itemType},{item.IsWeapon()},{item.IsEquipable()},{item.m_shared.m_isDrink},{item.GetArmor()},{item.m_shared.m_armorMaterial},{item.m_shared.m_food},{item.m_shared.m_foodStamina},{item.m_shared.m_foodEitr},{item.m_shared.m_ammoType},{item.m_shared.m_questItem},{item.m_shared.m_skillType}"); //crafted by '{item.m_crafterName}'
				}
			}
		}

		private static void ListLocalInventory(Terminal.ConsoleEventArgs args)
		{
			var radius = 50f; // Default radius
			var text = args.Length > 1 ? args.ArgsAll.ToLower() : null;

			//if (args.Length > 1)
			//	float.TryParse(args[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out radius);
			var nearbyContainers = Boxes.GetNearbyContainers(radius);
			Debug.Log($"searching {nearbyContainers.Count} containers within {radius} meters out of {Boxes.Containers.Count} known containers");
			Debug.Log($"showing the inventory of {nearbyContainers.Count} nearby containers");

			var itemCounts = new SortedDictionary<string, int>();
			foreach (var container in nearbyContainers)
			{
				var inventory = container.GetInventory();
				var items = inventory.GetAllItems();
				var alreadyHighlighted = false;
				//var firstTime = true;
				foreach (var item in items)
				{
					var itemName = item.m_shared.m_name;
					var localizedName = LocalizationManager.Instance.TryTranslate(itemName).ToLower();
					var itemCategory = item.GetCategory();
					var itemCategoryString = itemCategory.ToString().ToLower();
					GrabMaterials.Extensions.ItemCategory searchCategory = GrabMaterials.Extensions.ItemCategory.None;
					var isCategorySearch = text != null ? Enum.TryParse(text, true, out searchCategory) : false;
					var searchCategoryString = searchCategory.ToString().ToLower();
					//if (firstTime && itemCategoryString.Contains("food"))
					//{
					//	Debug.Log($"{itemName} {item.Name()} {localizedName} {itemCategory} '{text}' isCategory:{isCategorySearch} {searchCategory} {searchCategoryString.Contains(text)}");
					//	//firstTime = false;
					//}
					if (isCategorySearch ? itemCategory == searchCategory : text == null || item.Name().Contains(text) || itemName.Contains(text) || localizedName.Contains(text) || itemCategoryString.Contains(text))
					{
						if (itemCounts.ContainsKey(itemName))
						{
							itemCounts[itemName] += item.Count();
						}
						else
						{
							itemCounts[itemName] = item.Count();
						}
						if (!alreadyHighlighted)
						{
							container.Highlight();
							alreadyHighlighted = true;
						}
					}
				}
			}
			var msg = new StringBuilder();
			foreach (var kvp in itemCounts)
			{
				var localizedName = LocalizationManager.Instance.TryTranslate(kvp.Key);
				Debug.Log($"{kvp.Value} {localizedName}");
				msg.AppendLine($"{kvp.Value} {localizedName}");
			}
			Player.m_localPlayer.Message(MessageHud.MessageType.Center, msg.ToString());
		}

		private static void ListGrabPacks()
		{
			Debug.Log($"listing {Instance.GrabPacks.Length} configured grab packs");
			foreach (var grabPack in Instance.GrabPacks)
			{
				Debug.Log($"{grabPack.Name.Value} ({grabPack.Key.Value}): {grabPack.Items.Value}");
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