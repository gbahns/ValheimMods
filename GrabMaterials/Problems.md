# GrabMaterials — Code Review Findings

A snapshot of issues identified during a code review. Nothing has been changed yet — this is a backlog to work through later.

## Bugs

### 1. Multi-word piece names in grab packs are broken
**Files:** [GrabMaterialsMod.cs:111](GrabMaterialsMod.cs#L111), [ConsoleCommands.cs:55](ConsoleCommands.cs#L55), [ConsoleCommands.cs:402](ConsoleCommands.cs#L402)

Pack 6 is configured with `"Workbench,Stone Cutter,Stone Portal,Shield Generator,Bones:10"`. Inside `GrabMaterialsForPack`, the entire items string is run through `itemsString.Replace(" ", "")` before being split on `,`, which collapses `"Stone Cutter"` → `"StoneCutter"`.

The piece lookup is keyed on `localizedName.ToLowerInvariant()` with spaces preserved, so `"stonecutter"` will never match the lookup key `"stone cutter"`. Pack 6 fails silently.

**Suggested fix:** split on `,` first, then `Trim()` each entry instead of stripping all spaces up front.

### 2. `Extensions.CountItems` ignores its `inventory` parameter
**File:** [Extensions.cs:264-287](Extensions.cs#L264-L287)

Both `CountItems` overloads accept an `Inventory inventory` argument but immediately read `Player.m_localPlayer.GetInventory()` and count from *that*. Calling `someContainer.GetInventory().CountItems("wood")` would return the player count, not the container count.

Currently it works by coincidence (only player inventory is ever passed in), but it's a footgun for future callers.

**Suggested fix:** use the `inventory` parameter, drop the `Player.m_localPlayer` lookup.

### 3. `Extensions.Name` will throw on items not prefixed with `$item_`
**File:** [Extensions.cs:15-18](Extensions.cs#L15-L18)

`self.m_shared.m_name.Substring(6)` assumes every item name begins with `$item_`. Modded items, custom items, or any name shorter than 6 characters will throw `ArgumentOutOfRangeException`.

**Suggested fix:**
```csharp
var name = self.m_shared.m_name;
return name.StartsWith("$item_") ? name.Substring(6) : name;
```

### 4. `prefabName.Contains("Seed")` never matches
**File:** [Extensions.cs:130](Extensions.cs#L130)

`prefabName` is lowercased earlier in the method, so the case-sensitive `Contains("Seed")` (capital S) never fires. Today only the explicit `SeedNames` set classifies seeds; the catch-all is dead.

**Suggested fix:** change `"Seed"` to `"seed"`.

### 5. `ContainerFinder` has a NullReferenceException ordering bug
**File:** [ContainerFinder.cs:24-27](ContainerFinder.cs#L24-L27)

`container.GetComponent<ContainerFilterService>()` is invoked *before* the `if (container != null)` check, so a non-Container collider would NRE.

**Note:** this entire class appears to be dead code (no call sites) — easier to just delete the file.

### 6. `HookChatInputText` is a no-op Harmony patch
**File:** [ChestSearch.cs:9-61](ChestSearch.cs#L9-L61)

The class is decorated with `[HarmonyPatch(typeof(Chat), "SendText")]` but defines no `Prefix` / `Postfix` / `Transpiler` methods. The helper methods inside are never called from anywhere either.

**Suggested fix:** either finish wiring it up (`Postfix(string text) => SearchNearbyContainersFor(text)`) or delete the file.

### 7. `PlayerUpdateTeleportPatchCleanupContainers` looks like decompiled code
**File:** [ContainerPatches.cs:36-54](ContainerPatches.cs#L36-L54)

The LINQ `where` clauses are tortured (classic dnSpy output). The first `where` selects "bad" containers (null, null transform, or null inventory), but the second `where (Object)(object)container != (Object)null` then filters *out* the null containers — so null containers are never actually cleaned up.

**Suggested fix:** rewrite as a clear `Containers.RemoveAll(c => c == null || c.transform == null || c.GetInventory() == null)`.

### 8. `ContainersToAdd` / `ContainersToRemove` deferred lists are pointless
**File:** [Boxes.cs:15-46](Boxes.cs#L15-L46)

`AddContainer` and `RemoveContainer` push into the staging list and then *immediately* call `UpdateContainers()`, which flushes it. The deferral pattern provides no value as written.

**Suggested fix:** either commit to the deferred pattern (only flush at known-safe points like end of frame) or drop the staging lists and mutate `Containers` directly.

## Smaller items

- **Lone `SettingChanged` handler.** [GrabMaterialsMod.cs:118-121](GrabMaterialsMod.cs#L118-L121) only attaches a handler to `GrabPacks[8]`. Looks like leftover test code.
- **Typo.** [GrabMaterialsMod.cs:97](GrabMaterialsMod.cs#L97) — "selectede" in the config description.
- **Stale lookup caches.** `pieceLookup` and `itemLookup` ([ConsoleCommands.cs:24-25](ConsoleCommands.cs#L24-L25)) are static and never rebuilt. After logging out and into a different world (especially with different mods loaded), the cached `Piece` references could be stale or pointing at destroyed Unity objects. Consider clearing on world load.
- **Unused lookup.** `itemLookup` is populated by `BuildItemLookUp` but never read anywhere.
- **Suspicious using directive.** `using static MeleeWeaponTrail;` in [ConsoleCommands.cs:10](ConsoleCommands.cs#L10) is almost certainly an accidental IntelliSense import.
- **Commented-out code.** Significant amounts of dead, commented-out code throughout — would be cleaner to delete and rely on git history.
- **Pack rename doesn't propagate to button.** [GrabMaterialsMod.cs:55](GrabMaterialsMod.cs#L55) — `GrabPackConfig.Button.Name` is captured at construction time, so renaming a pack via the config UI won't update the registered button name. Functionally OK because `Update()` reads `grabPack.Button.Name`, but any displayed button label won't refresh.

## Suggested order to tackle

1. **#1** (multi-word piece names) — real user-visible bug.
2. **#4** (`"Seed"` casing) — one-character fix, restores intended behavior.
3. **#3** (`Substring(6)` crash risk) — defensive, cheap.
4. **#5, #6** — delete the dead files.
5. **#2** — fix while the code is fresh in mind.
6. **#7, #8** — cleanup, lower urgency.
7. Smaller items as cleanup passes allow.
