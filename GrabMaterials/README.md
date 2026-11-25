# Grab Materials

Pull materials out of nearby chests without clicking through each one. Craft-from-containers mods cover building at your base; this covers what you carry away from it. Heading out to explore means grabbing 10 wood, 20 fine wood, 10 greydwarf eyes and 2 surtling cores every time. Press one key instead.

## Grab packs

A grab pack is a list of build pieces and items on a hotkey. Press the key and the materials for everything in the pack come out of the chests in reach, with a panel showing each piece, its materials, and how much of each remains nearby. If anything is short, nothing is taken and the panel shows what is missing. Press the key again within 30 seconds to grab what is there anyway.

The defaults, all configurable under `Grab Pack 1` through `Grab Pack 10`:

| Key | Pack | Items |
|---|---|---|
| K | Explore | Workbench, Chest, Portal |
| Shift+K | Karve Explore | Workbench, Chest, Portal, Karve |
| Ctrl+K | Longship Explore | Workbench, Chest, Portal, Longship |
| Alt+K | Swamp Explore | Workbench, Chest, Portal, Campfire |
| L | Ashlands Explore | Workbench, Portal, Campfire:10 |
| Shift+L | Ashlands Flametal | Workbench, Stone Cutter, Stone Portal, Shield Generator, Bones:10 |
| Ctrl+L, Alt+L, ;, Shift+; | Packs 7 to 10 | empty |

An entry is a build piece or an item, by the name you see in game or its internal name, with an optional count: `Chest:2`, `Wood:50`. Any piece the hammer can build works, including pieces from other mods.

**Grab Delta** (on by default) takes only the shortfall between what you carry and what the pack needs. A grab a few seconds after another remembers what the first one was for, so `/g cart` and then an explore pack does not count the cart's wood twice. Each pack can override the global setting.

Packs that need a material you have never held stay hidden until you pick it up, so Karve and Longship packs are out of the way until you have nails.

## In the build menu

Hover a piece in the build menu and press **J** to grab its materials. With nothing under the cursor it uses the selected piece.

## Console commands

Type these in the chat window.

| Command | What it does |
|---|---|
| `/grab <piece or item> [count]`, `/g` | Materials for a piece (`/g portal`), or an amount of an item (`/g 10 wood`, `/g wood 10`). A pack by name or `/g pack 3` runs that pack. `/g new` takes one of every item in range you have never held, which teaches you their recipes. |
| `/inventory [filter]`, `/i` | A panel of everything in the nearby containers, grouped by category with icons and counts. Filter by name, category or section (`/i wood`, `/i food`, `/i combat`), `/i new` for items you have never held, `/i empty` to find and highlight the empty containers. |
| `/listpacks` | A panel of your packs with their keys and items. Click a row to run the pack, or the pencil to edit its name, items and delta setting in game. |
| `/search <text>`, `/s` | Highlights the containers holding anything matching the text. |
| `/store` | Puts everything in your inventory except the top row into the nearby containers. |
| `/gather [item]` | The same as the Gather key below. |
| `/sort` | Merges the stacks in the container you have open and sorts it. |
| `/badges` | Steps the container badges through off, on, and on with contents. |
| `/istyle` | Cycles the inventory panel between List and Table style. |
| `/isections` | Shows or hides the section headers in the inventory panel. |
| `/grabreset` | Forgets recent grabs, so the next delta grab looks only at your inventory. |

## The inventory panel

`/i` opens a wood-styled panel listing the items in reach by category. Items this character has never held carry a blue **(new)** tag. Material queued in kilns, smelters and refineries, and material sitting in wood stacks, stone piles and the like, is counted and noted on the row.

- **Left-click a row** to flash the containers that hold that item; click a category or section header for every container holding anything in it.
- **Right-click a row** to grab that item: 1, a few round amounts below what is nearby, all of it, or an amount you type. The panel redraws so you can keep pulling.
- **Escape** closes it. Movement and hotkeys keep working while it is open, and the cursor is free.
- **Pause** while it is open with the button in its top-right corner. On a dedicated server this takes the Pause My Server mod.
- Pick a List or Table style and a panel shape from single column to as wide as the screen under `Panel UI`. `Section Headers` there groups the categories under Combat, Materials, Food, Plants, Tools, Special and Misc. A panel taller than the screen scrolls.

## Pack HUD

A small list of your packs and their keys stays on screen, with the icon of each piece in the pack after its name. Press **O** to show or hide it. Corner, offsets and what it lists are under `Pack HUD`.

## Container badges

A round badge over each container you can see shows its free slots: green with room, amber at 3 or fewer, red when full, blue when it is completely empty. Within 10 m the badge also carries the icons of the one or two items that take the most slots, so the wood chest and the stone chest tell apart from across the room. Press **/** to step through off, badges, and badges with contents. Range, size, icon placement and whether badges show through walls are under `Container Badges`. Badges float over everything by default; turn off `Draw Badges In Front` and your character, walls and creatures cover them like they cover the chest.

## Gather

Look at a chest and press **\\**. The item that chest mostly holds is pulled into it from every other chest in reach, farthest first, so one material stops living in five places. When it fills, the overflow goes to the chest nearest it with room. Hold **Shift** and the chest's other items first move to your inventory to make room; hold **Ctrl** and they slide to other chests, each to one already holding that item where there is one.

## Tidy containers

Opening a chest merges its partial stacks and lays the items out by category and name, the way SimpleSort used to work. Choose Name, Weight or Value instead, turn it off, or list containers to leave alone under `Tidy Containers`. Armory racks and tombstones are never touched.

## Eggs

Drop a stack of eggs and they land as single eggs, scattered in front of you, because an egg in a stack never hatches. Chicken eggs, asksvin eggs and modded eggs alike. Turn it off under `Eggs`.

## Range and multiplayer

`Container Range` (default 20 m) is how far away a container can be for everything in the mod. Set it to whatever your craft-from-containers and auto-store mods use so one distance means "in reach" everywhere.

Every container is reached through the game's own open request, the same check as pressing Use. A chest you may not open, one inside someone else's ward, or one another player has open is left alone, and two players racing a chest cannot come away with the same item.

## Links

Join [the Discord](https://discord.gg/eH7UfRj5mG) to ask questions or give feedback.

The [backlog](https://github.com/users/gbahns/projects/1/views/1) and the [source, issues and full changelog](https://github.com/gbahns/ValheimMods/tree/main/GrabMaterials) are on GitHub.
