# Changelog

## 2.10.0 — unreleased

- **Gather**: look at a chest and press \ (the "Gather Key") and the item it mostly holds is pulled into it from every other chest within Container Range, farthest chest first, so one material stops living in five places. When the chest fills, the overflow goes to the chest nearest it with room. Hold Shift and the chest's other items first move to your inventory to make room; hold Ctrl and they slide to other chests in reach, each to a chest already holding that item where there is one. `/gather` does the same as the plain key; `/gather <item>` names the item.

## 2.9.0 — 2026-11-12

- **Right-click a row in the inventory panel to grab that item**: Grab 1, 5, 10 or 20, or type an amount (the field remembers your last amount for that item). Amounts the containers cannot cover are greyed out. The result shows as a HUD line and the panel redraws with the new counts, so you can keep pulling.
- **Container badges show what is inside**: within 10 m (the "Icon Range" setting) a badge carries the icons of the one or two items that take the most slots in the chest. "Icon Position" puts them right, down, left or up of the disc, or in a centered row under or over it.
- **Three-way badge toggle**: / and `/badges` step through off, badges, and badges with contents. "Toggle Cycle" can limit the key to off/badges or off/badges with contents. The on/off "Enabled" setting is now "Display".
- **Containers tidy themselves when you open them**: partial stacks merge, then the items are laid out by category and name (or Name, Weight or Value, under "Tidy Containers"), the way SimpleSort used to work. `/sort` does it on demand. A HUD line says what was merged and moved; an already tidy chest says nothing. Armory racks, tombstones and any prefab you list are left alone.

## 2.8.0 — 2026-11-12

- **Container badges**: a round badge over each nearby container shows its free slots, green with room, amber at 3 or fewer, red when full, blue with a white ring when it is completely empty. Only containers you can see get one. Press / or type `/badges` to hide or show them; range, size and the rest are under "Container Badges".

## 2.7.0 — 2026-11-11

- The grab results now say how much of each material is **left in the nearby containers**, as "(180 left nearby)", so you know whether the next build will find any.
- **Fixed: pieces from some mods could not be grabbed by name**, such as `/g Fast Longship` from TheGreatestShips. The piece list came from the game's startup prefab list, and a mod that registers its pieces later puts them only in the game's lookup dictionary. Both are read now, so any piece the hammer can build can be grabbed for.
- **Fixed: a pack entry with a space in its name, such as "Stone Cutter" or "Shield Generator", was never found.** The default Pack 6 had two of them. Piece names now match with or without spaces, the way item names already did.
- **Fixed: seeds the mod did not know by name were sorted as plants** instead of seeds.
- Internal cleanup: dead code removed, the container registry simplified, a teleport cleanup that never removed anything now does, and a typo in a setting description fixed.

## 2.6.1 — 2026-10-19

- **Fixed: the scrollbar from a tall `/i` panel stayed on screen beside the next grab-results panel.** It now leaves with the rows it belongs to.

## 2.6.0 — 2026-10-19

- **Panels taller than the screen now fit it and scroll.** The inventory panel in Table style, which lists every item in one column, ran off the bottom of the screen on a well-stocked base. Rows are now clipped to what fits, with a scrollbar beside them; the mouse wheel over the panel scrolls four rows per notch ("Scroll Rows" under [Panel UI]). The List style and the packs list get the same treatment when they are too tall.

## 2.5.4 — 2026-10-19

- **Pack hotkeys are registered by pack slot, not by pack name.** A pack's display name was also the name its hotkey was registered under, so two packs with the same name shared one key and the second was silently dead, and packs left nameless produced a string of "Cannot have duplicate button" warnings from Jotunn. Names are now purely yours to change.
- A pack with no key is still a pack: `/g <name>` and `/g pack <n>` reach it, and only the hotkey is left out. A pack with no name and no key is skipped. In each case the log says plainly what the pack is missing and how to give it, and the pack editor no longer saves a blank name.

## 2.5.3 — 2026-10-19

- **Fixed: `/i` showed no panel when a nearby chest held an item with no prefab.** Sorting an item into a category read its prefab name, and an item the game could not pair with a prefab, such as one left behind by a mod that is no longer installed, threw and took the whole panel with it after the chests had already flashed. Such items are now sorted by their name instead.

## 2.5.2 — 2026-10-19

- **Fixed: an "Idle Timeout" of 0 removed the panel before it was drawn.** The setting was taken literally, so the fade began on the first frame, and with a "Fade Duration" of 0 it also ended there. 0 now means the panel never fades on its own, which is what anyone setting it meant, and a fade of 0 hides the panel at once when the idle timeout ends.
- The log now has one line each time a panel is shown, with its timers, and a warning if the panel cannot be created because Jotunn's GUI layer is missing. A "the panel never shows" report can be answered from the log.

## 2.5.1 — 2026-10-18

- Bear Hide and Bear Paw are listed under Skins in `/i` and the inventory panel, with the other creature parts. They were falling through to the generic Material group.

## 2.5.0 — 2026-10-18

- `/i` now counts the material in **stacks and piles**: wood stacks, stone piles, coal piles, Ashwood stacks, black marble piles, coin piles and the rest. Tearing one down returns its full material, so a wood stack is 50 wood on your plot as surely as a chest of it. The amount is noted on the row the way a smelter's queue is, as "(50 in stack)" or "(150 in stacks)", and the stacks flash with the containers, both when the panel opens and when you click the row. Anything a mod adds that is named like a stack or pile and returns one material is counted too.
- When a material sits in more than one kind of place, the row now names them, as in "(60 in kiln and stacks)", instead of the old "(60 in processing)".

## 2.4.2 — 2026-10-13

- **Fixed: grabbing from a container you could not open duplicated the item, and storing into one lost it.** The game only checks who may open a chest at the moment you press Use, and it only saves a chest's contents from the machine that currently owns it, which is usually whoever is standing nearest. This mod reached into chests directly, so a Personal container, a chest inside someone else's ward, or one another player had open was taken from like any other, and if another machine owned it the removal never saved: you kept the copy and the chest got its item back. Grabs and `/store` now go through the game's own open request for every container another machine owns: that machine checks whether you may open it and whether it is in use, hands it over, and only then does the grab touch it, exactly as pressing Use would. One machine writes a chest at a time, so two players racing a chest can no longer come away with the same item. Chests you already own, which is nearly all of them when playing alone, skip the exchange, and a chest whose owner does not answer within a moment is left alone. `/i` and `/search` still list what is nearby.

## 2.4.1 — 2026-09-20

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
