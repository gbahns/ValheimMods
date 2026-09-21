using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Jotunn.Managers;
using UnityEngine;

namespace Armory
{
    /// <summary>
    /// Reads the player's current equipment into a LoadoutSlot and applies a saved
    /// LoadoutSlot back to the player. Items must already be in the player's inventory —
    /// nothing is created for free.
    /// </summary>
    internal static class LoadoutManager
    {
        // Cached accessors for Humanoid's protected equipment fields.  Our publicized DLL
        // marks them public so the code compiles, but at runtime the live assembly_valheim.dll
        // still has them protected — direct field access then throws FieldAccessException.
        // FieldRefAccess builds a fast delegate that bypasses the JIT visibility check.
        // Public so the UI layer can read current equipment for "is this loadout active?" checks.
        public static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> F_Helmet   = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_helmetItem");
        public static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> F_Chest    = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_chestItem");
        public static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> F_Leg      = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_legItem");
        public static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> F_Shoulder = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_shoulderItem");
        public static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> F_Utility  = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_utilityItem");
        public static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> F_Right    = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_rightItem");
        public static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> F_Left     = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_leftItem");

        /// <summary>
        /// True if the player is currently wearing exactly what the loadout has saved
        /// (matching SharedName + Quality on every equipped slot, including null vs null),
        /// AND every hotbar slot that the loadout has something saved for matches the
        /// player's current hotbar at that position.  Extra items in hotbar positions the
        /// loadout didn't specify are ignored.
        /// </summary>
        public static bool IsLoadoutActive(LoadoutSlot slot, Player player)
        {
            if (slot == null || player == null || slot.IsEmpty()) return false;

            // Equipped armor / weapons / shield must match exactly.
            if (!ItemMatches(slot.Helmet,    F_Helmet(player))) return false;
            if (!ItemMatches(slot.Chest,     F_Chest(player)))  return false;
            if (!ItemMatches(slot.Legs,      F_Leg(player)))    return false;
            if (!ItemMatches(slot.Shoulder,  F_Shoulder(player))) return false;
            if (!ItemMatches(slot.Utility,   F_Utility(player))) return false;
            if (!ItemMatches(slot.RightHand, F_Right(player)))  return false;
            if (!ItemMatches(slot.LeftHand,  F_Left(player)))   return false;

            // Hotbar: every position the loadout has SAVED something must have a matching item
            // at that position in the player's inventory.  Positions the loadout left empty
            // don't constrain the player (extra stuff in those slots is fine).
            var inv = player.GetInventory();
            if (slot.Hotbar != null)
            {
                for (int x = 0; x < 8 && x < slot.Hotbar.Length; x++)
                {
                    var saved = slot.Hotbar[x];
                    if (saved == null || string.IsNullOrEmpty(saved.SharedName)) continue;
                    var current = inv?.GetItemAt(x, 0);
                    if (!ItemMatches(saved, current)) return false;
                }
            }

            // Extended slots: same rule, but at the saved (GridX, GridY) outside the standard bag.
            if (slot.Extended != null && inv != null)
            {
                foreach (var saved in slot.Extended)
                {
                    if (saved == null || string.IsNullOrEmpty(saved.SharedName)) continue;
                    if (saved.GridX < 0 || saved.GridY < 0) continue;
                    var current = inv.GetItemAt(saved.GridX, saved.GridY);
                    if (!ItemMatches(saved, current)) return false;
                }
            }
            return true;
        }

        private static bool ItemMatches(SavedItem saved, ItemDrop.ItemData equipped)
        {
            if (saved == null && equipped == null) return true;
            if (saved == null || equipped == null) return false;
            return equipped.m_shared.m_name == saved.SharedName
                && equipped.m_quality       == saved.Quality;
        }

        // ── Capture ────────────────────────────────────────────────────────────────

