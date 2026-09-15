# Changelog

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
