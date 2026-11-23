# Armory

Craft an **Armory Rack** to save, store, and instantly recall named equipment sets — armor, weapons, shield, cape, utility, and the eight hotbar slots — without rummaging through chests every time you switch between farming, mining, exploring, and fighting.

The rack is also a real storage container. Drop your alternate gear in, walk up, press [Use], and pick the loadout you want. Whatever you're currently wearing is swapped *back into the exact slot the new gear came from*.

---

## Features

- **Named loadout slots** — save your current equipment as `"Farming"`, `"Mining"`, `"Boss"`, etc. Rename by clicking the slot name. Up to 10 slots per rack.
- **Combined armor + hotbar** — captures helm, chest, legs, cape, belt, trinket, **and** quickbar items 1–8.
- **Loadouts are the cells you choose** — each worn slot, hotbar key and quick slot can be in a loadout or out of it, and Load touches only what's in. A new loadout starts as armor and cape. Press **+** on a row to see every saved cell and add or remove some; the group labels add or remove a whole group. An armor set, a weapon kit and a food set load one after another to build an outfit.
- **Storage + loadouts in one** — the rack is also a Container, 12×6 by default and configurable. Load pulls items from the rack *and* your inventory; saved gear in the rack counts as "available".
- **In-place swap** — when Load equips a new item, whatever was in that slot gets stored back at the exact grid position the new item came from. Nothing ends up on the action bar unless that's where it was sitting.
- **Live status** — a green ● on the Load button means you're already wearing this loadout. Missing items show red in the summary text and red in the icon strip.
- **Compare window** — side-by-side view of your current gear vs. the saved set, with color-coded availability.
- **Two sizes** — the Armory Rack, 2 m wide and 3 m tall with 72 storage slots, and the Small Armory Rack at two thirds the size with 40, for one viking's sets in a corner. Both take every model, wood, decoration and crown.
- **Named racks** — point at a rack and press **Shift+[Use]** to name it. The name shows on the hover text and heads both panels, so a wall of racks is readable at a glance.
- **Personal or shared** — a button in the panel decides whether anyone may open the rack or only the player who built it. Build your own rack, set it to Personal, and your loadouts and the gear behind them are yours.
- **Optional in-game pause** — config flag pauses the game while the panel is open, the way the ESC menu does. Works by itself solo or hosting alone; on a dedicated server it takes [Pause My Server](https://valheim.thunderstore.io/package/DeathMonger/PauseMyServer/).
- **Copy and Paste** carry loadouts to another rack, a second base or a rebuilt one: Copy on the panel takes every loadout, a row's menu takes one, and Paste puts them into the other rack's empty slots, adding slots for the rest. Only the definitions travel; bring the items yourself, and until you do they show as missing. The clipboard is part of your character, so it survives logout and goes to any world. Another player's Personal rack opens read-only, so you can copy their loadouts without them having to share it.

---

## How to Use

1. Build an **Armory Rack** or a **Small Armory Rack** from the Hammer menu's **Furniture** tab. Requires a Workbench in range. See [Recipe](#recipe).
2. Press **[Use]** on the rack to open the panel — your inventory and the rack's storage appear alongside it. Each loadout row has an **Equip** button and a menu button (three bars) with Save, Show all cells, Compare and Delete; the steps below say "click Save" and so on, meaning that menu, or the row buttons if you turn those on in the config.
3. Click **Save** (↓) on a slot to capture what you're currently wearing and the items in hotbar slots 1–8.
4. Click **Load** (↑) on a saved slot to equip that loadout. Items come from the rack first, then your inventory. Whatever you were wearing gets stored in the slot the loaded item came from.
   - A row shows only the cells its loadout loads; a new one starts as helmet, chest, legs and cape. Press **+** to see every cell that was saved, click a cell to add it or take it out, and press **-** to hide the rest again. The label above a group (**Armor**, **Weapons**, **Food**, ...) adds or removes the whole group. Save always records everything, so a cell added later shows what you had there at the last save.
5. Click the slot name to rename it. Click the red **x** to delete the slot (empty slots and duplicates delete without confirmation; non-empty unique slots prompt first).
6. Click **Shared / Personal** in the top-left to choose who may open the rack. Only the player who built it can change this.
7. To name a rack, point at it and press **Shift+[Use]** — the same modifier Valheim uses for renaming a portal. An empty name returns it to "Armory".

### Personal racks

A rack set to **Personal** can only be opened by the player who built it — its storage and its loadouts are theirs to change. That is how each player keeps their own sets: build a rack, make it personal, and the gear those loadouts need lives inside it. Everyone else who presses **[Use]** on it gets a read-only view of the loadouts, to look at and **Copy** into a rack of their own; nothing in that view changes the rack, and the storage stays closed. A **Shared** rack is open to everyone, which is what you want for a communal armory.

Valheim enforces this itself, with the same rule it applies to a personal chest, and the same consequence: **nobody can hammer down a personal rack while it still holds items.** To remove one, empty it first or set it back to Shared.

---

## Recipe

| Material | Armory Rack | Small Armory Rack |
|---|---|---|
| Fine Wood | 16 | 10 |
| Core Wood | 8 | 5 |
| Bronze Nails | 20 | 12 |
| Bronze | 4 | 3 |
| Resin | 10 | 6 |

Buildable at the Workbench. Black Forest tier.

---

## Configuration

The config file is created at `BepInEx/config/GBahns.Armory.cfg` on first run.

**\[General\]**
- `Lock Configuration` (default: true) — while on, the server's synced settings override every client's, and only admins can change them.
- `Pause Game While Armory Open` (default: false) — pauses the game while the rack panel is open, the way the ESC menu does. It asks the game for the pause rather than stopping your own clock, so it works by itself when you are playing solo or hosting alone, and on a dedicated server it takes [Pause My Server](https://valheim.thunderstore.io/package/DeathMonger/PauseMyServer/) to grant it. Without that mod the request is refused on a server, which is the point — your clock stopping while the server runs on would only desync you. While the pause is in effect, a craft started from the inventory screen cannot finish until you close the rack; the craft timer counts game time.

**\[Storage\]** (synced from the server)
- `Rack Width` / `Rack Height` (default: 12 × 6) — the Armory Rack's storage grid; the Small Armory Rack follows at two thirds the columns and five sixths the rows (8 × 5 by default). Every rack takes the new size, standing or not; items left outside a smaller grid are moved into it by whoever opens the rack. A grid taller than the container panel is drawn with smaller cells.

**\[Style\]**
- **Style** button on the Manage Loadouts panel — picks the model, wood and decoration for that one rack, and for the Armoire its crown's shape and height, with a picture of the choice and the rack redrawn as you go, and everyone sees it that way. The metal decorations appear once you have reached that metal. `Use my default` makes it follow the Default settings again. Whoever can manage the rack's loadouts can restyle it, within a ward.
- `Default Model` (default: Armoire), `Default Decoration` (default: None), `Default Wood` (default: Darkwood) — how every rack looks that nobody has styled with the Style button; those racks change with these, styled ones keep their own.
  - Model: Armoire is a modeled wardrobe with a crown, arched door panels and iron strap hinges; Cabinet is a plainer boarded one; Panels is the original build from floor planks and shutters.
  - Decoration: what hangs on the doors of any model. Shields is a carved shield over crossed axes on each door; Bronze hangs the game's bronze buckler and crossed bronze swords on one door and crossed axes on the other, and Iron, Silver, Blackmetal, Carapace and Flametal the same in each later metal.
  - Wood: the wood the Armoire and Cabinet are drawn in, the game's darkwood, the darkwood beam's brown planks, or plain wood.
- `Default Crown Shape` (default: Flat) / `Default Crown Height` (default: Medium) — the Armoire's crown, for every Armoire nobody has given its own: Flat is the stepped molding, Ogee and Pointed a bonnet top rising in an S or to a point, as tall as the height says, Low, Medium or High.

**\[Window\]**
- `Show Summary Text` (default: true) — show the textual list of items below each loadout's name (`Helm:..., Chest:...`, etc.).
- `Show Icons` (default: true) — show the row of item icons at the bottom of each loadout row.
- `Show Row Buttons` (default: false) — show Save, Load, +, Cmp and x on every row. Off puts them in a menu behind one button per row, which takes less room and lets the panel be narrower.
- `Panel Position X` / `Panel Position Y` — auto-updated when you close the rack so the panel reopens where you last left it. Persists across game sessions.
- `Panel Width` / `Panel Height` — auto-updated when you drag the grip in the panel's bottom-right corner. A loadout's icon groups sit on one line when they fit the width and wrap only when they don't. A height of 0 fits the panel to its loadouts.

---

## Requirements

- [BepInEx 5](https://valheim.thunderstore.io/package/denikson/BepInExPack_Valheim/)
- [Jotunn](https://valheim.thunderstore.io/package/ValheimModding/Jotunn/)

**Install it on the dedicated server as well as on every client, at the same version.** The server turns away a client on an older version, because the rack's grid is a server setting the client has to follow. The rack is a new prefab, and
a server that does not have it deletes racks built near the world spawn — permanently, with the
gear and the saved loadouts inside them. That is Valheim's own handling of a prefab it cannot
resolve, not something this mod can guard against from the client.

---

## Compatibility

- **Multiplayer compatible** — loadout data is stored in the rack's ZDO, so it persists through world saves and is visible to other players. The rack opens as an ordinary container, so everything vanilla does for a chest it does here: the ownership hand-off that makes the storage grid draw and save, the refusal when someone else already has it open, wards, and the privacy setting.
- **Container mods** — AzuAutoStore, AzuCraftyBoxes, Grab Materials and anything else that scans for containers treat the rack as the chest it is, because that is exactly what it is.
- **AzuExtendedPlayerInventory** — supported. Gear worn in Azu's equipment row and items parked in its quick slots are saved with the loadout and restored to the same cells. Azu's extra inventory rows are treated as the ordinary bag they are and left alone.
- Should be compatible with most other mods; no vanilla systems are permanently altered.

---

## Notes

- Save/Load/Compare/Delete buttons disable themselves when not applicable (e.g. Load is disabled when the slot is empty or when you're already wearing the saved set).
- The rack's doors swing open while the panel is open — purely visual, no gameplay effect.
- Load never draws a weapon. It puts items in your cells and leaves what you hold to you, as any chest does.
