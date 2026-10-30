using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace TheGreatestShips
{
    /// <summary>
    /// World-load hooks: on every world load put the ships in the Hammer's piece table and in
    /// ZNetScene's prefab registry, and resolve their recipes.  Both callees catch their own
    /// exceptions, since one escaping an Awake postfix would abort the game's initialization.
    /// </summary>
    [HarmonyPatch(typeof(ObjectDB), "Awake")]
    internal static class ObjectDBAwakePatch
    {
        [HarmonyPostfix, HarmonyPriority(Priority.Last)]
        static void Postfix() => ShipPrefabs.OnObjectDBAwake();
    }

    /// <summary>The camera's boat zoom limit, from the config, once the camera exists.</summary>
    [HarmonyPatch(typeof(GameCamera), "Awake")]
    internal static class GameCameraAwakePatch
    {
        [HarmonyPostfix]
        static void Postfix(GameCamera __instance) => SailLook.ApplyCamera(__instance);
    }

    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    internal static class ZNetSceneAwakePatch
    {
        [HarmonyPostfix, HarmonyPriority(Priority.Last)]
        static void Postfix() => ShipPrefabs.OnZNetSceneAwake();
    }

    /// <summary>
    /// The build HUD shows a piece's requirements in a fixed row of slots and puts the crafting
    /// station in the slot after the last ingredient, so a recipe with as many ingredients as
    /// there are slots throws IndexOutOfRange (Hud.SetupPieceInfo) the moment it's selected.
    /// Several ships here have six or seven ingredients.  Grow the row by cloning its last slot
    /// as needed; the clones inherit the slot's layout and are left in place for later pieces.
    /// </summary>
    [HarmonyPatch(typeof(Hud), "SetupPieceInfo")]
    internal static class HudSetupPieceInfoPatch
    {
        static bool _logged;

        [HarmonyPrefix]
        static void Prefix(Hud __instance, Piece piece)
        {
            var slots = __instance.m_requirementItems;
            if (piece == null || piece.m_resources == null || slots == null || slots.Length == 0) return;

            int needed = piece.m_resources.Length + 1; // + the crafting station
            if (needed <= slots.Length) return;

            // The slots are placed by hand, not by a layout group, so a clone lands exactly on
            // top of the slot it was copied from: step each one along by the spacing between the
            // last two existing slots.
            var grown = new List<GameObject>(slots);
            Vector2 step = Vector2.zero;
            if (slots.Length >= 2)
            {
                var a = slots[slots.Length - 2].GetComponent<RectTransform>();
                var b = slots[slots.Length - 1].GetComponent<RectTransform>();
                if (a != null && b != null) step = b.anchoredPosition - a.anchoredPosition;
            }
            while (grown.Count < needed)
            {
                var last  = grown[grown.Count - 1];
                var clone = Object.Instantiate(last, last.transform.parent);
                clone.name = last.name;
                clone.transform.SetSiblingIndex(last.transform.GetSiblingIndex() + 1);
                var rect = clone.GetComponent<RectTransform>();
                if (rect != null) rect.anchoredPosition += step;
                grown.Add(clone);
            }
            __instance.m_requirementItems = grown.ToArray();

            if (!_logged)
            {
                _logged = true;
                Jotunn.Logger.LogInfo($"[TheGreatestShips] Build HUD had {slots.Length} requirement slots; grew it to {grown.Count} for {piece.m_name}.");
            }
        }
    }

    /// <summary>
    /// Items are saved by grid position, and the grid's size is not saved: it comes from the
    /// prefab on every load.  So a hold that changed shape leaves some items outside the new
    /// grid.  Vanilla half-covers this: Container.UpdateRows, run after every load, grows the
    /// inventory's height to the lowest row with an item in it (an 8 x 8 hold that became 11 x 6
    /// came back as 11 x 8), but nothing covers a lost column.  After a hold loads, anything
    /// outside the intended grid (the Container's own width and height) moves into a free slot
    /// inside it, and the height is set back.  Only the owner's copy is touched, and Changed()
    /// makes the owner save it.
    /// </summary>
    [HarmonyPatch(typeof(Container), "Load")]
    internal static class ContainerLoadFitPatch
    {
        static readonly HashSet<string> _ourShips =
            new HashSet<string>(System.Linq.Enumerable.Select(ShipDefinitions.All, d => d.PrefabName));

        // Private in the game (the publicized reference only makes it compile): marks the
        // inventory dirty so the owner saves it.
        static readonly System.Reflection.MethodInfo _changed = AccessTools.Method(typeof(Inventory), "Changed");

        [HarmonyPostfix]
        static void Postfix(Container __instance, bool __result)
        {
            if (!__result || __instance == null || !__instance.IsOwner()) return;
            if (!_ourShips.Contains(Utils.GetPrefabName(__instance.transform.root.gameObject))) return;

            var inventory = __instance.GetInventory();
            if (inventory == null) return;
            int width = __instance.m_width, height = __instance.m_height;   // the intended grid, not the grown one
            var items = inventory.GetAllItems();
            var taken = new HashSet<Vector2i>();
            foreach (var item in items) taken.Add(item.m_gridPos);

            int moved = 0;
            bool stranded = false;
            foreach (var item in new List<ItemDrop.ItemData>(items))
            {
                if (item.m_gridPos.x < width && item.m_gridPos.y < height) continue;
                var slot = new Vector2i(-1, -1);
                for (int y = 0; y < height && slot.x < 0; y++)
                    for (int x = 0; x < width; x++)
                        if (!taken.Contains(new Vector2i(x, y))) { slot = new Vector2i(x, y); break; }
                if (slot.x < 0) { stranded = true; break; }   // no room: leave it where vanilla's grown rows show it
                taken.Remove(item.m_gridPos);
                taken.Add(slot);
                item.m_gridPos = slot;
                moved++;
            }
            if (!stranded && inventory.GetHeight() != height)
                inventory.SetHeight(height);   // undo UpdateRows now that nothing sits below the grid
            if (moved > 0 || inventory.GetHeight() == height)
            {
                if (moved > 0) _changed?.Invoke(inventory, null);
                if (moved > 0)
                    Jotunn.Logger.LogInfo($"[TheGreatestShips] {__instance.m_name}: moved {moved} stack(s) that sat outside the {width}x{height} hold into free slots.");
            }
        }
    }

    /// <summary>
    /// A hold bigger than the inventory panel -- wider than its eight columns, or taller than the
    /// rows it shows, as the Big Busse's 8 x 8 is -- either overflows it (InventoryGrid.UpdateGui
    /// centers the grid, so half a slot falls off each side) or scrolls.  The slots are drawn
    /// smaller instead, square, at whatever scale fits both ways, re-spaced about the same
    /// center; the grid root, which UpdateGui sizes to the full grid every call (that is what
    /// makes it scroll), is sized to the drawn grid.  A hold that fits is left alone.
    ///
    /// Only the container grid, and only while it shows one of this mod's holds: the player's
    /// grid is another mod's to extend (AzuExtendedPlayerInventory lays its extra rows out
    /// itself, and re-spacing them scrambled its equipment slots), and other containers are
    /// their own mods' business.  The hold is recognized by the inventory the grid is showing,
    /// not by the container the player opened: a chest-peeking mod (PeekaChest) previews a hold
    /// by handing its inventory straight to the grid, with nothing opened, and judged by the
    /// opened container the preview drew a 10x5 hold at full size, with a scroll bar.
    /// </summary>
    [HarmonyPatch(typeof(InventoryGrid), "UpdateGui")]
    internal static class InventoryGridFitPatch
    {
        static readonly AccessTools.FieldRef<InventoryGrid, Inventory> _inventory =
            AccessTools.FieldRefAccess<InventoryGrid, Inventory>("m_inventory");

        // Private in the game; the publicized reference assembly only makes them compile, and
        // Mono refuses them at runtime (FieldAccessException, every frame the inventory is open).
        static readonly AccessTools.FieldRef<InventoryGui, InventoryGrid> _containerGrid =
            AccessTools.FieldRefAccess<InventoryGui, InventoryGrid>("m_containerGrid");
        static readonly AccessTools.FieldRef<InventoryGrid, List<InventoryElement>> _elements =
            AccessTools.FieldRefAccess<InventoryGrid, List<InventoryElement>>("m_elements");
        static readonly AccessTools.FieldRef<InventoryGrid, int> _width  = AccessTools.FieldRefAccess<InventoryGrid, int>("m_width");
        static readonly AccessTools.FieldRef<InventoryGrid, int> _height = AccessTools.FieldRefAccess<InventoryGrid, int>("m_height");
        static readonly AccessTools.FieldRef<InventoryGrid, float> _space = AccessTools.FieldRefAccess<InventoryGrid, float>("m_elementSpace");

        [HarmonyPostfix]
        static void Postfix(InventoryGrid __instance)
        {
            var gui = InventoryGui.instance;
            if (gui == null || __instance != _containerGrid(gui)) return;
            if (!ShipHolds.IsOurs(_inventory(__instance))) return;

            var elements = _elements(__instance);
            if (elements == null || elements.Count == 0) return;
            int width  = _width(__instance);
            int height = _height(__instance);
            if (width <= 0 || height <= 0) return;

            var panel = __instance.transform as RectTransform;
            float space      = _space(__instance);
            float gridWidth  = width * space;
            float gridHeight = height * space;
            if (panel == null || panel.rect.width <= 0f || panel.rect.height <= 0f) return;

            float scale = Mathf.Min(1f, panel.rect.width / gridWidth, panel.rect.height / gridHeight);
            if (scale >= 1f) return;

            // UpdateGui just sized the grid root to the full grid; size it to the drawn one so
            // nothing is left to scroll.
            if (__instance.m_gridRoot != null)
                __instance.m_gridRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, gridHeight * scale);

            var first = elements[0].transform as RectTransform;
            if (first == null || Mathf.Approximately(first.localScale.x, scale)) return; // already fitted

            float start = panel.rect.width / 2f - gridWidth * scale / 2f;
            for (int i = 0; i < elements.Count; i++)
            {
                var rect = elements[i].transform as RectTransform;
                if (rect == null) continue;
                int x = i % width, y = i / width;
                rect.localScale       = new Vector3(scale, scale, 1f);
                rect.anchoredPosition = new Vector2(start + x * space * scale, -y * space * scale);
            }
            Jotunn.Logger.LogInfo($"[TheGreatestShips] Inventory grid {width}x{height} is bigger than its panel ({panel.rect.width:0}x{panel.rect.height:0}); slots drawn at {scale:0.00} scale to fit.");
        }
    }

    /// <summary>
    /// Which inventories are this mod's holds.  A ship's Container makes its Inventory once, in
    /// Awake, and keeps that object for life (loading fills it, it does not replace it), so the
    /// inventory identifies the hold wherever it turns up -- in the open-container GUI or in a
    /// peek preview.  Entries of ships since destroyed are dropped when next looked at.
    /// </summary>
    [HarmonyPatch(typeof(Container), "Awake")]
    internal static class ShipHolds
    {
        static readonly HashSet<string> _ourShips =
            new HashSet<string>(System.Linq.Enumerable.Select(ShipDefinitions.All, d => d.PrefabName));
        static readonly Dictionary<Inventory, Container> _holds = new Dictionary<Inventory, Container>();

        [HarmonyPostfix]
        static void Postfix(Container __instance)
        {
            if (__instance == null) return;
            var inventory = __instance.GetInventory();
            if (inventory == null) return;
            if (_ourShips.Contains(Utils.GetPrefabName(__instance.transform.root.gameObject)))
                _holds[inventory] = __instance;
        }

        internal static bool IsOurs(Inventory inventory)
        {
            if (inventory == null || !_holds.TryGetValue(inventory, out var container)) return false;
            if (container != null) return true;
            _holds.Remove(inventory);   // the ship is gone; the inventory is a stale key
            return false;
        }
    }

    /// <summary>
    /// A ship's ImpactEffect deals its ramming damage (the longship's is 50 blunt, scaled by
    /// speed) to any creature one of its colliders strikes above 1.5 m/s -- and every rail, the
    /// mast and the deck are the ship.  A tamed boar has 10 hit points.  A calm animal moves with
    /// the deck and is never struck; a frightened one runs, and on a ship under sail the mast or
    /// the bow rail meets it at the ship's full speed: one hit, dead.  Players are spared by a
    /// flag on the effect; livestock is not.  On this mod's ships, a tamed creature is: the hit
    /// is skipped outright, knockback included.  Wild creatures are rammed as in vanilla.
    /// </summary>
    [HarmonyPatch(typeof(ImpactEffect), "OnCollisionEnter")]
    internal static class ImpactEffectSparesLivestockPatch
    {
        static readonly HashSet<string> _ourShips =
            new HashSet<string>(System.Linq.Enumerable.Select(ShipDefinitions.All, d => d.PrefabName));

        [HarmonyPrefix]
        static bool Prefix(ImpactEffect __instance, Collision info)
        {
            if (info == null || info.contactCount == 0) return true;

            string name = __instance.name;
            int clone = name.IndexOf('(');
            if (clone > 0) name = name.Substring(0, clone).Trim();
            if (!_ourShips.Contains(name)) return true;

            var other = info.GetContact(0).otherCollider;
            var hit = other != null ? Projectile.FindHitObject(other) : null;
            var character = hit != null ? hit.GetComponent<Character>() : null;
            return character == null || !character.IsTamed();
        }
    }

    // ── Temporary diagnostic (2026-10-19): what damages a ship with a lox aboard ──────────
    // Every damage event that reaches one of this mod's ships, and every physics contact that
    // reaches its impact handler, written to the log.  Remove once the lox question is settled.
    [HarmonyPatch(typeof(WearNTear), "RPC_Damage")]
    internal static class ShipDamageDiagnostic
    {
        static readonly HashSet<string> _ourShips =
            new HashSet<string>(System.Linq.Enumerable.Select(ShipDefinitions.All, d => d.PrefabName));
        static readonly AccessTools.FieldRef<MonsterAI, Character> _targetCreature =
            AccessTools.FieldRefAccess<MonsterAI, Character>("m_targetCreature");

        [HarmonyPrefix]
        static void Prefix(WearNTear __instance, HitData hit)
        {
            if (__instance == null || hit == null) return;
            string prefab = Utils.GetPrefabName(__instance.transform.root.gameObject);
            if (!_ourShips.Contains(prefab)) return;
            var body = __instance.GetComponentInParent<Rigidbody>();
            string nearby = "";
            foreach (var c in Character.GetAllCharacters())
                if (c != null && !c.IsPlayer() && Vector3.Distance(c.transform.position, hit.m_point) < 12f)
                {
                    var ai = c.GetComponent<MonsterAI>();
                    var target = ai != null ? _targetCreature(ai) : null;
                    var stat   = ai != null ? ai.GetStaticTarget() : null;
                    nearby += $"{Utils.GetPrefabName(c.gameObject)}{(c.IsTamed() ? "(tamed)" : "")} {Vector3.Distance(c.transform.position, hit.m_point):0.0}m"
                        + $"{(c.InAttack() ? " attacking" : "")}{(target != null ? " target=" + Utils.GetPrefabName(target.gameObject) : "")}"
                        + $"{(stat != null ? " static=" + stat.name + "/" + Utils.GetPrefabName(stat.transform.root.gameObject) : "")}; ";
                }
            var attacker = hit.GetAttacker();
            string who = hit.m_attacker.ToString();
            if (attacker != null)
            {
                var weapon = (attacker as Humanoid)?.GetCurrentWeapon();
                string aoes = "";
                foreach (var aoe in Object.FindObjectsByType<Aoe>(FindObjectsSortMode.None))
                    if (aoe != null && TamedAoeDiagnostic.Owner(aoe) == attacker) aoes += aoe.name + ",";
                who = $"{Utils.GetPrefabName(attacker.gameObject)}{(attacker.IsTamed() ? "(tamed)" : "")} {Vector3.Distance(attacker.transform.position, hit.m_point):0.0}m from the hit"
                    + $" inAttack={attacker.InAttack()} weapon={(weapon != null ? weapon.m_shared.m_name : "-")} aoes={(aoes == "" ? "none" : aoes)}";
            }
            var d = hit.m_damage;
            Jotunn.Logger.LogInfo($"[TheGreatestShips][diag] {prefab} damaged: type={hit.m_hitType} total={hit.GetTotalDamage():0.#} "
                + $"(blunt {d.m_blunt:0.#} slash {d.m_slash:0.#} pierce {d.m_pierce:0.#} chop {d.m_chop:0.#} pickaxe {d.m_pickaxe:0.#}) "
                + $"collider={(hit.m_hitCollider != null ? hit.m_hitCollider.name : "-")} attacker={who} "
                + $"speed={(body != null ? body.linearVelocity.magnitude : 0f):0.0} m/s creatures within 12 m: {(nearby == "" ? "none" : nearby)}");
        }
    }

    // Once per world load: the longship's colliders and fittings in hull coordinates, to lay
    // out a pen that clears the benches and the chest.  Temporary, with the rest of the diagnostic.
    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    internal static class ShipLayoutDiagnostic
    {
        static bool _done;

        [HarmonyPostfix, HarmonyPriority(Priority.Last)]
        static void Postfix(ZNetScene __instance)
        {
            if (_done || __instance == null) return;
            var ship = __instance.GetPrefab("VikingShip");
            if (ship == null) return;
            _done = true;
            try
            {
                var root = ship.transform;
                foreach (var col in ship.GetComponentsInChildren<Collider>(true))
                {
                    var t = col.transform;
                    string shape;
                    Vector3 center, size;
                    if (col is BoxCollider b) { shape = "box"; center = b.center; size = Vector3.Scale(b.size, t.lossyScale); }
                    else if (col is CapsuleCollider cc) { shape = "capsule"; center = cc.center; size = new Vector3(cc.radius * 2f, cc.height, cc.radius * 2f); }
                    else if (col is MeshCollider mc && mc.sharedMesh != null) { shape = mc.convex ? "mesh(convex)" : "mesh"; center = mc.sharedMesh.bounds.center; size = Vector3.Scale(mc.sharedMesh.bounds.size, t.lossyScale); }
                    else { shape = col.GetType().Name; center = Vector3.zero; size = Vector3.zero; }
                    var c = root.InverseTransformPoint(t.TransformPoint(center));
                    var rot = (Quaternion.Inverse(root.rotation) * t.rotation).eulerAngles;
                    Jotunn.Logger.LogInfo($"[TheGreatestShips][layout] {Path(t, root)} {shape} layer={LayerMask.LayerToName(col.gameObject.layer)} trigger={col.isTrigger} active={col.gameObject.activeInHierarchy} "
                        + $"center=({c.x:0.00}, {c.y:0.00}, {c.z:0.00}) size=({size.x:0.00}, {size.y:0.00}, {size.z:0.00}) rot=({rot.x:0}, {rot.y:0}, {rot.z:0})");
                }
                foreach (var mb in ship.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (mb == null) continue;
                    if (!(mb is Chair) && !(mb is Container) && !(mb is ShipControlls) && !(mb is Ladder) && !(mb is Ship) && !(mb is ImpactEffect)) continue;
                    var c = root.InverseTransformPoint(mb.transform.position);
                    Jotunn.Logger.LogInfo($"[TheGreatestShips][layout] {Path(mb.transform, root)} {mb.GetType().Name} at ({c.x:0.00}, {c.y:0.00}, {c.z:0.00})");
                }
                var sh = ship.GetComponent<Ship>();
                if (sh != null)
                {
                    foreach (var (name, obj) in new[] { ("sail", sh.m_sailObject), ("mast", sh.m_mastObject), ("rudder", sh.m_rudderObject) })
                        if (obj != null) { var c = root.InverseTransformPoint(obj.transform.position); Jotunn.Logger.LogInfo($"[TheGreatestShips][layout] {name} {Path(obj.transform, root)} at ({c.x:0.00}, {c.y:0.00}, {c.z:0.00})"); }
                    Jotunn.Logger.LogInfo($"[TheGreatestShips][layout] Ship: stearForceOffset={sh.m_stearForceOffset} stearForce={sh.m_stearForce} stearVelForceFactor={sh.m_stearVelForceFactor} backwardForce={sh.m_backwardForce} rudderSpeed={sh.m_rudderSpeed} angularDamping={sh.m_angularDamping} dampingForward={sh.m_dampingForward} dampingSideway={sh.m_dampingSideway}");
                }
                var body = ship.GetComponent<Rigidbody>();
                if (body != null) Jotunn.Logger.LogInfo($"[TheGreatestShips][layout] Rigidbody mass={body.mass} com=({body.centerOfMass.x:0.00}, {body.centerOfMass.y:0.00}, {body.centerOfMass.z:0.00}) inertia=({body.inertiaTensor.x:0}, {body.inertiaTensor.y:0}, {body.inertiaTensor.z:0}) angularDrag={body.angularDamping}");
            }
            catch (System.Exception e) { Jotunn.Logger.LogWarning($"[TheGreatestShips][layout] dump failed: {e.Message}"); }
        }

        static string Path(Transform t, Transform root)
        {
            string path = t.name;
            for (var p = t.parent; p != null && p != root; p = p.parent) path = p.name + "/" + path;
            return path;
        }
    }

    // Every attack a tamed creature starts, with the call path: the AI's DoAttack, or something else.
    [HarmonyPatch(typeof(Humanoid), "StartAttack")]
    internal static class TamedAttackDiagnostic
    {
        [HarmonyPrefix]
        static void Prefix(Humanoid __instance, Character target)
        {
            if (__instance == null || !__instance.IsTamed()) return;
            var weapon = __instance.GetCurrentWeapon();
            Jotunn.Logger.LogInfo($"[TheGreatestShips][diag] {Utils.GetPrefabName(__instance.gameObject)}(tamed) starts an attack: target={(target != null ? Utils.GetPrefabName(target.gameObject) : "none")} "
                + $"weapon={(weapon != null ? weapon.m_shared.m_name : "-")} inAttack={__instance.InAttack()}\n{Frames()}");
        }

        internal static string Frames()
        {
            var trace = new System.Diagnostics.StackTrace(2, false);
            var sb = new System.Text.StringBuilder();
            int n = 0;
            foreach (var frame in trace.GetFrames())
            {
                var m = frame.GetMethod();
                if (m == null) continue;
                sb.Append("    ").Append(m.DeclaringType?.Name).Append('.').Append(m.Name).Append('\n');
                if (++n >= 12) break;
            }
            return sb.ToString();
        }
    }

    [HarmonyPatch(typeof(Aoe), "Setup")]
    internal static class TamedAoeDiagnostic
    {
        static readonly AccessTools.FieldRef<Aoe, Character> _owner = AccessTools.FieldRefAccess<Aoe, Character>("m_owner");
        internal static Character Owner(Aoe aoe) => _owner(aoe);

        [HarmonyPrefix]
        static void Prefix(Aoe __instance, Character owner)
        {
            if (__instance == null || owner == null || !owner.IsTamed()) return;
            var d = __instance.m_damage;
            Jotunn.Logger.LogInfo($"[TheGreatestShips][diag] {Utils.GetPrefabName(owner.gameObject)}(tamed) gets an Aoe {__instance.name}: blunt {d.m_blunt:0.#} chop {d.m_chop:0.#} "
                + $"triggers={__instance.m_useTriggers} interval={__instance.m_hitInterval} ttl={__instance.m_ttl} attached={__instance.m_attachToCaster}\n{TamedAttackDiagnostic.Frames()}");
        }
    }

    // Once per world load: what the Lox carries -- its weapons' damage and AI settings, any Aoe
    // on it -- so the 20 blunt + 40 chop blows can be matched to a weapon.
    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    internal static class LoxLayoutDiagnostic
    {
        static bool _done;

        [HarmonyPostfix, HarmonyPriority(Priority.Last)]
        static void Postfix(ZNetScene __instance)
        {
            if (_done || __instance == null) return;
            var lox = __instance.GetPrefab("Lox");
            if (lox == null) return;
            _done = true;
            try
            {
                var humanoid = lox.GetComponent<Humanoid>();
                if (humanoid != null && humanoid.m_defaultItems != null)
                    foreach (var item in humanoid.m_defaultItems)
                    {
                        var drop = item != null ? item.GetComponent<ItemDrop>() : null;
                        if (drop == null) continue;
                        var sh = drop.m_itemData.m_shared;
                        var a = sh.m_attack;
                        Jotunn.Logger.LogInfo($"[TheGreatestShips][lox] weapon {item.name}: blunt {sh.m_damages.m_blunt:0.#} slash {sh.m_damages.m_slash:0.#} pierce {sh.m_damages.m_pierce:0.#} chop {sh.m_damages.m_chop:0.#} pickaxe {sh.m_damages.m_pickaxe:0.#} "
                            + $"aiTarget={sh.m_aiTargetType} aiRange={sh.m_aiAttackRange} attackType={a?.m_attackType} range={a?.m_attackRange} height={a?.m_attackHeight} "
                            + $"spawnOnTrigger={(a?.m_spawnOnTrigger != null ? a.m_spawnOnTrigger.name : "-")} projectile={(a?.m_attackProjectile != null ? a.m_attackProjectile.name : "-")}");
                    }
                foreach (var aoe in lox.GetComponentsInChildren<Aoe>(true))
                    Jotunn.Logger.LogInfo($"[TheGreatestShips][lox] Aoe {aoe.name}: blunt {aoe.m_damage.m_blunt:0.#} chop {aoe.m_damage.m_chop:0.#} triggers={aoe.m_useTriggers} interval={aoe.m_hitInterval} ttl={aoe.m_ttl} attached={aoe.m_attachToCaster} active={aoe.gameObject.activeSelf}");
                foreach (var col in lox.GetComponentsInChildren<Collider>(true))
                {
                    string shape = col is CapsuleCollider cc ? $"capsule r={cc.radius} h={cc.height} center={cc.center}" : col is BoxCollider b ? $"box size={b.size} center={b.center}" : col.GetType().Name;
                    Jotunn.Logger.LogInfo($"[TheGreatestShips][lox] collider {col.name}: {shape} layer={LayerMask.LayerToName(col.gameObject.layer)} trigger={col.isTrigger}");
                }
                var ai = lox.GetComponent<MonsterAI>();
                if (ai != null) Jotunn.Logger.LogInfo($"[TheGreatestShips][lox] MonsterAI attackPlayerObjects={ai.m_attackPlayerObjects} viewRange={ai.m_viewRange} alertRange={ai.m_alertRange}");
                var body = lox.GetComponent<Rigidbody>();
                if (body != null) Jotunn.Logger.LogInfo($"[TheGreatestShips][lox] Rigidbody mass={body.mass} scale={lox.transform.localScale}");
            }
            catch (System.Exception e) { Jotunn.Logger.LogWarning($"[TheGreatestShips][lox] dump failed: {e.Message}"); }
        }
    }

    // Every hit a tamed creature takes: if the lox is hurt aboard, by what.
    [HarmonyPatch(typeof(Character), "RPC_Damage")]
    internal static class TamedDamageDiagnostic
    {
        [HarmonyPrefix]
        static void Prefix(Character __instance, HitData hit)
        {
            if (__instance == null || hit == null || !__instance.IsTamed()) return;
            var attacker = hit.GetAttacker();
            Jotunn.Logger.LogInfo($"[TheGreatestShips][diag] {Utils.GetPrefabName(__instance.gameObject)}(tamed) hurt: type={hit.m_hitType} total={hit.GetTotalDamage():0.#} "
                + $"attacker={(attacker != null ? Utils.GetPrefabName(attacker.gameObject) : hit.m_attacker.ToString())} collider={(hit.m_hitCollider != null ? hit.m_hitCollider.name : "-")} health={__instance.GetHealth():0}");
        }
    }

    [HarmonyPatch(typeof(ImpactEffect), "OnCollisionEnter")]
    internal static class ShipContactDiagnostic
    {
        static readonly HashSet<string> _ourShips =
            new HashSet<string>(System.Linq.Enumerable.Select(ShipDefinitions.All, d => d.PrefabName));

        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        static void Prefix(ImpactEffect __instance, Collision info)
        {
            if (__instance == null || info == null || info.contactCount == 0) return;
            string name = __instance.name;
            int clone = name.IndexOf('(');
            if (clone > 0) name = name.Substring(0, clone).Trim();
            if (!_ourShips.Contains(name)) return;
            if (info.relativeVelocity.magnitude < __instance.m_minVelocity) return;   // vanilla ignores these too
            var other = info.GetContact(0).otherCollider;
            var hit = other != null ? Projectile.FindHitObject(other) : null;
            var character = hit != null ? hit.GetComponent<Character>() : null;
            Jotunn.Logger.LogInfo($"[TheGreatestShips][diag] {name} contact: other={(other != null ? other.name : "-")} root={(hit != null ? hit.name : "-")} "
                + $"layer={(other != null ? LayerMask.LayerToName(other.gameObject.layer) : "-")} relVel={info.relativeVelocity.magnitude:0.0} "
                + $"character={(character != null ? Utils.GetPrefabName(character.gameObject) + (character.IsTamed() ? "(tamed)" : "(wild)") + $" r={character.GetRadius():0.00} h={character.GetHeight():0.00}" : "none")} ourCollider={info.GetContact(0).thisCollider.name}");
        }
    }
}
