# The Greatest Map

One shared, living map for your server, without the cartography table mess.

- **Markers travel like exploration.** What you record or place goes onto your own map, saved with your character. At a cartography table your map and the shared map merge both ways, and others pick it up at their next table visit. Two players standing together with their maps out compare and merge their maps directly, markers and explored areas alike, no table needed. An erasure is a dated tombstone that wins over older copies of the marker, so erased markers stay erased instead of coming back from someone else's map. Erasing is off unless the server allows it, and then goes through the marker's right-click menu or Shift + right-click. A server can switch "Sharing Mode" to Instant to have every change go out at once instead.
- **The cartography table still works, and you no longer need one.** Stand at a table and your map and the shared map merge both ways, markers and explored ground alike, without pressing anything; it stays quiet unless something was actually exchanged. Two players standing together with their maps out merge directly, no table in sight, which is usually how it happens on the move. The shared map lives on the server, so the mod belongs there as well as on each player -- installed only on your own game, a table will carry fog and nothing else.
- **Vanilla's own pin sharing is off by default.** The five standard pins you place by hand are not written to or read from tables, and stale copies imported from a table before are swept away -- otherwise every table visit breeds another copy of markers this mod is already keeping straight for you. "Table Carries Player Markers" (Sharing) gives vanilla's behavior back if you want it.
- **A map as big as your screen.** The game draws the large map inside a fixed margin, which on a wide screen wastes
a lot of room. Drag the gold grip in its lower-right corner to open it out, up to the whole screen, and the strip
along its very top edge to move it; where you leave it is remembered. The map grows but the pin labels, the icon
column and the buttons stay the size they were, so a bigger map is a less crowded one rather than a magnified copy.

- **Where is everybody?** On the large map, `P` lights up the other players: their markers grow, take a color of
their own and breathe, and every other marker stops being drawn, so three moving dots are easy to find on a map
carrying hundreds of them. Press `P` again, or close the map, to bring it all back exactly as it was, including
whatever you had hidden yourself. Nothing is written and nothing is shared; it is a way of looking. Only players who
are sharing their position can be shown, since that is all the game tells your client, and you are told when nobody
is. "Highlight Players Hides Others" keeps the rest of the map if you would rather it stayed, and the color and size
are settings too.

- **A map you carry in your pocket.** No inventory slot. Press the map key and your character unfolds a map in the left hand and takes a pencil in the right. Recording happens only while it is out, and a folded-map icon sits among your status effects to say so ("Map Out Status Icon"). Attacking, drawing a weapon, getting hit or swimming puts it away. Two server-synced settings control how strict the map is: "Require Map Out To Record" (on by default) and "Require Map Out To Edit" (off by default; when on, placing, erasing and crossing off markers on the map screen also need the map out).
- **Honest auto-recording.** While the map is out, your character writes down the important things nearby that *you actually found*: berries, mushrooms, herbs, seeds, wild plants, ore deposits, Yggdrasil roots, dungeon entrances, runestones, camps, portals and campfires. Something counts as found only if you looked straight at it, had it under your crosshair, or interacted with it. There is no radar and nothing is ever revealed for you. Traders, boss altars and structures are off by default, the first two because the game already marks them itself.
- **Markers that look like the thing.** A recorded marker uses the icon of the item it gives you: dandelion, thistle, raspberries, each mushroom, copper, tin, silver. Dungeons and camps use the trophy of what lives there, boss altars the boss trophy. With icons this clear, plant and ore markers carry no text label by default, which is what keeps the map readable. A deposit is recognized by what mining it yields rather than by a list of names, so an ore added by a later update or another mod is marked without anything being configured.

## Install

Install on the **server and every client** (BepInEx). The server keeps the shared markers in
`BepInEx/config/TheGreatestMap/<world>_<seed>.pins.bin`. In a non-dedicated game the hosting
player is the server. Back up your world before trying it, as you would with any mod that writes to
your save.

A player who does not have the mod can still join a server that has it; they simply see vanilla
map behavior and no shared markers. Versions only have to match when the shared-marker format
changes: a server refuses a client older than its compatibility floor, which has been 0.2.0 since
that release, so a 0.2.x client can join a 1.0 server. A release that moves the floor says so at
the top of its changelog entry.

## Keys (configurable)

