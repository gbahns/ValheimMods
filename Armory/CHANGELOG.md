# Changelog

## 2.0.0 — 2026-09-21

Loadouts changed shape in this release: what a loadout loads is now chosen cell by cell, and the panel was rebuilt around that. Everything below is new since 1.3.0.


- **A loadout is the cells you choose.** Every cell a loadout saves — each worn slot, each hotbar key, each quick slot — can be in the loadout or left out of it. Load applies the cells that are in and leaves everything else exactly as it is. So "Fenris" can be armor alone, "Bow kit" the bow in 4 and the arrows in 7, and "Ashlands" the food and meads, and loading all three in a row builds the outfit. Ten slots go a lot further that way.
- A new loadout starts with the helmet, chest, legs and cape and nothing else. That is what most switching is; the belt, wisplight, food and tools you carry everywhere don't need saving.
- A row shows only the cells its loadout loads. Press **+** on the row to see every cell that was saved, click cells to add them or take them out, and press **-** to tuck the rest away again. The label above a group — Armor, Weapons, Food and so on — adds or removes the whole group at once, and its color says whether the group is all in, all out, or mixed.
- Save still records everything you are wearing and carrying, and keeps the row's choices cell for cell, so re-saving "Fenris" over new gear never resets what it loads. A cell added back later shows what was there at the last save.
- **The trinket slot is part of a loadout now**, under Accessories with the belt. It was not captured before.
- A worn slot that is in the loadout but empty means "wear nothing there": an included empty Cape takes your cape off. The summary line says so, as "no cape".
- **Load no longer draws a weapon.** It puts gear in the cells you chose and leaves what you hold to you, the way any chest does. The Weapon and Shield entries that recorded the hands are gone; a drawn weapon has no slot of its own, it sits in a hotbar cell, and that is where it is listed.
- Racks saved by an older version load everything, as they always did, until you take a cell out. An older version reading a rack from this one ignores the choices and the trinket, and drops both if it saves the slot again.
- Icons are a little smaller to make room for the labels.
- A rack keeps exactly the slots you leave it with. It used to pad itself back up to five every time it was opened, so deleting the last three was undone on the next visit. Only a rack that has never been saved to starts with five.
- **The row buttons fold into a menu.** By default each row now has an **Equip** button and a three-bar button that opens a menu with Save, Show all cells, Compare and Delete. That takes less than half the room, so the panel can be dragged much narrower. Equip reads "Wearing" in green while you have the loadout on. `Show Row Buttons` in the config brings the five separate buttons back.
- **The panel can be resized.** Drag the grip in its bottom-right corner. The rows follow the width as you drag: a loadout's icon groups sit on one line when they fit and wrap onto another only when they don't, so a wider panel is a way to keep every loadout on a single line. The size is remembered with the position; until you set a height, the panel fits its loadouts.
- "Saved", "Loaded" and "Deleted" now appear above the panel. They were Valheim's center message before, which draws beneath any mod panel, so they went unread behind this one.

- **Saving a loadout no longer sweeps up ordinary inventory.** With AzuExtendedPlayerInventory's "Extra Inventory Rows" turned on, whatever happened to be lying in those extra bag rows — resin, nails, arrows — was saved as part of the loadout, and Load would then try to put it back. The capture assumed the bag was exactly 8×4 and treated every cell beyond that as one of Azu's special slots. It now asks Azu which cells are its quick slots and equipment row, and captures only those; the rest of the bag is yours, however many rows it has.
- Loadouts saved before this version still carry those stray entries. Re-save each affected slot once and they are gone.
- Without AzuExtendedPlayerInventory, only worn gear and the hotbar are captured, as before. An Azu older than the version that publishes the cell queries gets the same treatment, with a note in the log.

## 1.3.0 — 2026-09-16

