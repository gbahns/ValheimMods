using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace Armory
{
    /// <summary>
    /// Registers the Armory Rack as a buildable Hammer piece.
    /// Phase 1 (OnVanillaPrefabsAvailable): clone piece_chest_wood, strip Container, attach ArmoryRack.
    /// Phase 2 (OnPiecesRegistered): set requirements, WearNTear, icon, and register with Jotunn.
    /// </summary>
    internal static class ArmoryPieces
    {
        private const string PieceName  = "armory_rack";
        private const string BasePrefab = "piece_chest_wood";
        private const string PieceTable = "_HammerPieceTable";
        private const string Category   = "Crafting";

        private static GameObject _clone;
        private static bool _clonesCreated;
        private static bool _piecesConfigured;

        private static readonly FieldInfo _namedPrefabsField =
            AccessTools.Field(typeof(ZNetScene), "m_namedPrefabs");

        private static Dictionary<int, GameObject> GetNamedPrefabs() =>
            ZNetScene.instance != null
                ? _namedPrefabsField?.GetValue(ZNetScene.instance) as Dictionary<int, GameObject>
                : null;

        // ── Phase 1: OnVanillaPrefabsAvailable ─────────────────────────────────────
        internal static void CreatePiece()
        {
            if (_clonesCreated) return;
            _clonesCreated = true;

            var basePrefab = PrefabManager.Instance.GetPrefab(BasePrefab);
            if (basePrefab == null)
            {
                Jotunn.Logger.LogError("[Armory] Base prefab 'piece_chest_wood' not found.");
                return;
            }

            _clone = PrefabManager.Instance.CreateClonedPrefab(PieceName, basePrefab);

            // Register in ZNetScene immediately so world ZDOs can resolve this prefab on load.
            var namedPrefabs = GetNamedPrefabs();
            if (namedPrefabs != null)
                namedPrefabs[_clone.name.GetStableHashCode()] = _clone;

            // Strip the vanilla Container — it implements Interactable and would intercept [Use].
            var container = _clone.GetComponent<Container>();
            if (container != null)
                Object.DestroyImmediate(container);

            _clone.AddComponent<ArmoryRack>();

            var piece = _clone.GetComponent<Piece>() ?? _clone.AddComponent<Piece>();
            piece.m_name        = "$armory_rack_name";
            piece.m_description = "$armory_rack_desc";
            piece.m_groundOnly  = true;
            piece.m_resources   = new Piece.Requirement[0];

            Jotunn.Logger.LogInfo("[Armory] Phase 1 — cloned armory_rack");
        }

        // ── Phase 2: OnPiecesRegistered ─────────────────────────────────────────────
        internal static void ConfigureAndRegister()
        {
            if (_piecesConfigured) return;
            _piecesConfigured = true;

            if (_clone == null)
            {
                Jotunn.Logger.LogError("[Armory] Phase 2: no clone from Phase 1 — Armory Rack unavailable.");
                return;
            }

            var piece = _clone.GetComponent<Piece>();
            if (piece == null) return;

            // WearNTear — copy destruction/hit effects from the wood chest.
            var refWnT = PrefabManager.Instance.GetPrefab(BasePrefab)?.GetComponent<WearNTear>();
            var wnt    = _clone.GetComponent<WearNTear>() ?? _clone.AddComponent<WearNTear>();
            wnt.m_health       = 500f;
            wnt.m_materialType = WearNTear.MaterialType.Wood;
            if (refWnT != null)
            {
                wnt.m_destroyedEffect = refWnT.m_destroyedEffect;
                wnt.m_hitEffect       = refWnT.m_hitEffect;
            }

            // Build requirements: 10 FineWood + 4 IronNails at a Workbench.
            var requirements = new List<Piece.Requirement>();
            var fineWoodDrop  = ObjectDB.instance.GetItemPrefab("FineWood")?.GetComponent<ItemDrop>();
            var ironNailsDrop = ObjectDB.instance.GetItemPrefab("IronNails")?.GetComponent<ItemDrop>();
            if (fineWoodDrop  != null) requirements.Add(new Piece.Requirement { m_resItem = fineWoodDrop,  m_amount = 10, m_recover = true });
            if (ironNailsDrop != null) requirements.Add(new Piece.Requirement { m_resItem = ironNailsDrop, m_amount = 4,  m_recover = true });
            piece.m_resources = requirements.ToArray();

            // Crafting station.
            piece.m_craftingStation = PrefabManager.Instance.GetPrefab("piece_workbench")?.GetComponent<CraftingStation>();

            // Icon: use the trophy of the Valkyrie armor / a fitting item. Fall back to chest icon.
            var iconDrop = ObjectDB.instance.GetItemPrefab("ArmorLeatherChest")?.GetComponent<ItemDrop>()
                        ?? ObjectDB.instance.GetItemPrefab("ArmorBronzeChest")?.GetComponent<ItemDrop>();
            if (iconDrop?.m_itemData?.m_shared?.m_icons?.Length > 0)
                piece.m_icon = iconDrop.m_itemData.m_shared.m_icons[0];

            // Place effects.
            var refPiece = PrefabManager.Instance.GetPrefab(BasePrefab)?.GetComponent<Piece>();
            if (refPiece != null)
                piece.m_placeEffect = refPiece.m_placeEffect;

            var pieceConfig = new PieceConfig
            {
                Name       = "Armory Rack",
                PieceTable = PieceTable,
                Category   = Category,
            };
            PieceManager.Instance.AddPiece(new CustomPiece(_clone, false, pieceConfig));
            Jotunn.Logger.LogInfo("[Armory] Phase 2 — registered armory_rack");
        }
    }
}
