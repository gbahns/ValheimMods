# Changelog

## 4.15.0 — unreleased

- Boss altars are no longer recorded, like traders and Hildir's quests before them: the game marks
  every altar it knows about and a record of our own was never drawn. Record Boss Altars, and the
  marker spacing, label spacing and look distance that went with it, are gone.
- Gone with them: Boss Altars and Hildir Quests Marker Size and On Minimap, four settings that
  were wired to nothing.
- A Record switch now decides what you copy as well as what you write down: markers of a kind you
  do not record are no longer taken off a cartography table or another player's map, and a merge
  says how many it turned away. Markers you recorded yourself always come back to you.
- New: Record Manual Markers From Others (Recording), on. Off turns away the markers other players
  placed by hand instead of copying them onto your map.
- New: Show Manual Markers From Others (Display), on. Off hides the ones already there, without
  turning anything away.
- New, under Dynamic Markers: Ships, Carts and Portals each get a Marker Size and an On Minimap
  setting, the two every other kind already had. Size goes to 400%, as the player marker does.
- Crossing a marker off by clicking it no longer takes it off the map before you can see what it
  was. It blinks for ten seconds first, the map says what it was, and clicking it again puts it
  back. The blink says it is on its way out rather than staying.
- New: Reveal Crossed Off Seconds and Flash Crossed Off Markers (Display), for how long that
  lasts and whether it blinks.

## 4.14.0 — 2026-11-23

- Sightings is now two kinds with a switch each: Fish, and Big Game. Big Game offers the serpent,
  bonemaw serpent, bear, vile, gjall, deer, troll, fallen valkyrie, seeker soldier, lox, stone
  golem, abomination, asksvin and morgen. Sightings you have already placed move to whichever of
  the two their picture belongs to.
- Rows under them are named after the creature rather than after its trophy: Serpent, not Serpent
  trophy.
- Right-clicking a row in the marker list does what its "+" does: the next marker you place by
  hand wears that row's picture.

## 4.13.1 — 2026-11-23

- Fixed: with Show Player Developments on, the minimap could turn into a solid white square and
  stay that way, hiding the map underneath while the markers kept drawing on top.

## 4.13.0 — 2026-11-22

- Sightings has a row for each thing you might have seen, instead of one serpent: every fish in
  the game, both serpents, the bear, the vile and the gjall. Each row has its own picture, its own
  switch, and a "+" to write down that you saw one.

## 4.12.1 — 2026-11-22

- Fixed: with Sharing Mode on Table, a character arriving in a world for the first time was handed
  the whole shared map on spawn instead of starting blank and merging at a cartography table.

## 4.12.0 — 2026-11-22

**Everyone needs this version to play together.**

- With RoundMinimap, the buildings drawn on the minimap no longer spill outside the circle, and
  they turn with it when it turns. Any mod that reshapes the minimap this way is matched, not just
  that one, and the layer goes back to square the moment the round minimap is switched off.
- Markers are kept inside the circle that is drawn rather than the largest one that would fit, so
  a circle scale under one no longer leaves markers out on the HUD.
- Fixed: a marker placed by hand vanished when you clicked away from its name box on a crowded
  map. It was being thinned as a repeat of the recorded markers around it; a marker you place by
  hand is never thinned now, and shows the moment you place it.
- Fixed: placing silver by hand drew the Ore pickaxe instead of silver once the name box closed.
  A marker you place keeps the picture you placed it with, which is also the row it files under
  and the switch that hides it.
- tgm_list says when a marker is not drawn because it was thinned, instead of reporting that no
  reason could be found.
- Fixed: markers this mod's older versions left in the character file came back as plain dots
  with their labels still on, instead of being tidied away, on the first map of each session.
- Those dots are now swept as well, by the marker each one stands on. One map was carrying two
  per marker, 16,198 in all, and they came back however often you deleted them.
- New: Portal Marker Style, under Display. ColoredPins is the plain portal pin in the portal
  color and is the default, Icons is the game's own build-menu render of that portal so the wood
  one and the stone one tell themselves apart, and OutlinedIcons and BoldOutlinedIcons trace the
  portal color around the render's own outline, thin or thick, which thickens it and colors it at
  once.
- New: Stone Portal Marker Color. Portals that take ore and metal through are drawn in their own
  color, so the one that carries the copper home is not the color of the six that do not.
- A portal marker wearing a picture breathes in brightness rather than color, because painting a
  picture of a thing takes the picture away.
- New: Stone Portals, under Icons. The picture for an ore-carrying portal nobody can build, and so
  has no render; it is a stone arch this mod draws.
- TheGreatestPortal 1.7.0 draws portals the way this mod is set to draw them, style and colors
  both, so a portal looks the same with either mod, both, or neither.
- The "+" that places a marker by hand is now on every row, not just the kind. Press it on Silver
  and the next marker you place is a silver vein, in the silver picture -- before you have mined
  one, or dug it up at all.
- The dim rows in the marker list wear their own picture instead of the kind's, so the ones under
  Ore no longer all look like a pickaxe, and each has a "+" to write down just the one you are
  standing at.
- Portals can be placed by hand. They were refused for being drawn live from the server, which was
  never a reason not to write down that there is one over there.
- New: Record What The Wishbone Finds, on. Whatever your wishbone is pinging about is written down
  where it stands, without digging it up.

## 4.11.1 — 2026-11-18

- New: Varied Ground Shades. Paths, roads and fields are drawn in shades that wander from
  place to place, so ground reads as ground instead of as a sticker. Set it to 0 for the
  flat color it was.
- Dirt paths and leveled ground are drawn darker, so ground sits under the buildings on it
  rather than outshining them. Set Dirt Path Color back to AB8042 for the old tan.
- Zooming the map out is much lighter: a pixel the painting has already covered is not
  covered again, where before four hundred cells could be drawn onto the same one.
- A redraw slow enough to feel now says so in the log, with what it drew.
- Drake and volture nests are recorded as the icon alone, like every other found thing.
  They had been writing their name beside every marker. Existing ones lose the label at the
  next sync; set Nests Label Spacing back to 0 if you want it.
- Recording no longer writes to the F5 console. It still goes to the HUD and the log; the
  console is for answers to things you typed, and recording scrolled them away.
- tgm_ground now reports the nearest painted point when there is none exactly underfoot,
  which is most times you stand on a speck and ask about it.
- The server settles the map picture far faster: whether a spot is part of somebody's base is
  worked out once per twelve-meter square instead of nine times for every cell.
