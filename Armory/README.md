# Armory

Craft an **Armory Rack** to save, store, and instantly recall named equipment sets — armor, weapons, shield, cape, utility, and the eight hotbar slots — without rummaging through chests every time you switch between farming, mining, exploring, and fighting.

The rack is also a real storage container. Drop your alternate gear in, walk up, press [Use], and pick the loadout you want. Whatever you're currently wearing is swapped *back into the exact slot the new gear came from*.

---

## Features

- **Named loadout slots** — save your current equipment as `"Farming"`, `"Mining"`, `"Boss"`, etc. Rename by clicking the slot name. Up to 10 slots per rack.
- **Combined armor + hotbar** — captures helm, chest, legs, cape, utility, right-hand, left-hand, **and** quickbar items 1–8.
- **Storage + loadouts in one** — the rack is also a Container (6×4 grid). Load pulls items from the rack *and* your inventory; saved gear in the rack counts as "available".
- **In-place swap** — when Load equips a new item, whatever was in that slot gets stored back at the exact grid position the new item came from. Nothing ends up on the action bar unless that's where it was sitting.
- **Live status** — a green ● on the Load button means you're already wearing this loadout. Missing items show red in the summary text and red in the icon strip.
- **Compare window** — side-by-side view of your current gear vs. the saved set, with color-coded availability.
- **Optional in-game pause** — config flag pauses the game while the panel is open, the way the ESC menu does. Works by itself solo or hosting alone; on a dedicated server it takes [Pause My Server](https://valheim.thunderstore.io/package/DeathMonger/PauseMyServer/).

---

## How to Use

1. Build an **Armory Rack** from the Hammer menu's **Furniture** tab. Requires a Workbench in range. See [Recipe](#recipe).
2. Press **[Use]** on the rack to open the panel — your inventory and the rack's storage appear alongside it.
3. Click **Save** (↓) on a slot to capture what you're currently wearing and the items in hotbar slots 1–8.
4. Click **Load** (↑) on a saved slot to equip that loadout. Items come from the rack first, then your inventory. Whatever you were wearing gets stored in the slot the loaded item came from.
5. Click the slot name to rename it. Click the red **x** to delete the slot (empty slots and duplicates delete without confirmation; non-empty unique slots prompt first).

---

## Recipe

| Material | Quantity |
|---|---|
| Fine Wood | 8 |
| Round Log | 4 |
| Bronze Nails | 4 |
| Bronze | 2 |
| Deer Hide | 2 |

Buildable at the Workbench. Black Forest tier.

---

## Configuration

The config file is created at `BepInEx/config/GBahns.Armory.cfg` on first run.

**\[General\]**
- `Pause Game While Armory Open` (default: false) — pauses the game while the rack panel is open, the way the ESC menu does. It asks the game for the pause rather than stopping your own clock, so it works by itself when you are playing solo or hosting alone, and on a dedicated server it takes [Pause My Server](https://valheim.thunderstore.io/package/DeathMonger/PauseMyServer/) to grant it. Without that mod the request is refused on a server, which is the point — your clock stopping while the server runs on would only desync you. While the pause is in effect, a craft started from the inventory screen cannot finish until you close the rack; the craft timer counts game time.

**\[UI\]**
- `Show Summary Text` (default: true) — show the textual list of items below each loadout's name (`Helm:..., Chest:...`, etc.).
- `Show Icons` (default: true) — show the row of item icons at the bottom of each loadout row.

**\[Window\]**
- `Panel Position X` / `Panel Position Y` — auto-updated when you close the rack so the panel reopens where you last left it. Persists across game sessions.

---

## Requirements

- [BepInEx 5](https://valheim.thunderstore.io/package/denikson/BepInExPack_Valheim/)
- [Jotunn](https://valheim.thunderstore.io/package/ValheimModding/Jotunn/)

---

## Compatibility

- **Multiplayer compatible** — loadout data is stored in the rack's ZDO, so it persists through world saves and is visible to other players. Opening the rack goes through the same ownership handshake vanilla uses for chests, so the storage grid draws and saves correctly whoever walks up to it, and a rack someone else already has open tells you so instead of letting you both edit it.
- **Wards and privacy** — the rack respects guard stones and the container privacy setting exactly as the chest it is built from does.
- **AzuExtendedPlayerInventory** — supported. Items sitting in Azu's extra equipment slots are detected and swapped correctly.
- Should be compatible with most other mods; no vanilla systems are permanently altered.

---

## Notes

- Save/Load/Compare/Delete buttons disable themselves when not applicable (e.g. Load is disabled when the slot is empty or when you're already wearing the saved set).
- The rack's doors swing open while the panel is open — purely visual, no gameplay effect.
- Held items (weapons/tools you have equipped in your hands) are part of the loadout and get swapped on Load just like armor.