| Key | Action |
|---|---|
| `Y` | Take the map out of your pocket / put it away |
| `U` | Read and write the nearest cartography table right now |
| `L` | Open the legend: every marker and the thing it stands for |
| `T` | On the large map: light up the other players and put every other marker out (T for team) |

Pick keys no other mod acts on. If another mod equips an item on the same key as the map, the
map folds straight back up (equipping a hand item always puts it away).

**Single player and hosted games:** the game you host is the server, so the shared marker store
lives on your machine and everything works without a dedicated server.

## How recording works

1. Play normally. Things you look at, hover over or interact with are remembered as *found*. Looking counts only with clear line of sight and within a sighting distance that depends on the thing: plants 20 m, ore 40 m, buildings and dungeon entrances 80 m. Your character remembers a find for 30 minutes (seeing it again restarts the clock), and the memory is saved with the character.

   Places too big to aim at — Fuling villages, tar pits, camps, dungeon entrances, boss altars — are found by looking in their direction rather than by putting the crosshair on them, since the crosshair lands on grass or sky or a passing goblin. The same rules hold: it must be in front of you, within its sighting distance measured to its edge, and not behind a hill. Only the landscape blocks the view, so a tree at the edge of a village or something standing in it makes no difference. `tgm_look` says which places it can see and why the others do not count.
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
**Left-click** it for the list: one column of the kinds that have markers on your map, each with
its icon and how many of it there are. Click any of them to hide or show that kind without leaving
the map, and the list stays open so you can change several. A kind that covers several types --
ore, dungeons, plants -- has a triangle at the head of its row: open it and the types are listed
underneath, each with a switch of its own, so the copper can go and the silver stay. Open as many
as you like; the list scrolls on the wheel while the pointer is on it, and the title strip, the
find box and the foot stay put while it does.

The triangle in the title strip, beside the gear and the stick pin, opens every heading at once and
shuts them all again. The headings and nothing else: the kinds under them keep their own triangles
and whatever state you left them in, since opening those as well would put a couple of hundred type
rows on the screen at one press. It shows the state the list is in rather than what the press would
do -- turned down while every heading is open, lying on its side while any of them is folded away.

Each heading carries a triangle button of its own, at the end of its row, which opens or shuts
every kind under that heading -- the fold between the corner button and a single row's triangle.
It turns down while everything under it is open, and it opens the heading too if you had folded
it.

Every kind sits under one of five headings, each with a triangle of its own: **Resources** (what
you harvest, and what runs out), **Points of Interest** (the places you found: dungeons, camps,
ruins, runestones, spawners), **Places** (the landmarks the game marks for you as you find them:
the boss altars, the traders, the center of the world -- a handful in a whole world),
**Placed Markers** (what you put on the map by hand, about something the catalog will never know) and
**Dynamic Markers** (ships, carts, portals, other players, and the fires and benches a base is
made of -- what is standing now rather than what was found). Pressing a heading hides every kind
under it.

Under Resources, two rows stand in place of the five plant kinds: **Farm** and **Gather**, each
listing its types directly. They are named for what you do about them rather than for what they
are, which is the point of splitting them at all. The reason to write a plant down is that you will have to come back for
it, and planting it is what stops that being true -- find one patch of magecap and you need never
find another, while nothing will ever grow you a raspberry bush. So one half is worth keeping on
the map and the other is worth folding away and forgetting. The line falls between types rather
than between kinds because the game puts it there: magecap and jotun puffs can be planted and the
other four mushrooms cannot, flax and barley can and fiddlehead cannot. Farm holds the three crop
seeds, barley, flax, jotun puffs, magecap and vineberries; Gather holds raspberries, the three
common mushrooms, dandelion, blueberries, thistle, cloudberries, fiddlehead and smoke puffs. Ore is
a row of its own either way, no vein being farmable. Progression order lists each half by biome --
the order you can first plant one, and the order you first walk past one -- since a Catalog line
orders its own kind and cannot say where one kind's entries stand against another's. "Split Plants By Farming" turns it off and lists the five kinds as
before; nothing about the markers themselves changes either way.

**The gear** in the list's title strip, beside the stick pin, opens the list's settings in front
of it. They close on a click away, and go with the list.