- The drawing reads only the cells near the window, instead of every cell in the world on every
  redraw.
- Tracing outlines no longer allocates a list for every corner it walks.
- The log's codec comparison is measured once per grid rather than every time the picture grows.
- A player joining no longer makes the server encode the whole picture again from nothing.
- A settle slow enough to be felt now says so in the log, with the number, counting the
  sending as well.
- A cartography table that has not changed since your last visit is no longer unpacked and
  read again; it was most of a quarter second, once a minute, to find nothing.
- Reading a picture from the server no longer copies it into a second table of the same
  cells.
- Reading and sorting a picture now say so in the log when either is slow enough to feel.

## 4.11.0 — 2026-11-16

**Everyone has to update together: a cell is recorded differently, so an old client and a new
server draw each other's maps wrong.**

- Paint Detail Meters can go down to 0.5, and says so in the log instead of silently ignoring a
  smaller number.
- The player-building drawing keeps up with the map instead of trailing it by a tenth of a
  second when you drag.
- Ground you leveled is drawn as dirt instead of left green. Only ground that actually went
  somewhere -- up into a wall, down into a pit -- is held back by Paint Raised Ground and Paint
  Dug Ground now, and a hole judges the ground it is in rather than everything within five
  meters of it.
- Shaped ground inside a base is drawn whatever Paint Raised Ground and Paint Dug Ground say:
  a yard leveled into a slope has real fill in it, and what is built around it is a better
  question than how far the ground moved. The same shaping out on a hillside is still left out.
- Fainter paint counts. The map used to need the ground half painted before it would draw it,
  which cut a meter or two off the edge of every worked patch.
- Dirt is drawn in the color of the dirt in the game, measured off it rather than picked. Your
  own Dirt Path Color is kept if you set one.
- Beehives, smelters, chests and other fixtures color the ground they stand on when no building
  does, so a yard full of hives is on the map.
- New command: tgm_ground, which says what the world's terrain record holds under your feet and
  why the map drew it that way.

- EXPERIMENTAL. Player buildings are drawn with a hard outline instead of as a block of color:
  the grid's steps are straightened into walls, so a hall looks drawn rather than pixelated. New:
  Draw Outlines.
- EXPERIMENTAL. The map is drawn from the shape of each piece rather than from a point in it, so
  a building covers the ground it actually stands on.
- EXPERIMENTAL. Paint Detail Meters now defaults to 1 instead of 4, which the change above makes
  usable.
- Least Pieces To Draw is counted over twelve meters rather than over cells, so changing Paint
  Detail Meters no longer retunes it. Removed: Least Pieces In One Cell.
- Ground you dug, leveled or raised is no longer drawn as though it were a road: only the hoe's
  path and paved road are. Ground you built up and ground you cut away are told apart, so an
  earth wall can be drawn without every mining scrape coming with it. New: Paint Raised Ground,
  Paint Dug Ground, Shaped Ground Least Meters.
- EXPERIMENTAL. The minimap is drawn sharp too, not just the large map. New: Sharp Minimap.
- Wooden building is a deeper brown and cultivated ground a darker one. Yours change with them
  unless you picked your own.
- Painted colors match the ones you picked. The sharp layer drew them lighter and flatter
  than the config showed.
- Ground is drawn as a surface rather than a half-empty lattice below a meter a cell.
- The soft halo around painted buildings is gone: the game's own map texture is no longer painted
  underneath the sharp layer, unless something still needs it.
- Changing Paint Detail Meters takes effect at once instead of at the next sweep.

## 4.10.0 — 2026-11-16

**Breaking: everyone needs this version.** The map picture carries two new kinds of ground and
the old road-reporting message is gone.

- Hoed dirt paths and farmland are drawn on the map, alongside paved road.
- Roads and paths are read from the world's own terrain records instead of being sampled around
  each player, so the whole world is on the map at once -- including ground laid long before the
  mod was installed, and ground nobody has walked since.
- The painting is recorded at 4 meters instead of 8, so shapes follow what is really there.
- The picture is compressed before it is sent, which is what makes the finer grid affordable.
- Map painting is marked experimental and is now off at both ends: Server Shares Player
  Developments and each player's own Show Player Developments both default to off.
- Renamed: Player Structures Enabled is now Server Shares Player Developments, and Show Map
  Painting is now Show Player Developments -- the painting covers roads, paths and fields as well
  as buildings, and the names now say who does which half. Both start at off; the old lines are
  tidied away.
- The large map draws the painting sharply. It is drawn over the part of the world you are
  looking at rather than over the whole of it, which is about a meter to a pixel zoomed in
  instead of twelve. The minimap is unchanged.
- Every color setting now lives in one [Colors] section instead of being filed with the thing
  it colors, so they sit together in the config manager. Your existing colors move with them.
- Chests, forges, hearths, ovens, kilns, smelters, cooking stations, incinerators, beehives
  and item stands no longer decide what a building is made of -- a wall of black metal
  chests was drawing as an iron building. They still count toward there being a building.
- Reinforced black marble and the carved marble heads draw as black marble rather than as
  iron.
- Black marble is drawn in its own near-black color instead of as stone. New: Black Marble
  Color.
- A cell and a map pixel are drawn as whatever they are mostly made of, instead of as the
  hardest material standing in them -- a wooden longhouse on stone footings came out stone
  gray. Building still wins over the ground it stands on.
- Every color setting is a real color now, so the config manager draws a swatch and sliders
  instead of a text box. Existing colors are kept.
- "Portal Pulse Color" says "no pulse" with a zero alpha rather than an empty value, since a
  color picker cannot type an empty one.
- New: Paint Dirt Paths, Paint Farmland, Paint Detail Meters, Dirt Path Color, Farmland Color.

## 4.9.3 — 2026-11-13

- Label Spacing says what it really does: it decides the markers you find from now on, and
  lowering it does not put labels back on the ones you already have.
- The marker list opens where you dragged it, pinned or not, and pinning no longer overwrites a
  spot you set yourself.
- The list's title bar drags it whether or not it is pinned. It only answered while pinned, which
  read as the title bar going unresponsive for no reason.
- "Marker Button At Map Bottom" puts the map pin beside the game's icon column and measures its
  offsets from that corner, so a button moved down there holds its place when the map is resized.

## 4.9.2 — 2026-11-11

- Portal markers stand down for the whole of TheGreatestPortal's map, not only while a
  destination is being chosen, so its "Show on map" no longer draws two markers per portal.

## 4.9.1 — 2026-11-10

