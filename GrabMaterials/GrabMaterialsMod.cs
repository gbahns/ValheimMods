using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
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
using System.IO;
using UnityEngine;

namespace GrabMaterialsMod
{

	[BepInPlugin(GrabMaterialsMod.ModGuid, "Grab Materials", "2.3.1")]
	[BepInProcess("valheim.exe")]
	public class GrabMaterialsMod : BaseUnityPlugin
	{
		const string ModGuid = "DeathMonger.GrabMaterialsMod";
		private readonly Harmony harmony = new Harmony(ModGuid);
		public static GrabMaterialsMod Instance;
		internal static ManualLogSource Log;

		public enum DeltaSetting
		{
			UseGlobal,  // defer to GrabDeltaGlobal
			On,         // delta enabled (only grab the shortfall)
			Off,        // delta disabled (always grab the full amount)
		}

		//private ButtonConfig GrabPortalMatsButton;
		public ConfigEntry<bool> GrabDeltaGlobal;
		public ConfigEntry<float> GrabDeltaLedgerTimeout;
		public ConfigEntry<float> HighlightDuration;
		public ConfigEntry<float> PanelIdleTimeout;
		public ConfigEntry<float> PanelFadeDuration;
		public ConfigEntry<bool> PanelDismissOnMovement;
		public ConfigEntry<bool> PanelCategoryUnderlines;
		public ConfigEntry<bool> PanelShowItemIcons;
		public ConfigEntry<bool> PanelMarkUndiscovered;
		public ConfigEntry<bool> PanelPauseWhileOpen;
		public ConfigEntry<float> PanelIconSize;
		public ConfigEntry<GrabMaterials.MaterialsPanel.InventoryShape> InventoryShape;
		public ConfigEntry<GrabMaterials.MaterialsPanel.InventoryStyle> InventoryStyle;
		public ConfigEntry<bool> ShowDistanceHud;
		public ConfigEntry<bool> ShowDistanceCoords;
		public ConfigEntry<float> DistanceHudOffsetX;
		public ConfigEntry<float> DistanceHudOffsetY;
		public ConfigEntry<bool> ShowPackHud;
		public ConfigEntry<KeyboardShortcut> PackHudToggleKey;
		public ConfigEntry<GrabMaterials.PackHud.Anchor> PackHudAnchor;
		public ConfigEntry<float> PackHudOffsetX;
		public ConfigEntry<float> PackHudOffsetY;
		public ConfigEntry<bool> PackHudShowBuildPieceKey;
		public ConfigEntry<bool> PackHudHideUndiscovered;
		private ButtonConfig GrabSelectedPieceMatsButton;
		//private ConfigEntry<KeyCode> GrabPortalMatsKeyboardConfig;
		//private ConfigEntry<InputManager.GamepadButton> GrabPortalMatsGamepadConfig;
		private ConfigEntry<KeyCode> GrabSelectedPieceMatsKeyboardConfig;
		internal KeyCode GrabSelectedPieceKey => GrabSelectedPieceMatsKeyboardConfig.Value;

		public class GrabPackConfig
		{
			//public string Name;
			//public KeyCode Key;
			//public string Items;
			//public bool GrabDelta;
			public ConfigEntry<string> Name;
			public ConfigEntry<KeyboardShortcut> Key;
			public ConfigEntry<string> Items;
			public ConfigEntry<DeltaSetting> GrabDelta;
			public ButtonConfig Button;

			public GrabPackConfig(ConfigFile config, string section, string name, KeyboardShortcut keyboardShortcut, string items)
			{
				Name = config.Bind(section, section+" Name", name, new ConfigDescription("Name of the grab pack"));
				Key = config.Bind(section, section+" Key", keyboardShortcut, new ConfigDescription("Key to grab materials for the grab pack"));
				Items = config.Bind(section, section+" Items", items, new ConfigDescription("Items to grab for the grab pack"));
				GrabDelta = config.Bind(section, section+" Grab Delta", DeltaSetting.UseGlobal, new ConfigDescription("UseGlobal = follow the 'Grab Delta (default)' setting; On = only grab the shortfall (e.g. if the pack needs 10 wood and you have 7, only 3 will be grabbed); Off = always grab the full amount."));
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
			Log = Logger;
			if (Instance == null)
			{
				Log.LogInfo("GrabMaterialsMod instance created and Awake called for the first time");
				Instance = this;
			}
			else if (Instance == this)
			{
				Log.LogWarning("GrabMaterialsMod Awake called an additional time");
			}
			else 
			{
				Log.LogError("GrabMaterialsMod instance already exists, additional one created, should it be destroyed?");
				Instance = this;
				//Destroy(this);
				//return;
			}
			harmony.PatchAll();
			InitConfig();
			SetupConfigWatcher();
			InitCommands();
			InitButtons();
		}

		// Reload the config when the .cfg file changes on disk so edits made in a text editor take
		// effect live.  BepInEx only reads the file at startup; without this, an edited file is
		// silently overwritten with the in-memory values the next time any setting is saved in-game.
		private FileSystemWatcher _configWatcher;
		private void SetupConfigWatcher()
		{
			try
			{
				_configWatcher = new FileSystemWatcher(BepInEx.Paths.ConfigPath, Path.GetFileName(Config.ConfigFilePath));
				_configWatcher.Changed += OnConfigFileChanged;
				_configWatcher.Created += OnConfigFileChanged;
				_configWatcher.Renamed += OnConfigFileChanged;
				_configWatcher.SynchronizingObject = ThreadingHelper.SynchronizingObject;
				_configWatcher.EnableRaisingEvents = true;
			}
			catch (Exception ex)
			{
				Log.LogWarning($"Could not watch config file for changes: {ex.Message}");
			}
		}

