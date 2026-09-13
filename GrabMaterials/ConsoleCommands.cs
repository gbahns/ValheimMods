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
using static GrabMaterialsMod.GrabMaterialsMod;

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

		// Global delta default — read live from config so toggling the "Grab Delta (default)"
		// setting takes effect without restart.
		internal static bool GlobalDelta => GrabMaterialsMod.GrabMaterialsMod.Instance?.GrabDeltaGlobal?.Value ?? true;

		// Resolve a per-pack tri-state against the global default.
		internal static bool ResolveDelta(GrabMaterialsMod.GrabMaterialsMod.DeltaSetting setting)
		{
			switch (setting)
			{
				case GrabMaterialsMod.GrabMaterialsMod.DeltaSetting.On: return true;
				case GrabMaterialsMod.GrabMaterialsMod.DeltaSetting.Off: return false;
				default: return GlobalDelta;
			}
		}

		// Pending ledger — tracks recently-grabbed amounts so back-to-back delta
		// grabs don't double-count the same inventory across different build plans.
		// Cleared on a TTL: if you don't follow up within the configured timeout,
		// the ledger forgets prior grabs (matches the player's own memory).
		// Keyed by the request name (the unsuffixed item name).
		private static readonly Dictionary<string, int> _pendingByName =
			new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		private static float _lastGrabTime;

		private static float LedgerTimeoutSeconds =>
			GrabMaterialsMod.GrabMaterialsMod.Instance?.GrabDeltaLedgerTimeout?.Value ?? 30f;

		internal static void ResetPendingLedger()
		{
			_pendingByName.Clear();
			_lastGrabTime = 0f;
		}

		private static void MaybeExpirePendingLedger()
		{
			if (_pendingByName.Count == 0) return;
			if (Time.time - _lastGrabTime > LedgerTimeoutSeconds) _pendingByName.Clear();
		}

		private static int GetPending(string name)
		{
			return _pendingByName.TryGetValue(name, out var v) ? v : 0;
		}

		private static void AddPending(string name, int amount)
		{
			if (amount <= 0) return;
			_pendingByName[name] = GetPending(name) + amount;
		}

		// "Run-again override": when the first grab of a request reports a shortage,
		// the request is remembered. A repeat of the *same* request (same label, or
		// same item list if there's no label) within OverrideTimeoutSeconds skips the
		// atomic-abort and grabs whatever's available. Lets the player partial-grab a
		// pack and head out to chop the missing finewood instead of grinding to a halt.
		private static string _lastFailedRequestKey;
		private static float _lastFailedRequestTime;
		private const float OverrideTimeoutSeconds = 30f;

		internal static void ClearOverrideState()
		{
			_lastFailedRequestKey = null;
			_lastFailedRequestTime = 0f;
		}

		private static string ComputeRequestKey(string requestLabel, List<ItemToGrab> aggregated)
		{
			if (!string.IsNullOrEmpty(requestLabel)) return "label:" + requestLabel.ToLowerInvariant();
			var sb = new StringBuilder("items:");
			foreach (var item in aggregated)
			{
				sb.Append(item.Name.ToLowerInvariant()).Append(':').Append(item.Count).Append(',');
			}
			return sb.ToString();
		}

		private static bool IsOverrideRequest(string key)
		{
			return _lastFailedRequestKey != null
				&& _lastFailedRequestKey == key
				&& Time.time - _lastFailedRequestTime <= OverrideTimeoutSeconds;
		}

		public static void GrabMaterialsForPiece(this Terminal.ConsoleEventArgs args)
		{
			Log.LogInfo($"GrabMaterialsForPiece({args.FullLine})");

			if (args.Length <= 1)
			{
				var msg = "usage: /grabpiece  <name>, e.g. /grab workbench";
				Chat.instance.SendMessage(msg);
				Log.LogInfo(msg);
				return;
			}

			var name = args[1];
			Log.LogInfo($"name of piece to grab materials for: {name}");

			//GrabMaterialsForPiece($"$item_{name}");
			GrabMaterialsForPiece(name);
		}

		public static void GrabMaterialsForPack(GrabMaterialsMod.GrabMaterialsMod.GrabPackConfig grabPack)
		{
			GrabMaterialsForPack(grabPack.Name.Value, grabPack.Items.Value, ResolveDelta(grabPack.GrabDelta.Value));
		}

		public static void GrabMaterialsForPack(string packName, string itemsString, bool grabDelta = false)
		{
			Log.LogInfo($"GrabMaterialsForPack({packName}, {itemsString})");
			var itemsToGrab = new List<ItemToGrab>();
			var entries = itemsString.Replace(" ", "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
			foreach (var entry in entries)
			{
				var parts = entry.Split(new[] { ':' }, StringSplitOptions.RemoveEmptyEntries);
				if (parts.Length > 2)
				{
					Log.LogError($"Invalid format for {packName} item list: was {itemsString}, expected 'item[:quantity],item[:quantity],...'");
					return;
				}
				var item = parts[0];
				var amount = 1;
				if (parts.Length == 2)
				{
					if (!int.TryParse(parts[1], out amount))
					{
						Log.LogError($"Invalid format for {packName} item list: was {itemsString}, expected 'item[:quantity],item[:quantity],...'");
						return;
					}
				}
				Log.LogInfo($"grabbing {amount} {item}");
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
		// "/g new" — one of every item in range this character has never held.  Holding an
		// item is what teaches you the recipes that use it, so this is mainly a way to learn
		// a batch of recipes from shared storage in one go.  Containers only: you cannot take
		// from a smelter, so the processors that "/i new" lists are not considered here.
		public static void GrabUndiscoveredItems(float radius = 50f)
		{
			var player = Player.m_localPlayer;
			if (player == null) return;

			var seen = new HashSet<string>();
			var itemsToGrab = new List<ItemToGrab>();
			foreach (var container in Boxes.GetNearbyContainers(radius))
			{
				var inventory = container.GetInventory();
				if (inventory == null) continue;
				foreach (var item in inventory.GetAllItems())
				{
					var sharedName = item.m_shared.m_name;
					if (player.IsMaterialKnown(sharedName)) continue;
					if (!seen.Add(sharedName)) continue;
					itemsToGrab.Add(new ItemToGrab(item.Name(), 1));
				}
			}

			if (itemsToGrab.Count == 0)
			{
				player.Message(MessageHud.MessageType.Center, "Nothing here you have not held before");
				return;
			}

			Log.LogInfo($"grabbing one each of {itemsToGrab.Count} never-held items");
			GrabItemsFromNearbyContainers(itemsToGrab, radius, "New Items");
		}

		public static void GrabItemsFromNearbyContainers(this Terminal.ConsoleEventArgs args)
		{
			Log.LogInfo($"GrabItemsFromNearbyContainers('{args.FullLine})' args.Length={args.Length}");

			if (args.Length > 1)
			{
				if (args[1] == "pack")
				{
					int packNumber = int.Parse(args[2]) - 1;
					GrabMaterialsForPack(GrabMaterialsMod.GrabMaterialsMod.Instance.GrabPacks[packNumber]);
					return;
				}

				if (args.Length == 2 && string.Equals(args[1], "new", StringComparison.OrdinalIgnoreCase))
				{
					GrabUndiscoveredItems();
					return;
				}

				foreach (var pack in GrabMaterialsMod.GrabMaterialsMod.Instance.GrabPacks)
				{
					var packName = args.ArgsAll;
					Log.LogInfo($"Checking pack {pack.Name.Value} against args.ArgsAll='{packName}'");
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
				Log.LogInfo($"count={count}, n={n}, nameStartingArg={nameStartingArg}, nameEndingArg={nameEndingArg}");

				var sb = new StringBuilder();
				for (int i = nameStartingArg; i <= nameEndingArg; i++)
				{
					if (sb.Length > 0) sb.Append(" "); // Add a space between words
					sb.Append(args[i]);
				}
				var name = sb.ToString();

				if (name != "")
				{
					Log.LogInfo($"grabbing {count} '{name}'");
					GrabItemsFromNearbyContainers(name, count);
					return;
				}
			}

			var msg = "usage: /grab <all | name> [count], e.g. /grab 10 wood";
			Chat.instance.SendMessage(msg);
			Log.LogInfo(msg);
		}

		public static Piece.Requirement[] GetPieceRequirements(string pieceName)
		{
			Log.LogInfo($"GetPieceRequirements({pieceName})");
			if (!ZNetScene.instance)
			{
				Log.LogWarning("Cannot look for prefab: ZNetScene.instance is null");
				return null;
			}
			Log.LogInfo("looking for prefab...");
			Log.LogInfo($"ZNetScene.instance has {ZNetScene.instance.m_prefabs.Count} prefabs");
			var prefab = ZNetScene.instance.m_prefabs.Find(_prefab => _prefab.name == pieceName);
			if (prefab == null)
			{
				Log.LogError($"No prefab found for {pieceName}");
				return null;
			}
			//var requirements = 
			//prefab.gameObject.GetComponent<Piece>().m_resources.ToList().ForEach(requirement =>
			//{
			//	Log.LogInfo($"{requirement.m_amount} {requirement.m_resItem.m_itemData.m_shared.m_name}");
			//});
			Log.LogInfo($"Prefab found: {prefab.name} {prefab.gameObject.name} {prefab.gameObject.tag} {prefab.gameObject.GetComponent<Piece>().m_name}");
			return prefab.gameObject.GetComponent<Piece>().m_resources;


			//var prefabManager = PrefabManager.Instance;
			//var prefab = prefabManager.GetPrefab(pieceName);
			//if (prefab == null)
			//{
			//	Log.LogError($"No prefab found for {pieceName}");
			//	return null;
			//}

			//var piece = prefab.GetComponent<Piece>();
			//if (piece == null)
			//{
			//	Log.LogError($"No Piece component found on prefab for {pieceName}");
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
					Log.LogInfo($"Grabbing for {pieceName}: {requirement.m_amount} {requirement.m_resItem.m_itemData.m_shared.m_name}");
					itemsToGrab.Add(new ItemToGrab(requirement.m_resItem.m_itemData.m_shared.m_name, requirement.m_amount));
				}
				GrabItemsFromNearbyContainers(itemsToGrab, 50f, pieceName, GlobalDelta);
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
			//	Log.LogInfo("No local player found");
			//	return;
			//}
			//foreach (var recipe in player.m_knownRecipes)
			//{
			//	Log.LogInfo($"{recipe}");
			//}


			//var prefabManager = PrefabManager.Instance;
			////prefabManager.Ge
			//var prefab = prefabManager.GetPrefab(pieceName);
			//if (prefab == null)
			//{
			//	var msg = $"No prefab found for {pieceName}";
			//	Log.LogInfo(msg);
			//	pieceName = $"$item_{pieceName}";
			//	prefab = prefabManager.GetPrefab(pieceName);
			//	if (prefab == null)
			//	{
			//		msg = $"No recipe found for {pieceName}";
			//		Log.LogInfo(msg);
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
			//	Log.LogInfo(msg);
			//	pieceName = $"$item_{pieceName}";
			//	recipe = ItemManager.Instance.GetRecipe(pieceName);
			//	if (recipe == null)
			//	{
			//		msg = $"No recipe found for {pieceName}";
			//		Log.LogInfo(msg);
			//		return;
			//	}
			//}
			//var resources = recipe.Recipe.m_resources;
			//if (resources == null)
			//{
			//	var msg = $"No resources found for {pieceName}";
			//	Log.LogInfo(msg);
			//	Chat.instance.SendMessage(msg);
			//	return;
			//}
			//foreach (var requirement in resources)
			//{
			//	Log.LogInfo($"{requirement.m_amount} {requirement.m_resItem.m_itemData.Name()}");
			//}

			//var player = Player.m_localPlayer;
			//Piece piece = null;
			//= player.GetPiece(pieceName);
			//Piece.s_allPieces.Find(Piece)
			//if (!piece)
			//{
			//	var msg = $"No build piece named {pieceName} selected";
			//	Log.LogInfo(msg);
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
			// original Needed in the panel). Reservations from recent grabs are
			// subtracted too — see the pending-ledger comment up top.
			if (grabDelta) MaybeExpirePendingLedger();
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
					// Reserve out items already grabbed for in-progress builds.
					var reserved = GetPending(aggregated[i].Name);
					if (reserved > 0) had[i] = Math.Max(0, had[i] - reserved);
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

			// Run-again override: if the player repeats a request that previously
			// failed on shortages, fall through to a partial grab instead of aborting.
			var requestKey = ComputeRequestKey(requestLabel, aggregated);
			var isOverride = anyShort && IsOverrideRequest(requestKey);

			if (anyShort && !isOverride)
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
				Log.LogInfo($"Cannot grab{(string.IsNullOrEmpty(requestLabel) ? "" : $" for {requestLabel}")} - missing: {string.Join(", ", debugShortages)}");
				var failTitle = string.IsNullOrEmpty(requestLabel)
					? "Missing materials — run again to grab what's available"
					: $"Missing materials for {requestLabel} — run again to grab what's available";
				MaterialsPanel.Show(failTitle, statuses);
				// Remember the request so a repeat within the window triggers the override.
				_lastFailedRequestKey = requestKey;
				_lastFailedRequestTime = Time.time;
				// Failed grab still indicates the player is actively building —
				// keep the ledger alive so the next grab inherits the window.
				if (grabDelta) _lastGrabTime = Time.time;
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
					Log.LogInfo($"grabbing {remaining} {itemToGrab.Name} from {nearbyContainers.Count} containers within {radius} meters");
					for (int j = 0; j < nearbyContainers.Count && remaining > 0; j++)
					{
						int countGrabbed = nearbyContainers[j].GrabItemFromContainer(itemToGrab.Name, remaining);
						remaining -= countGrabbed;
					}
				}
				var grabbedAmount = effectiveNeed[i] - remaining;
				if (grabDelta) AddPending(itemToGrab.Name, grabbedAmount);
				grabbed.Add(new MaterialsPanel.ItemStatus
				{
					Name = LocalizeItemName(itemToGrab),
					Needed = itemToGrab.Count,
					Had = had[i],
					Available = grabbedAmount,
				});
			}
			if (grabDelta) _lastGrabTime = Time.time;
			// Grab succeeded (full or partial-via-override) — clear the run-again state
			// so the next attempt of the same request starts fresh.
			ClearOverrideState();
			string successTitle;
			if (allCovered)
			{
				successTitle = string.IsNullOrEmpty(requestLabel) ? "Already have everything" : $"Already have everything for {requestLabel}";
				Log.LogInfo(successTitle);
			}
			else if (isOverride)
			{
				successTitle = string.IsNullOrEmpty(requestLabel) ? "Partial grab" : $"Partial grab for {requestLabel}";
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
				var translated = Localization.instance.Localize(piece.m_name);
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
				Log.LogInfo(msg);
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
					Log.LogWarning($"GrabMaterials: cannot reflect Hud.m_pieceIcons - {e.Message}");
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
			Log.LogInfo($"grabbing materials for selected piece {piece.name} - requires {resources.Count()} resources");
			if (resources != null)
			{
				List<ItemToGrab> itemsToGrab = new List<ItemToGrab>();
				foreach (var requirement in resources)
				{
					Log.LogInfo($"{requirement.m_amount} {requirement.m_resItem.m_itemData.Name()}");
					itemsToGrab.Add(new ItemToGrab(requirement.m_resItem.m_itemData.Name(), requirement.m_amount));
					//GrabItemsFromNearbyContainers(requirement.m_resItem.m_itemData.m_shared.m_name, requirement.m_amount);
				}
				GrabItemsFromNearbyContainers(itemsToGrab, 10f, LocalizePieceName(piece), GlobalDelta);
			}
		}

		private static void BuildPieceLookUp()
		{
			// ObjectDB contains all prefabs, including pieces.
			if (ObjectDB.instance == null) return;

			if (!ZNetScene.instance)
			{
				Log.LogWarning("Cannot index: ZNetScene.instance is null");
				return;
			}

			Log.LogInfo("building lookup table for all build pieces");
			foreach (var prefab in ZNetScene.instance.m_prefabs)
			{
				if (prefab.TryGetComponent<Piece>(out var piece))
				{
					if (prefab.name.ContainsAny("loot_chest", "TreasureChest"))
					{
						//Log.LogInfo($"Not adding {prefab.name} to build piece lookup table");
						continue;
					}

					// Get the localized, user-facing name (e.g., "Campfire")
					var localizedName = "";
					try
					{
						if (piece.m_name.StartsWith("$"))
						{ // if the name starts with $, it is a localization key
							localizedName = Localization.instance.Localize(piece.m_name);
						}
						else
						{
							localizedName = piece.m_name;
						}
					}
					catch (Exception e)
					{
						Log.LogError($"Error translating piece name {piece.m_name}: {e.Message}");
						localizedName = piece.m_name;
					}
					//Log.LogInfo($"{prefab.name} \"{localizedName}\" {GetPieceResourceList(piece)}");
					pieceLookup[localizedName.ToLowerInvariant()] = piece;
					//Log.LogInfo($"Piece: {piece.name} ({prefab.name})");
				}
				else
				{
					//Log.LogInfo($"Prefab {prefab.name} does not have a Piece component");
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
				//Log.LogInfo($"added item to list: {prefabName}");
			}

			Jotunn.Logger.LogInfo($"Built a lookup table with {itemLookup.Count} items.");
		}

		// "wood" -> "$item_wood", case-insensitive, built from ObjectDB so user-typed casing does not matter.
		private static Dictionary<string, string> sharedNameLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		private static void BuildSharedNameLookup()
		{
			if (ObjectDB.instance == null) return;
			foreach (var itemPrefab in ObjectDB.instance.m_items)
			{
				var drop = itemPrefab != null ? itemPrefab.GetComponent<ItemDrop>() : null;
				var shared = drop?.m_itemData?.m_shared?.m_name;
				if (string.IsNullOrEmpty(shared) || !shared.StartsWith("$item_")) continue;
				sharedNameLookup[shared.Substring(6)] = shared;
			}
		}

		// True when every material a pack resolves to has been held by the player at least once
		// (Valheim's "known material" set).  Entries that cannot be resolved to a piece or item
		// never hide a pack.  Quiet on purpose: the pack HUD calls this a few times a second.
		internal static bool AreAllPackMaterialsKnown(string itemsString, Player player)
		{
			if (player == null || string.IsNullOrWhiteSpace(itemsString)) return true;
			if (ObjectDB.instance == null || !ZNetScene.instance) return true;
			if (sharedNameLookup.Count == 0) BuildSharedNameLookup();
			if (pieceLookup.Count == 0) BuildPieceLookUp();

			var entries = itemsString.Replace(" ", "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
			foreach (var entry in entries)
			{
				var name = entry.Split(':')[0];
				if (pieceLookup.TryGetValue(name.ToLowerInvariant(), out var piece))
				{
					if (piece.m_resources == null) continue;
					foreach (var req in piece.m_resources)
					{
						var shared = req.m_resItem?.m_itemData?.m_shared?.m_name;
						if (!string.IsNullOrEmpty(shared) && !player.IsMaterialKnown(shared)) return false;
					}
				}
				else if (sharedNameLookup.TryGetValue(name.Replace("$item_", ""), out var sharedName))
				{
					if (!player.IsMaterialKnown(sharedName)) return false;
				}
			}
			return true;
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
				Log.LogInfo($"Found piece {name} in lookup table, grabbing materials for it");
				var resources = piece.m_resources;
				if (resources != null)
				{
					foreach (var requirement in resources)
					{
						Log.LogInfo($"{requirement.m_amount} {requirement.m_resItem.m_itemData.Name()}");
						itemsToGrab.Add(new ItemToGrab(requirement.m_resItem.m_itemData.Name(), requirement.m_amount * count));
					}
				}
			}
			else
			{
				Log.LogInfo($"No piece found for {name}, looking for material by this name instead");
				itemsToGrab.Add(new ItemToGrab(name, count));
			}
			return itemsToGrab;
		}

		public static void GrabItemsFromNearbyContainers(string name, int count = 1)
		{
			var radius = 50f; // Default radius
			var itemsToGrab = GetItemsToGrab(name, count);
			// GetItemsToGrab populates pieceLookup on first use; check after.
			var isPiece = pieceLookup.ContainsKey(name.ToLowerInvariant());
			var label = isPiece ? name : null;
			// Apply delta only when we're grabbing a piece's recipe (e.g. /g cart).
			// A bare /g wood request is literal — the user asked for that count.
			GrabItemsFromNearbyContainers(itemsToGrab, radius, label, isPiece && GlobalDelta);
		}
	}
}