- Sunken crypts wear the swamp key and dvergr bases their lantern, and a catalog line written by an older version is brought along: the correction only touches an entry still holding word for word what this mod used to ship.
- A heading in the marker list is the sum of the rows under it. It counted for itself, from
  a different set of markers than the rows read, so Placed Markers said 2 over 25 hammers
  and 33 dots and Places said 14 over rows adding to 26.
- The Ore list names the giants' remains: Giant Gear and Giant Skeleton, which shared the
  copper scrap and the black marble and so came out as two rows both called "Giant".
- A marker tooltip's prefab and icon line is off until you want it, under Troubleshooting in
  the gear menu.
- Hiding a kind hides it under your feet too, where you built it. Standing next to something
  shows it whatever is switched off -- so you can tell "none here" from "hidden" -- and that
  doubt does not arise about your own workbench.
- Menus name a marker by what it is, and a whole picture by what wears it: "Dvergr Statue",
  and "Hide all Dvergr Ruins markers" rather than the name of the block the picture was
  borrowed from. A picture is a costume, and several things can wear one.
- tgm_items finds a picture by the name the game shows it under, not only by its prefab id. Searching for what a thing is called came back empty while the thing was standing in the
  game: nobody calls the circlet HelmetDverger.
- tgm_items draws the pictures it finds, at the size a marker wears them, beside the name each one
  is typed as. It listed names, which is the one form in which a picture cannot be judged.
- Dvergr bases wear the dvergr lantern. The trophy is a braid that reads as nothing at pin size,
  and the fallen towers beside them wore the same one.
- A fallen dvergr guard tower is no longer recorded as a dvergr guard tower. The Enemy Bases
  line matched both, so 150 of the 350 towers in a world wore the name and the trophy of one
  the dvergr still hold. The fallen ones are scenery now, with the statues and viaducts.
- A marker list row covering several differently-named things is named by what they have in
  common, or by their kind: "Dvergr" rather than "Dvergr Harbour" over a row holding guard
  towers, excavations and lighthouses too. Several prefabs under one name keep it.
- Structures are listed by biome: Meadows, Black Forest, Swamp, Mountains. Each row switches,
  isolates and points at that biome on its own, so meadows farmsteads can go while mountain ruins
  stay. They all wear the house pin, so one row per picture was one row for the whole kind.
- A row in the marker list covering several things is named after the kind rather than after
  whichever of them came first. Every structure wears the house pin, so one row stood for farms,
  ruins and huts alike and called them all "Graveyard".
- tgm_identify can work out what a building is. The server was asked what stands at a
  marker and answered with the objects first and the place itself last, then kept only the
  first eight -- and a ruined house has dozens of planks within six meters, so the place was
  dropped from every answer that had one. **Needs the server updated as well as the client.**
- A structure the catalog has never heard of says what it is instead of "Structures". Turning on
  Structures Include Unlisted promises that the place's prefab name, tidied up, becomes the marker
  text, and only the kind ever came of it.
- Only a place the world generator actually places can be swept in as a structure. The rule asked
  whether the catalog had no line for the name, which is true of nearly every prefab in the game,
  so anything standing near a marker could answer for it.
- tgm_identify orphans strip forgets what a marker claims to stand for and keeps the marker, which
  is the right answer when the name is wrong rather than the marker. tgm_undo takes it back.
- Switching off a house pin you placed no longer hides every recorded structure, and switching off
  Structures no longer hides your house pins. Both wore the same picture and answered to one list,
  because the game draws no picture of an abandoned farm. Markers you placed now have their own
  list, and anything you had already switched off under Yours moves across by itself.

## 4.9.0 — 2026-11-10

**Everyone has to update. A server on 4.9.0 turns away clients older than 4.9.0, because 4.8.4 and
4.8.5 damage the shared map just by logging in.**

- Markers that were recorded no longer claim you placed them. A pass wrote the word "Manual" into
  every marker with a blank source, reasoning that the mod always writes down what it found, so a
  blank had to mean a person put it there. Formats before 5 had no source field at all, so every
  marker recorded before that is the same shape -- on the map that showed this up the guess was
  wrong 376 times out of 376. The pass is gone and the guess with it.
- tgm_unmanual puts those markers back, and the game itself says which is which. You place a pin
  by clicking the map, and a click on a map carries no height, so every pin anyone ever placed
  stands at no height at all -- while a recorded marker stands where the thing stands, and nothing
  in a Valheim world sits at zero. On the map this was built for, that agrees with the old flag
  4,703 times out of 4,703. Name a backup in format 6 or earlier and it is asked as well, since
  that is the last format that wrote the flag down; anything the two disagree about is named and
  left alone. tgm_undo takes the whole run back.
- This was worth repairing rather than noting because a marker claiming to be hand-placed is out
  of reach of everything: no group switch covers it, tgm_forgotten passes over it, tgm_identify
  refuses it on purpose -- and it cannot be told from the markers you really did place.
- A whole group can be erased from a marker's own menu: "stop recording and erase 47 black forest
  ruins", with the count on the label. Markers you have labeled stay. It erases for everyone, it
  needs the server's Allow Erasing Markers, and tgm_undo puts it all back.

- An erasure can be taken back. tgm_forgotten writes down the markers it erased, and tgm_undo
  puts them back -- for everyone, not just on your own map. They go back carrying today's date,
  and an erasure only beats a marker older than itself, so every other copy lets go of its
  erasure at the next merge. Restoring a backup could never do this: a backup replays the dates
  the markers had when it was taken, which are older than the erasure, so they lose to it.
- A marker that comes back stays back. Merging used to keep somebody's erasure even while
  keeping the newer marker it was about, which left the marker on the map and a tombstone for it
  sitting alongside -- drawn in the erased view, travelling onward as a claim nobody believed,
  and swept up by nothing.
- Flametal veins are recorded. They are mined off a leviathan lying in lava, the same arrangement
  the sea's barnacles have, so they are crossed off by themselves when it sinks -- and like the
  sea one, it never comes back up.
- Surtling geysers are recorded. The cores are the reason to walk to one, which is the same
  reason the bone and body piles in the graveyards beside them are marked.
- Volture nests draw with the volture egg, as drake nests draw with the dragon egg.

- What this mod records is three tiers now instead of two, and the tiers are in the code rather
  than in your config file: never recorded, off until you ask, and recorded. A marker carries
  only the prefab it was written for and every client works out its name, kind and picture from
  its own catalog, so a catalog that differs between players is not a preference -- it is a
  disagreement about what the same marker is, and whoever has the shorter one sees unnamed dots.