        public static LoadoutSlot CaptureCurrentEquipment(Player player)
        {
            var inv  = player.GetInventory();
            var slot = new LoadoutSlot
            {
                Name      = "New Loadout",
                Helmet    = Serialize(F_Helmet(player)),
                Chest     = Serialize(F_Chest(player)),
                Legs      = Serialize(F_Leg(player)),
                Shoulder  = Serialize(F_Shoulder(player)),
                Utility   = Serialize(F_Utility(player)),
                RightHand = Serialize(F_Right(player)),
                LeftHand  = Serialize(F_Left(player)),
                Hotbar    = new SavedItem[8],
            };
            // The hotbar is the top row of the grid, y=0; the game reads hotkey N from (N-1, 0).
            for (int x = 0; x < 8; x++)
                slot.Hotbar[x] = Serialize(inv?.GetItemAt(x, 0));

            // Dedicated cells added by AzuExtendedPlayerInventory: its quick slots and its
            // equipment row.  Equipped items there are already covered by the Helmet/Chest/etc
            // paths above, so they are excluded to avoid double-handling on Load.  Nothing else
            // in the grid is captured — the rest of the bag is the player's, not the loadout's.
            slot.Extended = CaptureExtendedItems(player, inv);

            if (inv != null)
            {
                Jotunn.Logger.LogInfo($"[Armory] Capture: {inv.GetWidth()}x{inv.GetHeight()} inventory, {inv.GetAllItems().Count} item(s); " +
                                      $"hotbar row 0, Azu cells {(AzuCompat.IsAvailable ? "queried" : "unavailable")}");
                foreach (var ext in slot.Extended)
                    Jotunn.Logger.LogInfo($"[Armory]   captured ({ext.GridX},{ext.GridY}) {ext.SharedName}  stack={ext.Stack}");
            }

            return slot;
        }

        // Scan the inventory for items sitting in cells Azu reserves (quick slots, equipment
        // row).  Skip anything that's currently equipped — those are captured by the
        // Helmet/Chest/Legs/... paths.  Returns each saved item with its (GridX, GridY).
        //
        // The test is Azu's own, per cell, never "is this outside 8×4": Azu's "Extra Inventory
        // Rows" option adds ordinary bag rows below the vanilla four, and a row-number rule
        // swept up whatever was lying there — resin, nails, arrows — as part of the loadout.
        private static List<SavedItem> CaptureExtendedItems(Player player, Inventory inv)
        {
            var result = new List<SavedItem>();
            if (inv == null || !AzuCompat.IsAvailable) return result;

            var equipped = new HashSet<ItemDrop.ItemData>();
            void TrackEquipped(ItemDrop.ItemData i) { if (i != null) equipped.Add(i); }
            TrackEquipped(F_Helmet(player));   TrackEquipped(F_Chest(player));
            TrackEquipped(F_Leg(player));      TrackEquipped(F_Shoulder(player));
            TrackEquipped(F_Utility(player));  TrackEquipped(F_Right(player));
            TrackEquipped(F_Left(player));

            foreach (var item in inv.GetAllItems())
            {
                if (item == null || item.m_stack <= 0) continue;
                if (equipped.Contains(item)) continue;
                int x = item.m_gridPos.x, y = item.m_gridPos.y;
                if (y == 0 && x < 8) continue;                     // hotbar, captured above
                if (!AzuCompat.IsDedicatedCell(inv, x, y)) continue;
                var saved = Serialize(item);
                if (saved == null) continue;
                saved.GridX = x;
                saved.GridY = y;
                result.Add(saved);
            }
            return result;
        }

        private static SavedItem Serialize(ItemDrop.ItemData item)
        {
            if (item == null) return null;
            return new SavedItem
            {
                SharedName = item.m_shared.m_name,
                Quality    = item.m_quality,
                Variant    = item.m_variant,
                Stack      = item.m_stack,
            };
        }

