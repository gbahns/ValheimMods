using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace ForesakenShrines
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
                    Jotunn.Logger.LogWarning($"[ForesakenShrines] Base prefab '{def.BasePrefab}' not found — skipping {def.PieceName}.");
                    continue;
                }

                var clone = PrefabManager.Instance.CreateClonedPrefab(def.PieceName, basePrefab);

                // Jotunn's batch that copies PrefabManager→ZNetScene runs before OnVanillaPrefabsAvailable.
                // Write directly to m_namedPrefabs now so world ZDOs can resolve this prefab on load.
                var namedPrefabs = GetNamedPrefabs();
                if (namedPrefabs != null)
                    namedPrefabs[clone.name.GetStableHashCode()] = clone;

                // Lift children so the stone base sits flush on terrain (BossStone origin is partially buried).
                float lowestY = MeasureLowestMeshY(clone);
                float lift    = -lowestY + 0.05f;
                foreach (Transform child in clone.transform)
                    child.localPosition += Vector3.up * lift;

                var interactable = clone.AddComponent<ShrineInteractable>();
                interactable.Definition = def;

                var piece = clone.GetComponent<Piece>() ?? clone.AddComponent<Piece>();
                piece.m_name        = def.DisplayName;
                piece.m_description = $"$shrine_{def.PieceName}_desc";
                piece.m_groundOnly  = true;
                piece.m_resources   = new Piece.Requirement[0];

                _clones.Add(clone);
                Jotunn.Logger.LogInfo($"[ForesakenShrines] Phase 1 — cloned: {def.PieceName}");
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
                Jotunn.Logger.LogWarning("[ForesakenShrines] Phase 2: no clones from Phase 1 — retrying.");
                _clonesCreated = false;
                CreateClones();
            }
            if (_clones.Count == 0)
            {
                Jotunn.Logger.LogError("[ForesakenShrines] Phase 2: clone creation failed — shrines unavailable.");
                return;
            }

            // Reference stone piece used to copy WearNTear effects and place effect.
            var stoneRef  = PrefabManager.Instance.GetPrefab("stone_wall_2x1");
            var refWnT    = stoneRef?.GetComponent<WearNTear>();
            var refPiece  = stoneRef?.GetComponent<Piece>();
            if (stoneRef == null)
                Jotunn.Logger.LogWarning("[ForesakenShrines] Phase 2: 'stone_wall_2x1' not found — WearNTear effects and place sound may be missing.");

            foreach (var def in ShrineDefinitions.All)
            {
                var clone = _clones.Find(c => c != null && c.name == def.PieceName);
                if (clone == null)
                {
                    Jotunn.Logger.LogWarning($"[ForesakenShrines] Phase 2: clone '{def.PieceName}' not found.");
                    continue;
                }

                var piece = clone.GetComponent<Piece>();
                if (piece == null) continue;

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
                    Jotunn.Logger.LogWarning($"[ForesakenShrines] {def.PieceName}: icon item '{def.IconItem}' not found.");

                // ── Place effect: stone sounds for build and demolish ────────────────
                if (refPiece != null)
                    piece.m_placeEffect = refPiece.m_placeEffect;

                // ── Crafting station (set directly — PieceConfig mock stays unresolved with fixReference=false) ──
                var stonecutterGo = PrefabManager.Instance.GetPrefab("piece_stonecutter");
                piece.m_craftingStation = stonecutterGo?.GetComponent<CraftingStation>();
                if (piece.m_craftingStation == null)
                    Jotunn.Logger.LogWarning($"[ForesakenShrines] {def.PieceName}: 'piece_stonecutter' not found — no crafting station.");

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
                Jotunn.Logger.LogInfo($"[ForesakenShrines] Phase 2 — registered: {def.PieceName}");
            }

            EnsureInPieceTable();
            EnsureInNamedPrefabs();
            UpdateUnlocks();
        }

        internal static void UpdateUnlocks()
        {
            if (ZoneSystem.instance == null)
            {
                Jotunn.Logger.LogInfo("[ForesakenShrines] UpdateUnlocks: ZoneSystem not ready — skipping.");
                return;
            }

            foreach (var def in ShrineDefinitions.All)
            {
                var customPiece = PieceManager.Instance.GetPiece(def.PieceName);
                if (customPiece?.Piece == null)
                {
                    Jotunn.Logger.LogWarning($"[ForesakenShrines] UpdateUnlocks: GetPiece('{def.PieceName}') returned null.");
                    continue;
                }

                bool hasKey = ZoneSystem.instance.GetGlobalKey(def.BossKey);
                customPiece.Piece.m_enabled = hasKey;
                Jotunn.Logger.LogInfo($"[ForesakenShrines] {def.PieceName}: key='{def.BossKey}' hasKey={hasKey} m_enabled={customPiece.Piece.m_enabled}");
            }
        }

        private static float MeasureLowestMeshY(GameObject root)
        {
            float lowest = 0f;
            bool  found  = false;

            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                foreach (var v in mf.sharedMesh.vertices)
                {
                    float y = root.transform.InverseTransformPoint(mf.transform.TransformPoint(v)).y;
                    if (!found || y < lowest) { lowest = y; found = true; }
                }
            }

            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.sharedMesh == null) continue;
                foreach (var v in smr.sharedMesh.vertices)
                {
                    float y = root.transform.InverseTransformPoint(smr.transform.TransformPoint(v)).y;
                    if (!found || y < lowest) { lowest = y; found = true; }
                }
            }

            return lowest;
        }

        private static void EnsureInNamedPrefabs()
        {
            if (_clones.Count == 0) return;
            var namedPrefabs = GetNamedPrefabs();
            if (namedPrefabs == null)
            {
                Jotunn.Logger.LogWarning("[ForesakenShrines] EnsureInNamedPrefabs: ZNetScene not accessible.");
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
                ? $"[ForesakenShrines] EnsureInNamedPrefabs: added {added} shrine(s)."
                : $"[ForesakenShrines] EnsureInNamedPrefabs: all {_clones.Count} already present.");
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
                Jotunn.Logger.LogInfo("[ForesakenShrines] EnsureInPieceTable: Hammer not accessible yet.");
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
                ? $"[ForesakenShrines] EnsureInPieceTable: inserted {added} shrine(s)."
                : $"[ForesakenShrines] EnsureInPieceTable: all {_clones.Count} already present.");
        }
    }
}