- Things at the never tier are still catalogued, and that is the point: a marker written for one
  in an older version keeps its name, its picture and a row in the marker list it can be hidden
  from. Deleting a catalog line could only ever stop the mod understanding a thing, never stop it
  recording one, which is how 133 markers became gold dots titled "marker".
- Right-click a marker and choose "Stop recording <its group>". The ones already on your map stay
  where they are; this is about what gets written from now on.
- New in the marker list's gear menu: "Show what you are not recording". It adds a dim row for
  every group nothing is being written for, with a switch to ask for it. A group that is off has
  no markers, so no list built from the map could ever have mentioned it.
- Turning a group back on fills in the places you have walked past recently, instead of waiting
  for you to go round again.
- A switch covers a group rather than a prefab, because a group is what you are thinking about:
  Black Forest Ruins is one row and eleven prefab names.
- New: tgm_tiers prints the whole table as your game is running it, and marks the groups you
  have switched.
- Offered and off: Black Forest Ruins, Mountain Ruins, Mountain Graves, Swamp Ruins, Plains
  Fortress Ruins, Ashlands Ruins, Shipwrecks, Surtling Geysers, Volture Nests. All of these were
  excluded outright before, with no way to ask for them short of editing prefab names by hand.
- Never recorded: the standing stones (stonehenges, stone circles, dolmens, waymarkers), the
  dvergr statues and viaducts, tar puddles, infested trees and the Places of Mystery. The stones
  used to be recorded -- 435 of them in a world with nothing in any of them -- and the Places of
  Mystery need no marker from us, the game pinning all three itself.
- The two mountain ruins that hold onion seeds match by prefix now, so StoneTowerRuins05_leet is
  recorded with the rest of its family rather than left out of both lists.
- Drake Nests is called Nests: the volture nests belong to it too, both being a nest you climb to
  for the eggs.
- A dvergr harbor is spelled the American way, like the rest of the mod.
- A marker draws with its kind's own picture rather than a plain dot where the catalog line
  that names it gives no picture of its own. An Abandoned Farm was a gold dot with a name,
  while the Structures icon sat configured as a house the whole time.
- A place recorded because its kind takes in unlisted prefabs keeps the kind that swept it in.
  A marker does not carry its kind -- it works it out from the catalog whenever it is read --
  so an unlisted one had no kind, and therefore no name, no picture and no row.
- The log says at every login how many recorded markers no longer know what they are, and what
  they stood for. A catalog line is edited once and the markers it unmakes sit on the map for
  months; nothing said so until somebody noticed a dot and asked.
- New: tgm_forgotten lists the markers that stand for something this mod no longer records,
  because the catalog has since dropped or excluded their prefab. Those lost their kind the day
  the line changed, and with it their name, picture and row: a gold dot whose tooltip says
  "marker". Add confirm to erase them; your map is backed up first.
- The game's own filter button moves both halves of a picture as well, so alt-clicking the
  hammer under the map hides every hammer and not just the ones this mod did not draw. And the
  buttons start where this mod's switches already are: vanilla forgets its filter every session
  while a picture switched off in the list stays off, so the two used to disagree at every login.
- One row per picture under Placed Markers, covering every marker that wears it -- the ones you
  dropped with the game's own button and the ones this mod recorded alike. One count, and a
  switch that moves both halves, so the game's button under the map grays out with the row.
  Markers placed by hand had no row at all: they belong to no kind, and the rows that unfold
  under a kind are built from kinds, so a map with fifty-seven dots on it listed none of them.
- A pin placed with the game's own controls is always taken on now, and the Share Placed Markers
  setting is gone. A pin and a marker were two things that looked the same and behaved
  differently under every switch, sweep and save; now there is one kind of thing on the map.
- Taking a pin on draws this mod's own marker in its place instead of adopting the pin itself.
  An adopted marker used to keep the game's pin number until the next login, so the game's own
  button hid it today and could not touch it tomorrow.
- A marker with no icon is drawn with a pin number of this mod's own, like every other marker.
  One carrying a number of the game's was drawn as that kind of vanilla pin, so vanilla's buttons
  under the map hid markers the marker list could not account for -- the dot button hid most of a
  map, the hammer and house buttons beside it hid nothing.
- The five rows for the pins you place by hand are always under Placed Markers, whether or not
  any are on the map. Each of those rows is also the button that places one, which is the only
  button for it once vanilla's strip is hidden.
- No sweep of this mod's takes down a pin the game put up for you: a boss altar you read a
  vegvisir for, one of Hildir's errands, a death, a bed. These live in your character file and
  nothing rebuilds them, so one deleted is gone. A discovered Bonemass altar was.
- New: tgm_rediscover puts one of those back. On its own it lists every place the world knows a
  stone marks and whether yours is marked; tgm_rediscover <x> <z> marks one.
- Two new switches in the gear menu, both off: Custom Boss Markers and Custom Hildir Markers.
  Each draws this mod's own marker in place of the game's pin for those places, never beside it.
- One marker per place, drawn from what the game has marked, so every altar gets one whether or
  not this mod ever recorded it, wearing its boss's trophy and name.
- A pin the game saved in your character file in another world is read by the name the game gave
  it. Valheim keeps boss and Hildir pins per character rather than per world, so a well-travelled
  character carries pins standing where this world has no location at all; thirteen altars came
  out as thirteen Eikthyrs.
- A place switched off and on again from the marker list gets its picture back.
- Markers for what the game marks itself -- boss altars, traders, Hildir's errands -- are taken
  down and kept down. The game pins all of those without being asked, so one of this mod's beside
  one was the same place marked twice. A marker you placed by hand where the game has pinned
  nothing is left alone.
- Switching a place off in the marker list no longer takes the game's own pin with it unless this
  mod is the one drawing that place. Hiding a marker's icon is about that kind of thing; hiding a
  place is about the place, and they are separate lists now.
- The three places Hildir sends you wear the trophy of whoever waits inside, and the ones nobody
  has walked into name themselves now.
- Hildir's errands have one row in the marker list instead of two.
- Boss Altars and Hildir Quests unfold into the places under them, each with its own switch,
  ping and picture.
- Those two rows count places rather than pins: her three errands used to count as nine.
- The ping on one of those rows finds its places however the game marked them.
- Showing a row of places again is instant. It asked the game to lay the whole map out, which on
  a well-used map is a second or two of rebuilding every pin on it to switch eleven back on.
