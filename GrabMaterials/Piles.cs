using System.Collections.Generic;
using UnityEngine;

namespace GrabMaterials
{
	// Stacks and piles: wood_stack, stone_pile, coal_pile, blackmarble_pile and the like.  They
	// are decorative build pieces whose single resource comes back in full when they are torn
	// down (Piece.DropResources returns every recoverable requirement), so a wood stack is 50
	// wood sitting on the plot as surely as a chest holding 50 wood is.  They are plain Pieces
	// rather than containers, so they are found through Valheim's own piece registry instead
	// of the container tracking in Boxes.
	internal static class Piles
	{
		internal struct Pile
		{
			public Piece Piece;
			public ItemDrop.ItemData Item;
			public int Amount;
			public string Kind;   // "stack" or "pile", from the prefab name, for the panel's label
		}

		// A stack or pile is a placed piece named *_stack or *_pile whose one requirement is
		// recoverable.  Prefab name rather than a fixed list, so mod-added piles count too.
		internal static bool TryGet(Piece piece, out Pile pile)
		{
			pile = default;
			if (piece == null || piece.m_resources == null || piece.m_resources.Length != 1) return false;
			var name = Utils.GetPrefabName(piece.gameObject.name).ToLowerInvariant();
			string kind;
			if (name.EndsWith("_stack")) kind = "stack";
			else if (name.EndsWith("_pile")) kind = "pile";
			else return false;
			var req = piece.m_resources[0];
			if (req.m_resItem == null || !req.m_recover || req.m_amount <= 0) return false;
			// A placed piece only: not the ghost on the hammer, not the prefab itself.
			var nview = piece.GetComponent<ZNetView>();
			if (nview == null || !nview.IsValid()) return false;
			pile = new Pile { Piece = piece, Item = req.m_resItem.m_itemData, Amount = req.m_amount, Kind = kind };
			return true;
		}

		internal static List<Pile> GetNearby(float radius)
		{
			var result = new List<Pile>();
			if (!Player.m_localPlayer) return result;
			var pieces = new List<Piece>();
			Piece.GetAllPiecesInRadius(Player.m_localPlayer.transform.position, radius, pieces);
			foreach (var piece in pieces)
				if (TryGet(piece, out var pile)) result.Add(pile);
			return result;
		}

		// Same flash a container or smelter gets.
		internal static void Highlight(Piece piece)
		{
			piece?.GetComponent<WearNTear>()?.Highlight();
		}
	}
}
