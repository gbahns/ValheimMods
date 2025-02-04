using System.Collections.Generic;
using System.Linq;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace GrabMaterials
{
    [HarmonyPatch(typeof(Chat), "SendText")]
    public class HookChatInputText
    {
        private const float SearchRadius = 50f;

        static void SearchNearbyContainersFor(string query)
        {
            foreach (Piece piece in GetNearbyMatchingPieces(query))
            {
                HighlightPiece(piece);
            }
        }

        static IEnumerable<Piece> GetNearbyMatchingPieces(string query)
        {
            List<Piece> pieces = new List<Piece>();

            Piece.GetAllPiecesInRadius(
                Player.m_localPlayer.transform.position,
                SearchRadius,
                pieces
            );

            return pieces
                .Where(p => p.GetComponent<Container>())
                .Where(p => ContainerContainsMatchingItem(p, query));
        }

        static bool ContainerContainsMatchingItem(Piece container, string query)
        {
            return container
                .GetComponent<Container>()
                .GetInventory()
                .GetAllItems()
                .Where(i => NormalizedItemName(i).Contains(query))
                .Any();
        }

        static string NormalizedItemName(ItemDrop.ItemData itemData)
        {
            return itemData.m_shared.m_name.ToLower();
        }

        static void HighlightPiece(Piece p)
        {
            WearNTear component = p.GetComponent<WearNTear>();

            if (component)
            {
                component.Highlight();
            }
        }
    }
}
