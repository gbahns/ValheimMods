# Changelog

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