		private void OnConfigFileChanged(object sender, FileSystemEventArgs e)
		{
			if (!File.Exists(Config.ConfigFilePath)) return;
			try
			{
				Config.Reload();
				Log.LogInfo("Config file changed on disk; settings reloaded");
			}
			catch (Exception ex)
			{
				Log.LogError($"Config file changed but could not be reloaded: {ex.Message}");
			}
		}

		private void InitConfig()
		{
			var configDescription = new ConfigDescription("Key to grab portal materials");
			//GrabPortalMatsKeyboardConfig = Config.Bind("Client config", "GrabPortalMaterialsKey", KeyCode.I, configDescription);
			//GrabPortalMatsGamepadConfig = Config.Bind("Client config", "GrabPortalMaterialsButton", InputManager.GamepadButton.ButtonSouth, configDescription);
			//new KeyboardShortcut(KeyCode.G, KeyCode.LeftShift, KeyCode.RightShift)
			GrabSelectedPieceMatsKeyboardConfig = Config.Bind("Grab Selected Piece", "GrabSelectedPieceMatsKey", KeyCode.J, new ConfigDescription("Key to grab materials for the currently selectede build piece"));
			HighlightDuration = Config.Bind("Client config", "Highlight Duration", 2f, new ConfigDescription("Duration in seconds to highlight containers when grabbing materials"));
			GrabDeltaGlobal = Config.Bind("Client config", "Grab Delta (default)", true, new ConfigDescription("If true, all grabs (packs, individual build pieces, /grab <piece>) only grab the shortfall between what you already have and what's needed. Per-pack settings can override this."));
			GrabDeltaLedgerTimeout = Config.Bind("Client config", "Grab Delta Ledger Timeout (seconds)", 30f, new ConfigDescription("Back-to-back delta grabs share a ledger so the same inventory isn't credited toward two different builds (e.g. /g cart then /g explore won't both 'see' the same 10 wood). After this many seconds without a delta grab, the ledger clears so the next grab considers only what's currently in your inventory. Set to 0 to effectively disable cross-grab tracking."));

			PanelIdleTimeout = Config.Bind("Panel UI", "Idle Timeout (seconds)", 15f, new ConfigDescription("Seconds the grab-results panel stays fully visible before fading out automatically."));
			PanelFadeDuration = Config.Bind("Panel UI", "Fade Duration (seconds)", 3f, new ConfigDescription("Seconds the grab-results panel takes to fade out."));
			PanelDismissOnMovement = Config.Bind("Panel UI", "Dismiss On Movement", true, new ConfigDescription("Start fading the panel when the player begins moving, attacking, blocking, or jumping. If false, only the idle timeout dismisses the panel."));
			InventoryStyle = Config.Bind("Panel UI", "Inventory Style", GrabMaterials.MaterialsPanel.InventoryStyle.List, new ConfigDescription("Display style for the /inventory panel. List = grouped by category, count + name per row (counts right-aligned). Table = flat 3-column table with Category / Item / Count headers."));
			PanelCategoryUnderlines = Config.Bind("Panel UI", "Category Underlines", true, new ConfigDescription("In List mode, draw a thin orange line under each category name. Has no effect in Table mode."));
			InventoryShape = Config.Bind("Panel UI", "Inventory Shape", GrabMaterials.MaterialsPanel.InventoryShape.SlightlyWide, new ConfigDescription("Target shape for the inventory panel in List mode. The panel auto-picks the column count that produces the closest match. MaxHeight = always 1 column. MaxWidth = fan out as many columns as fit on the screen."));
			PanelShowItemIcons = Config.Bind("Panel UI", "Show Item Icons", true, new ConfigDescription("Show each item's icon next to its name in the inventory panel."));
			PanelMarkUndiscovered = Config.Bind("Panel UI", "Mark Undiscovered Items", true, new ConfigDescription("Mark items in the /inventory panel that this character has never held with a '(new)' tag. Handy in multiplayer, where a shared chest is often full of a friend's crafting you have never handled yourself."));
			PanelPauseWhileOpen = Config.Bind("Panel UI", "Pause While Inventory Panel Open", false, new ConfigDescription("Pause the game while the /inventory panel is up, the way the ESC menu does. Toggle it from the button in the panel's top-right corner. This works by itself when playing solo or hosting alone; on a dedicated server it takes the Pause My Server mod. The button shows whether the pause actually took effect."));
			PanelPauseWhileOpen.SettingChanged += (s, e) => GrabMaterials.PanelPause.Refresh();
			PanelIconSize = Config.Bind("Panel UI", "Icon Size", 24f, new ConfigDescription("Width of the item-icon column in pixels. Icons are square and sized to fit. Beyond ~26 the row stays the same height so icons get visually capped by the row.", new AcceptableValueRange<float>(12f, 40f)));

			ShowDistanceHud = Config.Bind("Distance HUD", "Enabled", false, new ConfigDescription("Show a small always-on widget displaying the player's horizontal distance from the world center."));
			ShowDistanceCoords = Config.Bind("Distance HUD", "Show Coordinates", false, new ConfigDescription("Also include the player's (X, Z) coordinates in the distance widget."));
			DistanceHudOffsetX = Config.Bind("Distance HUD", "Offset X (px)", 10f, new ConfigDescription("Horizontal offset from the upper-left corner of the screen."));
			DistanceHudOffsetY = Config.Bind("Distance HUD", "Offset Y (px)", 10f, new ConfigDescription("Vertical offset from the top of the screen."));

			ShowPackHud = Config.Bind("Pack HUD", "Show Pack HUD", true, new ConfigDescription("Show a small on-screen list of your grab packs and their hotkeys, like the quick-slot labels in AzuExtendedPlayerInventory. Packs with no items are left out. The toggle key below flips this setting, so the state persists."));
			PackHudToggleKey = Config.Bind("Pack HUD", "Toggle Key", new KeyboardShortcut(KeyCode.O), new ConfigDescription("Key to show or hide the pack HUD."));
			PackHudAnchor = Config.Bind("Pack HUD", "Anchor", GrabMaterials.PackHud.Anchor.BottomLeft, new ConfigDescription("Which corner of the screen the pack HUD sits in. Offsets below are measured from that corner."));
			PackHudOffsetX = Config.Bind("Pack HUD", "Offset X (px)", 10f, new ConfigDescription("Horizontal distance from the anchored corner."));
			PackHudOffsetY = Config.Bind("Pack HUD", "Offset Y (px)", 300f, new ConfigDescription("Vertical distance from the anchored corner. The default clears the health and stamina bars in the bottom-left."));
			PackHudShowBuildPieceKey = Config.Bind("Pack HUD", "Show Build Piece Key", true, new ConfigDescription("Also list the key that grabs materials for the build piece under the cursor."));
			PackHudHideUndiscovered = Config.Bind("Pack HUD", "Hide Undiscovered Packs", true, new ConfigDescription("Hide packs that need a material you have never held. A pack reappears once you have picked up every material it needs, so early-game clutter like Karve or Longship packs stays out of the way until nails and the like are in hand."));

			//GrabPack1 = new GrabPackConfig(Config, "Grab Pack 1", new KeyboardShortcut(KeyCode.K), "wood:10,finewood:20,greydwarfeye:10,surtlingcore:2");
			//GrabPack2 = new GrabPackConfig(Config, "Grab Pack 2", new KeyboardShortcut(KeyCode.K, KeyCode.LeftShift), "wood:10,finewood:40,ancientbark:40,ironnails:100,deeerhide:20");
			//GrabPack3 = new GrabPackConfig(Config, "Grab Pack 3", new KeyboardShortcut(KeyCode.K, KeyCode.LeftAlt), "wood:12,stone:5");

			GrabPacks = new GrabPackConfig[]
			{
				new GrabPackConfig(Config, "Grab Pack 1", "Explore", new KeyboardShortcut(KeyCode.K), "Workbench,Chest,Portal"),
				new GrabPackConfig(Config, "Grab Pack 2", "Karve Explore", new KeyboardShortcut(KeyCode.K, KeyCode.LeftShift), "Workbench,Chest,Portal,Karve"),
				new GrabPackConfig(Config, "Grab Pack 3", "Longship Explore", new KeyboardShortcut(KeyCode.K, KeyCode.LeftControl), "Workbench,Chest,Portal,Longship"),
				new GrabPackConfig(Config, "Grab Pack 4", "Swamp Explore", new KeyboardShortcut(KeyCode.K, KeyCode.LeftAlt), "Workbench,Chest,Portal,Campfire"),
				new GrabPackConfig(Config, "Grab Pack 5", "Ashlands Explore", new KeyboardShortcut(KeyCode.L), "Workbench,Portal,Campfire:10"),
				new GrabPackConfig(Config, "Grab Pack 6", "Ashlands Flametal", new KeyboardShortcut(KeyCode.L, KeyCode.LeftShift), "Workbench,Stone Cutter,Stone Portal,Shield Generator,Bones:10"),
				new GrabPackConfig(Config, "Grab Pack 7", "Grab Pack 7", new KeyboardShortcut(KeyCode.L, KeyCode.LeftControl), ""),
				new GrabPackConfig(Config, "Grab Pack 8", "Grab Pack 8", new KeyboardShortcut(KeyCode.L, KeyCode.LeftAlt), ""),
				new GrabPackConfig(Config, "Grab Pack 9", "Grab Pack 9", new KeyboardShortcut(KeyCode.Semicolon), ""),
				new GrabPackConfig(Config, "Grab Pack 10", "Grab Pack 10", new KeyboardShortcut(KeyCode.Semicolon, KeyCode.LeftShift), ""),
			};

			GrabPacks[8].Name.SettingChanged += (sender, e) =>
			{
				Log.LogInfo($"Grabpack renamed {sender.ToString()} {e.ToString()}");
			};

			new ConfigFileWatcher(Config);
		}

