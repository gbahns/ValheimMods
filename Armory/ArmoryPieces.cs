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
    /// Registers the Armory Rack and the Small Armory Rack (#84) as buildable Hammer pieces.
    /// Phase 1 (OnVanillaPrefabsAvailable): clone piece_chest_blackmetal, strip Container, attach
    /// ArmoryRack, hide the chest and draw the rack's own look (RackVisual).
    /// Phase 2 (OnPiecesRegistered): set requirements, WearNTear, icon, and register with Jotunn.
    /// </summary>
    internal static class ArmoryPieces
    {
        private const string BasePrefab = "piece_chest_blackmetal";
        private const string PieceTable = "_HammerPieceTable";
        private const string Category   = "Furniture";

        private struct Ingredient
        {
            public string Item; public int Amount;
            public Ingredient(string item, int amount) { Item = item; Amount = amount; }
        }

        /// <summary>One buildable piece: what it is called, how big, what it costs.</summary>
        private sealed class Kind
        {
            public string       PrefabName;    // the ZNetScene name; the world's ZDOs hold its hash
            public string       NameKey;       // "$..." for the Piece and its Container
            public string       DescKey;
            public string       DisplayName;   // for Jotunn's PieceConfig
            public bool         Small;
            public Ingredient[] Recipe;
            public GameObject   Clone;         // once Phase 1 has run
        }

        // Black Forest tier (Bronze Age), priced to the piece: some 24 m² of board (about twice
        // the game's wardrobe), a core-wood carcass, a full ingot's worth of nails, an ingot per
        // strap hinge, and resin for the finish.  The small rack is two thirds of it.
        private static readonly Kind Standard = new Kind
        {
            PrefabName = "armory_rack", NameKey = "$armory_rack_name", DescKey = "$armory_rack_desc",
            DisplayName = "Armory Rack", Small = false,
            Recipe = new[] { new Ingredient("FineWood", 16), new Ingredient("RoundLog", 8), new Ingredient("BronzeNails", 20),
                             new Ingredient("Bronze", 4), new Ingredient("Resin", 10) },
        };
        private static readonly Kind SmallKind = new Kind
        {
            PrefabName = "armory_rack_small", NameKey = "$armory_rack_small_name", DescKey = "$armory_rack_small_desc",
            DisplayName = "Small Armory Rack", Small = true,
            Recipe = new[] { new Ingredient("FineWood", 10), new Ingredient("RoundLog", 5), new Ingredient("BronzeNails", 12),
                             new Ingredient("Bronze", 3), new Ingredient("Resin", 6) },
        };

        // In the order the hammer menu shows them: the small rack before the standard one.
        private static readonly Kind[] Kinds = { SmallKind, Standard };

        /// <summary>The standard rack's prefab, once Phase 1 has cloned it; null before.</summary>
        internal static GameObject Prefab => Standard.Clone;

        /// <summary>Both prefabs, once cloned; whatever exists before.</summary>
        internal static IEnumerable<GameObject> Prefabs
        {
            get { foreach (var k in Kinds) if (k.Clone != null) yield return k.Clone; }
        }

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
                Jotunn.Logger.LogError($"[Armory] Base prefab '{BasePrefab}' not found.");
                return;
            }

            foreach (var kind in Kinds) CreateClone(kind, basePrefab);
        }

        private static void CreateClone(Kind kind, GameObject basePrefab)
        {
            var _clone = kind.Clone = PrefabManager.Instance.CreateClonedPrefab(kind.PrefabName, basePrefab);

            // Register in ZNetScene immediately so world ZDOs can resolve this prefab on load.
            var namedPrefabs = GetNamedPrefabs();
            if (namedPrefabs != null)
                namedPrefabs[_clone.name.GetStableHashCode()] = _clone;

            // Strip the vanilla Container so it can be put back AFTER ArmoryRack in Phase 2.
            // This is purely about component order now, not about [Use]: Container is both
            // Hoverable and Interactable, and Player resolves those with two separate
            // GetComponentInParent calls that each take the FIRST match.  ArmoryRack first
            // means the rack's own hover text wins, while Container — the only Interactable
            // on the object — still answers the keypress, which is what we want it to do.
            var container = _clone.GetComponent<Container>();
            if (container != null)
                UnityEngine.Object.DestroyImmediate(container);

            // Which piece this is, before the look is drawn: RackVisual reads it for the scale.
            _clone.AddComponent<ArmoryRack>().Small = kind.Small;

            // The chest's own look goes; its Piece, WearNTear and ZNetView stay on the root.
            foreach (var r in _clone.GetComponentsInChildren<Renderer>()) r.enabled = false;

            // One collider for the whole rack, whatever look it wears; RackVisual.Build sizes it.
            _clone.AddComponent<BoxCollider>();

            RackVisual.Build(_clone);

            var piece = _clone.GetComponent<Piece>() ?? _clone.AddComponent<Piece>();
            piece.m_name        = kind.NameKey;
            piece.m_description = kind.DescKey;
            piece.m_groundOnly  = false;
            piece.m_groundPiece = false;
            piece.m_resources   = new Piece.Requirement[0];

            Jotunn.Logger.LogInfo($"[Armory] Phase 1 — cloned {kind.PrefabName}");
        }

        // The original look (Rack Look = Panels): the armoire composed from vanilla wood floor
        // panels, 2m wide (X) x 1m deep (Z) x 3m tall (Y), open front at +Z, solid sides, back,
        // top and bottom, under the ArmoryVisual child RackVisual hands in.  Child colliders are
        // stripped; the rack's one BoxCollider is on the root.
        internal static void BuildPanelLook(GameObject parent)
        {
            // Plain wood floor panels for back/sides/top/bottom — these read cleanly as a wood box.
            var panelSrc = PrefabManager.Instance.GetPrefab("wood_floor_1x1");
            if (panelSrc == null)
            {
                Jotunn.Logger.LogWarning("[Armory] wood_floor_1x1 unavailable — reverting to chest visual.");
                var rack = parent.transform.parent;
                if (rack != null)
                    foreach (var r in rack.GetComponentsInChildren<Renderer>()) r.enabled = true;
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
            Jotunn.Logger.LogInfo($"[Armory] Panel look: door prefab = '{doorName}'.");

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

            // Tint everything dark first — panels at 50% brightness for a charcoal wood look.
            // Then re-tint the front shutters slightly brighter (75%) so the doors read as a
            // distinct layer rather than blending into the body.  Uses Renderer.materials
            // (auto-instances) so we don't recolor other wood pieces in the world.
            ApplyTint(parent.transform, new Color(0.50f, 0.50f, 0.50f));

            var leftHinge  = parent.transform.Find("ArmoryDoorLeft");
            var rightHinge = parent.transform.Find("ArmoryDoorRight");
            if (leftHinge  != null) ApplyTint(leftHinge,  new Color(0.75f, 0.75f, 0.75f));
            if (rightHinge != null) ApplyTint(rightHinge, new Color(0.75f, 0.75f, 0.75f));

            Jotunn.Logger.LogInfo("[Armory] Panel look: composed 16 wood panels + 2 doors (panels 50%, shutters 75%).");
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
                var panel = InstantiateInert(panelSrc, hinge.transform);
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

        // A copy of a vanilla piece that is only a shape.  On a rack standing in the world the
        // copy is active, so its ZNetView would wake and register a real networked piece before
        // StripChildComponents reached it: switching Rack Look to Panels once left live
        // wood_window pieces sticking out of every rack (2026-11-13).  The game's placement
        // ghost uses this same flag to keep ZNetView from initializing during Instantiate.
        internal static GameObject InstantiateInert(GameObject source, Transform parent)
        {
            ZNetView.m_forceDisableInit = true;
            try { return UnityEngine.Object.Instantiate(source, parent); }
            finally { ZNetView.m_forceDisableInit = false; }
        }

        private static void AddPanel(GameObject parent, GameObject source, Vector3 localPos, Quaternion localRot)
        {
            var visual = InstantiateInert(source, parent.transform);
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

            // Icon: a picture of the player's default rack (#86), else the drawn 256×256 one
            // embedded here, else a vanilla icon so the entry doesn't disappear from the menu.
            _drawnIcon = LoadEmbeddedIcon("armory.png")
                      ?? ObjectDB.instance.GetItemPrefab("ArmorLeatherChest")?.GetComponent<ItemDrop>()
                                ?.m_itemData?.m_shared?.m_icons?[0];

            // Registered in Kinds order, which is the menu's order when Jotunn is the one that
            // fills the table; EnsureInPieceTable settles it when it is not.
            foreach (var kind in Kinds)
            {
                if (kind.Clone == null)
                {
                    Jotunn.Logger.LogError($"[Armory] Phase 2: no clone of {kind.PrefabName} from Phase 1 — piece unavailable.");
                    continue;
                }
                Configure(kind);
            }
            RefreshIcon();

            EnsureInPieceTable();
            EnsureInNamedPrefabs();
        }

        private static void Configure(Kind kind)
        {
            var _clone = kind.Clone;
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

            // Build requirements, from the kind's recipe.  Anything that fails the ObjectDB
            // lookup is skipped with a warning (defensive — should never happen for these
            // vanilla prefabs, but keeps the piece registerable even on a hypothetical Valheim
            // build that renames one).
            var requirements = new List<Piece.Requirement>();
            foreach (var ing in kind.Recipe)
            {
                var drop = ObjectDB.instance.GetItemPrefab(ing.Item)?.GetComponent<ItemDrop>();
                if (drop != null)
                    requirements.Add(new Piece.Requirement { m_resItem = drop, m_amount = ing.Amount, m_recover = true });
                else
                    Jotunn.Logger.LogWarning($"[Armory] Recipe ingredient '{ing.Item}' not found in ObjectDB — skipped.");
            }
            piece.m_resources = requirements.ToArray();

            // Crafting station.
            piece.m_craftingStation = PrefabManager.Instance.GetPrefab("piece_workbench")?.GetComponent<CraftingStation>();

            piece.m_icon = _drawnIcon;   // RefreshIcon replaces it with a photograph once both are configured

            // Place effects.
            var refPiece = PrefabManager.Instance.GetPrefab(BasePrefab)?.GetComponent<Piece>();
            if (refPiece != null)
                piece.m_placeEffect = refPiece.m_placeEffect;

            // Put the Container back, after ArmoryRack, so the rack is first in the Hoverable
            // lookup and the Container is the object's only Interactable.  Pressing [Use] is
            // therefore an ordinary chest open, and the loadout panel comes up off the back of it
            // in ArmoryOpenPatch.  Container.Awake builds the Inventory from
            // m_width/m_height/m_name/m_bkg, so we set those here and let Awake run on spawn.
            if (_clone.GetComponent<Container>() == null)
            {
                var storage = _clone.AddComponent<Container>();
                var refContainer = PrefabManager.Instance.GetPrefab(BasePrefab)?.GetComponent<Container>();
                if (refContainer != null)
                {
                    storage.m_bkg                = refContainer.m_bkg;
                    storage.m_destroyedLootPrefab = refContainer.m_destroyedLootPrefab;
                    // Carry the chest's access rules over too.  AddComponent leaves these at the
                    // field defaults (Public / no guard-stone check), which quietly made the rack
                    // the one container on the map a ward does not protect.
                    storage.m_privacy            = refContainer.m_privacy;
                    storage.m_checkGuardStone    = refContainer.m_checkGuardStone;
                }
                storage.m_name   = kind.NameKey;
                // The grid comes from the synced Rack Width / Rack Height settings (12×6 by
                // default, 8×5 for the small rack).  RackGridFitPatch draws a grid bigger than
                // the vanilla container panel at a smaller cell size, which is what used to pin
                // this at 8×4.  A change to the settings later goes through RackSize.Apply, onto
                // this prefab and onto the racks already standing.
                storage.m_width  = RackSize.WidthFor(kind.Small);
                storage.m_height = RackSize.HeightFor(kind.Small);
                // Logged rather than assumed: these come out of the chest prefab's asset data,
                // which is not readable from the assemblies, so the run itself is the record of
                // what the rack inherited.
                Jotunn.Logger.LogInfo($"[Armory] Container access copied from {BasePrefab}: privacy={storage.m_privacy}, checkGuardStone={storage.m_checkGuardStone}");
            }

            var pieceConfig = new PieceConfig
            {
                Name       = kind.DisplayName,
                PieceTable = PieceTable,
                Category   = Category,
            };
            PieceManager.Instance.AddPiece(new CustomPiece(_clone, false, pieceConfig));
            Jotunn.Logger.LogInfo($"[Armory] Phase 2 — registered {kind.PrefabName}");
        }

        // Jotunn's PieceManager.AddPiece does not always insert the clone into the Hammer's
        // m_pieces list, so the Hammer build menu never shows it.  Force-insert here.  Same
        // workaround pattern used by ForsakenShrines.  The menu is this list in order, so the
        // small rack is put directly before the standard one whichever path put them in.
        private static void EnsureInPieceTable()
        {
            var table = ObjectDB.instance
                ?.GetItemPrefab("Hammer")?
                .GetComponent<ItemDrop>()
                ?.m_itemData?.m_shared?.m_buildPieces;
            if (table == null)
            {
                Jotunn.Logger.LogInfo("[Armory] EnsureInPieceTable: Hammer not accessible yet.");
                return;
            }
            var list = table.m_pieces;
            foreach (var kind in Kinds)
            {
                if (kind.Clone == null || list.Contains(kind.Clone)) continue;
                list.Add(kind.Clone);
                Jotunn.Logger.LogInfo($"[Armory] EnsureInPieceTable: inserted {kind.PrefabName}.");
            }
            int small = SmallKind.Clone != null ? list.IndexOf(SmallKind.Clone) : -1;
            int big   = Standard.Clone  != null ? list.IndexOf(Standard.Clone)  : -1;
            if (small >= 0 && big >= 0 && small != big - 1)
            {
                list.RemoveAt(small);
                list.Insert(list.IndexOf(Standard.Clone), SmallKind.Clone);
                Jotunn.Logger.LogInfo("[Armory] EnsureInPieceTable: small rack moved before the standard one.");
            }
        }

        private static Sprite _drawnIcon;

        /// <summary>Photographs the default rack for the hammer menu, the small one at its own
        /// size; the Style settings call this again when they change, so the icons show what
        /// the player will build.</summary>
        internal static void RefreshIcon()
        {
            foreach (var kind in Kinds)
            {
                var piece = kind.Clone != null ? kind.Clone.GetComponent<Piece>() : null;
                if (piece == null) continue;
                var shot = ArmoryPreview.Icon(RackVisual.Defaults, 256, kind.Small ? ArmoryRack.SmallScale : 1f);
                piece.m_icon = shot != null ? shot : _drawnIcon;
            }
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
            var namedPrefabs = GetNamedPrefabs();
            if (namedPrefabs == null) return;
            foreach (var kind in Kinds)
            {
                if (kind.Clone == null) continue;
                var hash = kind.Clone.name.GetStableHashCode();
                if (namedPrefabs.ContainsKey(hash)) continue;
                namedPrefabs[hash] = kind.Clone;
                Jotunn.Logger.LogInfo($"[Armory] EnsureInNamedPrefabs: added {kind.PrefabName}.");
            }
        }
    }
}