- The marker list scrolls straight after a kind is unfolded.

## 4.8.5 — 2026-11-07

- Markers that borrow one of the game's own pin pictures -- runestones, structures, camps, boss
  altars, portals -- are no longer drawn as the game's own pins. Nothing could tell one from a pin
  you placed by hand, so copies leaked into your character file, came back every login as
  duplicates no switch of this mod's could hide, and travelled to other players through
  cartography tables.
- Those copies are taken off the map at login, including the ones that reached you from somebody
  else's map.
- Every marker now has its own "Hide all <icon> markers" switch; vanilla's icon buttons no longer
  hide and show this mod's markers.
- The log says once a session how many of this mod's pins were kept out of the character file.
- The marker list is held on the map every frame, not only when it is opened and dragged, so
  another mod that positions HUD objects by name cannot walk it off the edge of the screen.
- Pins belonging to other mods are left alone. This mod hands itself pin numbers from 100 and
  stops at 1000 because the numbers above that are other mods' -- The Greatest Portal draws at
  exactly 1000 -- but the test for "one of mine" had no upper end, so those pins were counted as
  wreckage, warned about, and deleted by tgm_clearlocal and at login.

## 4.8.4 — 2026-11-06

- The live ships, carts, portals and erased-marker ghosts are no longer counted as markers that
  have lost their record, and tgm_clearlocal no longer deletes them. They are drawn from what is
  happening now rather than from a record, and only the counting panel knew that.
- "No cartography table within reach" now says how near you have to be and that tgm_resync does
  the same work without a table. The reach is a setting and defaults to a meter.
- The Internal settings are gone. They were never settings -- they are this mod's note of what it
  has already done once -- and a mod manager showed six boxes with a blank default that filled
  themselves in. They move to progress.txt beside your maps, carried over at the next login.

## 4.8.3 — 2026-11-06

- Markers an older version left in the character file are taken off the map at login instead of
  waiting for tgm_clearlocal. They wore whatever picture their stale number meant that session,
  no switch could hide them and the panel could not count them.
- An erased crop stays erased. The erasure used to be forgotten at the next login, so a crop
  another player had recorded came straight back at the next sync.
- The log says so when markers lose the record behind them, and when they are taken off again.
  Such a marker is drawn but cannot be hidden or counted, so until now nothing said it was there.
- tgm_status counts the game's other kinds of pin -- memorials, Hildir marks, death markers,
  pings and the rest. Its two counts covered two of the game's eighteen kinds, so a map covered
  in the other sixteen was reported as having nothing on it.
- tgm_status names the picture each leftover is actually wearing. A leftover keeps the number it
  was saved under and that number means something different every login, so the same markers come
  back as rings one evening and houses the next.
- Pings, shouts, other players, the world's landmarks, the bed and the event marks are made
  afresh by the game every few seconds and never saved. One that was saved anyway came out of a
  file, cannot be told from the live one, and is now cleared at login and by tgm_clearlocal.

## 4.8.2 — 2026-11-06

- tgm_clearlocal no longer deletes the markers you placed the game's own way; it takes only this
  mod's leftovers and says how many of yours it spared. tgm_clearlocal all takes both.
- Markers placed by hand on older maps count as placed again rather than as ones the mod found:
  they keep their own picture, are no longer tinted as recorded, and are counted as placed.
- A marker that says nothing standing on one that knows what it is now folds into it. That pair
  is what two players get when one has worked out what something is and the other has not, and
  because the two no longer look alike nothing had ever folded them.

## 4.8.1 — 2026-11-06

- Readying the ping now wears off when you shut the map, like every other kind: a forgotten one
  turned your next double-click into a real ping to everybody in range instead of a marker.
- A marker whose icon names no real pin is drawn with the default pin and says so in the log,
  rather than being drawn as whichever of the game's own pictures that number happened to mean.
- One failing step of drawing the map no longer takes the rest with it, and names itself in the
  log: a map that came out carpeted, wrongly sized or missing every label now says which step.
- This mod's markers are never written into your character's own pin list now. One that lost
  track of itself used to be saved there, come back as a plain pin, and multiply every session
  until the map was covered in copies wearing the wrong pictures. Any already saved clear
  themselves the next time you play.
- tgm_status counts the pins on your map that no marker stands behind.

- Crowded maps work out what to draw faster: where there are too many markers to draw, widening a
  patch now merges the patches already worked out instead of measuring every marker again.

## 4.8.0 — 2026-11-04

- "You cannot write on your map while under attack" stays on the HUD and off the console.
- A leviathan is spotted at 150 m rather than 40 m, so its barnacles can be marked from the ship.
- Leviathans are in the catalog, so their markers have a row in the list instead of counting as orphans.
- A sync now sends only the markers that changed here, not the whole map: 96 KB became under a kilobyte.
- A marker recorded just before an autosave is no longer left on your map alone.
- A map too big for one message is sent in several, so the shared map can keep growing: it used to
  have a ceiling of roughly 38,000 markers, past which players were disconnected.
- Explored ground travels in a message of its own rather than behind the markers.
- **Zoomed out, one marker of each kind is drawn per patch of ground instead of all of them.** The
  full map was costing 13.9 fps with 2,939 markers on it and 27.1 with them hidden, nearly all of
  it Unity drawing marker after marker. A marker is only ever held back where another of its own
  kind is drawn close enough to touch it, and a patch always keeps one of each kind in it, so four
  mushrooms in a swamp still say there are mushrooms in that swamp. Markers near you, just-recorded
  ones and ones you asked for are never thinned. On the map's settings menu, and in Display as
  "Thin Overlapping Markers".

## 4.7.0 — 2026-11-02

### The marker list

- One column instead of two, and it scrolls. Every heading opens and shuts, and a triangle in the
  corner does all of them at once.
- Five headings: Resources, Points of Interest, Places, Placed Markers, Dynamic Markers. Places
  gets one row per thing -- World Center, Boss Altars, Traders, Hildir's Quests.
- A gear in the title strip holds the list's own settings: counts, grouping, marker order, labels,
  the map's own buttons, and the two switches that are about the whole map.
- **"show all"** sits in the title strip and is drawn only while something is hidden. The master
  switch is behind the gear, as **"Hide your markers"** -- named for what it takes off, since the
  boss altars, traders, Hildir's quests and world center always stayed.
- The plants can be grouped by whether you can farm them. Farm sits at the bottom of Resources,
  below Ore.
