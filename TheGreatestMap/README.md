# The Greatest Map

> **EARLY ALPHA / TEST BUILD.** Version 0.1.x is being tested by its authors on their own
> server. Expect rough edges, missing polish and changes between versions. Back up your world
> before trying it, and please report anything odd. Do not install it on a server whose players
> are not in on the test.

One shared, living map for your server, without the cartography table mess.

- **Markers share instantly.** Place a marker and everyone sees it. Erase it and it stays erased, for everyone, forever. The server keeps the only copy, so nothing ever comes back from a stale table.
- **The cartography table only carries exploration.** Walk up to it and it syncs the fog of war by itself (or press the sync key). Player-placed markers are no longer written to or read from tables, and old ones imported from tables are swept away.
- **A map you carry in your pocket.** No inventory slot. Press the map key and your character unfolds a map in the left hand and takes a pencil in the right. Recording happens only while it is out. Attacking, drawing a weapon, getting hit or swimming puts it away. Two server-synced settings control how strict the map is: "Require Map Out To Record" (on by default) and "Require Map Out To Edit" (off by default; when on, placing, erasing and crossing off markers on the map screen also need the map out).
- **Honest auto-recording.** While the map is out, your character writes down the important things nearby that *you actually found*: berry bushes, mushrooms, herbs, ore, dungeon entrances, runestones, traders, camps, boss altars and portals. Something counts as found only if you looked straight at it, had it under your crosshair, or interacted with it. There is no radar and nothing is ever revealed for you.
- **Markers that look like the thing.** A recorded marker uses the icon of the item it gives you: dandelion, thistle, raspberries, each mushroom, copper, tin, silver. Dungeons and camps use the trophy of what lives there, boss altars the boss trophy, traders coins. With icons this clear, plant and ore markers carry no text label by default, which is what keeps the map readable.

## Install

Install on the **server and every client** (BepInEx). The server keeps the shared markers in
`BepInEx/config/TheGreatestMap/<world>_<seed>.pins.bin`. In a non-dedicated game the hosting
player is the server.

A player who does not have the mod can still join a server that has it; they simply see vanilla
map behaviour and no shared markers. A player whose version of the mod is older than the
server's is refused at connect, so keep everyone on the same version.

## Keys (configurable)

| Key | Action |
|---|---|
| `Y` | Take the map out of your pocket / put it away |
| `U` | Read and write the nearest cartography table right now |

Pick keys no other mod acts on. If another mod equips an item on the same key as the map, the
map folds straight back up (equipping a hand item always puts it away).

**Single player and hosted games:** the game you host is the server, so the shared marker store
lives on your machine and everything works without a dedicated server.

## How recording works

1. Play normally. Things you look at, hover over or interact with are remembered as *found*.
2. Take the map out. After three seconds it starts writing down found things within ten metres.
3. Every plant gets its own icon (marker spacing 1 m for plants, 5 m for ore and portals, 20 m for locations), so a clump of four dandelions shows four dandelion icons. Plants, ore and runestones get no text label; dungeons, camps, traders, boss altars and portals do. Each kind's label rule is configurable: never, always, or one label per so many metres. A short message tells you when something was skipped because a marker is already there; the find stays pending and is written if that marker goes away.
4. Erase a recorded marker and the map will not record that kind of thing there again. Admins can lift that with `tgm_unsuppress`.

Recorded markers are tinted pale gold on the map so you can tell them from placed ones.

**Structures** (abandoned houses, log cabins, stone tower ruins, swamp huts, stonehenges, dvergr
towers and so on) are recorded too if you turn on "Record Structures", which is off by default.
They use the vanilla house icon and keep their name. It is meant for people who like to track
which ruins they have already searched: click the marker on the map to cross it off once you have
been through it. Common structures are matched by name prefix; any other outdoor location counts
as well unless "Structures Include Unlisted" is off or its prefab name is in the exclusion list.

## Console commands

| Command | What it does |
|---|---|
| `tgm_status` | Counts of shared markers, erased spots and pending discoveries |
| `tgm_list [n]` | Nearest shared markers |
| `tgm_import` | Share your existing local markers (the five standard icons) with everyone |
| `tgm_clearlocal` | Delete your local, non-shared player markers, including stale ones imported from tables |
| `tgm_sync` | Sync the nearest cartography table now |
| `tgm_resync` | Re-download shared markers from the server |
| `tgm_forget` | Forget found-but-unrecorded things |
| `tgm_wipe [auto\|all]` | Admin: erase recorded markers (default) or every shared marker, for everyone |
| `tgm_unsuppress` | Admin: allow recording again where recorded markers were erased |

## Configuration

`BepInEx/config/DeathMonger.TheGreatestMap.cfg`. The sharing rules (require the map to record,
require the map to edit, share placed markers, whether the table carries player markers) are server-synced. Everything
else is per player: keys, auto-sync radius and cooldown, record radius and dwell times, which
kinds of things to record, cluster radii, the prefab lists behind each kind, the icon for each
kind, and the position, rotation and scale of the parchment and pencil in your hands.

## Notes

- Boss, Hildir and memorial pins still share through the cartography table exactly as in vanilla.
- Pings work as always and never need the map out.
- The parchment shows the real world map around you; its position in the hand may need a tweak
  in the Visuals section for your taste.
- Version 0.1.0 is the first playable build. Expect rough edges, and please report them.

## Source

https://github.com/gbahns/ValheimMods
