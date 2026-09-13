# Changelog

## 0.2.3 — 2026-09-13

Client-side; works with a 0.2.x server.

- Right-clicking one of this mod's markers on the map now opens a small menu instead of erasing it: "Hide this marker", "Hide all Dandelion markers" (that icon), "Hide all herbs" (that kind), "Cross off" / "Uncross", "Erase for everyone" (dimmed unless the server allows erasing, as before), and "Show all hidden" once anything is hidden. Shift + right-click is still the quick erase where erasing is allowed. Vanilla pins keep their vanilla right-click.
- Markers hidden one at a time are remembered with the character; icon and kind choices go straight into the Display settings ("Hidden Icons", "Show <Kind>"). Hidden markers stay on the map and keep syncing.
- New console command `tgm_show` brings every hidden marker back in one go.
- Kind buttons on the map: under vanilla's icon buttons on the right edge of the large map there is now one button per kind that has markers on your map (berries, herbs, ore, structures and so on), showing the kind's icon. A click, left or right, hides or shows that kind, and a hidden kind's button turns gray, the same as vanilla's own filter buttons. Config "Kind Buttons On Map" (on by default).
- New option "Pause While Map Open" (off by default): the game pauses while the large map screen is open, the way the ESC menu does. It uses vanilla's own pause, so it works by itself solo or hosting alone, and on a dedicated server through Pause My Server, which grants it only while you are the only player online. The map screen's own timers (flick scrolling, click and double-click timing, the pin name box) now run on unscaled time, so the map stays usable while the game is paused, by this option or by an admin pause.

## 0.2.2 — 2026-09-13

Client-side; works with a 0.2.x server.

- The "Show <Kind>" switches, per-kind sizes and minimap switches now also cover markers recorded before kinds were stored (anything from before 0.1.4): their kind is worked out from their icon through your catalog settings and remembered on your map. Before this, old berry and herb markers ignored the switches.

## 0.2.1 — 2026-09-12

Client-side; works with a 0.2.0 server.

- Fences, poles and similar pieces no longer count as buildings: a structure marker needs a cluster of at least four connected pieces including a floor, wall, roof or door. Round-pole fences around a farm were getting house markers.
- New console command `tgm_erase <kind> [radius]` erases recorded markers of one kind near you (default 50 m) even when erasing by click is off; the erasure carries to everyone at the next merge. Use it to clean up the fence markers.
- `tgm_look` now says why a piece was not treated as a building.
- Comparing maps with another player now shares explored areas too, the way a cartography table does, not just markers ("Exchange Exploration", on by default).
- Hide what you don't want to see: "Show <Kind>" switches per kind hide a whole kind on both maps, and "Hidden Icons" hides single icons such as Dandelion. Hidden markers stay on your map and keep syncing. Both are in the new Display section and apply at once.

## 0.2.0 — 2026-09-12

Early alpha. **Server and all clients must update together** (new map format and network
messages; compatibility floor 0.2.0).

- **Markers now travel like exploration.** What you record or place goes onto your own map, saved with your character per world. At a cartography table your map and the shared map merge both ways; other players pick it up at their next table visit. "Sharing Mode" (server-synced) switches back to Instant, the old behavior, if a server prefers it.
- **Map to map.** Two players standing within 5 m with both maps out compare and merge their maps directly, no table needed. Once a minute per pair; a message reports what came across.
- **Erasures stay erased.** Erasing leaves a dated tombstone on your map; when maps merge, a tombstone beats any copy of the marker older than it, and a marker recorded again later beats the tombstone. Later change wins for cross-offs and labels too.
- **Erasing is off by default** ("Allow Erasing Markers", server-synced). When on, it takes Shift + right-click, so a stray click cannot lose a marker. Vanilla pins are unaffected.
- Existing shared maps load unchanged; personal maps start empty and fill from the shared map at the first table visit.

## 0.1.5 — 2026-09-12

Early alpha, fifth test build. Client-side only; works with a 0.1.4 server.

- Taking the map out writes down everything you have found recently, wherever you are now. The old ten-meter rule is gone by default ("Record Range" = 0; set a distance to bring it back).
- Your character remembers a find for 30 minutes ("Found Memory Minutes"); seeing it again restarts the clock. The memory is saved with the character, so a relog inside the window keeps it.
- How far away something counts as seen now depends on what it is ("<Kind> Look Distance"): plants 20 m, ore and portals 40 m, runestones 30 m, dungeons, structures, camps, altars and traders 80 m. Looking straight at it with clear line of sight is still required; nothing is ever found for you.
- The Meadows abandoned farm (WoodFarm1) is a structure with the house icon, not a draugr camp. Only WoodVillage1, the draugr village beyond 2000 m, gets the draugr trophy.
- New console command `tgm_look`: reports what the crosshair hits and every reason it would or would not be recorded. The look ray's hit buffer is much larger, so the nearest object can no longer be dropped in dense areas.
- Cartography table auto-sync is silent unless something is exchanged: the table is read first and only new areas count, and it is written (vanilla's "map saved" and effect) only when it lacks areas you have explored. The sync key reports "already up to date" when there is nothing to do. Sync radius default is now 1 m, meaning standing right at the table.
- Server: a file named `save-now` in `BepInEx/config/TheGreatestMap/` makes the server save the world and all player profiles at once (for hosts whose panel stop kills the server without saving; deploy scripts create it over the host's file API). New "Server Autosave Minutes" setting adds an extra periodic save on top of vanilla's.
- Each building in a compound (farm, village) gets its own marker, placed at the building's center, since each may hold a chest or beehive to search. Buildings are told apart by following connected world-built pieces from the one you looked at; fences and poles don't link buildings together. Opening a chest or harvesting a beehive crosses off that building only. Structure marker spacing is now 6 m, and structures carry no text label by default; the house icon says it all. Set "Structures Label Spacing" to 0 to get names back.
- Label rules are applied to recorded markers that already exist, once after each sync (config "Apply Label Rules To Existing Markers") or on demand with `tgm_relabel`: within each kind, the oldest marker of a same-named cluster keeps its label and the rest lose theirs, for everyone. Labels are only ever removed, never added back.

## 0.1.4 — 2026-09-11

Early alpha, fourth test build. **Server and all clients must update together**: markers now
carry their kind and network packets a format version, so the compatibility floor moves to 0.1.4.

- Opening a chest inside a structure crosses its marker off for everyone, so "have I searched this one" answers itself. A structure with no marker yet is remembered as searched and its marker starts crossed off once recorded. Config: "Cross Off Structures When Searched" (on by default).
- Recorded markers can be drawn smaller per kind: plants default to 60%, ore to 80%, everything else full size. Config: "<Kind> Marker Size", 20 to 100 percent.
- Each kind can be hidden on the small minimap while still showing on the large map. Config: "<Kind> On Minimap".
- Existing marker files and stores load unchanged; older markers simply have no kind and keep full size.

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
