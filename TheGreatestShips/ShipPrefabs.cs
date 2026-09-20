using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace TheGreatestShips
{
    /// <summary>
    /// Creates the ships from their vanilla hulls and keeps them registered.  Prefab lookup and
    /// cloning go through Jotunn's PrefabManager; the Hammer table, ZNetScene and recipes are
    /// handled directly (Jotunn's PieceManager is unsafe on Valheim 1.0 -- see
    /// ForsakenShrinesMod.Awake).
    /// </summary>
    internal static class ShipPrefabs
    {
        private sealed class Built
        {
            public GameObject Clone;
            // The base hull's own sail force, read from its prefab so a game update that retunes
            // the vanilla ship carries over.
            public float BaseSailForceFactor;
        }

        private static readonly Dictionary<ShipDefinition, Built> _built =
            new Dictionary<ShipDefinition, Built>();

        private static readonly FieldInfo _namedPrefabsField =
            AccessTools.Field(typeof(ZNetScene), "m_namedPrefabs");

        // ── Main menu: PrefabManager.OnVanillaPrefabsAvailable ──────────────────────
        internal static void CreateClones()
        {
            foreach (var def in ShipDefinitions.All)
            {
                if (_built.ContainsKey(def)) continue;
                try
                {
                    CreateClone(def);
                }
                catch (System.Exception e)
                {
                    Jotunn.Logger.LogError($"[TheGreatestShips] Creating {def.DisplayName} failed: {e}");
                }
            }

            if (!_vanillaLabeled && ShipConfig.NameVanillaShips.Value)
            {
                _vanillaLabeled = true;
                foreach (var name in VanillaShips)
                {
                    var prefab = PrefabManager.Instance.GetPrefab(name);
                    var shipName = prefab?.GetComponent<Piece>()?.m_name;
                    if (shipName != null)
                        LabelShip(prefab, shipName);
                }
            }
        }

        // Vanilla ships whose rudder and hold are labeled with the ship's name, so every ship can
        // be told apart by looking at its controls.  Piece names are localization tokens
        // ($ship_karve, $ship_longship, $ship_longship_ashlands for the Drakkar, $ship_raft), so the
        // labels follow the player's language.
        private static readonly string[] VanillaShips = { "Raft", "Karve", "VikingShip", "VikingShip_Ashlands" };
        private static bool _vanillaLabeled;
        private const string RudderToken  = "$piece_ship_rudder";   // "Use rudder"
        private const string StorageToken = "$msg_cart_storage";    // "Storage" (the cart's)

        // Hold: "Karve Storage" (vanilla says just "Storage", and not even translated).
        // Rudder: "Use rudder (Karve)" -- the game has no translated plain "Rudder", only the
        // "Use rudder" prompt, so the name goes after it.  Tokens keep both in the player's
        // language; the hold's name is also the title of its inventory panel, which is localized.
        private static void LabelShip(GameObject prefab, string shipName)
        {
            foreach (var container in prefab.GetComponentsInChildren<Container>(true))
                container.m_name = $"{shipName} {StorageToken}";
            foreach (var controls in prefab.GetComponentsInChildren<ShipControlls>(true))
            {
                // Rebuilt from the vanilla token, not appended, so a prefab copied from an
                // already-labeled vanilla ship does not end up with both names.
                controls.m_hoverText = controls.m_hoverText.Contains(RudderToken)
                    ? $"{RudderToken} ({shipName})"
                    : $"{controls.m_hoverText} ({shipName})";
            }
        }

        private static void CreateClone(ShipDefinition def)
        {
            var basePrefab = PrefabManager.Instance.GetPrefab(def.BasePrefab);
            if (basePrefab == null)
            {
                Jotunn.Logger.LogError($"[TheGreatestShips] Base prefab '{def.BasePrefab}' not found; the {def.DisplayName} is unavailable.");
                return;
            }

            var clone = PrefabManager.Instance.CreateClonedPrefab(def.PrefabName, basePrefab);
            if (clone == null)
            {
                Jotunn.Logger.LogError($"[TheGreatestShips] Could not clone '{def.BasePrefab}' as {def.PrefabName}.");
                return;
            }

            var ship = clone.GetComponent<Ship>();
            if (ship == null)
            {
                Jotunn.Logger.LogError($"[TheGreatestShips] '{def.BasePrefab}' has no Ship component.");
                return;
            }

            var piece = clone.GetComponent<Piece>();
            if (piece != null)
            {
                piece.m_name        = def.DisplayName;
                piece.m_description = def.Description;
            }

            var container = clone.GetComponentInChildren<Container>(true);
            if (container != null)
            {
                container.m_width  = def.StorageWidth;
                container.m_height = def.StorageHeight;
            }
            else
            {
                Jotunn.Logger.LogWarning($"[TheGreatestShips] {def.BasePrefab} has no Container; the {def.DisplayName} has no storage.");
            }

            LabelShip(clone, def.DisplayName);

            var cfg = ShipConfig.For(def);
            TintSail(def, clone, cfg.SailColor.Value);
            TintHull(def, clone, cfg.HullColor.Value, cfg.HullStripes.Value);
            SetScale(def, clone, cfg.Scale.Value);
            SetWidth(def, clone, cfg.Width.Value);
            SetLength(def, clone, cfg.Length.Value);

            if (def.Pen != null)
                AnimalPen.Build(clone, def.Pen);

            // Last, once the ship looks the way it will be built: its own Hammer icon.
            var icon = piece != null ? ShipIcons.Render(def, clone) : null;
            if (icon != null) piece.m_icon = icon;

            _built[def] = new Built { Clone = clone, BaseSailForceFactor = ship.m_sailForceFactor };
            ApplyHandling(def);
            Jotunn.Logger.LogInfo($"[TheGreatestShips] Cloned {def.BasePrefab} as {def.PrefabName} ({def.BaseName} sail force {ship.m_sailForceFactor}).");
        }

        // Sail: the Karve leaves Ship.m_sailObject unset, so the sail is found by its material
        // ("sail_white" on ship/mast/Karve_Sail/Karve_Sail, shader Custom/Vegetation).
        private static void TintSail(ShipDefinition def, GameObject clone, Color tint) =>
            Tint(def, clone, tint, 0, "sail",
                (renderer, material) => material.name.StartsWith("sail", System.StringComparison.OrdinalIgnoreCase));

        // Hull: the planks are the renderers named "hull" under visual_new, visual_worn and
        // visual_broken.  The mast and rudder share the hull's material, so matching the renderer
        // rather than the material leaves them bare wood.
        private static void TintHull(ShipDefinition def, GameObject clone, Color tint, int stripes) =>
            Tint(def, clone, tint, stripes, "hull",
                (renderer, material) => renderer.name.StartsWith("hull", System.StringComparison.OrdinalIgnoreCase));

        // Multiplies the tint into copies of the matching materials, so the vanilla ship keeps
        // its own.  One copy per original material, shared by every renderer that used it.
        // With stripes, the tint goes into bands of a copy of the texture instead of the color.
        private static void Tint(ShipDefinition def, GameObject clone, Color tint, int stripes, string part,
                                 System.Func<Renderer, Material, bool> matches)
        {
            if (tint == Color.white) return;

            var copies = new Dictionary<Material, Material>();
            int renderers = 0;
            foreach (var renderer in clone.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < materials.Length; i++)
                {
                    var material = materials[i];
                    if (material == null || !material.HasProperty("_Color") || !matches(renderer, material)) continue;
                    if (!copies.TryGetValue(material, out var copy))
                    {
                        copy = new Material(material) { name = material.name + "_" + def.PrefabName };
                        var striped = stripes > 0 ? StripeTexture(material.mainTexture, tint, stripes) : null;
                        if (striped != null)
                            copy.mainTexture = striped;
                        else
                            copy.color = material.color * tint;
                        copies[material] = copy;
                    }
                    materials[i] = copy;
                    changed = true;
                }
                if (changed)
                {
                    renderer.sharedMaterials = materials;
                    renderers++;
                }
            }
            if (renderers == 0)
                Jotunn.Logger.LogWarning($"[TheGreatestShips] {def.DisplayName}: no {part} found to tint.");
            else
                Jotunn.Logger.LogInfo($"[TheGreatestShips] {def.DisplayName}: tinted {part} on {renderers} renderer(s) ({string.Join(", ", System.Linq.Enumerable.Select(copies.Keys, m => m.name))}).");
        }

        // Copies a texture with every other one of stripes * 2 horizontal bands multiplied by the
        // tint.  Game textures are not CPU-readable, so the copy is rendered and read back from
        // the GPU.  Returns null where that is impossible (a dedicated server has no GPU, and
        // draws nothing anyway); the caller then paints the whole part instead.
        private static Texture2D StripeTexture(Texture source, Color tint, int stripes)
        {
            if (source == null || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return null;

            int w = source.width, h = source.height;
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active;
            Texture2D copy;
            try
            {
                Graphics.Blit(source, rt);
                RenderTexture.active = rt;
                copy = new Texture2D(w, h, TextureFormat.RGBA32, true, false)
                {
                    name       = source.name + "_striped",
                    wrapMode   = source.wrapMode,
                    filterMode = source.filterMode,
                    anisoLevel = source.anisoLevel,
                };
                copy.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
            }

            Color32 t = tint;
            var pixels = copy.GetPixels32();
            int bands = stripes * 2;
            for (int y = 0; y < h; y++)
            {
                if ((y * bands / h) % 2 != 0) continue;
                for (int x = 0, i = y * w; x < w; x++, i++)
                {
                    var p = pixels[i];
                    pixels[i] = new Color32((byte)(p.r * t.r / 255), (byte)(p.g * t.g / 255), (byte)(p.b * t.b / 255), p.a);
                }
            }
            copy.SetPixels32(pixels);
            copy.Apply(true, true);
            return copy;
        }

        // Stretches the whole ship sideways -- hull, deck, seats, ladder, colliders -- so
        // everything on board keeps its place.  Parts that turn (sail, rudder) skew slightly when
        // turned inside a non-uniform scale; at these widths that is hard to see.
        private static void SetWidth(ShipDefinition def, GameObject clone, float width)
        {
            width = Mathf.Clamp(width, 0.5f, 2f);
            if (Mathf.Approximately(width, 1f)) return;

            var scale = clone.transform.localScale;
            scale.x *= width;
            clone.transform.localScale = scale;
            Jotunn.Logger.LogInfo($"[TheGreatestShips] {def.DisplayName}: width x{width}.");
        }

        // The whole ship, all three axes.  Width and Length multiply on top of it.  The game
        // floats a hull by where its center of mass sits against the water level, so a scaled-up
        // hull rides deeper: its deck ends up at about the same height above the water, with
        // more hull beneath.
        private static void SetScale(ShipDefinition def, GameObject clone, float scale)
        {
            scale = Mathf.Clamp(scale, 0.5f, 2f);
            if (Mathf.Approximately(scale, 1f)) return;

            clone.transform.localScale *= scale;
            Jotunn.Logger.LogInfo($"[TheGreatestShips] {def.DisplayName}: scale x{scale}.");
        }

        // Same as SetWidth, but bow-to-stern (local Z, Ship.CustomFixedUpdate's transform.forward)
        // instead of side-to-side (local X).
        private static void SetLength(ShipDefinition def, GameObject clone, float length)
        {
            length = Mathf.Clamp(length, 0.5f, 2f);
            if (Mathf.Approximately(length, 1f)) return;

            var scale = clone.transform.localScale;
            scale.z *= length;
            clone.transform.localScale = scale;
            Jotunn.Logger.LogInfo($"[TheGreatestShips] {def.DisplayName}: length x{length}.");
        }

        // Speed, rudder and health, on the prefab and on every ship already in the world (they
        // keep the values they were spawned with, so a config change, including the server's
        // values arriving after login, is pushed to them too).
        //
        // Top speed is where the sail's push balances forward drag.  Ship.CustomFixedUpdate
        // applies m_sailForceFactor * wind as the push and speed² * m_dampingForward as the drag
        // on every physics step, so top speed ∝ √(m_sailForceFactor / m_dampingForward): the
        // multiplier is squared to scale the push.
        internal static void ApplyHandling(ShipDefinition def)
        {
            if (!_built.TryGetValue(def, out var built) || built.BaseSailForceFactor <= 0f) return;

            var cfg          = ShipConfig.For(def);
            float multiplier = Mathf.Clamp(cfg.Speed.Value, 0.1f, 5f);
            float factor     = built.BaseSailForceFactor * multiplier * multiplier;
            float rudder     = Mathf.Clamp(cfg.RudderSpeed.Value, 0.1f, 5f);
            float health     = Mathf.Max(1f, cfg.Health.Value);

            Apply(built.Clone.GetComponent<Ship>());

            int updated = 0;
            foreach (var updater in Ship.Instances)
            {
                if (updater is Ship ship && Utils.GetPrefabName(ship.gameObject) == def.PrefabName)
                {
                    Apply(ship);
                    updated++;
                }
            }
            Jotunn.Logger.LogInfo($"[TheGreatestShips] {def.DisplayName}: sail force {factor} (x{multiplier} top speed), rudder {rudder}, health {health}; {updated} ship(s) in the world updated.");

            void Apply(Ship ship)
            {
                ship.m_sailForceFactor = factor;
                ship.m_rudderSpeed     = rudder;
                var wnt = ship.GetComponent<WearNTear>() ?? ship.GetComponentInChildren<WearNTear>();
                if (wnt != null) wnt.m_health = health;
            }
        }

        // ── Every world load: ObjectDB.Awake / ZNetScene.Awake postfixes ───────────
        internal static void OnObjectDBAwake()
        {
            // The main menu has its own ObjectDB; only the world scene matters.
            if (SceneManager.GetActiveScene().name != "main") return;
            try
            {
                CreateClones();
                foreach (var def in ShipDefinitions.All)
                    ApplyRecipe(def);
                EnsureInPieceTable();
                EnsureInNamedPrefabs();
                NotifyHudCompass();
            }
            catch (System.Exception e)
            {
                Jotunn.Logger.LogError($"[TheGreatestShips] ObjectDB.Awake hook failed: {e}");
            }
        }

        // Temporary diagnostic (2026-09-20): which layers the camera stops at and which collide
        // with characters, to pick the pen pieces' layer.  Logged once per world load.
        private static bool _probed;
        private static void ProbeLayers()
        {
            if (_probed || GameCamera.instance == null) return;
            _probed = true;
            int mask = GameCamera.instance.m_blockCameraMask.value;
            var parts = new List<string>();
            foreach (int layer in new[] { 0, 9, 10, 14, 15, 16, 28 })
                parts.Add($"{layer}:{LayerMask.LayerToName(layer)} camera={((mask >> layer) & 1) == 1} hitsCharacter={!Physics.GetIgnoreLayerCollision(9, layer)}");
            Jotunn.Logger.LogInfo($"[TheGreatestShips] Camera block mask {mask}; " + string.Join("; ", parts));
        }

        internal static void OnZNetSceneAwake()
        {
            try
            {
                EnsureInNamedPrefabs();
                ProbeLayers();
            }
            catch (System.Exception e)
            {
                Jotunn.Logger.LogError($"[TheGreatestShips] ZNetScene.Awake hook failed: {e}");
            }
        }

        internal static void ApplyRecipe(ShipDefinition def)
        {
            if (!_built.TryGetValue(def, out var built) || ObjectDB.instance == null) return;
            var piece = built.Clone.GetComponent<Piece>();
            if (piece == null) return;

            var requirements = ShipConfig.ParseRecipe(ShipConfig.For(def).Recipe.Value, def.DisplayName);
            if (requirements.Length == 0)
            {
                Jotunn.Logger.LogWarning($"[TheGreatestShips] {def.DisplayName} recipe is empty or invalid; keeping the previous one.");
                return;
            }
            piece.m_resources = requirements;
        }

        // Each ship goes right after its vanilla hull (and after any of ours already there), so
        // the variants sit beside the original in the menu, in definition order.
        private static void EnsureInPieceTable()
        {
            var table = ObjectDB.instance
                ?.GetItemPrefab("Hammer")
                ?.GetComponent<ItemDrop>()
                ?.m_itemData?.m_shared?.m_buildPieces;
            if (table == null) return;

            var ours = new HashSet<GameObject>();
            foreach (var built in _built.Values) ours.Add(built.Clone);

            foreach (var def in ShipDefinitions.All)
            {
                if (!_built.TryGetValue(def, out var built) || table.m_pieces.Contains(built.Clone)) continue;

                int index = table.m_pieces.FindIndex(p => p != null && p.name == def.BasePrefab);
                if (index < 0)
                {
                    table.m_pieces.Add(built.Clone);
                    continue;
                }
                index++;
                while (index < table.m_pieces.Count && ours.Contains(table.m_pieces[index])) index++;
                table.m_pieces.Insert(index, built.Clone);
            }
        }

        private static void EnsureInNamedPrefabs()
        {
            if (ZNetScene.instance == null) return;
            if (!(_namedPrefabsField?.GetValue(ZNetScene.instance) is Dictionary<int, GameObject> namedPrefabs)) return;

            foreach (var built in _built.Values)
            {
                int hash = built.Clone.name.GetStableHashCode();
                if (!namedPrefabs.ContainsKey(hash))
                    namedPrefabs[hash] = built.Clone;
            }
        }

        // Soft dependency on HUDCompass (Neobotics): it builds its one-time list of "which
        // pieces are ships" at the main menu (FejdStartup.Start), before Jotunn's
        // OnVanillaPrefabsAvailable has actually fired for mods -- like this one -- that clone a
        // vanilla hull, so its map/compass pins never include ours. No reference to
        // HUDCompass.dll: found only by scanning loaded assemblies, so nothing breaks if it
        // isn't installed, and a HUDCompass update that renames the method just silently stops
        // the nudge instead of failing to build or load.
        private static bool _hudCompassChecked;
        private static MethodInfo _hudCompassRebuildMarkerTypes;

        private static void NotifyHudCompass()
        {
            if (!_hudCompassChecked)
            {
                _hudCompassChecked = true;
                foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.GetName().Name != "HUDCompass") continue;
                    var type = asm.GetType("neobotics.ValheimMods.DynamicMapMarkers");
                    _hudCompassRebuildMarkerTypes = type?.GetMethod("BuildDynamicMarkerTypes",
                        BindingFlags.NonPublic | BindingFlags.Static);
                    break;
                }
            }
            try
            {
                _hudCompassRebuildMarkerTypes?.Invoke(null, null);
            }
            catch (System.Exception e)
            {
                Jotunn.Logger.LogWarning($"[TheGreatestShips] HUDCompass rebuild failed: {e.Message}");
            }
        }
    }
}
