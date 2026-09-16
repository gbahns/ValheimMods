# Grab Materials

Created on January 30 2025.<br>
Version 2.0.0 released on September 9 2026.

> **Moved:** Grab Materials is now published under **DeathMonger**. If you installed it from the old `MojoRyzen/GrabMaterials` listing, uninstall that one and install this one. The plugin ID and config file are unchanged, so your settings, grab packs and hotkeys carry over.

Quickly pull materials from nearby chests into your inventory without having to manually click on each chest.  The concept is similar to mods that let you craft or build directly from containers, but for things that you're not building in your base.  The typical example is workbench and portal when exploring - you're constantly having to grab 10 wood, 20 finewood, 10 greydwarf eyes, and 2 surtling cores.  This mod removes that tedium.

<h3>New in version 2.4.0</h3>

<li>'/i empty' — highlights every nearby container with nothing in it and lists them by type with a count.  Handy when you need somewhere to put a haul.
<li>Fixed the inventory and grab-pack panels blocking the keyboard.  With a panel open you can walk and use your hotkeys again; the cursor stays free for clicking, the mouse no longer turns the camera, and scrolling the list does not zoom.

<h3>New in version 2.3.0</h3>

<li>Fixed a bug that could destroy items when your inventory was full — a grab emptied the container before checking that the items fit.  Nothing leaves a container now until it is safely in your inventory.
<li>'/g new' — grabs one of every item in range you have never held, which is a quick way to learn the recipes they unlock.
<li>Escape closes the inventory panel instead of opening the game menu; press it again for the menu.
<li>The inventory and grab-pack panels now take the mouse cursor while open, so you can point at and click rows directly.
<li>Fixed some items showing a raw token such as "[$item_upgrader_tier0 $item_upgrader_armor $item_upgrader_name]" instead of a name.  Valheim 1.0 added items whose name is several localization tokens joined together, and names now go through the game's own translator, which handles them.
<li>Optional pause while the inventory panel is open — toggle it with the button in the panel's top-right corner and it is remembered.  It uses the game's own pause, so it works solo or hosting alone; on a dedicated server it takes the Pause My Server mod.  The button shows whether the pause actually took effect.

<h3>New in version 2.2.0</h3>

<li>'/i new' — lists just the items in range you have never held, instead of searching names for the word "new".

<h3>New in version 2.1.0</h3>

<li>Undiscovered item marker — '/inventory' tags anything this character has never held with a blue '(new)'.  Most useful in multiplayer, where a shared chest is often full of a friend's crafting you have never handled yourself.  Configurable under "Panel UI".

<h3>New in version 2.0.0</h3>

<li>Inventory panel — '/inventory' (or '/i') opens a wood-styled panel listing items in nearby containers, grouped by category with item icons.  Optional filter on item name or category, e.g. '/i wood' or '/i food'.
<li>Configurable inventory layout — choose a List (grouped by category) or Table (flat columns) style, and a panel shape from MaxHeight (single column) through Square to MaxWidth (fans out as many columns as fit on the screen).  The mod auto-picks the column count that best matches your shape.  Cycle styles in-game with '/istyle'.
<li>Click to highlight — click any inventory row to highlight nearby containers holding that item; click a category header to highlight every container holding any item in the category.
<li>Grab pack panel — '/listpacks' opens an in-game panel showing each pack's name, hotkey, delta state, and items.  Click a row to trigger that pack; click the ✎ pencil to edit the pack's name, items, and delta inline (no need to alt-tab to the config file).
<li>Pack HUD — a small always-on list of your grab packs and their hotkeys (like the quick-slot labels in AzuExtendedPlayerInventory), so you never have to remember what is on Shift+K vs Ctrl+K.  Packs with no items are left out.  Press O to show or hide it; corner and offsets are configurable under "Pack HUD".  Packs that need a material you have never held are hidden until you have picked it up (configurable).
<li>Global Grab Delta default — new "Grab Delta (default)" client setting (default ON) makes every grab take only the shortfall between what you have and what's needed.  Per-pack settings are now a tri-state — UseGlobal (the default), On, or Off — so you can override either way.
<li>Cross-grab delta ledger — back-to-back delta grabs no longer double-count the same inventory.  E.g. '/g cart' (10 wood) then '/g explore' (20 wood total) — the ledger remembers the cart's wood is already spoken for, so Explore grabs the 20 it actually needs.  The ledger clears after 30 seconds of inactivity (configurable) so a fresh build session starts clean.  Use '/grabreset' to clear it explicitly.
<li>Default hotkeys moved — Valheim 1.0 put its new radial menu on G, so the pack defaults are now K (packs 1-4), L (5-8) and ; (9-10) with the same Shift/Ctrl/Alt modifiers.  Existing configs keep whatever keys they have; if yours are still on G, rebind the packs (or the radial) to stop both firing at once.

<h3>New in version 1.1.0</h3>

<li>Grab results are now shown in a wood-styled panel at the top center of the screen (instead of a brief HUD message).  The panel lists every requested material with a green ✓ for items that are fully available and a red ✗ with "X of Y Item (missing N)" for shortages.
<li>Atomic grab — if any requested material is short, nothing is taken from any container; the panel tells you exactly what's missing so you can go get it.
<li>The panel auto-fades over 3 seconds when you start moving, attacking, blocking, or jumping (after a brief grace period to handle the case where you trigger the grab while already running).  After 15 seconds of no input it fades on its own.  Click anywhere on the panel to dismiss it instantly.
<li>New "Panel UI" config section to tune the idle timeout, the fade duration, and whether movement dismisses the panel.
<li>You can now grab materials for any build piece by hovering over its icon in the build menu and pressing 'j' — no need to select it first.  (Falls back to the selected piece when you're not hovering anything.)
<li>Grab pack "Grab Delta" flag is now supported — when enabled for a pack, only the shortfall between what you already have and what's needed is grabbed.  E.g. if the pack needs 10 wood and you have 7, only 3 will be pulled.

<h3>Fix in version 1.0.1</h3>

<li>Fixed a bug where it would grab 10 stone instead of 10 wood for the Chest build piece

<h3>New in version 1.0.0</h3>

<li>Hit 'j' when the build menu is open to automatically grab materials for the currently selected build piece
<li>Support for ten different grab packs with configurable keyboard shortcuts
<li>Grab the materials for a build piece by typing '/grab portal' in the chat window
<li>Grab the materials for a grab pack by typing '/grab <n>' or '/grab <pack-name>' in the chat window
<li>Specify build piece names in grab pack configuration
<li>Shorthand console commands, e.g. type '/g' instead of '/grab'
<li>Specify the count before or after the material name, e.g. '/grab 10 wood' or '/grab wood 10'
<li>Match on both the user-friendly name that you see in-game and the internal ID.
<li>Case insensitive matching
<li>'/listpacks' to write the list of grabpacks to the log
<li>Configure how long containers are highlighted when you pull items from them or search for items
<li>'/inventory [search-text]' to count the number of items within range; if search-text specifies, looks for items that match on name or category

<h3>Fixes in version 1.0.0</h3>

<li>Settings changes are now automatically picked up when you change the config file directly
<li>Destroyed containers not removed from the list causing NullPointerException
<li>Removed unnecessary logging


Join [my discord server](https://discord.gg/eH7UfRj5mG) to ask questions or provide feedback.

View [the backlog](https://github.com/users/gbahns/projects/1/views/1) here.

See the [project on GitHub](https://github.com/gbahns/ValheimMods/tree/main/GrabMaterials) for source, issues and the full changelog.
