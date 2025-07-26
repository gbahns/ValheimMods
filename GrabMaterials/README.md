# MojoRyzen's Grab Materials Mod

Created on January 30 2025.<br>
Version 1.0.0 released on July 19 2025.

Quickly pull materials from nearby chests into your inventory without having to manually click on each chest.  The concept is similar to mods that let you craft or build directly from containers, but for things that you're not building in your base.  The typical example is workbench and portal when exploring - you're constantly having to grab 10 wood, 20 finewood, 10 greydwarf eyes, and 2 surtling cores.  This mod removes that tedium.

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

<h3>Fixes</h3>

<li>Settings changes are now automatically picked up when you change the config file directly
<li>Destroyed containers not removed from the list causing NullPointerException
<li>Removed unnecessary logging

<h3>Fix in version 1.0.1</h3>

<li>Fixed a bug where it would grab 10 stone instead of 10 wood for the Chest build piece


Join [my discord server](https://discord.gg/2gnsrZSN) to ask questions or provide feedback.

View [the backlog](https://github.com/users/gbahns/projects/1/views/1) here.

See [wiki](https://thunderstore.io/c/valheim/p/MojoRyzen/GrabMaterials/wiki/3012-mojoryzens-grab-materials-mod/) for more details.
