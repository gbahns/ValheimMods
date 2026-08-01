using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace GrabMaterials
{
	// Helpers for reading the contents of Smelter-derived processors (charcoal
	// kiln, smelter, blast furnace, windmill, spinning wheel, eitr refinery).
	// Items live in the Smelter's ZDO ("item0"..."itemN" for the input queue,
	// "fuel" as a float for fuel charge) rather than a Unity Inventory, so we
	// fish them out directly from the synced ZDO.
	internal static class Processors
	{
		// Smelter's m_nview / m_maxOre / m_fuelItem are compile-time public via
		// the publicized assembly but private in the runtime DLL Valheim ships,
		// so direct access throws FieldAccessException. AccessTools bypasses
		// that the same way ConsoleCommands does for Hud.m_pieceIcons.
		private static AccessTools.FieldRef<Smelter, ZNetView> _nviewAccessor;
		private static AccessTools.FieldRef<Smelter, int> _maxOreAccessor;
		private static AccessTools.FieldRef<Smelter, ItemDrop> _fuelItemAccessor;

		private static ZNetView GetNview(Smelter s)
		{
			if (_nviewAccessor == null) _nviewAccessor = AccessTools.FieldRefAccess<Smelter, ZNetView>("m_nview");
			return _nviewAccessor(s);
		}

		private static int GetMaxOre(Smelter s)
		{
			if (_maxOreAccessor == null) _maxOreAccessor = AccessTools.FieldRefAccess<Smelter, int>("m_maxOre");
			return _maxOreAccessor(s);
		}

		private static ItemDrop GetFuelItem(Smelter s)
		{
			if (_fuelItemAccessor == null) _fuelItemAccessor = AccessTools.FieldRefAccess<Smelter, ItemDrop>("m_fuelItem");
			return _fuelItemAccessor(s);
		}

		// Returns prefab-name -> count for everything currently inside this
		// smelter: queued inputs plus any fuel charge (in whole units of the
		// fuel item). Empty dict if the ZDO isn't ready.
		internal static Dictionary<string, int> GetQueuedItems(Smelter smelter)
		{
			var result = new Dictionary<string, int>();
			if (smelter == null) return result;
			var nview = GetNview(smelter);
			if (nview == null || !nview.IsValid()) return result;
			var zdo = nview.GetZDO();
			if (zdo == null) return result;

			// Input queue — Smelter stores each slot as ZDO string "itemN".
			var maxOre = GetMaxOre(smelter);
			for (int i = 0; i < maxOre; i++)
			{
				var ore = zdo.GetString("item" + i, "");
				if (string.IsNullOrEmpty(ore)) continue;
				result.TryGetValue(ore, out var n);
				result[ore] = n + 1;
			}

			// Fuel — stored as a float; floor to whole units of the fuel item
			// (a half-burnt log isn't grabbable anyway).
			var fuelItem = GetFuelItem(smelter);
			if (fuelItem != null)
			{
				var fuel = (int)zdo.GetFloat("fuel", 0f);
				if (fuel > 0)
				{
					var fuelPrefabName = fuelItem.gameObject.name;
					result.TryGetValue(fuelPrefabName, out var n);
					result[fuelPrefabName] = n + fuel;
				}
			}

			return result;
		}

		// Briefly flash the smelter's piece — same visual as Container.Highlight().
		internal static void Highlight(Smelter smelter)
		{
			if (smelter == null) return;
			smelter.GetComponent<WearNTear>()?.Highlight();
		}

		// User-facing label for a smelter — e.g. "kiln", "smelter", "blast furnace".
		// Derived from the Piece component's localized name so we get whatever
		// Valheim itself calls it, lowercased to read naturally inline ("3 in kiln").
		internal static string GetLabel(Smelter smelter)
		{
			if (smelter == null) return "processing";
			var piece = smelter.GetComponent<Piece>();
			if (piece == null || string.IsNullOrEmpty(piece.m_name)) return "processing";
			string label;
			try
			{
				label = piece.m_name.StartsWith("$")
					? Jotunn.Managers.LocalizationManager.Instance.TryTranslate(piece.m_name)
					: piece.m_name;
			}
			catch { label = piece.m_name; }
			if (string.IsNullOrEmpty(label)) return "processing";
			return label.ToLower();
		}
	}
}