- **Racks can be named.** Point at one and press **Shift+[Use]** — the same modifier that renames a portal or edits a sign — and type a name. It replaces "Armory" on the hover text, heads the loadout panel, and heads the storage panel beside it, so a row of racks along a wall tells you which is which without opening any of them. Leave the name empty to go back to "Armory".
- **A rack can be yours alone.** A Personal/Shared button in the top-left of the panel decides whether anyone may open the rack or only the player who built it. Personal is how you keep your own loadouts without sharing a rack's ten slots with everyone on the server — and since a rack is also its own storage, the gear each set needs comes with it. Only the builder can flip the switch; everyone else sees which it is.
- The enforcement is Valheim's own, not this mod's: a personal rack refuses with the same message a personal chest does, and the game will not let anyone hammer one down while it still holds items. If you want to remove a personal rack, empty it first or set it back to Shared.
- The setting lives on the rack rather than in your config, because the game checks access on both the player asking and the machine that owns the rack — a per-player setting would let those two disagree about who may open what.
- **Everyone needs this version for Personal to hold.** That same pair of checks runs on the machine of whoever is asking and on whoever owns the rack — usually the player standing at it, not you. A player still on an older version leaves the rack public as far as their game is concerned and walks straight in, and once in, nothing stops them saving over your loadouts either. Update the server and every player together. This is no different from vanilla's personal chest, which is only enforced by the copy of the game doing the asking.
- A rack the game never recorded a builder for cannot be made personal, and says so. Access is enforced by comparing against the builder, so a rack with no builder on record would refuse everyone permanently — and a private container holding items cannot be hammered down either.

## 1.2.0 — 2026-09-15

**Install this version on your dedicated server as well as on every client.** Earlier versions were client-only.

- **A server without this mod deletes racks built near the world spawn.** The rack is a prefab that exists only because this mod creates it, and Valheim does not simply ignore an object a server cannot resolve — the server takes ownership of it and destroys it, permanently, with the stored gear and every saved loadout inside. It only reaches as far as the server simulates around the world center, so a base further out was never at risk, but a rack near spawn was. Installing the mod on the server is the fix, and the gate that made that impossible is gone.
- **The rack behaves like a chest in multiplayer, because it now is one.** It used to open its storage itself rather than letting the game do it, which skipped the hand-off Valheim performs when you open a container. Anyone who was not already the object's owner got the loadout panel with no storage grid beside it, and anything they moved into the rack — including the gear a Load swapped back in — was discarded without a word. Opening the rack is now an ordinary container open, so the grid draws and saves for whoever walks up to it, a rack someone else already has open says so instead of letting you both edit it, and wards and the container privacy setting apply exactly as they do to the chest it is built from.
- **The optional pause no longer throws the inventory off the screen.** Valheim slides the inventory and container panels into place with an animation, and an animation runs on game time, so stopping the clock froze that slide part-way: the player's grid sat off the top of the screen with the rack's grid over what was left of it. It now finishes properly while the pause is held.
- **That pause asks the game rather than stopping your own clock.** It goes through the same call the ESC menu makes, so it still works by itself solo or hosting alone, while on a dedicated server it takes [Pause My Server](https://valheim.thunderstore.io/package/DeathMonger/PauseMyServer/) to grant it. Previously it froze your client while the server carried on without you, which is a good way to desync. One limitation remains: while the pause is in effect a craft started from the inventory screen cannot finish, because the craft timer counts game time too. Close the rack to let it run.
- Nothing needed to change for AzuAutoStore, AzuCraftyBoxes, Grab Materials or anything else that works with containers. They always saw the rack, because its container is a real one.

## 1.0.0 — 2026-09-09

First public release, built for Valheim 1.0.7.

- Armory Rack build piece (Hammer > Furniture; needs a Workbench in range) that is both a storage container and a loadout station.
- Up to 10 named loadouts per rack, each capturing helm, chest, legs, cape, utility, right hand, left hand and hotbar slots 1-8.
- Load pulls gear from the rack first, then your inventory, and stores what you were wearing back into the exact slot the new item came from.
- Live status on each slot, a compare window for current gear versus the saved set, and an optional pause while the panel is open.
- Loadouts are stored in the rack's ZDO, so they survive saves and work in multiplayer.
