# Changelog

## 2.4.1 — 2026-09-18

- **One configurable container range.** "Container Range" under [Client config] is how far away a container can be for every command in the mod. It was hardcoded and inconsistent: `/g`, `/i` and `/search` reached 50 m while the build-piece hotkey only reached 10 m, so materials the panel counted could be out of the hotkey's reach. The default is 20 m to match AzuCraftyBoxes' default pull range; set it to whatever your craft-from-containers and auto-store mods use so one distance means "in reach" everywhere. Anyone used to the old 50 m grab can set it back.
- Removed the test commands `/listcontainers`, `/listlocalcontainers`, `/listcontents`, `/listpieces` and `/count`. They only wrote to the log and existed to prove the container tracking worked; `/i` covers what they did.
- Item names can be typed with or without spaces: `/g core wood`, `/g corewood` and `/g roundlog` all grab Corewood, and `/g 10 fine wood` or `/g fine wood 10` both work.
- **Fixed: the results panel showed a raw token such as `[item_corewood]`** when you asked for an item by its display name rather than its internal one (`/g corewood` instead of `/g roundlog`). The panel now names the item it actually took.
- **Fixed: `/g 100 carrot` grabbed carrot seeds.** The cultivator's Carrot plant is a build piece whose recipe is one carrot seed, and piece names were checked before item names, so the crop shadowed the vegetable. The same went for turnips, onions, barley, flax and every other crop. When a name is both a crop and an item, the item now wins; the seeds are still there by their own name (`/g 100 carrot seeds`).

## 2.4.0 — 2026-09-16

- **Fixed: an open inventory or packs panel blocked the whole keyboard.** Only Escape worked, so you could not walk, use a hotkey, or open anything else while a panel was up. Panels now behave like the game's own inventory: the cursor is free and the mouse no longer turns the camera, while movement and hotkeys keep working. Scrolling a panel still does not zoom the camera, and typing in the pack editor is still kept out of the game.
- `/i empty` lists every nearby container that has nothing in it and highlights them all, grouped by container type with a count. Handy when you are looking for somewhere to put a haul, or hunting for that chest you emptied and forgot about.

## 2.3.1 — 2026-09-13

- The inventory and grab-pack panels now take the mouse cursor while they are open, so you can point at rows and click them directly. Previously the only way to get a cursor was to open the ESC menu, which Escape no longer does. Transient grab results still do not take the cursor.
- **Fixed: some items showed a raw token instead of a name**, such as `[$item_upgrader_tier0 $item_upgrader_armor $item_upgrader_name]`. Valheim 1.0 added items whose name is several localization tokens joined together, and every name here was being looked up as a single key, which those never match. Names now go through the game's own translator, which substitutes each token in place.
- The inventory panel's pause control is now an icon rather than a line of text, and it shows the pause state three ways: grey when off, orange when the game is genuinely paused, and struck through in red when the pause was requested and refused.

## 2.3.0 — 2026-09-13

- **Fixed: a grab could destroy items when your inventory was full.** Items were removed from the container before being added to your inventory, and the add was never checked, so anything that did not fit was lost. Nothing now leaves a container until it is safely in your inventory; whatever does not fit stays where it was.
- `/g new` grabs one of every item in range this character has never held. Holding an item is what teaches you the recipes that use it, so this is a quick way to learn a batch of recipes from shared storage. Containers only, since you cannot take from a smelter.
- Escape now closes the inventory panel instead of opening the game menu. Press it again for the menu.
- Optional **pause while the `/inventory` panel is open**, toggled from a button in the panel's top-right corner and remembered between sessions. It goes through the game's own pause, so it works by itself solo or hosting alone; on a dedicated server it takes the Pause My Server mod. The button reports the real pause state and reads "Not paused" in red when the request was refused, so it never implies a pause that is not happening.

## 2.2.0 — 2026-09-13

- `/i new` now lists only the items in range this character has never held, instead of searching item names for the word "new". The per-row **(new)** tags are omitted in that view, since every row qualifies.

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