- The types under a kind can be ordered by progression, commonest first, or A to Z.
- A row's buttons wait for the pointer, "137 done" included, so twenty rows read as twenty names.
- A pinged marker is drawn in front of the others.
- The game's row of pin buttons can come off the map, this list having grown to cover every one of
  them. The ping line that stands in for its ping button can be switched off (Display, "Ping Row").
- Gone: "hide everything crossed off", which every row now does for itself; "show erased markers",
  which moved behind the gear; and the "Other Markers" row.

### What gets recorded

- **Markers within 50 m are drawn even when you have switched them off**, so the map near you is
  always honest: nothing there means nothing recorded there. Display, "Show Hidden Markers Within".
- **Camps are now Enemy Bases**, and everywhere something lives is one: fuling villages and towers,
  draugr villages, greydwarf camps, the four dvergr building types, charred fortresses. Recorded on
  by default -- they used to be split between Camps and Structures, and Structures is off by
  default, so a fresh install recorded a fuling village and not the fortress beside it.
- Drake Nests are a kind of their own. Hildir's three quest locations are named and never recorded,
  the game pinning those itself.
- **Both draugr village prefabs are recorded.** Only one of the two was listed, so half the draugr
  villages in the world were being written as structures named after their prefab.
- The spawners use the game's own names -- greydwarf nest, evil bone pile, body pile, Monument of
  Torment. **The swamp's bone piles were not being recorded at all.**
- No longer recorded, being buildings with nothing in them to search: swamp huts, ruins, wells and
  graveyards; infested trees; stone houses; stone tower ruins; Black Forest ruins; charred ruins;
  fortress ruins; volture nests; ship settings; shipwrecks. Abandoned houses, farms and log cabins
  stay, so you can still tell which you have searched.
- A charred fortress gets one marker, not one per building inside its walls. Anything dvergr wears
  the dvergr trophy, whatever the prefab is called.
- Vineberries are in the catalog.
- A kind's own picture is no longer one of its members' pictures. Sunken crypts wear iron scrap, and
  evil bone piles wear bones -- each was sharing a trophy with something else.
- `Icon Size Tweaks` (Display) evens out artwork drawn to different margins.

### Commands

- **`tgm_identify orphans`** finds the markers whose prefab the catalog no longer names -- what a
  dropped type leaves standing on the map, belonging to no kind -- and will erase them.
- **`tgm_locations [word]`** counts what this world is actually made of, by type, and says which
  kind each lands in and how many stand in ground you have uncovered.
- `tgm_survey` waves what it would write before writing it, takes the kinds to take, fills in
  nearest first, and says what the markers are rather than only how many. `tgm_undo` takes back an
  `adopt` or `survey` run.
- `tgm_spawners`, `tgm_items`, `tgm_missing` and `tgm_list` all say more.
- A marker's tooltip names the prefab it stands for and the icon key it wears.
- A marker can be given a label by hand, and the box for it opens under the marker.

### Fixes

- The one-time passes that bring an old map up to date run once per map and then stop.
- Erasing a wild plant marker on purpose sticks.
- Nothing forgets what a marker is unless you ask it to.
- A marker that knows nothing about itself is no longer filed as a dungeon.
- The live portals were being counted twice.
- A deposit already mined is written crossed off, and one the catalog has no line for is marked by
  the sweep anyway.
- A marker you had crossed off stays crossed off through a survey.

## 4.6.2 -- 2026-10-30

- **Tar pit markers were losing what they are and turning into plain dots.** Every login, the
  pass that brings a map up to date asked whether each marker's source was in the catalog, and
  asked it in the one way that cannot see a wildcard line -- so `TarPit*` answered for TarPit1,
  TarPit2 and TarPit3 everywhere in the mod except there. Reading that as "the catalog has never
  heard of this", the pass concluded the name must be wrong and threw it away; and since a marker
  whose picture and kind both follow from its source writes neither of them down, throwing the
  source away left a marker with no source, no icon, no kind and no name. Twenty-five of them on
  the live map, one for every tar pit anybody had marked since the 26th, each sitting beside the
  tar pit marker that came back from the server afterwards. Plain `TarPit` matched exactly and was
  never touched, which is what gave it away.

  Three fixes, because it took three mistakes: the question now goes to the catalog itself, which
  knows about prefix lines, the built-in list and the `_frac` twins of breakables; correcting a
  marker that really is misidentified now leaves it saying what kind it is rather than saying
  nothing at all; and the markers already blanked are erased at the next login where a marker
  that does know what it is stands on the same spot. The erasure travels, so one player's login
  tidies everybody's map.

- **An anonymous dot labelled "marker" is one of these too, even when it has a name on the
  record.** Nineteen markers on the live map carried a source nothing could draw -- fifteen
  reading `Pickable_Tar`, the tar inside a tar pit, and four reading `MountainGraveStone01`.
  Each is a true name for a real thing, and each was useless: no catalog line covers them, so
  the marker had no picture and no kind, and every pass that might have repaired it read the
  source, concluded the marker was accounted for, and moved on. A marker nobody can read is a
  marker that says nothing, and it is treated as one now.

  Those came from the world being asked what stands at a place and the answer being written down
  whatever it was. The answer has to be a thing the catalog can draw now, or it is no answer.

  And where the world names a thing that is already marked -- the tar pit the tar is lying in --
  the unreadable marker is erased rather than named, so one pit ends up with one marker instead
  of two. How near counts as the same place is the place's own size, which the game knows: a tar
  pit is wide enough that two people who find it write their markers five to nine meters apart.

- **A marker with nothing left on it is asked about rather than left standing.** The repair that
  asks the world what is at a place has always said that a blank marker is the obvious case for
  it, and has never once asked about one: it skipped anything with no kind, and a blank has no
  kind either, so the case it was written for was the one case it passed over. Those are asked
  about now, and because a blank has no icon for the answer to agree with, they are named by the
  nearest thing standing within six meters that the catalog has a line for. That is the weakest
  rule in here and it is used nowhere else -- every marker that still wears an icon is held to
  its icon, as before. It is used here because the alternative is a dot that can never become
  anything else, and because the only thing a wrong answer can name is something that really is
  standing there.

  Nobody has to go anywhere: the server holds every location the generator placed and every
  object in the world whether a player is near it or not, so it answers for the whole map at
  once, at the next login.

  And the answer now sticks. Writing a marker down under the name just worked out for it takes
  the old record off the map without leaving an erasure behind, which is right for every answer
  that came out of the catalog -- every other copy of the map works the same answer out of the
  same catalog and arrives there by itself. A marker that said nothing is the one case where
  that is false: there is nothing on it for anyone to work anything out from, so a copy that has
  not been repaired handed the blank straight back, and the folding pass could not settle the
  pair either, because a dot and a tar pit are different pictures. Those are erased as they are
  rewritten, so the answer travels instead of being argued with forever.