        // ── Apply ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// Unequips all current gear then equips each item found in the player's inventory or
        /// the rack's storage (if provided).  Items in the rack are moved into the player's
        /// inventory first, since Valheim requires items to be in the player's inventory to
        /// equip them.  Returns (equippedCount, missingCount).
        /// </summary>
        public static (int found, int missing) ApplyLoadout(Player player, LoadoutSlot slot, Inventory rackInv = null)
        {
            var playerInv = player.GetInventory();

            // Snapshot every currently-equipped item BEFORE unequip so we know each slot's
            // grid position.  When we later pull a replacement from the rack we move the old
            // item to that rack position — i.e. items swap places exactly.
            var oldHelmet   = F_Helmet(player);
            var oldChest    = F_Chest(player);
            var oldLegs     = F_Leg(player);
            var oldShoulder = F_Shoulder(player);
            var oldUtility  = F_Utility(player);
            var oldRight    = F_Right(player);
            var oldLeft     = F_Left(player);

            if (oldHelmet   != null) player.UnequipItem(oldHelmet,   false);
            if (oldChest    != null) player.UnequipItem(oldChest,    false);
            if (oldLegs     != null) player.UnequipItem(oldLegs,     false);
            if (oldShoulder != null) player.UnequipItem(oldShoulder, false);
            if (oldUtility  != null) player.UnequipItem(oldUtility,  false);
            if (oldRight    != null) player.UnequipItem(oldRight,    false);
            if (oldLeft     != null) player.UnequipItem(oldLeft,     false);

            int found = 0, missing = 0;
            TryEquip(player, playerInv, rackInv, slot.Helmet,    oldHelmet,   ref found, ref missing);
            TryEquip(player, playerInv, rackInv, slot.Chest,     oldChest,    ref found, ref missing);
            TryEquip(player, playerInv, rackInv, slot.Legs,      oldLegs,     ref found, ref missing);
            TryEquip(player, playerInv, rackInv, slot.Shoulder,  oldShoulder, ref found, ref missing);
            TryEquip(player, playerInv, rackInv, slot.Utility,   oldUtility,  ref found, ref missing);
            TryEquip(player, playerInv, rackInv, slot.RightHand, oldRight,    ref found, ref missing);
            TryEquip(player, playerInv, rackInv, slot.LeftHand,  oldLeft,     ref found, ref missing);

            // Restore extended-inventory items (Azu food/potion/trinket slots) BEFORE the hotbar
            // pass — extended slots have fixed positions assigned by Azu, and moving items there
            // first means any subsequent hotbar swaps don't accidentally land an item in an
            // extended slot's target position.
            if (slot.Extended != null)
            {
                foreach (var saved in slot.Extended)
                {
                    if (saved == null || string.IsNullOrEmpty(saved.SharedName)) continue;
                    int destX = saved.GridX, destY = saved.GridY;
                    if (destX < 0 || destY < 0) continue;  // missing position info — can't restore

                    var (newItem, source) = FindItem(saved, playerInv, rackInv);
                    if (newItem == null) { missing++; continue; }

                    if (source == playerInv && newItem.m_gridPos.x == destX && newItem.m_gridPos.y == destY)
                    {
                        found++;
                        continue;
                    }

                    var oldOccupant = playerInv.GetItemAt(destX, destY);
                    if (oldOccupant == newItem) { found++; continue; }
                    try
                    {
                        SwapItemInto(playerInv, source, newItem, destX, destY, oldOccupant);
                        found++;
                    }
                    catch (System.Exception e)
                    {
                        Jotunn.Logger.LogWarning($"[Armory] Extended-slot move failed for '{saved.SharedName}' → ({destX},{destY}): {e.Message}");
                        missing++;
                    }
                }
            }

            // Restore the hotbar (bottom row, y=0).  For each saved slot, find a matching item
            // (player inv first, then rack) and move it to that hotbar position.  Whatever was
            // previously at the target hotbar slot gets stowed at the new item's old position.
            if (slot.Hotbar != null)
            {
                for (int x = 0; x < 8 && x < slot.Hotbar.Length; x++)
                {
                    var saved = slot.Hotbar[x];
                    if (saved == null || string.IsNullOrEmpty(saved.SharedName)) continue;

                    var (newItem, source) = FindItem(saved, playerInv, rackInv);
                    if (newItem == null) { missing++; continue; }

                    if (source == playerInv && newItem.m_gridPos.x == x && newItem.m_gridPos.y == 0)
                    {
                        found++;
                        continue;
                    }

                    var oldOccupant = playerInv.GetItemAt(x, 0);
                    if (oldOccupant == newItem) { found++; continue; }
                    try
                    {
                        SwapItemInto(playerInv, source, newItem, x, 0, oldOccupant);
                        found++;
                    }
                    catch (System.Exception e)
                    {
                        Jotunn.Logger.LogWarning($"[Armory] Hotbar move failed for slot {x}: {e.Message}");
                        missing++;
                    }
                }
            }

            return (found, missing);
        }