- **Counts**: whether a heading, kind and type says how many markers it has, and how many of
  those are drawn where the two differ. Off is quieter to read down and worth having once you know
  your own map. The count of what you are finished with keeps its button either way, that one
  being the switch for them as well as the number.
- **Grouping**: whether the plants are split into Farm and Gather in place of the five plant
  kinds.
- **The Map's Own Buttons**: whether the game's row of pin buttons is off the large map, this list
  having grown to cover every one of them.
- **Marker Order**: **Progression** (the order the catalog lists them in, which is the order you
  meet them -- a dandelion before a thistle, copper before silver), **Most first**, or **A to Z**.
  Reorder a Catalog line and the progression order follows it.
- **Labels**: marker labels, portal names and boss names, each with a switch. Three rather than one
  because they are not the same complaint -- marker labels run to thousands, a base writes six
  portal names across itself, and the five boss names the game writes itself are each worth
  reading.

The small buttons at the end of a row -- place by hand, only, ping, and the count of the ones you
have crossed off -- are drawn on the row the pointer is resting on, and any one of them that is
already doing something is drawn wherever it is, so an armed kind, a filtered one, or one hiding
its crossed-off markers goes on saying so. Each button answers for itself: a lit one does not
bring its neighbors out with it, or hiding everything crossed off would fill the list with
buttons.

The ping makes every marker of that kind swell and wave for a few seconds, and brings them in
front of the rest of the map while they do -- a marker pulsing behind its neighbors on a crowded
map is a button that did nothing as far as you can tell. They stay in front afterwards, so what
you last asked about is what you can see.

"12 done" at the end of a row hides the twelve markers of that kind you have crossed off, and
lights up while they are hidden. A type row carries its own count, so the burial chambers you are
finished with can go while the sunken crypts you have cleared stay, and the copper you have worked
out can go while the raspberry bushes you have picked stay. One row at the foot does the lot at
once.

"Hide all" at the right of the title strip, next to the stick pin, is the master switch: check it
and everything you have put on the map comes off at once -- this mod's markers, the pins you
placed by hand, your death markers and your bed -- whatever the switches below say. The game's own
landmarks stay, since they are the map rather than notes on it: boss altars, traders, Hildir's
camps and the center of the world. So do other players, their pings and their shouts. Uncheck it
and everything comes back exactly as it was. It is a box to check rather than a button because it
does not show everything -- it only stops holding everything off.

Pressing a type row while its whole kind is hidden shows that type: the other types of that kind
stay hidden, so the map changes by exactly the row you pressed rather than bringing the kind back
wholesale.

At the foot of the list is the row that puts back everything these switches hide -- the kinds, the
crossed-off groups, icons hidden one picture at a time and markers hidden one at a time, the same
as `tgm_show`. It names what it will put back ("Show everything hidden -- 2 kinds, 5 markers") and
is there only while there is something to put back, so it cannot be read as another copy of the
hide-all row above it. Config "Marker Button On Map" turns the button off.

**Finding one thing.** At the top of that list is a find box. Type into it and the map keeps only
the markers mentioning what you typed, as the letters go in: "frost" leaves the frost caves,
"eggs" leaves the portal you named 3 Eggs, "ore" leaves the ore. It reads the words written on a
marker, the type it is, and the name of its kind, so you need not know which of those your word
lives in. Press Enter and what matched becomes rows, one per type with a count, and clicking one
narrows the map to exactly that. Every kind's row also ends in **only**, which does the same for
the whole kind in one press, and takes it back when pressed again.

None of this is remembered or written to the config. It sits over the hide switches rather than
among them: whatever you had hidden stays hidden and is still hidden when you stop looking, and
there is no list of kinds to switch back on afterwards. While it lasts it overrides those
switches, because asking for frost caves means the frost caves whether or not dungeons are
switched off -- though a marker you hid by hand stays hidden, since that means "not this one"
rather than "not right now". The row under the box says what the map is showing and puts it all
back when clicked, and `tgm_find` does the same from the console for a map that is not even out.

