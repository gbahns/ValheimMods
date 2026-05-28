using System;
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
    /// Phase 1 (OnVanillaPrefabsAvailable): clone piece_chest_blackmetal, strip Container, attach ArmoryRack.
    /// Phase 2 (OnPiecesRegistered): set requirements, WearNTear, icon, and register with Jotunn.
    /// </summary>
    internal static class ArmoryPieces
    {
        private const string PieceName  = "armory_rack";
        private const string BasePrefab = "piece_chest_blackmetal";
        private const string PieceTable = "_HammerPieceTable";
        private const string Category   = "Furniture";

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
                UnityEngine.Object.DestroyImmediate(container);

            _clone.AddComponent<ArmoryRack>();

            // Replace the blackmetal chest visual with a composite wood-panel armoire.
            BuildArmoireMesh(_clone);

            var piece = _clone.GetComponent<Piece>() ?? _clone.AddComponent<Piece>();
            piece.m_name        = "$armory_rack_name";
            piece.m_description = "$armory_rack_desc";
            piece.m_groundOnly  = false;
            piece.m_groundPiece = false;
            piece.m_resources   = new Piece.Requirement[0];

            Jotunn.Logger.LogInfo("[Armory] Phase 1 — cloned armory_rack");
        }

        // Compose the armoire shape from vanilla wood floor panels.  Dimensions: 2m wide (X) x
        // 1m deep (Z) x 3m tall (Y), open front at +Z, solid sides/back/top/bottom.  The cloned
        // blackmetal chest visual is hidden but its components (Piece, WearNTear, ZNetView) are
        // retained on the root.  Child colliders are stripped in favor of a single combined
        // BoxCollider so the whole rack interacts as one shape.
        private static void BuildArmoireMesh(GameObject parent)
        {
            // Hide the chest renderers — we're replacing the visual entirely.
            var chestRenderers = parent.GetComponentsInChildren<Renderer>();
            foreach (var r in chestRenderers) r.enabled = false;

            // Plain wood floor panels for back/sides/top/bottom — these read cleanly as a wood box.
            var panelSrc = PrefabManager.Instance.GetPrefab("wood_floor_1x1");
            if (panelSrc == null)
            {
                Jotunn.Logger.LogWarning("[Armory] wood_floor_1x1 unavailable — reverting to chest visual.");
                foreach (var r in chestRenderers) r.enabled = true;
                return;
            }

            // Shutter prefab for the front doors — the slatted look reads as wardrobe doors.
            // Falls back to wood_floor_1x1 if no shutter variant is available on this Valheim
            // version.  Logs the resolved name so we can confirm which one shipped.
            GameObject doorSrc = null;
            string     doorName = null;
            foreach (var candidate in new[] { "wood_shutter", "wood_shutter_closed", "wood_window_shutter", "wood_window" })
            {
                doorSrc = PrefabManager.Instance.GetPrefab(candidate);
                if (doorSrc != null) { doorName = candidate; break; }
            }
            if (doorSrc == null) { doorSrc = panelSrc; doorName = "wood_floor_1x1 (shutter unavailable)"; }
            Jotunn.Logger.LogInfo($"[Armory] BuildArmoireMesh: door prefab = '{doorName}'.");

            // Back wall: 2-wide x 3-tall at z = -0.5.  Texture faces -Z (outside the box).
            for (int x = 0; x < 2; x++)
                for (int y = 0; y < 3; y++)
                    AddPanel(parent, panelSrc, new Vector3(-0.5f + x, 0.5f + y, -0.5f), Quaternion.Euler(-90, 0, 0));

            // Left side: 1-wide x 3-tall at x = -1.  Texture faces -X.
            for (int y = 0; y < 3; y++)
                AddPanel(parent, panelSrc, new Vector3(-1f, 0.5f + y, 0f), Quaternion.Euler(0, 0, -90));

            // Right side: 1-wide x 3-tall at x = +1.  Texture faces +X.
            for (int y = 0; y < 3; y++)
                AddPanel(parent, panelSrc, new Vector3(1f, 0.5f + y, 0f), Quaternion.Euler(0, 0, 90));

            // Top: 2-wide x 1-deep at y = 3.  Texture faces +Y.
            for (int x = 0; x < 2; x++)
                AddPanel(parent, panelSrc, new Vector3(-0.5f + x, 3f, 0f), Quaternion.identity);

            // Bottom: 2-wide x 1-deep at y = 0.  Texture faces -Y.
            for (int x = 0; x < 2; x++)
                AddPanel(parent, panelSrc, new Vector3(-0.5f + x, 0f, 0f), Quaternion.Euler(180, 0, 0));

            // Front: double doors hinged at the outer front edges, swinging outward.  Each door
            // is a 1m-wide x 3m-tall slab built from 3 stacked shutter panels parented under an
            // empty hinge GameObject — rotating the hinge swings the whole door around its outer edge.
            // The shutter prefab's pivot sits at the hinge-side of its mesh and the mesh extends
            // away from the pivot, so we place the pivot AT the rack's hinge (offset 0).  The
            // LEFT door is mirrored on X so the shutter's visible mesh extends inward toward the
            // center seam (mesh defaults to extending in the opposite direction).
            BuildDoor(parent, doorSrc, "ArmoryDoorLeft",  new Vector3(-1f, 0f, 0.5f), 0f, flipped: true);
            BuildDoor(parent, doorSrc, "ArmoryDoorRight", new Vector3( 1f, 0f, 0.5f), 0f, flipped: false);

            // Single combined BoxCollider so the whole armoire raycasts as one shape.
            var col = parent.AddComponent<BoxCollider>();
            col.center = new Vector3(0, 1.5f, 0);
            col.size   = new Vector3(2f, 3f, 1f);

            // Tint everything dark first — panels at 50% brightness for a charcoal wood look.
            // Then re-tint the front shutters slightly brighter (75%) so the doors read as a
            // distinct layer rather than blending into the body.  Uses Renderer.materials
            // (auto-instances) so we don't recolor other wood pieces in the world.
            ApplyTint(parent.transform, new Color(0.50f, 0.50f, 0.50f));

            var leftHinge  = parent.transform.Find("ArmoryDoorLeft");
            var rightHinge = parent.transform.Find("ArmoryDoorRight");
            if (leftHinge  != null) ApplyTint(leftHinge,  new Color(0.75f, 0.75f, 0.75f));
            if (rightHinge != null) ApplyTint(rightHinge, new Color(0.75f, 0.75f, 0.75f));

            Jotunn.Logger.LogInfo("[Armory] BuildArmoireMesh: composed 16 wood panels + 2 doors (panels 50%, shutters 75%).");
        }

        // Walk a transform subtree and apply a color tint to every enabled renderer.  Skip
        // disabled renderers (e.g. the hidden chest meshes) so we don't waste material instances
        // on something the player never sees.
        private static void ApplyTint(Transform root, Color tint)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled) continue;
                foreach (var m in r.materials)
                    if (m.HasProperty("_Color"))
                        m.color = tint;
            }
        }

        // Build one swinging door as a child hinge GameObject containing 3 stacked panels.
        // The hinge sits at the rack's outer-front corner; the panels offset inward toward the
        // door's free end so the door pivots around its outer edge.  Pass flipped=true to mirror
        // the panel mesh on its X axis (used for the right door so the shutter's hinge-edge
        // texture aligns with the outer edge of the armory).
        private static void BuildDoor(GameObject parent, GameObject panelSrc, string hingeName,
                                      Vector3 hingeLocalPos, float panelOffsetX, bool flipped)
        {
            var hinge = new GameObject(hingeName);
            hinge.transform.SetParent(parent.transform, false);
            hinge.transform.localPosition = hingeLocalPos;
            hinge.transform.localRotation = Quaternion.identity;

            // 3 panels stacked vertically, offset inward from the hinge.  Wood shutter prefabs
            // are already authored as vertical wall pieces (+Z forward), so no rotation needed.
            var scale = flipped ? new Vector3(-1f, 1f, 1f) : Vector3.one;
            for (int y = 0; y < 3; y++)
            {
                var panel = UnityEngine.Object.Instantiate(panelSrc, hinge.transform);
                panel.transform.localPosition = new Vector3(panelOffsetX, 0.5f + y, 0f);
                panel.transform.localRotation = Quaternion.identity;
                panel.transform.localScale    = scale;
                panel.name = panelSrc.name + "_panel";
                StripChildComponents(panel);
            }
        }

        // Same stripping logic as AddPanel, but extracted so BuildDoor can also use it with the
        // mirrored scale.  Door must be stripped first because its Awake reads m_nview, and we
        // also remove ZNetView next — leaving Door alive would NRE on every spawn.
        private static void StripChildComponents(GameObject visual)
        {
            foreach (var c in visual.GetComponentsInChildren<Door>())           UnityEngine.Object.DestroyImmediate(c);
            foreach (var c in visual.GetComponentsInChildren<Piece>())          UnityEngine.Object.DestroyImmediate(c);
            foreach (var c in visual.GetComponentsInChildren<WearNTear>())      UnityEngine.Object.DestroyImmediate(c);
            foreach (var c in visual.GetComponentsInChildren<ZNetView>())       UnityEngine.Object.DestroyImmediate(c);
            foreach (var c in visual.GetComponentsInChildren<ZSyncTransform>()) UnityEngine.Object.DestroyImmediate(c);
            foreach (var c in visual.GetComponentsInChildren<Collider>())       UnityEngine.Object.DestroyImmediate(c);
            foreach (var c in visual.GetComponentsInChildren<SnapToGround>())   UnityEngine.Object.DestroyImmediate(c);
        }

        private static void AddPanel(GameObject parent, GameObject source, Vector3 localPos, Quaternion localRot)
        {
            var visual = UnityEngine.Object.Instantiate(source, parent.transform);
            visual.transform.localPosition = localPos;
            visual.transform.localRotation = localRot;
            visual.name = source.name + "_panel";
            StripChildComponents(visual);
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

            // Build requirements — Black Forest tier (Bronze Age):
            //   8 FineWood, 4 RoundLog (core wood), 4 BronzeNails, 2 Bronze, 2 DeerHide.
            // Anything that fails the ObjectDB lookup is silently skipped (defensive — should
            // never happen for these vanilla prefabs, but keeps the piece registerable even on
            // a hypothetical Valheim build that renames one).
            var requirements = new List<Piece.Requirement>();
            void AddReq(string prefabName, int amount)
            {
                var drop = ObjectDB.instance.GetItemPrefab(prefabName)?.GetComponent<ItemDrop>();
                if (drop != null)
                    requirements.Add(new Piece.Requirement { m_resItem = drop, m_amount = amount, m_recover = true });
                else
                    Jotunn.Logger.LogWarning($"[Armory] Recipe ingredient '{prefabName}' not found in ObjectDB — skipped.");
            }
            AddReq("FineWood",    8);
            AddReq("RoundLog",    4);
            AddReq("BronzeNails", 4);
            AddReq("Bronze",      2);
            AddReq("DeerHide",    2);
            piece.m_resources = requirements.ToArray();

            // Crafting station.
            piece.m_craftingStation = PrefabManager.Instance.GetPrefab("piece_workbench")?.GetComponent<CraftingStation>();

            // Icon: load custom 256×256 from embedded resource.  If that fails, fall back
            // to a vanilla chest icon so the entry doesn't disappear from the build menu.
            piece.m_icon = LoadEmbeddedIcon("armory.png")
                        ?? ObjectDB.instance.GetItemPrefab("ArmorLeatherChest")?.GetComponent<ItemDrop>()
                                  ?.m_itemData?.m_shared?.m_icons?[0];

            // Place effects.
            var refPiece = PrefabManager.Instance.GetPrefab(BasePrefab)?.GetComponent<Piece>();
            if (refPiece != null)
                piece.m_placeEffect = refPiece.m_placeEffect;

            // Re-add a Container so the rack can physically store items.  We stripped the
            // vanilla chest's Container in Phase 1 to prevent it intercepting [Use]; now we
            // attach a fresh one AFTER ArmoryRack so ArmoryRack stays first in the Hoverable /
            // Interactable lookup order (the player presses E and gets our UI, not the chest UI —
            // but our UI does call InventoryGui.Show(this container) so the chest grid still
            // shows alongside the loadout panel).  Container.Awake builds the Inventory from
            // m_width/m_height/m_name/m_bkg, so we set those here and let Awake run on spawn.
            if (_clone.GetComponent<Container>() == null)
            {
                var storage = _clone.AddComponent<Container>();
                var refContainer = PrefabManager.Instance.GetPrefab(BasePrefab)?.GetComponent<Container>();
                if (refContainer != null)
                {
                    storage.m_bkg                = refContainer.m_bkg;
                    storage.m_destroyedLootPrefab = refContainer.m_destroyedLootPrefab;
                }
                storage.m_name   = "$armory_rack_name";
                // 8×5 = 40 slots — bigger than any vanilla chest (black metal is 6×4=24) so late-game
                // players can keep multiple full armor sets, weapons, capes, and utility items in one
                // rack.  Width matches the player inventory so the side-by-side UI lines up cleanly.
                storage.m_width  = 8;
                storage.m_height = 5;
            }

            var pieceConfig = new PieceConfig
            {
                Name       = "Armory Rack",
                PieceTable = PieceTable,
                Category   = Category,
            };
            PieceManager.Instance.AddPiece(new CustomPiece(_clone, false, pieceConfig));
            Jotunn.Logger.LogInfo("[Armory] Phase 2 — registered armory_rack");

            EnsureInPieceTable();
            EnsureInNamedPrefabs();
        }

        // Jotunn's PieceManager.AddPiece does not always insert the clone into the Hammer's
        // m_pieces list, so the Hammer build menu never shows it.  Force-insert here.  Same
        // workaround pattern used by ForsakenShrines.
        private static void EnsureInPieceTable()
        {
            if (_clone == null) return;
            var table = ObjectDB.instance
                ?.GetItemPrefab("Hammer")?
                .GetComponent<ItemDrop>()
                ?.m_itemData?.m_shared?.m_buildPieces;
            if (table == null)
            {
                Jotunn.Logger.LogInfo("[Armory] EnsureInPieceTable: Hammer not accessible yet.");
                return;
            }
            if (table.m_pieces.Contains(_clone))
            {
                Jotunn.Logger.LogInfo("[Armory] EnsureInPieceTable: armory_rack already present.");
                return;
            }
            table.m_pieces.Add(_clone);
            Jotunn.Logger.LogInfo("[Armory] EnsureInPieceTable: inserted armory_rack.");
        }

        // Load a 256×256 PNG embedded as a managed resource and return it as a Sprite.
        // Returns null on any failure (caller falls back to a vanilla icon).
        private static Sprite LoadEmbeddedIcon(string fileName)
        {
            try
            {
                var asm  = Assembly.GetExecutingAssembly();
                var name = Array.Find(asm.GetManifestResourceNames(), n => n.EndsWith(fileName));
                if (name == null) return null;
                using (var stream = asm.GetManifestResourceStream(name))
                {
                    if (stream == null) return null;
                    var bytes = new byte[stream.Length];
                    stream.Read(bytes, 0, bytes.Length);

                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (!UnityLoadImage(tex, bytes)) return null;
                    return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), tex.width);
                }
            }
            catch (Exception e)
            {
                Jotunn.Logger.LogWarning($"[Armory] Failed to load embedded icon '{fileName}': {e.Message}");
                return null;
            }
        }

        // UnityEngine.ImageConversionModule can't be referenced directly without hitting a
        // netstandard 2.0 vs 2.1 conflict — resolve LoadImage via reflection at runtime.
        // Unity loads the module itself; this just calls into the already-loaded type.
        private static bool UnityLoadImage(Texture2D tex, byte[] bytes)
        {
            try
            {
                var t = Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule");
                var m = t?.GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]) });
                return m != null && (bool)m.Invoke(null, new object[] { tex, bytes });
            }
            catch
            {
                return false;
            }
        }

        // Belt-and-braces: ensure the cloned prefab is resolvable via ZNetScene's named-prefab
        // dictionary so loaded ZDOs (placed racks from previous sessions) can find it.
        private static void EnsureInNamedPrefabs()
        {
            if (_clone == null) return;
            var namedPrefabs = GetNamedPrefabs();
            if (namedPrefabs == null) return;
            var hash = _clone.name.GetStableHashCode();
            if (!namedPrefabs.ContainsKey(hash))
            {
                namedPrefabs[hash] = _clone;
                Jotunn.Logger.LogInfo("[Armory] EnsureInNamedPrefabs: added armory_rack.");
            }
        }
    }
}