        // Attempts to equip the saved item, pulling it from wherever it lives (player inv or
        // rack).  If a different item was previously equipped in this slot, that item is moved
        // into the exact slot the new item came from — items swap places, no slot left "wrong".
        private static void TryEquip(Player player, Inventory playerInv, Inventory rackInv,
                                     SavedItem saved, ItemDrop.ItemData oldEquipped,
                                     ref int found, ref int missing)
        {
            if (saved == null) return;
            var (newItem, source) = FindItem(saved, playerInv, rackInv);
            if (newItem == null) { missing++; return; }

            // Same item is still here — just re-equip and we're done.
            if (oldEquipped == newItem)
            {
                player.EquipItem(newItem, false);
                found++;
                return;
            }

            // If nothing is currently equipped in this slot but there's still an item of the
            // same equipment type sitting in the player's inventory (e.g. occupying Azu Extended
            // Player Inventory's dedicated slot), treat that as the item being displaced and
            // swap it to the new item's source position.  Without this, that item would be
            // left in place and the player would end up with two of the same slot type in
            // their inventory.
            if (oldEquipped == null)
                oldEquipped = FindDisplacedSameType(playerInv, newItem);

            try
            {
                // newItem can be anywhere; we just need it in playerInv to equip.  If there's
                // an old item to swap with, SwapItemInto puts oldEquipped at newItem's source
                // position.  If no old item, newItem just moves to the first empty slot.
                var targetPlayerSlot = oldEquipped != null
                    ? oldEquipped.m_gridPos
                    : FindFirstEmptySlot(playerInv);
                if (targetPlayerSlot.x < 0)
                {
                    Jotunn.Logger.LogWarning($"[Armory] Player inv full — can't pull '{saved.SharedName}'");
                    missing++;
                    return;
                }
                SwapItemInto(playerInv, source, newItem, targetPlayerSlot.x, targetPlayerSlot.y, oldEquipped);
            }
            catch (System.Exception e)
            {
                Jotunn.Logger.LogWarning($"[Armory] TryEquip swap failed for '{saved.SharedName}': {e.Message}");
                missing++;
                return;
            }

            // Re-find the item by name in player inv (its reference may have been replaced).
            var equipMe = FindMatching(playerInv, saved) ?? newItem;
            player.EquipItem(equipMe, false);
            found++;
        }

        // Find any item in the player inventory with the same equipment-slot semantics as
        // newItem (same m_itemType), excluding newItem itself.  This catches items sitting in
        // an extended inventory slot (Azu's dedicated helmet/chest/legs/cape/utility slots)
        // that aren't currently equipped but still occupy the "slot" visually.
        private static ItemDrop.ItemData FindDisplacedSameType(Inventory inv, ItemDrop.ItemData newItem)
        {
            if (inv == null || newItem == null) return null;
            var newType = newItem.m_shared?.m_itemType;
            return inv.GetAllItems().FirstOrDefault(i =>
                i != newItem
                && i.m_stack > 0
                && i.m_shared != null
                && i.m_shared.m_itemType.Equals(newType));
        }

        // Valheim's Inventory.Changed() is non-public at runtime.  Cached MethodInfo so we can
        // call it via reflection after we mutate m_inventory directly — Container subscribes
        // to Changed to know when to write its inventory to the ZDO, and the UI redraws from it.
        private static readonly System.Reflection.MethodInfo M_InvChanged =
            AccessTools.Method(typeof(Inventory), "Changed");

        // Valheim 1.0 changed the signature to Changed(bool success, bool cheatedStateChanged).
        // Vanilla's own move/remove paths pass (false, false), which still fires m_onChanged; the
        // flags only gate an achievement toast.  Build the arg array from the runtime parameter
        // list (default values) so both the old 0-arg and new 2-arg forms work.
        private static readonly object[] M_InvChangedArgs = BuildInvChangedArgs();

        private static object[] BuildInvChangedArgs()
        {
            var ps = M_InvChanged?.GetParameters();
            if (ps == null || ps.Length == 0) return null;
            var args = new object[ps.Length];
            for (int i = 0; i < ps.Length; i++)
                args[i] = ps[i].ParameterType.IsValueType ? System.Activator.CreateInstance(ps[i].ParameterType) : null;
            return args;
        }

        // Inventory.m_inventory (the underlying List<ItemData>) is ALSO non-public at runtime
        // — direct field access throws FieldAccessException even though our publicized stub
        // claims public.  FieldRef builds a fast delegate that bypasses the JIT visibility check.
        private static readonly AccessTools.FieldRef<Inventory, System.Collections.Generic.List<ItemDrop.ItemData>> F_InvList =
            AccessTools.FieldRefAccess<Inventory, System.Collections.Generic.List<ItemDrop.ItemData>>("m_inventory");

