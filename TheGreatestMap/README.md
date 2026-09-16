# The Greatest Map

> **ALPHA.** In steady use on its authors' own server, and open to anyone who wants it. Expect
> changes between versions and the occasional rough edge, and please report anything odd. Back up
> your world before trying it, as you would with any mod that writes to your save.

One shared, living map for your server, without the cartography table mess.

- **Markers travel like exploration.** What you record or place goes onto your own map, saved with your character. At a cartography table your map and the shared map merge both ways, and others pick it up at their next table visit. Two players standing together with their maps out compare and merge their maps directly, markers and explored areas alike, no table needed. An erasure is a dated tombstone that wins over older copies of the marker, so erased markers stay erased instead of coming back from someone else's map. Erasing is off unless the server allows it, and then goes through the marker's right-click menu or Shift + right-click. A server can switch "Sharing Mode" to Instant to have every change go out at once instead.
- **The cartography table only carries exploration.** Stand at it and it syncs the fog of war by itself (or press the sync key), and it stays quiet unless something is actually exchanged. Player-placed markers are no longer written to or read from tables, and old ones imported from tables are swept away.
- **A map you carry in your pocket.** No inventory slot. Press the map key and your character unfolds a map in the left hand and takes a pencil in the right. Recording happens only while it is out. Attacking, drawing a weapon, getting hit or swimming puts it away. Two server-synced settings control how strict the map is: "Require Map Out To Record" (on by default) and "Require Map Out To Edit" (off by default; when on, placing, erasing and crossing off markers on the map screen also need the map out).
- **Honest auto-recording.** While the map is out, your character writes down the important things nearby that *you actually found*: berries, mushrooms, herbs, seeds, wild plants, ore deposits, dungeon entrances, runestones, camps and portals. Something counts as found only if you looked straight at it, had it under your crosshair, or interacted with it. There is no radar and nothing is ever revealed for you. Traders, boss altars and structures are off by default, the first two because the game already marks them itself.
- **Markers that look like the thing.** A recorded marker uses the icon of the item it gives you: dandelion, thistle, raspberries, each mushroom, copper, tin, silver. Dungeons and camps use the trophy of what lives there, boss altars the boss trophy. With icons this clear, plant and ore markers carry no text label by default, which is what keeps the map readable. A deposit is recognized by what mining it yields rather than by a list of names, so an ore added by a later update or another mod is marked without anything being configured.

## Install

Install on the **server and every client** (BepInEx). The server keeps the shared markers in
`BepInEx/config/TheGreatestMap/<world>_<seed>.pins.bin`. In a non-dedicated game the hosting
player is the server.

A player who does not have the mod can still join a server that has it; they simply see vanilla
map behavior and no shared markers. Versions only have to match when the shared-marker format
changes: a server refuses a client older than its compatibility floor, which has been 0.2.0 since
that release, so a 0.2.x client can join a 0.3.x server. A release that moves the floor says so at
the top of its changelog entry.

## Keys (configurable)

| Key | Action |
|---|---|
| `Y` | Take the map out of your pocket / put it away |
| `U` | Read and write the nearest cartography table right now |
| `L` | Open the legend: every marker and the thing it stands for |

Pick keys no other mod acts on. If another mod equips an item on the same key as the map, the
map folds straight back up (equipping a hand item always puts it away).

**Single player and hosted games:** the game you host is the server, so the shared marker store
lives on your machine and everything works without a dedicated server.

## How recording works

1. Play normally. Things you look at, hover over or interact with are remembered as *found*. Looking counts only with clear line of sight and within a sighting distance that depends on the thing: plants 20 m, ore 40 m, buildings and dungeon entrances 80 m. Your character remembers a find for 30 minutes (seeing it again restarts the clock), and the memory is saved with the character.
2. Take the map out and it writes down everything found recently, wherever you are now. Set "Record Range" if you'd rather only record what is near you.
   You cannot write while something is hitting you ("Cannot Write Under Attack", on, for five seconds after the last blow). Enemies merely being nearby are no obstacle, unlike resting, so you can stand your ground and write if you choose. "Cannot Write While" optionally asks you to hold still too: Never, Running or Moving.