- **"Show all" on the map now shows all of it.** It put back the kinds, the master switch and the
  crossed-off groups, but not icons hidden one picture at a time with "Hide all X markers" -- so
  hiding cloudberries could only be undone from a marker's own menu, and every marker it applied
  to was hidden. It now does exactly what `tgm_show` does, markers hidden one at a time included
  -- and both of them put worked-out deposits back as well, which neither of them did. The mod
  already counted "Show Cleared Deposits" among the things that hide markers, so it would tell
  you something was hidden, be asked to show everything, and go on hiding it.

- **What is painted is the size of what was built.** A four meter wooden road was coming out
  around thirty meters wide with edges that wandered, which is not a drawing of a road so much as
  a rumor of one. Two things were making it:

  - Every painted map pixel was grown into the ring of pixels around it, turning each one into a
    three by three block. A map pixel is already about ten meters of world, so that is thirty.
    The growing was there because a single pixel out of two thousand gets averaged into the
    terrain when the game scales the map down, and a lone building reads as a smudge -- but for
    anything longer than it is wide the cure was worse. `Paint Spread` is 0 now, and anyone still
    holding the old 1 because their config file was written before today is moved to 0 and told
    so in the log. Raise it again if you read the map at arm's length.
  - A cell was drawn when a single piece stood in it, and a road clips the corner of the cells
    to either side of it with a piece or two. Those cells were then painted whole -- eight meters
    of map for a corner caught in passing, in and out along the length of the road, which is
    where the ragged edge came from. A cell now has to hold a few pieces of its own before it is
    drawn (`Least Pieces In One Cell`, 3 by default, the server's): a road or a wall passing
    through a cell leaves several, a clipped corner leaves one.

  A map pixel is still ten meters, so a four meter road is still drawn wider than it is. It is
  drawn as one line of pixels now rather than a band three times that, and its edges follow what
  was actually built.

- **Wooden building is drawn in the lighter brown of the game's own wood pieces.** The darker
  brown it used to be was close enough to the map's swamp color that people read it as swamp,
  which is worse than not drawing it -- a map that says the wrong thing costs more than a map
  that says nothing. Anyone still holding the old color because their config file was written
  before today is moved to the new one and told so in the log; anyone who chose a color of their
  own keeps it.

- **"Player Arrow Size" no longer applies while you have a ship's helm.** The arrow is made
  bigger so it can be found among markers, and the sea has hardly any -- so out there the size
  buys nothing and costs the precision you want while steering. Both arrows go back to normal
  for as long as you are at the rudder, since the game draws yours and the ship's together then,
  and come back the moment you step away. A passenger on the deck keeps the larger arrow.

## 4.6.1 -- 2026-10-29

- **The map painting drew brown rectangles in places with nothing in them.** Two reasons, and
  both of them are what the sweep was told rather than what it did.

  - Everything a player raises is a piece, so a campfire lit while exploring, a workbench put
    down to repair a hammer, a lone portal in a field and a torch on a hilltop all counted as
    building -- and each came out as a rectangle thirty meters across, because a single map pixel
    is grown so that small things can be seen at all. What tells those apart from a building is
    not what they are but how much of it there is, so a cell is drawn now only where several
    pieces stand within a cell of it ("Least Pieces To Draw", 6 by default; 1 draws everything,
    as it used to). The neighborhood rather than the cell alone, so a wall or a jetty running off
    the side of a base is not chopped off where the pieces thin out.
  - A ship and a cart are built pieces that wear out like any other, so they reached the sweep
    looking exactly like a wall -- and they move, which left a brown rectangle wherever anybody
    had ever parked. They are left to the live markers that already draw them.

  Both are the server's work, so the server needs this version before the map gets any tidier.

- **The map painting is off by default now, behind one switch**, while it is still being got
  right. `Player Structures Enabled` under `[Map Painting]`, the server's, and off means off: the server does not sweep, no client samples
  the ground it walks over, nothing is sent, and a map that was already painted is put back
  exactly as the game drew it. Everything else in that section only matters once it is on.

## 4.6.0 -- 2026-10-28

- **A ship under sail slides along the map instead of jumping every five seconds.** Finding a
  vessel means walking the world, which is why the sweep is slow and rare, and until now the
  sweep was also the only thing that moved a pin. But asking a vessel we have *already* found
  where it is now is a dictionary lookup, and the answer is live, because the player sailing the
  ship is the one keeping that object up to date on the server. So the two questions are asked
  separately now: the sweep goes on answering "what is out there" every few seconds, and
  movement is passed on four times a second by default ("Transport Movement Seconds").

  A moored harbor costs nothing at all, because nothing has moved and nothing is sent; only a
  vessel that has actually travelled a meter and a half is mentioned, and the message naming it
  is nine bytes. On the receiving end the pin is nudged where it stands rather than every pin
  being taken down and put up again — a full redraw four times a second is the kind of thing
  that has stalled this client into disconnecting before.

  Players on an older version are unaffected rather than broken: the extra number rides at the
  end of the vessel list where an older client simply stops reading, and the movement message is
  one it never registered, so it keeps the five-second behavior it has today.

  Carts are measured the same way as ships but they are the slow case: a player at walking pace
  covers about 0.6 m in a tick, so the threshold is three quarters of a meter and is measured
  flat. Flat because the map has no third dimension and a moored ship's swell is mostly up and
  down; three quarters of a meter because a meter and a half meant a cart was only reported every
  third tick, so the thing most in need of this got the least of it.

  `tgm_transports` now says whether movement is reaching you at all, and how long ago.

- **What players have built, and the roads they have paved, are colored into the map.** Not
  markers: a base is not a point, and a marker cannot say whether a place is a shed or a hall.
  Wooden building draws brown, stone gray, iron slate, and paved road its own pale color, all
  configurable. Because it goes into the map picture itself rather than on top of it, it shows
  on the large map, the minimap and the parchment in your hands alike, with panning, zoom and
  fog already handled -- you do not see somebody's base through unexplored ground, only once you
  have been there. The resolution is the map's own, so a shed is a pixel and a base is a patch.

  - **Buildings** are swept on the server, every two minutes by default. Only pieces a player
    raised count: every piece the world generator places carries no builder, so the ruins, camps
    and crypts are left alone. The sweep walks the world's objects once and asks each what it is,
    rather than asking the world four hundred times where each buildable prefab is -- the same
    answer for a four-hundredth of the work, which on a world of a million objects is the
    difference between a sweep and an outage.
  - **Paved roads** cannot be swept for, because paving is paint on the terrain and terrain only
    exists where somebody is standing. So each client reports the paved ground it walks over, the
    server keeps it for everybody and saves it beside the map. A road appears on everyone's map
    the first time anyone walks it, and stays.

  - **It is drawn solidly, which took more than a color.** The game builds its map from three
    pictures: a flat color for each biome, a mask that draws forest over it, and a height that
    decides where the sea is and how the land is shaded. Coloring only the first came out
    muddled -- in the black forest the mask went on drawing trees over the paint, and over water
    nothing showed at all, because the sea is drawn from the heights and a dock painted below the
    water line is painted under the sea. So a painted pixel now has its forest cleared as well,
    and if it stood under the water line it is lifted a meter above it ("Paint Over Water", on by
    default; off leaves the water alone and loses the docks). What is drawn is the paint and
    nothing else.
  - **It survives the game drawing its own map.** The game generates the world map into the
    texture it already has, and at a world load it does that *after* the first paint -- so the
    paint was being written and then replaced seven seconds later, and because a repaint only
    touches what has moved, nothing was ever drawn again. A map that has been rewritten
    underneath us is noticed now by reading a few of our own pixels back every couple of
    seconds, which costs nothing and catches any cause, not just this one.
  - **And it is drawn large enough to see.** A map pixel is about ten meters, so a shed is one
    pixel out of two thousand on a map the game scales down and smooths -- a lone pixel is
    averaged into the terrain around it until it is a stain rather than a building. Each painted
    pixel is grown into the ring around it ("Paint Spread", 1 by default; 0 is true to the meter
    and easy to miss, 2 and 3 are for reading at a glance).

  Switched on by default. "Paint Player Buildings" and "Paint Paved Roads" are the server's,
  since they decide whether the server sweeps and whether every client samples; "Show Map
  Painting", the four colors, the spread and the water are yours. **`tgm_paint`** reports what is
  being drawn, how coarse the map is, whether the forest and the water are being got out of the
  way, and whether the ground under you reads as paved.

- **Places you are plainly looking at are found now, without having to aim at them.** Fuling
  villages and tar pits were the worst of it: you could stand on a rise staring straight down
  into one and nothing would be written down. The reason was that finding something has always
  meant one ray down the middle of the screen hitting an object the catalog knows -- which is
  exactly right for a berry bush, and useless for a place sixty meters across. Looking at a tar
  pit, the crosshair is on tar, which is terrain. Looking at a village, it is on grass, on the
  sky above the roofs, or on a goblin walking past. The only way either ever registered was the
  crosshair happening to cross a hut wall.

  So places are now asked about directly, with the question a person would ask: is it in front
  of me, is it near enough, and is anything in the way. **The landscape is the only thing allowed
  to be in the way** -- a hill between you and a tar pit hides it, but its own huts, the trees
  around it and whatever is walking through it do not. The sighting distance for a place is also
  measured to its edge rather than its origin, so "ore: 40 m" now means forty meters from the rim
  of a tar pit instead of forty meters from the middle of one.

  This is still the "found, not detected" rule, not radar: a place behind you, over a ridge or
  past its sighting distance is not found, and the same dwell applies. Set a kind's Look Distance
  to 0 to switch sighting off for it entirely, as before. Structures are deliberately left out --
  a farm or a ruin is marked building by building, and a sighting of the whole compound would put
  a second marker in the middle of the yard.

- **`tgm_look` reports the places as well as the crosshair**, listing each location around you
  with the one thing that ruled it out: not in view, too far, blocked by the landscape, or seen.

- **The settings that reach other people are the server's now.** Which group a setting belongs
  in follows one question -- does your choice change what happens on somebody else's machine --
  and three of them were answering yes while sitting in the personal half.

  - **Exchange Radius** and **Exchange Cooldown** are `[Synced with Server]`. Only one of the two
    players decides: whoever reaches out uses their own radius and their own cooldown, and the
    other machine does half the work. So a wide radius started an exchange on somebody from
    across the camp, and a short cooldown made them pay a few hundred milliseconds for it again
    and again. That was not a tuning preference, it was the one setting in the mod that let one
    player stall another -- and 4.5.2 exists because a stall that long on a moving ship puts you
    in the water.
  - **Forget Crops On Tilled Ground** is `[Synced with Server]`. It erases markers for everyone,
    which is exactly why its three neighbors -- folding duplicates, the label rules and the
    dungeon icon repair -- have always been the server's. Your own "Record Plants In Your Base"
    still holds the sweep off your client.
  - **Exchange Exploration** is `[Synced with Server]`. It was never symmetric: it governs only
    what you send, and taking the other player's explored ground in is not gated on it at all. So
    switching it off left you receiving everything and giving nothing back. What sharing means on
    a server is the server's to say.
  - **Auto Sync**, **Sync Radius** and **Auto Sync Cooldown** under `[Cartography Table]` are
    `[Synced with Server]`. A table visit looks local and is not: its last step is a merge of your
    map with the shared map on the server, so how readily you touch a table is how readily you ask
    the server to do that work. Five players with a wide radius and a short cooldown standing in a
    base full of tables is a load nobody chose. **The sync key is untouched** -- asking for a sync
    by hand is always allowed, whatever these say, so nobody is ever stuck with a stale map.

  Your file keeps your own values for all of them, as it does for every synced setting, so single
  player and hosting your own game are unchanged. On a server, note that a value you had set --
  auto-sync switched off, say -- now gives way to the server's while you are connected.

- **The three "Server only" settings are marked as the server's too.** "Stop Waiting For A Player
  After", "Server Autosave Minutes" and "Transport Sweep Seconds" are read on the server and
  nowhere else, so a player editing their copy was always doing nothing. They now carry
  `[Synced with Server]`, which is the mod's existing way of saying exactly that.

- **"Stop Waiting For A Player After" has moved to the `[Server]` section**, beside the autosave
  interval. It was only ever read on the server; in `[Sharing]` it looked like something a player
  could set. Your value comes with it.

Older versions: CHANGELOG-4.5-and-older.md in the repo, and CHANGELOG-0.x.md before that.