        private static void InvokeChanged(Inventory inv)
        {
            if (inv == null || M_InvChanged == null) return;
            try { M_InvChanged.Invoke(inv, M_InvChangedArgs); } catch { /* swallow — UI staleness is harmless */ }
        }

        /// <summary>
        /// Direct list manipulation move.  Avoids Valheim's Inventory.MoveItemToThis entirely
        /// — that method is broken on this build: it adds the item at the destination but
        /// leaves a qty=0 phantom (cross-inv) or a duplicate list entry (within-inv) at the
        /// source.  We Remove from source list, Add to dest list, and update m_gridPos.
        /// </summary>
        private static void DirectMove(Inventory destInv, Inventory sourceInv,
                                       ItemDrop.ItemData item, int destX, int destY)
        {
            if (sourceInv != destInv)
            {
                F_InvList(sourceInv).Remove(item);
                F_InvList(destInv).Add(item);
            }
            item.m_gridPos = new Vector2i(destX, destY);
        }

        /// <summary>
        /// Moves newItem into destInv[destX, destY], displacing whatever was previously there
        /// (oldOccupant) to newItem's original source position.  Uses a temp slot to avoid
        /// transient slot conflicts during the swap.
        /// </summary>
        private static void SwapItemInto(Inventory destInv, Inventory sourceInv,
                                         ItemDrop.ItemData newItem, int destX, int destY,
                                         ItemDrop.ItemData oldOccupant)
        {
            var sourcePos = newItem.m_gridPos;

            if (oldOccupant == null || oldOccupant == newItem)
            {
                DirectMove(destInv, sourceInv, newItem, destX, destY);
            }
            else
            {
                // Park oldOccupant in a temp empty slot first (prefer sourceInv since that's
                // where it'll ultimately land), then move newItem to its final destination,
                // then move oldOccupant from the temp slot to newItem's original position.
                var tempPos = FindFirstEmptySlot(sourceInv);
                var tempInv = sourceInv;
                if (tempPos.x < 0)
                {
                    tempPos = FindFirstEmptySlot(destInv);
                    tempInv = destInv;
                }

                if (tempPos.x < 0)
                {
                    // Both inventories full — fall back to a simple move (the swap target's
                    // old item ends up at a conflicting position; degenerate edge case).
                    DirectMove(destInv, sourceInv, newItem, destX, destY);
                }
                else
                {
                    DirectMove(tempInv, destInv, oldOccupant, tempPos.x, tempPos.y);
                    DirectMove(destInv, sourceInv, newItem, destX, destY);
                    DirectMove(sourceInv, tempInv, oldOccupant, sourcePos.x, sourcePos.y);
                }
            }

            InvokeChanged(destInv);
            if (sourceInv != destInv) InvokeChanged(sourceInv);
        }