3. Every plant gets its own icon (marker spacing 1 m for plants, 5 m for ore and portals, 20 m for locations), so a clump of four dandelions shows four dandelion icons. Only traders and portals get a text label by default; everything else relies on its icon. Each kind's label rule is configurable: never, always, or one label per so many meters. A short message tells you when something was skipped because a marker is already there; the find stays pending and is written if that marker goes away.
4. Removing a marker depends on what it is. One you placed by hand is your own note, so you can always delete it; anyone else needs the server to allow erasing. A recorded marker stands for something really in the world, which does not forget, so those stay behind "Allow Erasing Markers" and are best thought of as a repair for a marker that is wrong. Hiding is the everyday way to stop seeing something. Erasing a recorded marker by hand also stops that spot being recorded again, which admins can lift with `tgm_unsuppress`.

Recorded markers are tinted pale gold on the map so you can tell them from placed ones, portals
excepted: they get their own color, described below.

**Hover a marker** on the large map and it tells you what it is: its name where it has one, what
kind it is and which icon it carries, whether it has been cleared or searched, and who recorded it
and when. Most markers carry no text label on purpose, since that is what keeps a map with hundreds
of them readable, so hovering is where the detail lives. "Marker Tooltips" turns it off.

**Right-click a marker** from this mod on the map screen for its menu: hide this one marker, hide
every marker with that icon (say all dandelions), hide the whole kind (all herbs), cross it off or
uncross it, delete or erase it (always yours to delete if you placed it; recorded markers need the
server to allow erasing), and, once anything is
hidden, show all hidden again. Hidden markers stay on your map and keep syncing; they are just not
drawn. The icon and kind choices are the Display settings "Hidden Icons" and "Show <Kind>", so they
can also be edited by hand, and `tgm_show` in the console brings everything back. Vanilla pins keep
their vanilla right-click.

**The marker button.** Above vanilla's icon buttons on the right edge of the large map sits a map
pin in the same pale gold as recorded markers. **Right-click** it to hide or show every marker from
this mod at once, the pin turning gray while they are hidden. That is the same gesture vanilla uses
on its own icon buttons, so right-click means the same thing everywhere in that column. Unlike the
per-kind switches, it also covers markers that have no kind, such as ones you placed yourself.
**Left-click** it for the list of kinds that have markers on your map, each with its icon. Click any
of them to hide or show that kind without leaving the map, and the list stays open so you can change
several. It also carries the hide-all row and a "Show all" row. Config "Marker
Button On Map" turns the button off.