A marker you have just recorded is drawn on both maps for thirty seconds even when its kind or its
icon is hidden, so writing something down always shows you what you wrote ("Reveal New Markers
Seconds", 0 turns it off). It does not override the button's own hide-all or a marker you hid by
hand, and it cannot reveal a kind whose icon vanilla's button has switched off.

Structures, portals, camps and boss altars are in that list too, even though they draw on vanilla's
own map icons (house, portal, campfire, boss) and vanilla's buttons hide those as well. The two are
not the same: vanilla's button hides every pin with that icon including ones you placed by hand,
while ours hides only the markers this mod recorded.

**Under Places there is one row per thing, however it got onto the map.** The game marks a
handful of places by itself as you find them -- the boss altars, the traders, Hildir's three quest
sites, the center of the world -- and draws every one of them as the same kind of pin, which is why
they were a single row called Landmarks. They are told apart here by the location each one stands
on, and the row that is already about that thing takes it over: **Boss Altars** means the altars the
world marks, the ones a vegvisir pointed you to and any this mod recorded; **Traders** means the
game's trader pins and any marker of ours. **World Center** and **Hildir's Quests** are rows of
their own, and anything the mod cannot name keeps a row called **Landmarks**.

Which of the world's locations are altars and which are traders comes out of the Boss Altars and
Traders catalog lines, so editing a line moves these rows with it. These pins are hidden one at a
time rather than through vanilla's filter, which is one switch for all of them; "Hidden Landmarks"
is where that is written down.

Nothing records a trader -- the game has already marked him, and a second pin saying the same thing
is worse than none. Boss altars can be recorded, off by default, because this mod's marker wears the
trophy of the boss whose altar it is.

**The pins you placed yourself are in the list as well.** Under Placed Markers there are rows for the
five icons the game lets you drop, your death marker and your bed. These press vanilla's own switch
rather than a copy of it, so hiding your hammer pins here is the same act as double-clicking the
hammer under the map, and that button grays out to match. The bed is a switch the game itself does
not offer. The five placeable icons also carry the place-by-hand button, which arms the game's own
placing for that icon -- so a row of the game's portal pins appears only when you have actually
dropped one by hand, the live portal markers being this mod's own and counted on their own row.

The game gives these pins no names -- they are five pictures, and what each means is whatever you
decided it means -- so they ship named after what they look like (Fire, House, Hammer, Dot, Portal)
and "Vanilla Pin Names" is where you write down what yours are for: `Icon2=Places,Icon3=Resources`.

**So the game's own row of pin buttons can come off the map.** Every one of them has a row here now
-- the same switch, the same placing -- and two sets of controls for one map, in a corner of it,
saying the same things in different words, is one set too many. "Hide Vanilla Controls" does it,
off to begin with, since it takes part of the game's own screen away. The whole strip goes: the
buttons, both of the plates behind them -- the game keeps the five you place in one box and the
death and boss filters in another -- and the little mouse pictures beside them. Nothing else of the
map's is touched -- the public-position box, the biome name, the pin-name box and the shared-map
hint are each asked for by name and left where they are -- and nothing is hidden while the marker
button is off, that being the way into the list.

The ping is the exception, and it has a line at the foot of the list: a ping is a gesture rather
than a kind of marker, so there is no row for it to be. "Ping a spot out loud", then double-click
the map, as its button did. While it is armed the line says so, and a double-click pings instead
of placing a marker until you stop it -- which is also what the game's button did, more quietly.

**Things that run out get crossed off, not forgotten.** Mining a deposit to nothing, or picking a
plant that never grows back, crosses its marker off for everyone instead of erasing it, so you can
see which ground has already been worked rather than walking back to an empty spot. Using something
up counts as finding it, so if it had no marker yet your character remembers it like any other find
and writes it down already crossed off the next time the map comes out. Copper and
silver are the exception, and are left alone unless "Cross Off Mined Copper" or "Cross Off Mined
Silver" is turned on. Much of a copper deposit or silver vein can be out of sight underground, so a
player cannot always tell whether they have got all of it, and a map that crossed it off for them
would be telling them something they could not know. Tin and plants are different: there is no
doubt when those are done. You can always cross one off by hand.

**Labelling a marker.** A marker's right-click menu offers "Add label", or "Change label" if it
already has one, and opens the game's own pin-name box with the label in it: Enter keeps what you
typed and Escape leaves it be, exactly as when you name a pin you have just placed. "Remove label"
takes one off, which is its own item because the game reads an empty box as a cancel. The label
travels with the marker, so everybody sees it at the next sync. Most
markers are written without one deliberately -- the icon says what a berry bush is, and a map with
a thousand names on it is a wall of words -- so this is for the things nothing can work out:
"good fishing", "Marco's base", "the tin we never finished". "Show Marker Labels" behind the gear
still decides whether any of them are drawn.

**Hiding what you are done with.** Crossed-off markers stay on the map, and which of them are
drawn is decided on the rows they already have in the marker button's list -- the "12 done" button
at the end of a kind's row, or of one of its type rows, plus the one row at the foot that covers
every kind at once. Underneath, two different ideas:

- **Cleared deposits**, by type: the copper you have worked out, the raspberry bushes you have
  picked. The thing itself is gone rather than merely done with, so there is no group for one of
  these to belong to and they are hidden by their icon, in "Hidden Cleared Types". "Show Cleared
  Deposits" is the blanket switch over all of them; turning one type back on while it is off lifts
  the blanket and leaves the rest hidden.
- **Searched places**, by type: Structures on their own, then dungeons by type (Burial Chambers,
  Sunken Crypts, Frost Caves, Troll Caves, Infested Mines, Bear Caves, Other Dungeons), then any
  other kind you have crossed off, and your own placed markers. The button on the Dungeons row
  covers every dungeon type and leaves Structures alone.

A dungeon marker does not store which dungeon it is, so its type is read from its icon, the same way
you tell them apart on the map. Structures all share the vanilla house icon, so abandoned houses,
log cabins and stone towers are one group. A crossed-off marker's right-click menu offers the
switch that covers it, plus "Hide all searched dungeons" on a dungeon. The choices are kept in
"Hidden Searched Places" in the Display section, and the record is kept either way.

**Portals are drawn from the world, not remembered.** Everything else the mod records stays where
you found it, so writing it down once is enough. A portal is built by players and gets renamed,
torn down and rebuilt, so a marker written once is wrong within days. Instead the server reads the
game's own list of every portal and your map draws that: a renamed portal is right immediately, a
rebuilt one appears, and one that is gone leaves nothing behind. Nothing is stored, nothing is
shared, and switching "Show Portals" off leaves no trace. Because the list is the world's, every
portal is drawn rather than only the ones someone has seen -- portals are built by the players, so
on a server where everyone is in the same group this is simply the truth; on a public server, or
if you would rather learn a portal the way you learn everything else, turn the kind off.

**Ships and carts are drawn where they are now.** Every vessel in the world appears on the map at
its current position -- the longship moored at the outpost, the karve somebody sailed off in, the
cart left halfway up a mountain. Like portals these are drawn only: nothing is written to
your map, nothing is shared, nothing is left behind when you switch the kind off, and none of it can
reach a cartography table. The "found, not detected" rule does not apply to them, which is
deliberate -- a vessel is your own property, and knowing where you parked it is not a discovery
about the world.

The server does the finding, so this covers the whole world rather than the part loaded around you:
a boat forty minutes away is still on your map, and it is still there after you rejoin. Modded ships
are included without anyone maintaining a list, because the vessels to watch for are worked out from
what is actually in the world. Each one is called what the game calls it -- a longship reads
"Longship", not "Viking Ship" -- though the names are off by default ("Show Transport Labels"),
since a harbor of six ships writes six names across the coast and the icon already says what is
there. A vessel that is moving does not wait for the next sweep: the server passes its new
position on four times a second ("Transport Movement Seconds"), so a ship under sail slides
along the map, while a moored harbor sends nothing because nothing has moved. The sweep itself,
which is what finds vessels in the first place, runs every five seconds by default ("Transport Sweep
Seconds"); these pins answer "where did I leave it" rather than "where is it this second", and a
ship riding the swell at anchor does not count as having moved. Hovering one names it, which is
how to tell a knarr from a longship with the labels off. Their settings live together under
"Dynamic Markers", apart from the settings for markers written on the map.

Portals get their own color, since they are the points you travel between and are worth spotting at
a glance. By default they fade slowly between two shades of orange, which reads as a gentle pulse rather
than a color change; set "Portal Marker Color" and
"Portal Pulse Color" to any hex value or color name, clear the pulse color for a steady one, and
use "Portal Pulse Seconds" for the speed.

**Pause while the map is open** ("Pause While Map Open" in the Display section, off by default):
the game pauses while the large map screen is open, the way the ESC menu does. Solo or hosting
alone it works by itself. On a dedicated server it takes the Pause My Server mod, which then pauses
the world only while you are the only player online; with others online nothing pauses. Closing
the map resumes.

**Campfires** are recorded when you build one, and when you see one another player built: they are
where a group stopped, and worth finding again. The marker is the campfire's own picture from the
build menu, in its fire colors, with no text label. Taking a campfire down removes its marker for
everyone. Fires that belong to camps and villages the world generated are not counted. "Record
Campfires" turns it off.

**Workbenches** work exactly the same way, and are there for the players who hold the spawning off
around a base with benches rather than with fires. Only one somebody built counts, the marker is
the bench's own build-menu picture, and taking the bench down takes the marker with it. Any
crafting station can be listed under "Workbenches" in the Catalog section -- a forge, a
stonecutter -- and the workbench is the one listed because it is the one that holds spawning off.

**Structures** (abandoned houses, log cabins, stone tower ruins, stonehenges, dvergr towers and so
on) are recorded too if you turn on "Record Structures", which is off by default. Nothing in the
swamp is: what is worth going to there is already marked by something else, so the huts, wells,
ruins, graveyards and wrecks are left alone rather than painting the biome gold.
They use the vanilla house icon, without a text label. It is meant for people who like to track
which ruins they have already searched: opening a chest inside one crosses its marker off for
everyone (config "Cross Off Structures When Searched"), and you can always cross one off by hand
with a click on the map. Common structures are matched by name prefix; any other outdoor location
counts as well unless "Structures Include Unlisted" is off or its prefab name is in the exclusion list.

The exclusion list is about places the catalog does not name: a location **listed** under Structures
is recorded because it is listed, whatever the exclusions say, so leaving one out for good means
taking its catalog line out too. The mod does both for a type it has decided should never be
recorded, once, to catalog lines you already had.

## Console commands

| Command | What it does |
|---|---|
| `tgm_status` | Counts of shared markers, erased spots and pending discoveries |
| `tgm_list [n]` | Nearest shared markers |
| `tgm_find [text]` | Show only the markers mentioning some text, and list the types that matched; on its own, show everything again |
| `tgm_transports` | Which vessel prefabs are watched, how many ships and carts are out there, and why one is or is not drawn |
| `tgm_dedupe [apply]` | List markers standing on one another, or fold each group into one |
| `tgm_import` | Share your existing local markers (the five standard icons) with everyone |
| `tgm_clearlocal` | Delete your local, non-shared player markers, including stale ones imported from tables |
| `tgm_sync` | Sync the nearest cartography table now |
| `tgm_resync` | Re-download shared markers from the server |
| `tgm_forget` | Forget found-but-unrecorded things |
| `tgm_wipe [auto\|all]` | Admin: erase recorded markers (default) or every shared marker, for everyone |
| `tgm_unsuppress` | Admin: allow recording again where recorded markers were erased |
| `tgm_look` | What the crosshair hits and every reason it would or would not be recorded |
| `tgm_paint` | What is colored into the map for player building and paved roads |
| `tgm_portals` | What the portal system believes, and why a portal near you is or is not drawn |
| `tgm_erase <kind> [radius]` | Erase recorded markers of one kind near you, even when erasing by click is off |
| `tgm_relabel [kind]` | Apply the label rules to existing markers; labels are only ever removed |
| `tgm_reicon` | Repair dungeon markers that took the swamp crypt key because their cave was not in the catalog |
| `tgm_deposits` | Every deposit in the game, what it yields, and whether the catalog knows it |
| `tgm_admin` | Ask the server whether it treats you as an admin, and what player id it sees |
| `tgm_items <word>` | List the game's items matching a word, for choosing a marker icon |
| `tgm_missing [all]` | What this mod can record that your map has none of yet, by kind |
| `tgm_legend` | Open the legend (same as the legend key) |
| `tgm_show` | Show every hidden marker again (single markers, hidden icons, hidden kinds and hidden crossed-off markers) |
| `tgm_mapui` | What the large map's layout measures: how much screen it uses and how its parts are anchored |
| `tgm_spawners` | Every spawner prefab in the game, what it sends out, whether it can be broken, and whether the catalog knows it |
| `tgm_locations [word]` | Every place the generator put in **this world**, counted by type, biggest first: which kind each lands in, the name it would wear, how many stand in ground you have uncovered, and whether it was swept in unlisted. The list a decision about what to stop recording is argued from |
| `tgm_items <word>` | Every picture the game has whose name contains that word: items, build pieces, the map's location pictures and its own pin sprites |
| `tgm_adopt [reach] [apply] [plants]` | Test worlds: ask the world what your hand-placed markers were pointing at, write the recorded marker for it, and remove the hand-placed pin |
| `tgm_survey [count] [all] [apply] [kinds]` | Test worlds: mark every location and resource the world holds where you have explored, as if you had recorded them; name kinds to take only those. Without `apply` it waves them on the map instead of writing them, for as long as a ping lasts; `tgm_survey off` ends that wave early |
| `tgm_undo [name] [confirm]` | Take back a `tgm_adopt` or `tgm_survey` run: its markers go, the hand-placed pins it removed come back |

`tgm_adopt` and `tgm_survey` are for building a map to test against, not for playing. Both list
what they would do and write nothing until told to `apply`, both obey the same spacing, label and
erasure rules as ordinary recording, and both need the world itself -- single player, a world you
are hosting, or the server's own console.

**A survey without `apply` also waves what it would write**, with the map open: a pin at each
spot, wearing the icon that marker would wear, pale and pulsing so they can be told from the
markers that are really there. A count cannot be looked at -- "611 berries" says how much and
nothing about whether they come out as a readable scatter or a green crust over the Meadows -- and
that is the question worth asking before writing a thousand markers.

Nothing is written, saved or shared, and the wave is all of it: the preview comes off the map when
the pulse ends, after `Locate Seconds`. Run the dry run again to see it again, or `tgm_survey off`
to end a wave early. The nearest 2,000 are drawn and the report says how many were left out,
because a preview that stalls the client is worse than no preview.

A marker you had crossed off is written crossed off, and if something already stood for that
thing, that marker is crossed off instead -- the tick is the one thing on a hand-placed pin that
nothing else in the world remembers.

Either of them can be taken back. Applying one copies your whole map aside first (`tgm_backups`
lists it, `tgm_restore` puts it back) and writes down what it did, so `tgm_undo` can remove exactly
the markers that run wrote and put back exactly the pins it removed, leaving anything recorded
since where it is. `tgm_undo` on its own describes the most recent run; `tgm_undo confirm` does it.
It lifts its markers rather than erasing them, so the map is simply as it was and nothing is
suppressed -- which also means nothing travels, and a copy that already holds them (another
player, a cartography table) would hand them back at the next merge.

`tgm_adopt` reads the picture on each hand-placed pin before it reads the ground: a hammer means a
place, a dot means a resource, a fire means a fire, and distance decides only between answers of
the same sort. It never answers with a plant unless told `plants`, and it never answers with
anything inside a dungeon, since Valheim keeps those interiors in a space of their own and a
marker written for one would land on the surface above the entrance. A marker you had named is
converted only if the answer agrees with the name; everything it leaves alone is listed with what
the world offered for it, and **Adopt Name Hints** in the config is where your own shorthand goes
(`T=TrollCave, S=Greydwarf, PLAINS=none`), including `none` for the notes that are not about
anything in the world. **Adopt Pin Meanings** overrides what it made of each picture, which the
listing prints.

## What players have made, drawn on the map

Markers say *what is here*. They cannot say how big it is, or what runs between. So player
building and paved roads are colored into the map instead — wooden building the light warm brown
of the game's wood pieces, stone gray, iron slate, paved road its own pale shade, each
configurable under `[Map Painting]`.

When enabled, draws colors on the map to represent player-built structures and roads.
**`Player Structures Enabled` is off by default**, while this is still being got right. It lives
under `[Map Painting]` and is the server's; until it is on nothing here happens at all — no
sweeping, no sampling, nothing sent, and any map already painted goes back to the way the game
drew it.

It goes into the map picture the game itself draws, which means the large map, the minimap and
the parchment in your hands all show it, with panning, zoom and fog handled for free: a base
under unexplored ground stays hidden until you have been there. The resolution is the map's own
— a shed is about a pixel, a base is a patch, and a road is a line a little wider than the road
really is.

The game draws that map from three pictures, not one: a flat color per biome, a mask that draws
forest over it, and a height that decides where the sea is. So a painted pixel has its forest
cleared too, and one that stood under the water line is lifted a meter above it — otherwise the
black forest draws trees over the paint and a dock is painted under the sea. `Paint Over Water`
turns the lifting off if you would rather the map kept its coastline exactly. `Paint Spread`
decides how many map pixels around each painted one are painted as well: 1 by default, because a
single pixel of two thousand is averaged away into the terrain when the map is drawn scaled down.

Only pieces a player raised are drawn. Every piece the world generator places carries no
builder, so ruins, camps and crypts are left out of it.

And only where there is enough of it. Everything a player raises is a piece, so a campfire lit
while exploring or a workbench put down to repair a hammer would otherwise be a building in the
middle of nowhere; a cell is drawn where `Least Pieces To Draw` pieces stand within a cell of it,
which is a hut and not a torch. Ships and carts are left out of it too — they are built pieces
like any other, but they move, and the live markers already draw them.

Roads work the other way round from everything else here. Paving is paint on the terrain, and
terrain only exists where somebody is standing, so there is nothing to sweep: each client reports
the paved ground it walks over, and the server keeps it for everybody and saves it beside the
map. A road reaches every player's map the first time any one of them walks it. `tgm_paint` says
what is being drawn and whether the ground under you reads as paved.

## Configuration

`BepInEx/config/DeathMonger.TheGreatestMap.cfg`. Settings fall into two groups, and which group a
setting is in follows one question: does your choice change what other players see?

**The server's**, marked `[Synced with Server]` and settable only by admins while "Lock
Configuration" is on (it is by default): the sharing mode, whether erasing recorded markers is
allowed, whether the map must be out to record or to edit, whether placed markers are shared,
whether the cartography table carries player markers, whether mined-out copper and silver and
searched structures are crossed off, how near and how often two players' maps merge (the exchange
radius and cooldown), whether comparing maps shares explored ground, everything under
`[Cartography Table]`, and the three that rewrite markers other people wrote (the label rules
applied on sync, the dungeon icon repair, and forgetting crops on tilled ground). These are rules
of the shared map: crossing a deposit off puts a line through it on everybody's map, so it should
not be decided by whoever happened to swing the pick.

Two of those are there for a second reason. The exchange radius and cooldown are used by whichever
player reaches out, and the other machine does half the work, so they are the one pair that lets
one player spend another player's frames. And the cartography table settings look local but are
not: the last step of a table visit is a merge of your map with the shared map on the server, so
they decide how often each client asks the server to work. The sync key is deliberately exempt --
asking for a sync by hand always works, so nobody is ever stuck with a stale map.

The settings whose descriptions begin "Server only" are marked `[Synced with Server]` as well.
Nothing on a client reads them, and the mark is how the mod says so. Turning "Lock Configuration" off lets any player set them for the whole
server, which is how it worked before 1.2.0. Single player and hosting your own game are
unaffected — you are the server.

**Yours**, and nobody else's business: keys, the record radius, what
stops you writing (under attack, and optionally while moving), which kinds of things to record,
marker and label spacing, marker size per kind (plants draw at 60% by default), whether each kind
shows on the small minimap, the prefab lists behind each kind, the fallback icon for each kind,
everything under Display including all the hiding, the legend's size and position, and the position,
rotation and scale of the parchment and pencil in your hands. Your own file keeps your own values
for the server's settings too, so what you play with alone is never overwritten by a server you
visited.

## Notes

- Boss, Hildir and memorial pins still share through the cartography table exactly as in vanilla.
- Server admins: some hosts' panel "stop" kills the server without a world save. On the server, a file
  named `save-now` in `BepInEx/config/TheGreatestMap/` makes the game save the world and all player
  profiles at once (create it over the host's file API before stopping), and "Server Autosave Minutes"
  in the server's config adds an extra periodic save. The `[Server]` section holds that autosave
  interval and how far behind a player may fall before the server stops holding erased markers open
  for them; the ship and cart sweep interval sits with the other dynamic-marker settings under
  `[Dynamic Markers]`. All three are read on the server alone.
- Pings work as always and never need the map out.
- The parchment shows the real world map around you; its position in the hand may need a tweak
  in the Visuals section for your taste.

## Source

https://github.com/gbahns/ValheimMods