        // Same row-priority order as FindFirstEmptySlot: prefer the main bag over the hotbar.
        private static Vector2i FindEmptySlotExcept(Inventory inv, Vector2i exclude1, Vector2i exclude2)
        {
            if (inv == null) return new Vector2i(-1, -1);
            int w = inv.GetWidth(), h = inv.GetHeight();
            for (int y = 1; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (inv.GetItemAt(x, y) == null
                        && !(x == exclude1.x && y == exclude1.y)
                        && !(x == exclude2.x && y == exclude2.y))
                        return new Vector2i(x, y);
            for (int x = 0; x < w; x++)
                if (inv.GetItemAt(x, 0) == null
                    && !(x == exclude1.x && 0 == exclude1.y)
                    && !(x == exclude2.x && 0 == exclude2.y))
                    return new Vector2i(x, 0);
            return new Vector2i(-1, -1);
        }

        private static ItemDrop.ItemData FindMatching(Inventory inv, SavedItem saved)
        {
            if (inv == null || saved == null) return null;
            var exact = inv.GetAllItems().FirstOrDefault(i =>
                i.m_shared.m_name == saved.SharedName && i.m_quality == saved.Quality);
            if (exact != null) return exact;
            return inv.GetAllItems()
                .Where(i => i.m_shared.m_name == saved.SharedName)
                .OrderByDescending(i => i.m_quality)
                .FirstOrDefault();
        }

        // ── Inventory lookup ───────────────────────────────────────────────────────

        /// <summary>
        /// Finds a matching item across the supplied inventories (in order), returning the item
        /// and the inventory it came from.  Prefers exact quality; falls back to the highest
        /// available quality of the same item type.
        /// </summary>
        public static (ItemDrop.ItemData item, Inventory source) FindItem(SavedItem saved, params Inventory[] inventories)
        {
            if (saved == null) return (null, null);
            // First pass: exact quality match in any inventory.  m_stack > 0 filters out the
            // qty=0 phantoms that earlier broken MoveItemToThis runs may have left behind.
            foreach (var inv in inventories)
            {
                if (inv == null) continue;
                var exact = inv.GetAllItems().FirstOrDefault(i =>
                    i.m_shared.m_name == saved.SharedName && i.m_quality == saved.Quality && i.m_stack > 0);
                if (exact != null) return (exact, inv);
            }
            // Second pass: best-quality match of the same item type.
            foreach (var inv in inventories)
            {
                if (inv == null) continue;
                var best = inv.GetAllItems()
                    .Where(i => i.m_shared.m_name == saved.SharedName && i.m_stack > 0)
                    .OrderByDescending(i => i.m_quality)
                    .FirstOrDefault();
                if (best != null) return (best, inv);
            }
            return (null, null);
        }

        // ObjectDB.m_items is the full list of item prefabs.  Non-public at runtime so we go
        // through AccessTools, same pattern as the Humanoid equipment field refs.
        private static readonly AccessTools.FieldRef<ObjectDB, List<GameObject>> F_ObjectDBItems =
            AccessTools.FieldRefAccess<ObjectDB, List<GameObject>>("m_items");

        // Lazy SharedName → icon Sprite cache built on first access.
        private static Dictionary<string, Sprite> _iconBySharedName;

        private static void BuildIconCache()
        {
            if (_iconBySharedName != null) return;
            _iconBySharedName = new Dictionary<string, Sprite>();
            if (ObjectDB.instance == null) return;
            List<GameObject> items = null;
            try { items = F_ObjectDBItems(ObjectDB.instance); }
            catch (System.Exception e)
            {
                Jotunn.Logger.LogWarning($"[Armory] Couldn't read ObjectDB.m_items for icon lookup: {e.Message}");
                return;
            }
            if (items == null) return;
            foreach (var prefab in items)
            {
                if (prefab == null) continue;
                var drop   = prefab.GetComponent<ItemDrop>();
                var shared = drop?.m_itemData?.m_shared;
                if (shared?.m_name == null) continue;
                if (shared.m_icons != null && shared.m_icons.Length > 0)
                    _iconBySharedName[shared.m_name] = shared.m_icons[0];
            }
        }

        /// <summary>
        /// Returns the item icon sprite for a saved item, or null if not found.  Caches the
        /// SharedName → Sprite map on first call, so repeated UI redraws are cheap.
        /// </summary>
        public static Sprite GetIcon(SavedItem saved)
        {
            if (saved == null || string.IsNullOrEmpty(saved.SharedName)) return null;
            BuildIconCache();
            return _iconBySharedName != null && _iconBySharedName.TryGetValue(saved.SharedName, out var icon)
                ? icon : null;
        }

        /// <summary>
        /// Cheap availability check used by the UI to color "missing" items red in the slot
        /// summary.  Returns true if a matching item (same SharedName, any quality, stack > 0)
        /// exists in any of the supplied inventories.
        /// </summary>
        public static bool IsItemAvailable(SavedItem saved, params Inventory[] inventories)
        {
            if (saved == null || string.IsNullOrEmpty(saved.SharedName)) return true;
            foreach (var inv in inventories)
            {
                if (inv == null) continue;
                if (inv.GetAllItems().Any(i =>
                        i.m_shared?.m_name == saved.SharedName && i.m_stack > 0))
                    return true;
            }
            return false;
        }

        // Manual empty-slot finder — Valheim's Inventory.FindEmptySlot is non-public at runtime
        // despite our publicized stub claiming it is, so direct calls throw MethodAccessException.
        // GetWidth/GetHeight/GetItemAt are all public so iterating works fine.
        //
        // Search rows 1..h-1 first (the main bag), then fall back to row 0 (the hotbar) only
        // if every other row is full.  This keeps displaced items out of the action bar
        // unless there's literally nowhere else to put them.
        private static Vector2i FindFirstEmptySlot(Inventory inv)
        {
            if (inv == null) return new Vector2i(-1, -1);
            int w = inv.GetWidth(), h = inv.GetHeight();
            for (int y = 1; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (inv.GetItemAt(x, y) == null)
                        return new Vector2i(x, y);
            for (int x = 0; x < w; x++)
                if (inv.GetItemAt(x, 0) == null)
                    return new Vector2i(x, 0);
            return new Vector2i(-1, -1);
        }

        /// <summary>
        /// Legacy single-inventory search retained for the compare panel ("is this item somewhere
        /// the player can reach?" check).  Searches the given inventory only.
        /// </summary>
        public static ItemDrop.ItemData FindInInventory(Inventory inventory, SavedItem saved)
        {
            if (saved == null) return null;
            var all = inventory.GetAllItems();
            var exact = all.FirstOrDefault(i =>
                i.m_shared.m_name == saved.SharedName && i.m_quality == saved.Quality);
            if (exact != null) return exact;
            return all
                .Where(i => i.m_shared.m_name == saved.SharedName)
                .OrderByDescending(i => i.m_quality)
                .FirstOrDefault();
        }

        public static bool IsInInventory(Inventory inventory, SavedItem saved) =>
            saved == null || FindInInventory(inventory, saved) != null;

        // ── Display helpers ────────────────────────────────────────────────────────

        public static string GetItemDisplayName(SavedItem saved)
        {
            if (saved == null) return string.Empty;
            var localized = LocalizationManager.Instance?.TryTranslate(saved.SharedName);
            return !string.IsNullOrEmpty(localized) ? localized : saved.SharedName;
        }

        /// <summary>
        /// Ensures the ArmoryData has at least <paramref name="count"/> named slots.
        /// </summary>
        public static void EnsureSlots(ArmoryData data, int count)
        {
            while (data.Slots.Count < count)
                data.Slots.Add(new LoadoutSlot { Name = $"Slot {data.Slots.Count + 1}" });
        }

        /// <summary>
        /// Relocate any items whose saved grid position is outside the container's current
        /// width/height into the first empty in-bounds slot.  Vanilla Inventory.Load happily
        /// loads items at any position from the ZDO, but the vanilla InventoryGui only renders
        /// cells inside (0..width-1, 0..height-1) — so an item left at e.g. y=4 in a chest
        /// that's been resized down to height=4 becomes invisible and unreachable until we
        /// move it.  Called by ArmoryUI.Open before showing the chest panel.
        /// </summary>
        public static void MigrateOutOfBoundsItems(Inventory inv)
        {
            if (inv == null) return;
            int w = inv.GetWidth();
            int h = inv.GetHeight();

            // Snapshot first — we'll be mutating m_gridPos while iterating.
            var oob = new List<ItemDrop.ItemData>();
            foreach (var item in inv.GetAllItems())
            {
                if (item == null) continue;
                int x = item.m_gridPos.x, y = item.m_gridPos.y;
                if (x < 0 || x >= w || y < 0 || y >= h) oob.Add(item);
            }
            if (oob.Count == 0) return;

            int moved = 0, lost = 0;
            foreach (var item in oob)
            {
                var dest = FindFirstEmptySlot(inv);
                if (dest.x < 0)
                {
                    // Chest is somehow full of in-bounds items already — nothing we can do
                    // without dropping the item to the world (which would be surprising).
                    Jotunn.Logger.LogWarning($"[Armory] Migrate: '{item.m_shared?.m_name}' at ({item.m_gridPos.x},{item.m_gridPos.y}) — chest full, cannot relocate");
                    lost++;
                    continue;
                }
                Jotunn.Logger.LogInfo($"[Armory] Migrate: '{item.m_shared?.m_name}' ({item.m_gridPos.x},{item.m_gridPos.y}) → ({dest.x},{dest.y})");
                item.m_gridPos = dest;
                moved++;
            }

            if (moved > 0)
            {
                InvokeChanged(inv);
                Player.m_localPlayer?.Message(MessageHud.MessageType.Center,
                    lost > 0
                        ? $"Armory: relocated {moved} item(s), {lost} still out of bounds (chest full)"
                        : $"Armory: relocated {moved} item(s) from out-of-bounds slots");
            }
        }
    }
}