A marker you have just recorded is drawn on both maps for thirty seconds even when its kind or its
icon is hidden, so writing something down always shows you what you wrote ("Reveal New Markers
Seconds", 0 turns it off). It does not override the button's own hide-all or a marker you hid by
hand, and it cannot reveal a kind whose icon vanilla's button has switched off.

Structures, portals, camps and boss altars are in that list too, even though they draw on vanilla's
own map icons (house, portal, campfire, boss) and vanilla's buttons hide those as well. The two are
not the same: vanilla's button hides every pin with that icon including ones you placed by hand,
while ours hides only the markers this mod recorded.

**Things that run out get crossed off, not forgotten.** Mining a deposit to nothing, or picking a
plant that never grows back, crosses its marker off for everyone instead of erasing it, so you can
see which ground has already been worked rather than walking back to an empty spot. "Show Cleared
Deposits" hides them once cleared if you would rather have a clean map, and "Show Searched Places"
does the same for everything else you have crossed off, chiefly structures you have searched, so
the map can show only the places you have not been to yet. The two are separate because they mean
different things: a cleared deposit is gone, a searched ruin is still standing and merely done
with. The record is kept either way.

**Portals are kept current, not remembered.** Everything else the mod records stays where you
found it, so writing it down once is enough. A portal is built by players and gets renamed and torn
down, so instead the server reads the game's own list of portals and the markers follow it: a
renamed portal's marker is renamed, and a portal that is gone has its marker erased for everyone.
"Map All Portals" (on by default) goes further and draws every portal that currently exists, not
only the ones someone has seen, which suits a server where everyone is in the same group. Turn it
off and portals are learned the way everything else is, by seeing one yourself or by someone
sharing their map with you. The extra portals are drawn only, never saved and never shared.

Portals get their own color, since they are the points you travel between and are worth spotting at
a glance. By default they fade slowly between two shades of orange, which reads as a gentle pulse rather
than a colour change; set "Portal Marker Color" and
"Portal Pulse Color" to any hex value or color name, clear the pulse color for a steady one, and
use "Portal Pulse Seconds" for the speed.

**Pause while the map is open** ("Pause While Map Open" in the Display section, off by default):
the game pauses while the large map screen is open, the way the ESC menu does. Solo or hosting
alone it works by itself. On a dedicated server it takes the Pause My Server mod, which then pauses
the world only while you are the only player online; with others online nothing pauses. Closing
the map resumes.

**Structures** (abandoned houses, log cabins, stone tower ruins, swamp huts, stonehenges, dvergr
towers and so on) are recorded too if you turn on "Record Structures", which is off by default.
They use the vanilla house icon, without a text label. It is meant for people who like to track
which ruins they have already searched: opening a chest inside one crosses its marker off for
everyone (config "Cross Off Structures When Searched"), and you can always cross one off by hand
with a click on the map. Common structures are matched by name prefix; any other outdoor location
counts as well unless "Structures Include Unlisted" is off or its prefab name is in the exclusion list.

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
| `tgm_look` | What the crosshair hits and every reason it would or would not be recorded |
| `tgm_portals` | What the portal system believes, and why a portal near you is or is not drawn |
| `tgm_erase <kind> [radius]` | Erase recorded markers of one kind near you, even when erasing by click is off |
| `tgm_relabel [kind]` | Apply the label rules to existing markers; labels are only ever removed |
| `tgm_reicon` | Repair dungeon markers that took the swamp crypt key because their cave was not in the catalog |
| `tgm_deposits` | Every deposit in the game, what it yields, and whether the catalog knows it |
| `tgm_admin` | Ask the server whether it treats you as an admin, and what player id it sees |
| `tgm_items <word>` | List the game's items matching a word, for choosing a marker icon |
| `tgm_legend` | Open the legend (same as the legend key) |
| `tgm_show` | Show every hidden marker again (single markers, hidden icons and hidden kinds) |

## Configuration

`BepInEx/config/DeathMonger.TheGreatestMap.cfg`. The sharing rules (require the map to record,
require the map to edit, share placed markers, whether the table carries player markers) are server-synced. Everything
else is per player: keys, auto-sync radius and cooldown, the record radius, what stops you writing
(under attack, and optionally while moving), which kinds of things to record, marker and label
spacing, marker size per kind (plants draw at 60% by default), whether each kind shows on the small
minimap, the prefab lists behind each kind, the fallback icon for each kind, the legend's size and
position, and the position, rotation and scale of the parchment and pencil in your hands.

## Notes

- Boss, Hildir and memorial pins still share through the cartography table exactly as in vanilla.
- Server admins: some hosts' panel "stop" kills the server without a world save. On the server, a file
  named `save-now` in `BepInEx/config/TheGreatestMap/` makes the game save the world and all player
  profiles at once (create it over the host's file API before stopping), and "Server Autosave Minutes"
  in the server's config adds an extra periodic save.
- Pings work as always and never need the map out.
- The parchment shows the real world map around you; its position in the hand may need a tweak
  in the Visuals section for your taste.

## Source

https://github.com/gbahns/ValheimMods