		private static void InitCommands()
		{
			//grab materials from nearby containers
			new Terminal.ConsoleCommand("grab", "[items] - grab items from nearby containers. Use a piece name, a pack name, or 'new' for one of every item you have never held.", (args) => { args.GrabItemsFromNearbyContainers(); });
			new Terminal.ConsoleCommand("g", "[items] - grab items from nearby containers. Use a piece name, a pack name, or 'new' for one of every item you have never held.", (args) => { args.GrabItemsFromNearbyContainers(); });
			new Terminal.ConsoleCommand("grabselected", "", (args) => { ConsoleCommands.GrabMaterialsForSelectedPiece(); });
			new Terminal.ConsoleCommand("grabpiece", "grab materials for named build piece, e.g. workbench or portal", (args) => { args.GrabMaterialsForPiece(); });

			//view container info
			new Terminal.ConsoleCommand("listcontainers", "list all known containers", (args) => { ListKnownContainers(); });
			new Terminal.ConsoleCommand("listlocalcontainers", "[radius] - Finds containers within the radius.", (args) => { ListLocalContainers(args); });
			new Terminal.ConsoleCommand("listcontents", "[radius] - Finds containers within the radius and lists their contents.", (args) => { ListLocalContainerContents(args); });
			new Terminal.ConsoleCommand("listpacks", "Lists your configured grab packs.", (args) => { ListGrabPacks(); });
			new Terminal.ConsoleCommand("grabreset", "Clear the grab-delta ledger so the next delta grab considers only what's currently in your inventory.", (args) => {
				GrabMaterials.ConsoleCommands.ResetPendingLedger();
				Player.m_localPlayer?.Message(MessageHud.MessageType.Center, "Grab delta ledger cleared.");
			});
			new Terminal.ConsoleCommand("inventory", "[filter] - counts of items in nearby containers. Filter by item name, by category, with 'new' for items you have never held, or with 'empty' to find and highlight empty containers.", (args) => { ListLocalInventory(args); });
			new Terminal.ConsoleCommand("i", "[filter] - counts of items in nearby containers. Filter by item name, by category, with 'new' for items you have never held, or with 'empty' to find and highlight empty containers.", (args) => { ListLocalInventory(args); });
			new Terminal.ConsoleCommand("istyle", "[1-2] - cycle inventory display style (1=List, 2=Table)", (args) => { SetInventoryStyle(args); });

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
				var packRef = grabPack;
				grabPack.Key.SettingChanged += (s, e) => RebindButton(packRef.Button, packRef.Key.Value.MainKey);
			}
		}

