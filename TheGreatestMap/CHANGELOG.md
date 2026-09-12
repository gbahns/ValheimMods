# Changelog

## 0.1.2 — 2026-09-11

Early alpha, third test build.

- Buildings are found by looking at their walls, floors, chests or doors, or by opening a chest or door inside them. The game spawns those pieces as separate objects with no link back to the location, so before this only a location's non-networked parts counted, and abandoned houses were never recorded.
- Version bumps no longer force the server and every player to update at the same moment: the compatibility floor stays at 0.1.0 until the shared-marker format changes.

## 0.1.1 — 2026-09-11

Early alpha, second test build.

- Burial chambers and the other well-known locations keep their intended icons (skeleton trophy, troll trophy, boss trophies, coins) even when the catalog line in an older config file lacks the `|Icon` override.
- Dungeons, camps and boss altars no longer carry a text label by default; their trophy icon says what they are. Set the kind's "Label Spacing" to 0 to get labels back.
- Loads on Linux dedicated servers (the plugin no longer filters on the process name).
- Two server-synced strictness settings: "Require Map Out To Record" (on: found things are only recorded while the pocket map is out) and "Require Map Out To Edit" (now off by default; on gates placing, erasing and crossing off markers on the map screen).

## 0.1.0 — 2026-09-11

**Early alpha / test build.** Published so the authors' own server can test it; expect rough
edges and changes between versions. For Valheim 1.0.7. Install on the server and all clients.

- Markers use the icon of the thing itself: the item a plant gives, what a deposit drops, trophies for dungeons, camps and boss altars, coins for traders, the house pin for structures.
- One icon per plant (1 m spacing) and no text label on plants, ore or runestones; labels on locations. Both rules configurable per kind.
- Optional recording of structures (abandoned houses, ruins, towers), off by default, for tracking which ones you have searched.
- Pocket map defaults to Y, table sync to U, to stay clear of common quick-slot keys.

- Shared markers with a server-side store: placed markers appear for everyone at once, erased markers stay erased, late joiners get the full set on spawn.
- Cartography tables carry exploration only; auto-sync when in reach plus a sync key. Stale player markers imported from tables are swept.
- Pocket map: keybind, no inventory slot, both hands (parchment showing the live world map, pencil). Required to place or erase markers. Put away on attack, weapon draw, damage, swimming, teleport, death.
- Honest auto-recording of found things (looked at, hovered, or interacted with) within 10 m after the map has been out for 3 s. Erasing a recorded marker suppresses re-recording there.
- Eight new marker icons (berries, mushrooms, herbs, ore, dungeons, runestones, traders, camps) plus boss altars and portals on vanilla icons.
- Console commands: tgm_status, tgm_list, tgm_import, tgm_clearlocal, tgm_sync, tgm_resync, tgm_forget, tgm_wipe, tgm_unsuppress.
