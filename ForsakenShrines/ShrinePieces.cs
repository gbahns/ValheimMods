using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ForsakenShrines
{
    /// <summary>
    /// Creates and registers the shrine build pieces.  Prefab lookup and cloning go through
    /// Jotunn's PrefabManager (it resolves Valheim 1.0's soft-referenced BossStone prefabs);
    /// everything after that — piece setup, Hammer table insertion, ZNetScene registration and
    /// unlock state — is done directly against the game.  Jotunn's PieceManager is not used;
    /// see ForsakenShrinesMod.Awake for why.
    /// </summary>
    internal static class ShrinePieces
    {
        // Cloned prefabs, one per ShrineDefinition, created once per game session.
        private static readonly List<GameObject> _clones = new List<GameObject>();

        // ZNetScene.m_namedPrefabs is private; use AccessTools so runtime enforcement doesn't throw.
        private static readonly FieldInfo _namedPrefabsField =
            AccessTools.Field(typeof(ZNetScene), "m_namedPrefabs");

        // Player.UpdateKnownRecipesList is private.  It is what makes a build piece appear in
        // the Hammer: a piece is listed only once its m_name is in the player's known recipes,
        // and the scan skips pieces whose m_enabled is false.  Vanilla runs it from Player.Awake
        // (before our OnSpawned unlock refresh) and again on inventory changes, so we run it
        // ourselves right after toggling m_enabled.
        private static readonly MethodInfo _updateKnownRecipesList =
            AccessTools.Method(typeof(Player), "UpdateKnownRecipesList");

        private static Dictionary<int, GameObject> GetNamedPrefabs() =>
            ZNetScene.instance != null
                ? _namedPrefabsField?.GetValue(ZNetScene.instance) as Dictionary<int, GameObject>
                : null;

        private static bool _clonesCreated    = false;
        private static bool _piecesConfigured = false;

        private static GameObject FindClone(string pieceName) =>
            _clones.Find(c => c != null && c.name == pieceName);

        // ── Phase 1: PrefabManager.OnVanillaPrefabsAvailable (main menu) ───────────
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
                if (clone == null)
                {
                    Jotunn.Logger.LogWarning($"[ForsakenShrines] Failed to clone '{def.BasePrefab}' as {def.PieceName}.");
                    continue;
                }

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

        // ── Phase 2: ObjectDB.Awake postfix (every scene load) ──────────────────────
        internal static void OnObjectDBAwake()
        {
            // The main menu (FejdStartup) has its own ObjectDB; only the world scene matters.
            if (SceneManager.GetActiveScene().name != "main") return;
            try
            {
                ConfigurePieces();
                EnsureInPieceTable();
                EnsureInNamedPrefabs();
                UpdateUnlocks();
            }
            catch (System.Exception e)
            {
                Jotunn.Logger.LogError($"[ForsakenShrines] ObjectDB.Awake hook failed: {e}");
            }
        }

        // ZNetScene.Awake postfix (every world load).  Placed shrines resolve their prefab
        // through ZNetScene, so the clones must be registered again for each new ZNetScene.
        internal static void OnZNetSceneAwake()
        {
            try
            {
                EnsureInNamedPrefabs();
            }
            catch (System.Exception e)
            {
                Jotunn.Logger.LogError($"[ForsakenShrines] ZNetScene.Awake hook failed: {e}");
            }
        }

        // One-time piece setup that needs ObjectDB: requirements, icons, effects, station.
        private static void ConfigurePieces()
        {
            if (_piecesConfigured) return;

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
            _piecesConfigured = true;

            // Reference stone piece used to copy WearNTear effects, place effect, and category.
            var stoneRef  = PrefabManager.Instance.GetPrefab("stone_wall_2x1");
            var refWnT    = stoneRef?.GetComponent<WearNTear>();
            var refPiece  = stoneRef?.GetComponent<Piece>();
            if (stoneRef == null)
                Jotunn.Logger.LogWarning("[ForsakenShrines] Phase 2: 'stone_wall_2x1' not found — WearNTear effects and place sound may be missing.");

            // Build category.  Valheim 1.0 sizes PieceTable's per-category lists to exactly
            // Piece.PieceCategory.Max, so the separate "Forsaken Shrines" tab that earlier
            // versions added through Jotunn cannot exist on 1.0 (Jotunn's category patches are
            // precisely what broke).  Shrines share the vanilla tab of the reference stone piece
            // instead — the tab the player already builds stonecutter pieces from, whatever this
            // game version labels it.
            var category = refPiece != null ? refPiece.m_category : Piece.PieceCategory.BuildingStonecutter;
            if (category < 0 || category >= Piece.PieceCategory.Max)
                category = Piece.PieceCategory.BuildingStonecutter;

            var stonecutterGo = PrefabManager.Instance.GetPrefab("piece_stonecutter");
            var stonecutter   = stonecutterGo?.GetComponent<CraftingStation>();
            if (stonecutter == null)
                Jotunn.Logger.LogWarning("[ForsakenShrines] Phase 2: 'piece_stonecutter' not found — shrines will have no crafting station.");

            foreach (var def in ShrineDefinitions.All)
            {
                var clone = FindClone(def.PieceName);
                if (clone == null)
                {
                    Jotunn.Logger.LogWarning($"[ForsakenShrines] Phase 2: clone '{def.PieceName}' not found.");
                    continue;
                }

                var piece = clone.GetComponent<Piece>();
                if (piece == null) continue;

                piece.m_category = category;

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

                // ── Crafting station ─────────────────────────────────────────────────
                piece.m_craftingStation = stonecutter;

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

                Jotunn.Logger.LogInfo($"[ForsakenShrines] Phase 2 — configured: {def.PieceName} (category {category})");
            }
        }

        // Enable each shrine piece only once its boss's global key is set.
        internal static void UpdateUnlocks()
        {
            if (ZoneSystem.instance == null)
            {
                Jotunn.Logger.LogInfo("[ForsakenShrines] UpdateUnlocks: ZoneSystem not ready — skipping.");
                return;
            }

            foreach (var def in ShrineDefinitions.All)
            {
                var piece = FindClone(def.PieceName)?.GetComponent<Piece>();
                if (piece == null)
                {
                    Jotunn.Logger.LogWarning($"[ForsakenShrines] UpdateUnlocks: no piece for '{def.PieceName}'.");
                    continue;
                }

                bool hasKey = ZoneSystem.instance.GetGlobalKey(def.BossKey);
                piece.m_enabled = hasKey;
                Jotunn.Logger.LogInfo($"[ForsakenShrines] {def.PieceName}: key='{def.BossKey}' hasKey={hasKey} m_enabled={piece.m_enabled}");
            }
        }

        // Re-runs the game's known-piece scan for the local player so a shrine that was just
        // enabled shows up in the Hammer at once instead of after the next inventory change.
        internal static void RefreshKnownPieces()
        {
            var player = Player.m_localPlayer;
            if (player == null || _updateKnownRecipesList == null) return;
            try
            {
                _updateKnownRecipesList.Invoke(player, null);
            }
            catch (System.Exception e)
            {
                Jotunn.Logger.LogWarning($"[ForsakenShrines] UpdateKnownRecipesList failed: {e.InnerException?.Message ?? e.Message}");
            }
        }

        private static void EnsureInNamedPrefabs()
        {
            if (_clones.Count == 0) return;
            var namedPrefabs = GetNamedPrefabs();
            if (namedPrefabs == null)
            {
                Jotunn.Logger.LogInfo("[ForsakenShrines] EnsureInNamedPrefabs: ZNetScene not up yet.");
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
