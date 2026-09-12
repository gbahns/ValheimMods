# Changelog

## 2.1.0 — 2026-09-12

- The `/inventory` panel now tags items this character has never held with a blue **(new)**. In multiplayer a shared chest is often full of a friend's crafting you have never handled yourself, and this makes those stand out. Turn it off with "Mark Undiscovered Items" under [Panel UI].

## 2.0.0 — 2026-09-09

**Now published under DeathMonger**
- Grab Materials moved from the `MojoRyzen` team to `DeathMonger`. The old listing is deprecated; uninstall it if you have it. The plugin ID (`DeathMonger.GrabMaterialsMod`) and config file are unchanged, so settings carry over.

**Valheim 1.0**
- Rebuilt against Valheim 1.0.7 (Unity 6). Pre-1.0 builds fail at load with `MissingMethodException: Terminal.ConsoleCommand` on the new game; this build fixes that.
- Dependencies bumped to BepInExPack 5.4.2350 and Jotunn 2.30.0.

**Grab Delta**
- New "Grab Delta (default)" client config (on by default) drives every grab unless a pack overrides it.
- Per-pack Grab Delta is now a tri-state: Use Global / On / Off (default Use Global). This replaces the old per-pack true/false setting; existing packs fall back to Use Global.
- Cross-grab ledger: back-to-back delta grabs (e.g. `/g cart` then `/g explore`) no longer double-count the same inventory. Clears after 30 seconds of inactivity (configurable); `/grabreset` clears it manually.

**Panels**
- `/listpacks` now opens an in-game panel (Edit | Name | Hotkey | Delta | Items). Click a row to trigger that pack.
- In-game pack editor: click the pencil on a pack row to edit its name, items, and delta without leaving the game.
- Pack HUD: a small on-screen list of your grab packs and their hotkeys, like AzuExtendedPlayerInventory's quick-slot labels, so you never have to remember what is on Shift+K vs Ctrl+K. Packs with no items are left out. Toggle with O (configurable); corner and offsets under the new "Pack HUD" config section.
- The pack HUD hides packs that need a material you have never held, so Karve and Longship packs stay out of the way until you have handled nails; switch off with "Hide Undiscovered Packs".
- The materials panel now includes items queued in kilns, smelters, and refineries, marked "(N in kiln)" and similar.

**Hotkeys**
- Default pack keys moved from G / Y / U to K / L / ; (same Shift, Ctrl and Alt modifiers) because Valheim 1.0 opens its new radial menu on G and ignores modifiers. Existing configs keep their keys; if yours are still on G, rebind the packs or the radial so both do not fire together.

**Grabbing**
- Run-again override: repeating a grab that failed on a shortage within 30 seconds grabs whatever is available.

**Other**
- Log output now attributes to `[Grab Materials]` instead of `[Unity Log]`.
- The Distance HUD now defaults to off.
- Editing the .cfg file while the game is running now takes effect immediately, including pack hotkeys; previously the file was only read at startup and could be overwritten by the in-game values.
