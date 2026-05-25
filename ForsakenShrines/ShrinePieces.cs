using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace ForsakenShrines
{
    internal static class ShrinePieces
    {
        private const string PieceTable = "_HammerPieceTable";
        private const string Category   = "Forsaken Shrines";

        // Cloned prefabs kept so EnsureInPieceTable can insert them directly when needed.
        private static readonly List<GameObject> _clones = new List<GameObject>();

        // ZNetScene.m_namedPrefabs is private; use AccessTools so runtime enforcement doesn't throw.
        private static readonly FieldInfo _namedPrefabsField =
            AccessTools.Field(typeof(ZNetScene), "m_namedPrefabs");

        private static Dictionary<int, GameObject> GetNamedPrefabs() =>
            ZNetScene.instance != null
                ? _namedPrefabsField?.GetValue(ZNetScene.instance) as Dictionary<int, GameObject>
                : null;

        private static bool _clonesCreated  = false;
        private static bool _piecesConfigured = false;

        // ── Phase 1: OnVanillaPrefabsAvailable ──────────────────────────────────────
        internal static void CreateClones()
        {
            if (_clonesCreated) return;
            _clonesCreated = true;

            foreach (var def in ShrineDefinitions.All)
            {
                var basePrefab = PrefabManager.Instance.GetPrefab(def.BasePrefab);
                if (basePrefab == null)
                    GetNamedPrefabs()?.TryGetValue(def.BasePrefab.GetStableHashCode(), out basePrefab);
                if (basePrefab == null)
                {
                    Jotunn.Logger.LogWarning($"[ForsakenShrines] Base prefab '{def.BasePrefab}' not found — skipping {def.PieceName}.");
                    continue;
                }

                var clone = PrefabManager.Instance.CreateClonedPrefab(def.PieceName, basePrefab);

                // Jotunn's batch that copies PrefabManager→ZNetScene runs before OnVanillaPrefabsAvailable.
                // Write directly to m_namedPrefabs now so world ZDOs can resolve this prefab on load.
                var namedPrefabs = GetNamedPrefabs();
                if (namedPrefabs != null)
                    namedPrefabs[clone.name.GetStableHashCode()] = clone;

                // No prefab-level lift: BossStone children stay at their natural local positions.
                // Vertical placement is handled by PlacementGhostHeightPatch + ShrineInteractable's
                // lack of a ground-snap.

                var interactable = clone.AddComponent<ShrineInteractable>();
                interactable.Definition = def;

                var piece = clone.GetComponent<Piece>() ?? clone.AddComponent<Piece>();
                piece.m_name        = def.DisplayName;
                piece.m_description = def.Description;
                // m_groundOnly causes Player.PlacePiece to snap the spawn position back to terrain
                // Y, overriding the lowered Y our ghost patch set.  Natural-terrain validation is
                // handled by ShrinePlacement.Prefix's IsNaturalTerrain check, so disabling this
                // doesn't lose the constraint — only the unwanted terrain snap.
                piece.m_groundOnly  = false;
                piece.m_groundPiece = false;
                piece.m_clipGround  = false;
                piece.m_clipEverything = false;
                piece.m_resources   = new Piece.Requirement[0];

                _clones.Add(clone);
                Jotunn.Logger.LogInfo($"[ForsakenShrines] Phase 1 — cloned: {def.PieceName}");
            }
        }

        // Per-world-load: insert clones into piece table and refresh unlock state.
        internal static void OnWorldLoad()
        {
            EnsureInPieceTable();
            UpdateUnlocks();
        }

        // ── Phase 2: OnPiecesRegistered ──────────────────────────────────────────────
        internal static void ConfigureAndRegister()
        {
            if (_piecesConfigured) return;
            _piecesConfigured = true;

            if (_clones.Count == 0)
            {
                Jotunn.Logger.LogWarning("[ForsakenShrines] Phase 2: no clones from Phase 1 — retrying.");
                _clonesCreated = false;
                CreateClones();
            }
            if (_clones.Count == 0)
            {
                Jotunn.Logger.LogError("[ForsakenShrines] Phase 2: clone creation failed — shrines unavailable.");
                return;
            }

            // Reference stone piece used to copy WearNTear effects and place effect.
            var stoneRef  = PrefabManager.Instance.GetPrefab("stone_wall_2x1");
            var refWnT    = stoneRef?.GetComponent<WearNTear>();
            var refPiece  = stoneRef?.GetComponent<Piece>();
            if (stoneRef == null)
                Jotunn.Logger.LogWarning("[ForsakenShrines] Phase 2: 'stone_wall_2x1' not found — WearNTear effects and place sound may be missing.");

            // Register the custom tab once; with fixReference=false Jotunn never writes
            // PieceConfig.Category back to piece.m_category, so we do it manually.
            var shrineCategory = PieceManager.Instance.AddPieceCategory(Category);

            foreach (var def in ShrineDefinitions.All)
            {
                var clone = _clones.Find(c => c != null && c.name == def.PieceName);
                if (clone == null)
                {
                    Jotunn.Logger.LogWarning($"[ForsakenShrines] Phase 2: clone '{def.PieceName}' not found.");
                    continue;
                }

                var piece = clone.GetComponent<Piece>();
                if (piece == null) continue;

                piece.m_category = shrineCategory;

                // ── Build requirements from config string ───────────────────────────
                piece.m_resources = ShrineConfig.BuildRequirements(def.PieceName);

                // ── Icon: use the boss trophy so each shrine shows a distinct image ──
                var iconPrefab = ObjectDB.instance.GetItemPrefab(def.IconItem);
                var iconDrop   = iconPrefab?.GetComponent<ItemDrop>();
                var icon       = iconDrop?.m_itemData?.m_shared?.m_icons?.Length > 0
                    ? iconDrop.m_itemData.m_shared.m_icons[0]
                    : null;
                if (icon != null)
                    piece.m_icon = icon;
                else
                    Jotunn.Logger.LogWarning($"[ForsakenShrines] {def.PieceName}: icon item '{def.IconItem}' not found.");

                // ── Place effect: stone sounds for build and demolish ────────────────
                if (refPiece != null)
                    piece.m_placeEffect = refPiece.m_placeEffect;

                // ── Crafting station (set directly — PieceConfig mock stays unresolved with fixReference=false) ──
                var stonecutterGo = PrefabManager.Instance.GetPrefab("piece_stonecutter");
                piece.m_craftingStation = stonecutterGo?.GetComponent<CraftingStation>();
                if (piece.m_craftingStation == null)
                    Jotunn.Logger.LogWarning($"[ForsakenShrines] {def.PieceName}: 'piece_stonecutter' not found — no crafting station.");

                // ── WearNTear: hammer hover highlight + destruction effects ───────────
                var wnt = clone.GetComponent<WearNTear>() ?? clone.AddComponent<WearNTear>();
                wnt.m_health        = 1500f;
                wnt.m_materialType  = WearNTear.MaterialType.Stone;
                wnt.m_noRoofWear    = true;
                wnt.m_noSupportWear = true;
                if (refWnT != null)
                {
                    wnt.m_destroyedEffect = refWnT.m_destroyedEffect;
                    wnt.m_hitEffect       = refWnT.m_hitEffect;
                }

                var pieceConfig = new PieceConfig
                {
                    Name       = def.DisplayName,
                    PieceTable = PieceTable,
                    Category   = Category,
                };
                var cp = new CustomPiece(clone, false, pieceConfig);

                if (cp.Piece.m_resources != null)
                    cp.Piece.m_resources = System.Array.FindAll(cp.Piece.m_resources, r => r?.m_resItem != null);

                PieceManager.Instance.AddPiece(cp);
                Jotunn.Logger.LogInfo($"[ForsakenShrines] Phase 2 — registered: {def.PieceName}");
            }

            EnsureInPieceTable();
            EnsureInNamedPrefabs();
            UpdateUnlocks();
        }

        internal static void UpdateUnlocks()
        {
            if (ZoneSystem.instance == null)
            {
                Jotunn.Logger.LogInfo("[ForsakenShrines] UpdateUnlocks: ZoneSystem not ready — skipping.");
                return;
            }

            foreach (var def in ShrineDefinitions.All)
            {
                var customPiece = PieceManager.Instance.GetPiece(def.PieceName);
                if (customPiece?.Piece == null)
                {
                    Jotunn.Logger.LogWarning($"[ForsakenShrines] UpdateUnlocks: GetPiece('{def.PieceName}') returned null.");
                    continue;
                }

                bool hasKey = ZoneSystem.instance.GetGlobalKey(def.BossKey);
                customPiece.Piece.m_enabled = hasKey;
                Jotunn.Logger.LogInfo($"[ForsakenShrines] {def.PieceName}: key='{def.BossKey}' hasKey={hasKey} m_enabled={customPiece.Piece.m_enabled}");
            }
        }

        private static void EnsureInNamedPrefabs()
        {
            if (_clones.Count == 0) return;
            var namedPrefabs = GetNamedPrefabs();
            if (namedPrefabs == null)
            {
                Jotunn.Logger.LogWarning("[ForsakenShrines] EnsureInNamedPrefabs: ZNetScene not accessible.");
                return;
            }
            int added = 0;
            foreach (var clone in _clones)
            {
                if (clone == null) continue;
                int hash = clone.name.GetStableHashCode();
                if (!namedPrefabs.ContainsKey(hash)) { namedPrefabs[hash] = clone; added++; }
            }
            Jotunn.Logger.LogInfo(added > 0
                ? $"[ForsakenShrines] EnsureInNamedPrefabs: added {added} shrine(s)."
                : $"[ForsakenShrines] EnsureInNamedPrefabs: all {_clones.Count} already present.");
        }

        private static void EnsureInPieceTable()
        {
            if (_clones.Count == 0) return;
            var table = ObjectDB.instance
                ?.GetItemPrefab("Hammer")
                ?.GetComponent<ItemDrop>()
                ?.m_itemData?.m_shared?.m_buildPieces;
            if (table == null)
            {
                Jotunn.Logger.LogInfo("[ForsakenShrines] EnsureInPieceTable: Hammer not accessible yet.");
                return;
            }
            int added = 0;
            foreach (var clone in _clones)
            {
                if (clone == null || table.m_pieces.Contains(clone)) continue;
                table.m_pieces.Add(clone);
                added++;
            }
            Jotunn.Logger.LogInfo(added > 0
                ? $"[ForsakenShrines] EnsureInPieceTable: inserted {added} shrine(s)."
                : $"[ForsakenShrines] EnsureInPieceTable: all {_clones.Count} already present.");
        }
    }
}