		// Jotunn registers each pack key with ZInput once, at ZInput.Load, and its GetButtonDown patch
		// requires BOTH that registered key and the live config shortcut to be down.  It only re-registers
		// on change for plain KeyCode configs, not KeyboardShortcut ones, so a pack key edited in the
		// config would need a restart.  This does the same rebind Jotunn does internally.
		private static readonly System.Reflection.MethodInfo KeyCodeToPathMethod =
			AccessTools.Method(typeof(ZInput), "KeyCodeToPath", new[] { typeof(KeyCode), typeof(bool) });

		private static void RebindButton(ButtonConfig button, KeyCode key)
		{
			try
			{
				if (button == null || ZInput.instance == null || KeyCodeToPathMethod == null) return;
				var def = ZInput.instance.GetButtonDef(button.Name);
				if (def == null) return;
				var path = KeyCodeToPathMethod.Invoke(null, new object[] { key, false }) as string;
				if (string.IsNullOrEmpty(path)) return;
				def.Rebind(path);
				Log.LogInfo($"Rebound {button.Name} to {key}");
			}
			catch (Exception ex)
			{
				Log.LogWarning($"Could not rebind {button?.Name}: {ex.Message}");
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

		// Player.TakeInput() is protected at runtime (the publicized DLL only makes it look public,
		// and calling it throws MethodAccessException), so mirror its intent with the public checks.
		private static bool GuiHasFocus()
		{
			return Console.IsVisible() || Menu.IsVisible() || InventoryGui.IsVisible()
				|| TextInput.IsVisible() || StoreGui.IsVisible() || Minimap.IsOpen()
				|| PlayerCustomizaton.IsBarberGuiVisible()
				|| (Chat.instance != null && Chat.instance.IsChatDialogWindowVisible());
		}

		private void Update()
		{
			// First, so that being unable to move never depends on anything below succeeding.
			GrabMaterials.MaterialsPanel.RefreshInputBlock();
			GrabMaterials.MaterialsPanel.Tick();
			GrabMaterials.DistanceHud.Tick();
			GrabMaterials.PackHud.Tick();
			GrabMaterials.PanelPause.Refresh();

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

				if (PackHudToggleKey.Value.IsDown() && !GuiHasFocus())
				{
					ShowPackHud.Value = !ShowPackHud.Value;
					GrabMaterials.PackHud.Refresh();
				}

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
				Log.LogWarning("Cannot index: ZNetScene.instance is null");
				return;
			}

			//Log.LogWarning("listing all recipies");
			//foreach (Recipe recipe in ObjectDB.instance.m_recipes)
			//{
			//	Log.LogInfo($"Recipe: {recipe.name} {(recipe.m_item != null ? recipe.m_item.name : "m_item is null")} {recipe.m_enabled} {recipe.IsValid()} {recipe.m_craftingStation}");
			//	//Log.LogInfo($"{recipe.m_item.name} {recipe.m_item.enabled} {recipe.m_enabled} {recipe.m_item.m_itemData.Count()} {recipe.IsValid()} {recipe.m_craftingStation}");
			//}

			Log.LogWarning("listing build pieces (prefabs with an associated component)");
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
							localizedName = GrabMaterials.Extensions.Localize(piece.m_name);
						}
						else
						{
							localizedName = $"{piece.m_name}";
						}
					}
					catch (Exception e)
					{
						Log.LogError($"Error translating piece name {piece.m_name}: {e.Message}");
						localizedName = $"translation failed";
					}
					if (prefab.name == piece.name)
					{
						//Log.LogInfo($"{prefab.name} \"{localizedName}\" {GetPieceResourceList(piece)}");
						// Check if the piece has an enabled recipe
						var hasEnabledRecipe = false;
						foreach (Recipe recipe in ObjectDB.instance.m_recipes)
						{
							// Find the matching recipe AND check if it's enabled.
							if (recipe.m_item == piece && recipe.m_enabled)
							{
								hasEnabledRecipe = true; // Found an enabled recipe for this item.
								break; // No need to check further recipes.
							}
						}

						Log.LogInfo($"{prefab.name} \"{localizedName}\" {piece.GetResourceList()} {hasEnabledRecipe} {prefab.activeInHierarchy} {piece.enabled} {piece.m_enabled} {piece.isActiveAndEnabled} {piece.IsPlacedByPlayer()} {piece.m_category} {piece.m_craftingStation}");
						//if (localizedName == "Chest")
						//	Log.LogInfo($"{hasEnabledRecipe} {prefab.activeInHierarchy} {prefab.activeSelf} {prefab.gameObject.activeInHierarchy} {prefab.gameObject.activeSelf} {piece.m_destroyedLootPrefab} {piece.enabled} {piece.m_enabled} {piece.isActiveAndEnabled} {piece.IsPlacedByPlayer()} {prefab.name} \"{localizedName}\" {piece.GetResourceList()} {piece.m_category} {piece.m_craftingStation}");
					}
					else
					{
						Log.LogError($"DIFFERENT NAMES Piece: {prefab.name} {piece.name} {localizedName}");
					}
					//Log.LogInfo($"Piece: {piece.name} ({prefab.name})");
				}
			}

			//Jotunn.Managers.PieceManager.Instance.GetPiece().Pieces.ForEach(piece =>
			//{
			//	Log.LogInfo($"{piece.name}");
			//});

			////this just list the build pieces available on the currently selected workbench category
			//var pieces = player.GetBuildPieces();
			//if (pieces == null)
			//{
			//	Log.LogInfo("No build pieces found");
			//	return;
			//}
			//Log.LogInfo($"listing {pieces.Count()} pieces");
			//foreach (var piece in pieces)
			//{
			//	Log.LogInfo($"{piece.name}");
			//}

			////this seems to only give the players base recipes without even a hammer
			//var recipes = new List<Recipe>();
			//player.GetAvailableRecipes(ref recipes);
			//Log.LogInfo($"listing {recipes.Count()} recipes");
			//foreach (var recipe in recipes)
			//{
			//	Log.LogInfo($"{recipe}");
			//}

			//var objectDB = ObjectDB.instance;
			//if (objectDB == null)
			//{
			//	Log.LogError("ObjectDB instance is null");
			//	return;
			//}

			//foreach (var prefab in objectDB.m_items)
			//{
			//	Log.LogInfo($"Prefab: {prefab.name}");
			//}

			//Log.LogWarning("listing named prefabs");
			//foreach (var prefab in ZNetScene.instance.m_namedPrefabs.Values)
			//{
			//	Log.LogInfo($"Named Prefab: {prefab.name}");
			//}

			//PieceTable pieceTable = GetPieceTable();
			//Jotunn.Utils.ModRegistry.GetPieces().ForEach(piece =>
			//{
			//	Log.LogInfo($"{piece.name}");
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
			Log.LogInfo($"listing {Boxes.Containers.Count} known containers");
			foreach (var container in Boxes.Containers)
				Log.LogInfo($"{++i}. {container.name} {container.m_name}  ({container.GetType()} {container.GetInstanceID()})");
		}

		private static void ListLocalContainers(Terminal.ConsoleEventArgs args)
		{
			int i = 0;
			var radius = 10f; // Default radius
			if (args.Length > 1)
				float.TryParse(args[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out radius);
			var nearbyContainers = Boxes.GetNearbyContainers(radius);
			Log.LogInfo($"listing {nearbyContainers.Count} containers within {radius} meters out of {Boxes.Containers.Count} known containers");
			foreach (var container in nearbyContainers)
				Log.LogInfo($"{++i}. {container.name} {container.m_name}  ({container.GetType()} {container.GetInstanceID()})");
		}

		private static void ListLocalContainerContents(Terminal.ConsoleEventArgs args)
		{
			var radius = 50f; // Default radius
			if (args.Length > 1)
				float.TryParse(args[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out radius);
			var nearbyContainers = Boxes.GetNearbyContainers(radius);
			Log.LogInfo($"listing {nearbyContainers.Count} containers within {radius} meters out of {Boxes.Containers.Count} known containers");
			Log.LogInfo($"showing the contents of {nearbyContainers.Count} nearby containers");
			foreach (var container in nearbyContainers)
			{
				Log.LogInfo($"contents of {container.name} {container.GetInstanceID()}:");
				var inventory = container.GetInventory();
				var items = inventory.GetAllItems();
				foreach (var item in items)
				{
					var localizedName = "";
					localizedName = GrabMaterials.Extensions.Localize(item.m_shared.m_name);
					//Log.LogInfo($"{item.Name()} ({item.m_shared.m_name}) [{localizedName}] {item.Count()} crafted by '{item.m_crafterName}'\ntooltip: {item.GetTooltip()}\nname: {item.Name()}\n{item.ToString()}");
					Log.LogInfo($"{item.Count()},{localizedName},{item.Name()},{item.GetCategory()},{item.m_shared.m_itemType},{item.IsWeapon()},{item.IsEquipable()},{item.m_shared.m_isDrink},{item.GetArmor()},{item.m_shared.m_armorMaterial},{item.m_shared.m_food},{item.m_shared.m_foodStamina},{item.m_shared.m_foodEitr},{item.m_shared.m_ammoType},{item.m_shared.m_questItem},{item.m_shared.m_skillType}"); //crafted by '{item.m_crafterName}'
				}
			}
		}

		private static void SetInventoryStyle(Terminal.ConsoleEventArgs args)
		{
			var styles = (GrabMaterials.MaterialsPanel.InventoryStyle[])Enum.GetValues(typeof(GrabMaterials.MaterialsPanel.InventoryStyle));
			GrabMaterials.MaterialsPanel.InventoryStyle next;
			if (args.Length > 1 && int.TryParse(args[1], out var n) && n >= 1 && n <= styles.Length)
			{
				next = styles[n - 1];
			}
			else
			{
				// No arg / bad arg → cycle to next.
				var current = Instance.InventoryStyle.Value;
				var idx = Array.IndexOf(styles, current);
				next = styles[(idx + 1) % styles.Length];
			}
			Instance.InventoryStyle.Value = next;
			Player.m_localPlayer?.Message(MessageHud.MessageType.Center, $"Inventory style: {next}");
		}

		// Convert an enum name like "RawMeat" to a display label "Raw Meat".
		private static string FormatCategoryName(GrabMaterials.Extensions.ItemCategory cat)
		{
			var name = cat.ToString();
			var sb = new StringBuilder(name.Length + 4);
			for (int i = 0; i < name.Length; i++)
			{
				if (i > 0 && char.IsUpper(name[i])) sb.Append(' ');
				sb.Append(name[i]);
			}
			return sb.ToString();
		}

		private static void ListLocalInventory(Terminal.ConsoleEventArgs args)
		{
			var radius = 50f; // Default radius
			var text = args.Length > 1 ? args.ArgsAll.ToLower() : null;

			// "/i new" lists only what this character has never held, rather than matching the
			// word "new" against item names.  Valheim keeps that set itself and fills it the
			// first time an item enters your inventory.
			var player = Player.m_localPlayer;
			var isNewSearch = text == "new";

			// "/i empty" asks about the containers themselves rather than their contents,
			// so it skips the item and processor walk entirely.
			if (text == "empty")
			{
				ListEmptyContainers(Boxes.GetNearbyContainers(radius));
				return;
			}

			var nearbyContainers = Boxes.GetNearbyContainers(radius);
			var nearbySmelters = Boxes.GetNearbySmelters(radius);
			Log.LogInfo($"searching {nearbyContainers.Count} containers and {nearbySmelters.Count} processors within {radius} meters");

			// Bucket matching items by category, then by m_shared.m_name (sorted) for stable display.
			var byCategory = new Dictionary<GrabMaterials.Extensions.ItemCategory, SortedDictionary<string, int>>();
			// One sprite per unique item name (first encountered wins).
			var iconsByName = new Dictionary<string, UnityEngine.Sprite>();
			// Per-item, per-station-type tally of materials sitting in a smelter/kiln/etc.
			// Keyed first by item m_shared.m_name, then by station label ("kiln",
			// "smelter", etc.) so the panel can show "(N in kiln)" when an item only
			// lives in one type of station, and fall back to "(N in processing)" when
			// it's spread across multiple.
			var inProcessByNameByLoc = new Dictionary<string, Dictionary<string, int>>();
			foreach (var container in nearbyContainers)
			{
				var inventory = container.GetInventory();
				if (inventory == null) continue;
				var alreadyHighlighted = false;
				foreach (var item in inventory.GetAllItems())
				{
					var itemName = item.m_shared.m_name;
					var localizedName = GrabMaterials.Extensions.Localize(itemName).ToLower();
					var itemCategory = item.GetCategory();
					var itemCategoryString = itemCategory.ToString().ToLower();
					GrabMaterials.Extensions.ItemCategory searchCategory = GrabMaterials.Extensions.ItemCategory.None;
					var isCategorySearch = text != null ? Enum.TryParse(text, true, out searchCategory) : false;

					var matches = isNewSearch
						? (player != null && !player.IsMaterialKnown(itemName))
						: isCategorySearch
						? itemCategory == searchCategory
						: text == null || item.Name().Contains(text) || itemName.Contains(text) || localizedName.Contains(text) || itemCategoryString.Contains(text);
					if (!matches) continue;

					if (!byCategory.TryGetValue(itemCategory, out var byName))
					{
						byName = new SortedDictionary<string, int>();
						byCategory[itemCategory] = byName;
					}
					if (byName.ContainsKey(itemName)) byName[itemName] += item.Count();
					else byName[itemName] = item.Count();

					if (!iconsByName.ContainsKey(itemName) && item.m_shared.m_icons != null && item.m_shared.m_icons.Length > 0)
					{
						iconsByName[itemName] = item.m_shared.m_icons[0];
					}

					if (!alreadyHighlighted)
					{
						container.Highlight();
						alreadyHighlighted = true;
					}
				}
			}

			// Walk processors (kilns, smelters, blast furnaces, windmills, etc.) — their
			// queued inputs + fuel are real materials on the player's plot. Same filter
			// and bucketing as containers, plus a parallel inProcess tally so the panel
			// can show "(N in process)" next to the affected rows.
			foreach (var smelter in nearbySmelters)
			{
				var queued = GrabMaterials.Processors.GetQueuedItems(smelter);
				if (queued.Count == 0) continue;
				var stationLabel = GrabMaterials.Processors.GetLabel(smelter);
				var alreadyHighlighted = false;
				foreach (var kvp in queued)
				{
					var prefab = ObjectDB.instance?.GetItemPrefab(kvp.Key);
					if (prefab == null) continue;
					var itemDrop = prefab.GetComponent<ItemDrop>();
					if (itemDrop == null) continue;
					var itemData = itemDrop.m_itemData;
					var itemName = itemData.m_shared.m_name;
					var localizedName = GrabMaterials.Extensions.Localize(itemName).ToLower();
					var itemCategory = itemData.GetCategory();
					var itemCategoryString = itemCategory.ToString().ToLower();
					GrabMaterials.Extensions.ItemCategory searchCategory = GrabMaterials.Extensions.ItemCategory.None;
					var isCategorySearch = text != null ? Enum.TryParse(text, true, out searchCategory) : false;

					var matches = isNewSearch
						? (player != null && !player.IsMaterialKnown(itemName))
						: isCategorySearch
						? itemCategory == searchCategory
						: text == null || itemData.Name().Contains(text) || itemName.Contains(text) || localizedName.Contains(text) || itemCategoryString.Contains(text);
					if (!matches) continue;

					if (!byCategory.TryGetValue(itemCategory, out var byName))
					{
						byName = new SortedDictionary<string, int>();
						byCategory[itemCategory] = byName;
					}
					if (byName.ContainsKey(itemName)) byName[itemName] += kvp.Value;
					else byName[itemName] = kvp.Value;

					if (!inProcessByNameByLoc.TryGetValue(itemName, out var byLoc))
					{
						byLoc = new Dictionary<string, int>();
						inProcessByNameByLoc[itemName] = byLoc;
					}
					byLoc.TryGetValue(stationLabel, out var priorAtLoc);
					byLoc[stationLabel] = priorAtLoc + kvp.Value;

					if (!iconsByName.ContainsKey(itemName) && itemData.m_shared.m_icons != null && itemData.m_shared.m_icons.Length > 0)
					{
						iconsByName[itemName] = itemData.m_shared.m_icons[0];
					}

					if (!alreadyHighlighted)
					{
						GrabMaterials.Processors.Highlight(smelter);
						alreadyHighlighted = true;
					}
				}
			}

			if (byCategory.Count == 0)
			{
				var emptyMsg = string.IsNullOrEmpty(text) ? "No items in nearby containers"
					: isNewSearch ? "Nothing here you have not held before"
					: $"No items match '{text}'";
				Player.m_localPlayer.Message(MessageHud.MessageType.Center, emptyMsg);
				return;
			}

			// "(new)" marker for anything this character has never had in hand.  Valheim tracks that
			// set itself and fills it the first time an item enters your inventory.
			var markUndiscovered = (Instance?.PanelMarkUndiscovered?.Value ?? true) && player != null && !isNewSearch;

			// Iterate enum values in declaration order so the panel displays a stable, gameplay-grouped order.
			var groups = new List<GrabMaterials.MaterialsPanel.InventoryGroup>();
			foreach (GrabMaterials.Extensions.ItemCategory cat in Enum.GetValues(typeof(GrabMaterials.Extensions.ItemCategory)))
			{
				if (cat == GrabMaterials.Extensions.ItemCategory.None) continue;
				if (!byCategory.TryGetValue(cat, out var byName)) continue;

				var items = new List<GrabMaterials.MaterialsPanel.InventoryItem>(byName.Count);
				foreach (var kvp in byName)
				{
					var localizedName = GrabMaterials.Extensions.Localize(kvp.Key);
					var isNew = markUndiscovered && !player.IsMaterialKnown(kvp.Key);
					Log.LogInfo($"{kvp.Value} {localizedName} [{cat}]{(isNew ? " (new, never held)" : "")}");
					iconsByName.TryGetValue(kvp.Key, out var icon);

					// Sum per-station tallies into a total + single label. If the item
					// sits in only one type of station ("kiln"), use that; if it's split
					// across multiple types, fall back to the generic "processing".
					var inProcess = 0;
					string locLabel = null;
					if (inProcessByNameByLoc.TryGetValue(kvp.Key, out var byLoc))
					{
						foreach (var entry in byLoc) inProcess += entry.Value;
						if (byLoc.Count == 1)
						{
							foreach (var entry in byLoc) { locLabel = entry.Key; break; }
						}
						else
						{
							locLabel = "processing";
						}
					}

					items.Add(new GrabMaterials.MaterialsPanel.InventoryItem { Name = localizedName, Count = kvp.Value, InProcess = inProcess, InProcessLocation = locLabel, Icon = icon, SharedName = kvp.Key, Undiscovered = isNew });
				}
				groups.Add(new GrabMaterials.MaterialsPanel.InventoryGroup
				{
					CategoryName = FormatCategoryName(cat),
					Items = items,
				});
			}

				var title = string.IsNullOrEmpty(text) ? "Inventory"
				: isNewSearch ? "New Items"
				: $"Inventory: {text}";
			GrabMaterials.MaterialsPanel.ShowCategorizedInventory(title, groups, Instance.InventoryStyle.Value);
		}

		// "/i empty" — find every nearby container holding nothing, highlight them all, and
		// list them by type with a count.  This asks about the containers themselves rather
		// than their contents, so it does not walk items or processors at all.
		//
		// No access check: Container.CheckAccess is public in the publicized build but private
		// at runtime, so calling it would throw MethodAccessException.  Listing a chest you
		// cannot open matches what the rest of /i already does anyway.
		private static void ListEmptyContainers(List<Container> nearbyContainers)
		{
			Log.LogInfo($"checking {nearbyContainers.Count} nearby containers for empty ones");

			var byName = new SortedDictionary<string, int>();
			var total = 0;

			foreach (var container in nearbyContainers)
			{
				if (container == null) continue;
				var inventory = container.GetInventory();
				if (inventory == null || inventory.NrOfItems() > 0) continue;

				var name = GrabMaterials.Extensions.Localize(container.m_name);
				if (string.IsNullOrEmpty(name)) name = container.name;
				if (byName.ContainsKey(name)) byName[name]++;
				else byName[name] = 1;
				total++;

				container.Highlight();
			}

			if (total == 0)
			{
				if (Player.m_localPlayer != null)
					Player.m_localPlayer.Message(MessageHud.MessageType.Center, "No empty containers nearby");
				return;
			}

			// One row per container type.  No SharedName: these rows are containers, not items,
			// so there is nothing for click-to-highlight to match against, and the panel leaves
			// a row without one unclickable.
			var items = new List<GrabMaterials.MaterialsPanel.InventoryItem>(byName.Count);
			foreach (var kvp in byName)
			{
				Log.LogInfo($"{kvp.Value} empty {kvp.Key}");
				items.Add(new GrabMaterials.MaterialsPanel.InventoryItem { Name = kvp.Key, Count = kvp.Value });
			}

			var groups = new List<GrabMaterials.MaterialsPanel.InventoryGroup>
			{
				new GrabMaterials.MaterialsPanel.InventoryGroup { CategoryName = "Empty Containers", Items = items },
			};
			GrabMaterials.MaterialsPanel.ShowCategorizedInventory("Empty Containers", groups, Instance.InventoryStyle.Value);
		}

		// Format a BepInEx KeyboardShortcut as e.g. "Shift + G" / "Ctrl + Alt + Y" / "G".
		// BepInEx's default ToString() outputs "G + LeftShift" which reads awkwardly;
		// this trims the Left/Right side prefix and renames Control->Ctrl.
		internal static string FormatShortcut(KeyboardShortcut shortcut)
		{
			var sb = new StringBuilder();
			foreach (var mod in shortcut.Modifiers)
			{
				var name = mod.ToString();
				if (name.StartsWith("Left")) name = name.Substring(4);
				else if (name.StartsWith("Right")) name = name.Substring(5);
				if (name == "Control") name = "Ctrl";
				sb.Append(name).Append(" + ");
			}
			sb.Append(shortcut.MainKey.ToString());
			return sb.ToString();
		}

		private static string FormatDelta(GrabPackConfig pack)
		{
			var resolved = GrabMaterials.ConsoleCommands.ResolveDelta(pack.GrabDelta.Value);
			switch (pack.GrabDelta.Value)
			{
				case DeltaSetting.On:  return "On";
				case DeltaSetting.Off: return "Off";
				default:               return resolved ? "On (default)" : "Off (default)";
			}
		}

		private static void ListGrabPacks()
		{
			Log.LogInfo($"listing {Instance.GrabPacks.Length} configured grab packs");
			var rows = new List<GrabMaterials.MaterialsPanel.PackRow>(Instance.GrabPacks.Length);
			foreach (var grabPack in Instance.GrabPacks)
			{
				Log.LogInfo($"{grabPack.Name.Value} ({grabPack.Key.Value}): {grabPack.Items.Value}");
				var packRef = grabPack;  // capture for closure
				var hasItems = !string.IsNullOrEmpty(grabPack.Items.Value);
				rows.Add(new GrabMaterials.MaterialsPanel.PackRow
				{
					Name = grabPack.Name.Value,
					Hotkey = FormatShortcut(grabPack.Key.Value),
					Items = grabPack.Items.Value,
					Delta = FormatDelta(grabPack),
					OnClick = hasItems ? (System.Action)(() => GrabMaterials.ConsoleCommands.GrabMaterialsForPack(packRef)) : null,
					OnEdit = () => OpenPackEditor(packRef),
				});
			}
			GrabMaterials.MaterialsPanel.ShowPacks("Grab Packs", rows);
		}

		private static void OpenPackEditor(GrabPackConfig pack)
		{
			GrabMaterials.MaterialsPanel.ShowPackEditor(
				title: $"Edit: {pack.Name.Value}",
				initialName: pack.Name.Value,
				initialItems: pack.Items.Value,
				initialDelta: pack.GrabDelta.Value,
				onSave: (newName, newItems, newDelta) =>
				{
					pack.Name.Value = newName;
					pack.Items.Value = newItems;
					pack.GrabDelta.Value = newDelta;
					ListGrabPacks();  // back to the list, with updated values
				},
				onCancel: () => ListGrabPacks());
		}

		private static void FindContainersWithMatchingItems(Terminal.ConsoleEventArgs args)
		{
			if (args.Length <= 1)
			{
				var msg = "Please specify the text you want to search for in nearby containers";
				Chat.instance.SendMessage(msg);
				Log.LogInfo(msg);
				return;
			}

			var radius = 50f; // Default radius
			var text = args[1];

			var nearbyContainers = Boxes.GetNearbyContainers(radius);
			Log.LogInfo($"searching for {text} in {nearbyContainers.Count} containers within {radius} meters");
			foreach (var container in nearbyContainers)
			{
				var inventory = container.GetInventory();
				var items = inventory.GetAllItems();
				foreach (var item in items)
				{
					if (item.Name().Contains(text))
					{
						Log.LogInfo($"it contains {text}!");
						container.Highlight();
					}
				}
				if (inventory.ContainsItemByName(text))
				{
					Log.LogInfo($"it contains {text}!");
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

			Log.LogInfo($"storing {itemsToMove.Count()} items in {nearbyContainers.Count} containers within {radius} meters");

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
				Log.LogInfo($"{count} {itemName} in inventory");
			}
			else
			{
				count = playerInventory.CountItems();
				Log.LogInfo($"{count} items in inventory");
			}
		}
	}
}