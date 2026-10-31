# Changelog

## 4.7.0 -- unreleased

- **The marker list says less and tells you more.** Players have called it confusing and
  overwhelming, and the four things in it that earned that are gone:

  - **"Show all" and "Show all markers" are no longer two rows four apart.** One was the master
    switch and the other undid every hide in the mod, and from the words there was no telling
    which was which. The master switch keeps its row, since that is the only row those words were
    ever about. The other now names what it will put back -- "Show everything hidden -- 2 kinds,
    5 markers" -- and is drawn only when it would put something back, which on most maps means
    not at all.

  - **Every kind says how many it has.** "Ore (3)" and "Ore (219)" are different maps, and the
    only way to tell them apart before this was to look at the map and count. Counted afresh as
    the list refreshes rather than when it opened, because recording goes on while a pinned list
    stays open.

  - **A kind unfolds into the types under it.** The triangle at the head of a row opens it:
    copper, tin, silver and obsidian under ore, each with its own switch, its own "only" and its
    own ping. Until now the only switch for a single type lived in the right-click menu of a
    marker of that type, which you had to find on the map first -- and hiding ore to be rid of
    the tin took the silver with it. One kind is open at a time, so the list cannot grow off the
    bottom of the map; "Marker List Tree" turns the triangles off.

  - **A row's three buttons wait for the pointer.** Twenty rows with place, only and ping on each
    is sixty things to read before the first decision, and what a reader wants from this list is
    the names. They are drawn on the row the pointer is on, and on any row whose button is
    already doing something -- an armed or filtered kind has to go on saying so. The names have
    the whole row while the buttons are away, so most of them no longer end in an ellipsis.
    "Marker List Buttons On Hover" puts all three back on every row.

  The header above the kinds no longer reads "Marker kinds + place, only, ping". It named three
  buttons at the end of every row, which is the one place a reader does not need them named: each
  has a tooltip of its own, and there is nothing to explain until the pointer brings one out.

- **Two tools for a test world: `tgm_adopt` and `tgm_survey`.** Neither is anything a player
  needs, and both say so in their own help text. They exist because a map worth testing against
  takes weeks to make by playing, and the mod can do in a second what it is designed to do slowly.

  - **`tgm_adopt` turns markers you placed by hand into recorded ones.** A hammer dropped on a
    burial chamber and a dot dropped on a copper vein say what they are only to the person who put
    them there. The world standing underneath them knows exactly what was being pointed at, and
    the server holds that knowledge for every place at once whether anybody is there or not -- so
    it asks, writes the marker recording would have written, and removes the hand-placed pin, so
    one thing ends up with one marker. It looks twenty meters around each pin by default, because
    a pin dropped by eye on a map a thousand meters to the inch lands near a thing rather than on
    it; `tgm_adopt 40` looks further.

    A pin with nothing identifiable near it is left exactly where it is, and listed with its name
    and position. It is the only outcome here that would lose something a player meant, and the
    player is the only one who knows what they meant.

  - **`tgm_survey` marks what was never looked at closely enough to record.** Every location the
    generator placed and every berry bush, mushroom, thistle and deposit standing in the ground,
    across the part of the map you have uncovered -- or `tgm_survey all` for the whole world,
    explored or not. The locations come from the generator's own table and the loose things from
    the server's own records, so nothing has to be walked to.

  Both list what they would do and write nothing until told to `apply`. Both go through the same
  spacing, the same respect for a spot where a marker was erased, the same switched-off kinds and
  the same one-label-per-so-many-meters as ordinary recording, so a map made this way is made of
  the same markers an honestly played one is -- which is the whole point of testing against it.
  And both need the world itself rather than a copy of its map: single player, a world you are
  hosting, or the server's own console. They say so plainly rather than half working.

  `tgm_survey` stops at a thousand markers unless given a bigger number, because a developed world
  holds hundreds of thousands of things worth a picture and a map carrying all of them is not a
  map.

  A first run over a map with six hundred hand-placed markers on it taught both tools five things,
  which are now in them:

  - **Nothing underground is an answer about the surface.** Valheim builds the inside of a dungeon
    in a space of its own above y = 3000, and both tools measured flat distance -- so the eighty
    yellow mushrooms in the burial chambers under a hill stood a few meters from a pin dropped on
    the hill and answered for it. Yellow mushrooms grow nowhere else, which is how it was caught.
    Both tools now skip anything on the other side of that line, which is the rule recording has
    always followed by refusing to record at all while you are inside a dungeon.

  - **What a marker was for now outranks what is nearest to it.** A hammer dropped on a troll cave
    has a mushroom eight meters away and the cave fifteen, and the old rule took the mushroom: it
    offered the place the generator put there last of all, on the reasoning that a thing at arm's
    length is usually what a marker means. That is true of a marker this mod wrote and false of
    one dropped by eye on a map a thousand meters to the inch. So the picture on the pin is read
    first -- a hammer means a place, a dot means a resource, a fire means a fire -- and distance
    now decides only between answers of the same sort. Every location within reach is a candidate
    rather than only the nearest one, so a ruin ten meters away can no longer answer for a cave
    fifteen meters away.

  - **A name you typed is never thrown away quietly.** "2*boar" became Dandelion and "T" became
    Yellow Mushrooms, and because the hand-placed pin is removed after a marker is written, each
    of those was the loss of the only record of what was meant. A marker you named is now
    converted only if the answer agrees with the name, and the listing names every one it left
    alone together with what the world offered for it. **Adopt Name Hints** is where your own
    shorthand goes -- `T=TrollCave, S=Greydwarf, PLAINS=none, BARNACLE=none` -- for the names no
    answer can agree with because they are one letter long, and for the notes that are not about
    anything in the world at all. A leading star level is ignored, so one hint covers T and 1*T.

  - **Plants are not an answer unless you ask for them.** A plant grows within twenty meters of
    very nearly everything, so while plants were in the running they answered for whatever a pin
    really meant: 175 of 565 answers were a berry, a mushroom or a herb. `tgm_adopt plants` takes
    them anyway.

  - **A campfire is a campfire.** A pin dropped on a fire you lit in the Ashlands answered "charred
    spawner" -- the thing the fire was lit to suppress -- because the catalog's prefab road refuses
    campfires, a prefab being unable to say who lit it. The record carries a creator, which is the
    same question the piece road asks the piece, so a fire somebody built is now read as one.

  And the pin that mattered most was the one the guessing could not name. The game calls its five
  placeable pictures mapicon_fire, mapicon_house, mapicon_hammer and mapicon_pin -- that last one
  being the plain dot, which is what anybody reaches for when the thing is a vein of copper rather
  than a building, and which was 246 of those 609 markers. Those four are now known by name
  instead of being guessed at from the words in them.

- **Both tools can be taken back: `tgm_undo`.** `tgm_adopt apply` writes several hundred markers
  and removes several hundred hand-placed pins on one press, and until now neither half could be
  undone. A recorded marker needs the server's permission to erase and is erased one at a time; a
  pin that has been removed is gone from the character file at its next save, and nothing else in
  the game remembers it. So applying either tool now does two things first: it copies your whole
  map aside, where `tgm_backups` lists it and `tgm_restore` puts it back, and it writes down what
  it did -- the key of every marker written and every pin removed, whole. `tgm_undo` reads that
  back: those markers go, those pins return, and anything recorded since is left exactly where it
  is, which is the one thing restoring a copy of the map cannot manage. It describes the run before
  it touches anything and wants `confirm`, and a run that has been taken back is set aside so it
  cannot be taken back twice. Both tools print the line that undoes them when they finish.

  Two smaller things came out of the same run: the visible spawners a player can walk up to and
  break (a greydwarf nest, a draugr pile) were missing from the road both tools read the world
  with, so a hammer dropped on a nest found nothing; and both listings capped their lists at
  twelve lines, which is no use at all when those lists are the markers the tool is *not* going to
  touch. The console still gets forty of each; the log gets all of them.

  Neither can write the anonymous dots 4.6.2 spent a release repairing. Both go in through the
  same door every recorded marker goes through, which resolves a picture before the marker exists
  and writes it down when the catalog cannot derive it again -- so a thing the catalog has no line
  for is not turned into a marker in the first place, rather than being caught on the way out.

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

## 4.5.3

- **The erasure ledger is now written on the path the map is actually written on.** 4.5.2
  put that save in `SaveIfDirty`, which only runs when the world is saved or put away --
  barely better than the shutdown-only behavior it was meant to fix. The live server showed
  it plainly: the map came out holding 438 erasures while the ledger beside it still
  described 1,010, the set from before the drop. It now goes down with the map on the
  throttled save that does the work, and a player syncing counts as something to write even
  when the map itself has not changed.

- **The ledger no longer keeps rows for erasures that are already gone.** The crop sweep and
  the ghost sweep remove erasures without telling it, so the pruning now runs after both and
  forgets anything with nothing behind it. 572 of the live ledger's 1,010 rows described
  erasures that had left.

- **`tgm_underway`** says what the gate that holds map sharing on a moving deck can see:
  which ship it picked, whether the tiller or the deck answered, how fast the game thinks
  that hull is going, and whether anything is waiting. The queue behind that gate is
  simple; reading the player's footing is the part that can be wrong, and it needed a way
  to be checked by one person on a boat rather than two.

## 4.5.2

- **Comparing maps no longer throws you off a moving ship.** Taking in another player's
  explored ground is the same work as reading a cartography table -- a few hundred
  milliseconds on the main thread, in a single frame. At a table nobody minds. Under sail
  the hull keeps moving through the frames the game did not draw, physics catches up in
  steps large enough to carry the deck past you, and you come out the other side in the
  water. Markers now go through at once, as they always did, and the explored ground is
  held until the deck settles -- seconds, at anchor or ashore. Both players are spared,
  not just the one who held their map out first.

- **The erasure ledger is written with the map, not only when the world is put away.** A
  server is killed as often as it is stopped politely, and a ledger that only survived a
  clean shutdown came back empty after every crash. That loses no markers, but it does
  mean every erasure is held open for another full round of everyone syncing -- so the
  thing would have gone on never dropping anything, and looked like it was working.

## 4.5.1

- **An erasure stops being kept once everyone has it.** An erasure exists because some copy
  of the map might still hold the marker it rubbed out; once every player has taken it in,
  it cannot hold anything back and is only cost. The server now records who has synced and
  drops the ones everybody has.

  Measured in changes to the map rather than in days, because what puts a returning player
  out of step is not how long they were away but how much happened while they were: a
  month away from a sleeping server costs nothing, a week away from five people playing
  nightly is plenty. A player who falls far enough behind is set aside rather than waited
  for, or one person who stopped playing would pin every erasure made afterwards for good.
  They keep their place and one sync brings them back into the count.

  How far behind is too far is a new setting, `Stop Waiting For A Player After`, in
  changes. The default of 5,000 is deliberately generous -- on a map of five people it is
  roughly two months of ordinary play -- because the two mistakes do not cost the same.
  Waiting too long keeps a few dead records; giving up too early lets a returning
  player's stale map put deleted markers back, which is the thing this is for.

- **Erased places can be shown on the map.** A row in the marker list, or `tgm_erased`,
  greys in every spot where a marker was rubbed out, captioned with what it was and when
  it went. An erasure is otherwise the one thing on a map that cannot be seen, though it
  holds a marker back on every copy forever: 373 of them sat unexamined on one map until
  somebody read the file with a script, and 309 of those turned out to have been written
  in three single minutes by a migration rather than by anybody erasing anything. Drawn,
  that is obvious at a glance. Nothing is changed by looking.

- **Obsolete erasures are cleared on every load again.** The sweep had been narrowed to run
  only while converting a map, because it could not tell an erasure left behind by an old
  rename from the one that carries a fold between machines -- they are the same shape. Age
  tells them apart: a fold's erasure goes up at the next sync, so only those older than a
  day are swept. 197 had collected on one map in the meantime.

- **Hiding marker labels is instant.** It took about a quarter of a second on a full map,
  because every pin was destroyed and built again: a marker created without a name has no
  label to switch back on, so the switch had to remake them all. Every named marker keeps
  its label now and the label is hidden instead, which is what the game's own pins already
  needed. Almost nothing pays for it -- a recorded berry bush carries no name and still
  gets no label at all.

## 4.5.0 -- 2026-10-26

- **Portal markers are drawn in front of everything else.** A portal is the marker you are
  looking for when you need it, and the one most likely to be buried: portals stand in
  bases, and a base is where the berries, the campfires and the workbenches all are.

- **Marker labels and portal labels can each be switched off.** Two buttons at the foot of
  the marker list, and a setting for each. They are kept apart because they are not the
  same complaint: a portal name is worth reading and there are a dozen of them, while
  marker names run to thousands and the label rules are already thinning them. A base with
  six portals writes six words across itself, which is reason enough to want those gone
  while keeping the rest. Nothing is lost either way -- hovering still names a marker, the
  list still finds it by name, and switching back on writes them all again.
  Marker labels cover the game's own pins too -- a death marker, a bed, a boss, one of
  Hildir's -- which in practice are the labelled ones, since a recorded marker is usually
  a berry bush that explains itself and carries no name at all.

## 4.4.0 -- 2026-10-25

**Everyone must update, including the server.** Nothing in the map format changed and a
mixed group would in principle have worked -- the new sync was built so an older copy
simply does not see it and is answered in full. That path was reasoned rather than
tried, though, and an untested road is not one to send other people down.

- **A sync carries what changed instead of the whole map.** A cartography table used to
  send your map up and the shared map back, both in full, almost always to find that
  nothing had changed. The server now counts its own changes and answers with just the
  ones you have not heard; a map with nothing new of its own goes up empty. Nothing is
  ordered by any clock, so a machine whose clock is wrong cannot cause a marker to be
  skipped, and anything the server is unsure of still sends the whole map.

- **Traders and people can be marked by hand.** Nothing records a trader -- the game pins
  the ones you have met -- but "the trader is over there" is worth writing down for
  somebody who has not met him, and the rule against recording them had quietly become a
  rule against writing them. Both now have the place-by-hand button. People get one too,
  on the Players row, for marking where somebody is building or where they went down,
  along with an "only" button to show just those.

- **A console command that waits for the server now says how it ended.** `tgm_resync`
  printed "Merging with the shared map..." and nothing further: the answer arrives over
  the network, and it was being announced only on the HUD, which does not draw while the
  game is paused -- exactly when the console is open. Anything worth telling a player now
  reaches the console too.

- **New setting, Echo Log To Console (off).** Repeats this mod's log lines in the dev
  console as they happen, for watching a sync, a repair or a format conversion without
  alt-tabbing to the log file.

- **The log no longer reports every pause.** It was there to tell a frozen client from a
  paused one while a disconnection was being chased, and it answered that: a pause cannot
  cost a connection. Since the map pauses the game by design, the lines then fired every
  time anybody opened the map and said nothing. Long frames are still reported.

- **A merge says when an erasure turned a marker away.** Erasures never retire and
  nothing knows when they safely could; counting what they actually do is how that gets
  decided with evidence rather than a guess.

## 4.3.0 -- 2026-10-23

**The map is written a field at a time instead of a marker at a time, and the erasures that had
grown to outweigh the markers are gone.** Everyone must update, including the server.

A map used to go out as a run of marker records, each one repeating the same owner, the same
source and the same author as its neighbors. Now each field is a column of its own, every
repeated string is held once and referred to by number, positions go as centimeter steps from
the marker before, and times go as seconds rather than ten-millionths. A four-and-a-half
thousand marker map went from 587 KB to 140 KB.

- **Re-identifying a marker no longer records an erasure.** Working out that a house pin is
  really a stone tower files it under a new name; it does not delete it. The old code erased and
  re-added, and the erasure outlived everything it was meant to guard -- 4,401 of them appeared
  on one map in a single hour, and together they were larger than every marker on it.
- **Erasures that were only a marker's former name are dropped on load**, so a map that already
  collected them cleans itself up without anyone running anything.
- **Clearing crops off a farm leaves nothing behind.** A marker written for a planted turnip
  was never a discovery, and the record of rubbing one out is not worth keeping: recording
  already refuses to write a crop on tilled ground or inside a base, so there is nothing for an
  erasure to hold back. 141 of them went from one map.
- **Positions are kept to the centimeter** and times to the second. Nothing on a map is measured
  finely enough for the difference to be visible.
- **The trader markers are retired.** Valheim marks Haldor, Hildir and the Bog Witch itself
  the moment you find one, so ours was a second pin saying the same thing. Erasing them by
  hand never stuck, because the erasure only existed on the map that made it and the next
  merge brought them back.

## 4.2.0 -- 2026-10-22

**Markers that did not know what they were now work it out by themselves, and the map on the wire
is nearly half the size.** Everyone must update, including the server: a 4.2 server turns away
older clients and says why.

Old maps carry markers written before the mod recorded what a thing *is* -- a house pin that
could be an abandoned house, a ruin or a stone tower, with nothing on it to say which. Until now
the only cure was a console command. Now the game is asked at every login and the markers settle
themselves, with nothing to run and nothing to read.

The server knows what stands anywhere in the world, whether or not anybody is there, so your map
is repaired wherever you have been rather than only where you go back to.

- **An answer is only believed when it agrees with the marker.** The world can truthfully say
  "this is the ruin's ground" about a patch of carrots growing inside a ruin -- a true answer to
  a different question, and the reason this used to be a manual command people were warned about.
  Now the thing the world names has to wear the icon the marker already wears, or it is left
  alone. A carrot patch is not a ruin, and the two no longer get confused.
- **Markers whose kind is the whole answer say so.** A runestone is a runestone; there was never
  anything further to work out, but those markers looked exactly like the ones nobody had got
  round to. Now they are marked as settled, and "unidentified" means only what it says.
- **`tgm_counts` reports how many markers do not know what they are**, by kind. It is the number
  to watch: if it climbs, something is eating your map's memory of itself.
- **Four fields left the record.** How a marker is drawn, what kind it is, its pin type and
  whether it was recorded all follow from what it stands for, so keeping them was keeping a
  second copy of an answer the marker already gives -- and a second copy is one that can
  disagree. It did: forty-two markers spent weeks filed under a category the catalog had moved
  them out of, because the stale copy was the one being read. The shared map went from 170 KB to
  about 91 KB on the wire in the same stroke.
- Nothing is lost converting, from any version as far back as the first. A copy of your map is
  kept before it happens, as always.

- **Crop markers in a farm that has been reshaped now clear.** Forgetting a planted crop asked
  only whether the ground beneath it is tilled right now, while refusing to record one asks that
  *or* whether it stands inside your base. A plot dug out to a different shape left its old
  markers on untilled earth, out of reach of the only question being asked, and walking over them
  did nothing -- re-tilling underneath was the only cure. Both sides ask the same thing now:
  whatever the mod would decline to write down today, it is willing to forget.

- **Working out what a marker is no longer counts as erasing it.** Filling in what an old marker
  stands for gives it a new name, and the mod used to record the old name as an erasure -- which
  on a map that had never been identified meant thousands of them in one sitting, outweighing the
  markers themselves. Those are cleared away on sight now, and no new ones are made. Real
  erasures are untouched: what you deleted stays deleted.

Also fixed, all of them older than this release:

- **A marker you placed by hand only counted if you pressed Enter.** Place one, then dismiss the
  name box with Escape or a click elsewhere, and the game kept the pin while this mod never heard
  of it -- so it had no tooltip, no marker menu, right-click erased it outright, and after a
  reload it drew as the wrong picture entirely. Whether a marker worked came down to how you had
  dismissed a text box, which is why it seemed random. Both ways work now.
- **Placing several of a kind no longer needs arming for each one.** Pick a kind and place as many
  as you like; it lasts until you close the map. Before, the second onward were refused and left
  as those same orphans.
- **A marker is styled the moment it is accepted**, rather than keeping the size the game gave it
  until your next click happened to redraw the map.
- **"Right-click the map to place a marker" was wrong**, and worse than wrong: right-click opens
  the marker menu, whose first entry hides the marker. It says double-click now, which is what
  places a pin and always was.
- **`tgm_list` says why a marker is hidden**, not merely that it is, and reports when a pin on
  screen disagrees with the record behind it. Six separate switches can put a marker away and
  from the outside they look identical.

## 4.1.0 -- 2026-10-22

**Portals are drawn from the world now instead of being remembered.** Update the server too.

A portal is not a discovery. It gets renamed, torn down and rebuilt, so a marker written once is
wrong within days -- which is why portal markers were being quietly renamed and erased in the
background to chase the world. The game already keeps a list of every portal and the server
already sends it, so that whole dance was keeping a copy in step with something we were being
told anyway. Portals now work the way ships and carts do: the server says where they are, your
map draws them, and nothing is stored.

What you will notice:

- A renamed or rebuilt portal is right **immediately**, rather than after the next correction.
- A torn-down portal leaves nothing behind -- no marker, no erasure to carry around forever.
- The portal markers already on your map are retired once, automatically, the first time you
  play. You do not lose any portals; they are drawn from the live list instead.
- **"Map All Portals" is gone.** It asked whether to draw the portals nobody had recorded, which
  is not a question any more -- every portal is drawn, or none. **"Show Portals"**, with the
  ships and carts under Dynamic Markers, is now the switch. If you had Map All Portals turned
  off, turn Show Portals off.

## 4.0.1 -- 2026-10-22

**Fixes markers losing track of what they are.** Update everyone, including the server.

When two copies of one marker met, the mod kept whichever had the lower identifier, so that every
player folded the pair the same way round. Since 4.0.0 an identifier ends with what the marker
stands for, and "not worked out yet" sorts before every real answer -- so the copy that knew less
won, every time. One player who had never identified their map was enough to undo it for
everyone: 325 structures, 31 spawners and a dungeon went back to a generic pin in one evening,
and the map grew by half on the wire from the erasures each fold left behind.

Now the copy that knows what a thing is survives, and where both know the same, the one that was
found first -- rather than whichever happened to sort lowest, which quietly meant the same
player's copy won every time.

If markers on your map have gone generic, `tgm_identify apply` works them out again once everyone
is on 4.0.1. Doing it before that will not stick.

## 4.0.0 -- 2026-10-21

**Everyone must update, including the server.** A 4.0 server turns away older clients and says
why. The way markers are identified has changed, and a 3.x client cannot read the new maps.

- **The shared map is half the size on the wire.** 227 KB down to 112 KB on our server's 4,644
  markers. Every marker carried a 33-byte made-up identifier, which was a quarter of the whole
  map and the one part compression could do nothing with. It is gone: a marker is identified by
  who recorded it, where it stands and what it is, which the marker already said. The game cannot
  send more than 512 KB in one message, and going over it is what was disconnecting people at the
  cartography table, so this is room to grow rather than a saving for its own sake.

- **Your map brings itself up to date now.** Markers written before the mod recorded *what* a
  thing is get filled in by themselves, shortly after you log in, once. This is what
  `tgm_identify apply` did, and you no longer have to know it exists. Markers you placed yourself
  are marked as placed by hand rather than left blank.

- **A copy of your map is kept before any of this happens.** The first time a map is read in the
  older format, it is written out untouched to `BepInEx/config/TheGreatestMap/backups/` -- the
  shared map on the server, and each character's own map, which otherwise lives inside the
  character file where it cannot be copied. `tgm_backups` lists them; `tgm_restore <name>` puts
  one back. You should never need it.

Nothing to do: every marker, erasure and suppression carries over, and old maps are read and
converted as they are opened.

One exception, and it is counted and reported rather than quietly done. On a map written before
markers recorded *what* they stand for, two markers at the very same point by the same player are
now one marker -- the same bush written down twice, once as a thistle and once as a dandelion.
They were always duplicates; the later of the two is kept, and the log says how many there were.

Small print: markers you had hidden one at a time with "Hide this marker" are shown again once,
because that list referred to the old identifiers. Hide them again and they stay hidden.

## 3.2.1 — 2026-10-21

**Farms are no longer mapped plant by plant.** Planting a turnip grows the very same thing the
wild one is, so a field was recorded one marker per crop -- 172 of them on one map, and 29 within
twenty meters of each other. Two changes:

- Crops growing on ground somebody tilled are not recorded.
- Markers already written for them are erased as you walk past, a few seconds after you come
  near. You need do nothing; one player clears a field for everyone.

If you *want* your garden on the map, set **Record Plants In Your Base = true**, or
**Forget Crops On Tilled Ground = false** to keep the markers you already have. Set it before you
play: erased markers are gone for everyone and do not come back.

Only crops on tilled ground are touched. Wild berries, mushrooms and herbs are untouched, and so
is anything you placed by hand.

Also:

- **`tgm_counts`** -- what your map is made of, by kind and by icon.
- `tgm_order` now reports where it looked, and looks where you stand when the map is closed. It
  used only the mouse pointer before, which made it answer about ground you were not on.
- A marker is never written without an owner. The game reports no player id while you are
  spawning or logging out, and the code quietly wrote zero instead; a find now waits.
- A find is struck off as recorded only once its marker exists. It was marked first, so a write
  that failed lost the discovery for good.

## 3.2.0 — 2026-10-21

**Update the server as well as every player.** Maps are compressed on the wire now; both shapes
are read, so nothing refuses anything, but only an updated server sends the smaller one.

- **Maps are compressed when they cross the wire.** The shared map had reached 543 KB and the
  most Steam will carry in one message is 512 KB. Compressed it is 233 KB. Markers are alike
  enough to shrink well, though not as well as they might -- most of the bulk is identifiers and
  positions, which do not compress -- so this is headroom rather than a cure: about six thousand
  more markers at the present density.
- **The server's answer was the one message that was not compressed.** It went as a raw snapshot
  of the whole shared map while everything else already went through the packer, which made it
  the largest thing this mod sends and the first to outgrow what the game can carry.
- **A cartography table now says what it is doing, step by step, on both sides** -- starting, its
  own work and how long each part took, whether our map went up and how large it was, and when
  the server answered. The server logs the map it received and the one it sent back. A sync that
  gets stuck now shows where instead of leaving a silent gap and a disconnected player.

## 3.1.3 — 2026-10-21

- **An unchanged map is no longer sent to the server again.** The whole map went up on every
  sync, and a map carrying four and a half thousand markers is half a megabyte. Steam sends that
  as one ordered message, so until it has gone, everything queued behind it waits -- including
  the replies the server is listening for. Standing at a cartography table did that repeatedly,
  the server stopped hearing from the client, and after thirty seconds it closed the connection.
  Nothing is lost by staying quiet: the server already holds exactly that map, and anything
  arriving from anyone else still comes down as usual.
- Note for large maps: this makes it rarer, not smaller. A map that *has* changed still goes up
  whole, and at half a megabyte that is close to the largest single message Steam will carry.
  Sending only what changed, and compressing it, is the proper repair and is next.

## 3.1.2 — 2026-10-21

- **Fixed comparing maps in the field stopping the game for seconds at a time.** Tidying up after
  a merge compared every marker with every other marker of the same icon: seven hundred blueberry
  bushes is half a million comparisons, and each one worked out a marker's kind afresh to ask how
  close is too close. On a map carrying four thousand markers that ran into whole seconds, every
  time two players met and merged -- long enough to be thrown off the server. Markers are now
  filed by where they are, so each is compared with the handful standing near it and nothing else
  is looked at. The same shape, and the same cure, in the pass that thins out labels.
- **The tidying says how long it took** when it takes longer than a fifth of a second, broken
  down by pass. The stop was plain to everyone playing and invisible in the log.

## 3.1.1 — 2026-10-20

- **Withdrawn: portals drawn over everything else at the same spot**, added in 3.0.0. It never
  worked reliably -- a portal beside Hildir stayed behind her icon while others came out in
  front, seemingly by luck of creation order -- and reordering pins is expensive enough to be a
  suspect in client stalls. Better nothing than an unreliable fix that costs frames. The problem
  it was aimed at is real and will be solved another way; `tgm_order` is kept for working on it,
  and reports what is drawn over what under the pointer.
- **A watchdog for disconnects.** When the game stops for more than a second, or sits paused for
  more than twenty, a line goes in the log with the wall clock on it. A server that drops a
  client says only "ZRpc timeout detected", which looks the same whether the client froze, sat
  paused, or simply stopped being heard; these two numbers tell those apart. Silent otherwise.

- **Stopped describing the cartography table as though it had been taken away.** It read as
  "tables no longer carry markers", when what is off by default is vanilla's own pin sharing --
  your maps still merge at a table, markers and all. "Table Carries Player Markers" (Sharing)
  gives vanilla's behavior back for the five standard pins, which is the thing that was switched
  off and sounded like a thing that was removed.
- Said that a table is optional now: two players standing together with their maps out merge
  directly, which is usually how it happens anyway.
- Said that the shared map lives on the server, so the mod belongs there too. Installed only on
  your own game, a table carries fog and nothing else, which is the likeliest reason for someone
  finding markers do not travel.

## 3.1.0 — 2026-10-20

Client-side.

- **Your own arrow on the big map can be made larger** ("Player Arrow Size", Display: a
  percentage, 100 as now, up to 400). Pan and zoom around a few hundred markers and the one thing
  you need to find again is a small arrow among them. It was the only thing on the map with no
  size of its own, because the game draws it as part of the map rather than as a marker. The
  arrow shown while you are aboard a ship is covered too, so it does not shrink back the moment
  you step on a boat.
- **The other players' markers can be made larger** ("Player Marker Size", Display: a percentage,
  100 as now, up to 400). On a map carrying hundreds of markers, three moving dots are easy to
  lose among them, and growing the dots is the quietest way to keep them findable -- nothing is
  hidden and nothing else changes. These are the game's own markers rather than this mod's, which
  is why they were the one thing with no size setting of their own.
- The spotlight and the ping grow from that size rather than from the normal one, so a player set
  larger stays larger while either is running.

## 3.0.1 — 2026-10-20

- **A server on 3.x now turns away a client older than 3.0.0**, with a reason: "The Greatest Map
  needs to be at least version 3.0.0. You have version 2.1.0." Before this it let them in and
  they quietly stopped receiving the shared map -- their own markers still worked and still
  reached everyone else, so nothing looked broken from either side while they saw less and less
  of the group's map. Being told to update at the door is a better answer than a map that goes
  stale without saying why.
- The floor is 3.0.0 rather than this version, so 3.0.0 clients are not shut out by it. It only
  rises when the saved format changes again, which the framing added in 3.0.0 is meant to avoid.

## 3.0.0 — 2026-10-20

**Everyone needs this together, the server included.** The saved format has changed, and versions
before this one refuse a format they do not know rather than read around it. Mixed versions will
not see each other's maps until everybody has updated. This should be the last time that is true:
from this version on, a marker travels as its own length-counted record, so a future field is
stepped over instead of losing the reader its place, and an unknown version is read for what it
does understand instead of being refused.

- **A marker now remembers what it is, not just the picture it was drawn with.** Decide that a
  sunken crypt suits a draugr trophy better than a swamp key and every sunken crypt on the map
  changes at once -- yours, and the ones other people wrote. The icon is worked out when the map
  is drawn, from the same catalog that recording and the legend read, so all three always agree.
- It follows that an icon is now a matter of taste rather than a thing written into the shared
  map. Changing one costs nothing, travels nowhere, and two players can disagree about what a
  frost cave looks like without either of them being wrong.
- **`tgm_identify` tells older markers what they are.** It matches each one against the catalog by
  its kind, its icon and its name, reports what it found, and writes nothing until
  `tgm_identify apply`. Worth running once, and worth running *before* changing any more icons:
  it reads markers back through the icons they were written with, so it gets harder the more those
  move. Markers it cannot place keep the icon they have.
- **Where the icon cannot settle it, the world is asked.** A dozen catalog lines share the house
  pin, and the names that told them apart were erased by the label rules long ago, so there is
  nothing left on a structure's marker to read. The building is still standing, though, and the
  server holds the list the world generator placed: every location, where it is and what it is
  called. `tgm_identify` puts the ones it cannot account for to the server and reads the answer
  off the world, so an abandoned farm is recorded as an abandoned farm because that is what
  stands there -- not because it was the first line in the catalog wearing that icon.
- **The marker list can be pinned open**, by the stick pin in its top right corner. Pinned, it
  stops closing when you click elsewhere on the map and comes back by itself every time you open
  the map, in the place you left it. The pin stands upright and gold when it is holding, and lies
  over at an angle, dim, when it is not, so its state reads without a word beside it. Escape
  still closes the list either way, because a key reached for to dismiss something should dismiss
  it. Pinning is on the list itself rather than only in the config, so it can be set without
  leaving the map.
- **A ping on every row in the marker list.** Press it and that kind's markers swell and
  brighten twice a second for a few seconds, then settle. It answers "where are all the dungeons"
  without putting anything else away, which is what separates it from the find box: nothing is
  hidden, nothing is changed, and the rest of the map stays where you can see it. A row in the
  find results pings that one type instead. "Locate Seconds" (Display) sets how long.
- **A kind you have switched off still answers.** Its markers come back for as long as the pulse
  lasts and hide themselves again when it ends -- being told nothing because you turned that kind
  off an hour ago is a poor answer to "where are they", and hiding is a standing preference
  rather than something to undo and redo to glance at the map.
- **Ships, carts and the other players can be pinged too**, though none of those markers belong
  to this mod: the first two are drawn from the server's sweep and the players are vanilla's own.
  "Where is everybody" is the same question the list answers for everything else, so Players has
  a row of its own -- clicking it gives the spotlight the H key gives, and the ping only makes
  them wave.
- A marker placed by hand follows its kind's icon, since its kind is all it ever claimed to be.
- Markers of a kind the catalog does not divide -- portals, runestones -- follow their kind's icon
  directly. There is only one sort of each, so there was never anything to tell apart.
- **Portals are drawn over everything else at the same spot.** Crews put a portal beside a trader
  and name it after them, and vanilla draws a trader's own icon at double size -- so the portal
  sat underneath it, barely visible, at exactly the place you most want to see one.
- **Traders are no longer recorded.** The game marks a trader the moment you find one, so a
  marker of ours was a second pin saying the same thing in a different picture. Nothing records
  them now, and the recording settings for the kind are gone with it.
- They keep their row in the marker list all the same, because "where is the nearest trader" is
  the same question the list answers for everything else -- the ping points at vanilla's own
  pins instead of ours. Like ships, carts and players, the row has no "place one by hand".
- Markers already recorded for traders stay until you remove them: `tgm_erase Trader 100000`
  clears them, and the erasure travels like any other.
- **Spawners are their own kind**, no longer filed under camps. A greydwarf nest is not a camp:
  nobody lives there and the reason to walk to one is to break it. The kind holds the nests and
  piles you can actually walk up to -- Greydwarf Nest, Draugr Pile, Evil Bone Pile, Charred
  Spawner -- and crossing one off means cleared, as with a vein, because a spawner you have
  broken is gone for good. Camps keeps the settlements: Fuling Villages and Draugr Villages.
- They are listed by name rather than found by their component, which is the opposite of how
  ships are found, and deliberately. Valheim fills its dungeons and camps with invisible spawn
  points named `Spawner_<creature>` -- over five thousand in one explored world -- and a rule
  that caught them would bury the map. It would also have missed the evil bone pile, whose prefab
  does not follow the naming of the other two.
- **`tgm_identify` refiles markers the catalog has since moved.** A greydwarf nest written while
  nests were camps stays a camp otherwise, which would leave the same thing in two places at
  once. A marker that knows what it is is filed where that thing is filed now.
- **Guck is recorded**, as a deposit, so a worked-out sack crosses off like a vein. There is a lot
  of it -- this world holds over 1,300 sacks -- so a swamp will read as a dense patch of markers.
  Turn "Show Ore" off, add `Guck` to "Hidden Icons", widen the ore marker spacing, or take the two
  guck lines back out of the catalog, whichever suits.
- Changing a kind's fallback icon now redraws the map, which it did not before.

## 2.2.0 — 2026-10-20

Client-side.

- **A tar pit is marked once, on the pit, instead of on each pool of tar in it.** The pit is the
  thing worth sailing to; the tar in it is what you go there for, and scattering markers across
  one is a poor way of saying "tar here". This world holds 30 pits and 224 pools, which is the
  whole argument. Pits now record as a kind of deposit, so a worked-out one crosses off like any
  other and the marker stays to say it is done.
- **If you have been playing a while, this will not reach you on its own.** The catalog lives in
  your config file, and a config file always wins over a changed default -- that is what makes it
  yours. Replace `Pickable_Tar=Tar Pit` in "Ore" with `TarPit*=Tar Pit|Tar` to pick it up. The
  same goes for the Yggdrasil roots added in 1.3.0: a file written before then has never recorded
  one, and `YggdrasilRoot*=Yggdrasil Root|Sap` on the end of that line fixes it.
- Guck is still not recorded, and that is deliberate rather than an omission: this world has 1,307
  guck sacks in it against 3,536 markers on the whole map, so recording them would roughly double
  everything you see. Anyone who wants them can add `GuckSack=Guck|Guck,GuckSack_small=Guck|Guck`
  to a catalog line -- "Ore" treats them as a supply that runs out -- and raise that kind's marker
  spacing so a swamp reads as a few patches rather than a thousand sacks.

## 2.1.0 — 2026-10-20

Client-side. Needs a 2.0.0 server for ships and carts, as 2.0.0 did.

- **Hovering a ship or a cart names it.** The labels are off by default, so until now a hull on the
  map was a picture and nothing else. The tooltip says what it is, that this is where the thing is
  rather than where somebody found it, and the prefab underneath -- which is the line worth having
  when a modded vessel turns up looking like something it is not. There is no finder and no date,
  because a vessel has no history here, only a position.
- Those pins are drawn in a way that keeps them out of the saved profile and out of a cartography
  table, and every lookup the game and this mod do over map pins skips exactly that kind of pin --
  which is why a vessel cannot be clicked or crossed off like a marker, and why the tooltip had to
  ask the vessel list where things are rather than ask the map.
- **The dynamic markers have a config section of their own**, "Dynamic Markers": "Show Ships",
  "Show Carts", "Show Transport Labels" and "Transport Sweep Seconds" were mixed in among the
  display and server settings, which are all about markers written on the map. Nothing in the new
  section is recorded, shared or saved, and keeping them apart says so.
- Moving a setting between sections would normally reset it, since BepInEx binds section and key
  together and leaves the old line behind as an orphan. The old value is carried across once, so
  if you had already turned the carts off they stay off. A choice made in the new section wins over
  a leftover from the old one.

## 2.0.0 — 2026-10-19

A round number because of what the first entry is, not because anything breaks. Every marker this
mod has drawn until now has been a record of something a player found; these are the first that
are not. Nothing has to be upgraded in step: an older server never answers the request, so its
players see no ships and everything else works exactly as before, and an older client ignores a
broadcast it was never listening for. The lowest version this mod will talk to is unchanged.

- **Ships and carts on the map, where they are now.** Every vessel in the world is drawn at its
  current position: the longship moored at the outpost, the karve somebody sailed off in, the cart
  left halfway up a mountain. Two new kinds, Ships and Carts, each with its own switch in the
  marker list and its own icon setting.
- These are the one thing on this map that is not a memory. Everything else recorded stays where
  it was found, because ore does not wander off; a longship does, so nothing is written down.
  The server sweeps the world and tells everyone, and each client draws what it was told. Nothing
  enters your map, nothing is shared, nothing survives the switch being turned off, and none of it
  can reach a cartography table.
- It follows that the "found, not detected" rule is not in play here, which was a deliberate
  decision rather than an oversight: a vessel is the group's own property, and knowing where you
  parked it is not a discovery about the world.
- **The whole world, not the part you have loaded.** The server holds every ship and cart there
  is, so a boat forty minutes away is on your map. This is the part a client-side mod cannot do:
  watching things appear around you only ever sees the bubble you are standing in, and loses
  track of them when you rejoin.
- **Modded ships are included without anybody maintaining a list.** The vessels to watch for are
  worked out by looking at what is in the world and keeping whatever is a ship or a cart
  underneath, so TheGreatestShips' hulls are covered, as is whatever comes next. A hardcoded list
  of names would quietly miss them, and a ship that exists and never appears on the map is the
  worst thing this feature could do. Finding them means reading the table the game resolves a
  saved object through rather than the list the world was built from: a hull cloned at runtime by
  another mod only ever reaches the first of those, which is why the first build of this saw none
  of TheGreatestShips' eight.
- **A vessel is called what the game calls it.** Ships are build pieces and carry their own names,
  so the longship reads "Longship" rather than "Viking Ship", which is merely the name of the
  prefab underneath. The naming happens on your own machine, in your own language, and a modded
  hull names itself the same way with nothing here knowing about it.
- **Those names are off by default** ("Show Transport Labels", Display). A harbor with six ships
  in it writes six names across the coast, and the icon already says what is there.
- The sweep is spread over several frames and runs every five seconds by default ("Transport
  Sweep Seconds", server only). These pins answer "where did I leave it" rather than "where is it
  this second": a longship under sail covers twenty meters in two seconds, so a tighter interval
  buys little and costs a walk of every object in the world. A moored ship riding the swell does
  not count as having moved, so a still harbor sends nothing to anybody.
- They answer the find box like everything else: "only Ships" leaves the fleet and puts the rest
  of the map away, and looking for frost caves puts the fleet away too.
- `tgm_transports` says which vessel prefabs are being watched, how many are out there, how many
  are drawn, and why.

## 1.9.0 — 2026-10-16

Client-side; works with a 0.2.x server.

- **Look for one thing, and see only that.** A map shared for a few weeks carries hundreds of
  markers, and finding the frost caves among them means reading past everything else. The marker
  list has a find box at the top: type "frost" and the map keeps the frost caves and puts the rest
  away, as the letters go in. It reads what is written on a marker as well as what the marker is,
  so "eggs" finds the portal somebody named 3 Eggs, and the kind's own name works too -- "ore"
  leaves the ore. Press Enter and what matched becomes rows, one per type with a count, to pick
  exactly one of them. Empty the box and everything comes back.
- **"only", at the end of every kind's row**, for the same thing in one press: show the portals and
  nothing else. Press it again to take it back.
- None of this is written down. It sits over the hide switches rather than among them, so whatever
  you had hidden stays hidden and comes back untouched -- there is no list of twenty kinds to
  switch on again afterwards. For the same reason it overrides them while it lasts: asking for
  frost caves means the frost caves whether or not dungeons happen to be switched off. A marker
  hidden by hand stays hidden, since that one means "not this one" rather than "not right now".
- The row under the find box says what the map is showing -- "Only Frost Caves — 7 of 412" -- and
  clicking it shows everything again. The marker menu's "Show all hidden" clears it too.
- `tgm_find frost` does the same from the console, and lists the types that matched with their
  counts; `tgm_find` on its own puts the map back. Useful when the map is not even out.
- While the find box has the keyboard, the game's keys and this mod's are left alone, so searching
  for a frost cave no longer takes the map out and opens the inventory on the way.
- **Fixed every structure outside the meadows calling itself an "Abandoned Farm"** in its tooltip,
  which 1.8.2 introduced when it started heading a tooltip with what the thing is. One house pin
  covers abandoned farms, swamp huts and stone towers alike, so the catalog maps that pin to each
  of them and the lookup took whichever was listed first. A marker still wearing its kind's
  fallback icon now says nothing about which of them it is, and the tooltip falls back to the kind
  -- "Structures" -- as it did before. The hide list already drew that line, where these all go to
  the catch-all group; this is the same rule in the one place that had missed it.

## 1.8.2 — 2026-10-13

Client-side; works with a 0.2.x server.

- **A crossed-off marker now fades, rather than only wearing a small cross.** That cross is drawn
  over the picture at the marker's own size, so a dark or busy icon swallows it -- the cultist
  trophy on a frost cave especially -- and a neighboring marker's label, a portal's for instance,
  can cover it outright. The marker itself washes toward gray and goes part transparent, which
  reads as done at any distance and whatever is drawn on top of it. "Crossed Off Fade" (Display,
  70) sets how far, and 0 keeps them as bright as before. Most of the effect is in darkening the
  marker rather than in transparency: these sit on a dark map, so washing a gold icon toward pale
  barely changes it.
- The cross itself is painted a clear red and drawn last, so nothing of this mod's sits over it.
- **Two markers on one thing fold into one.** Recording refuses to write a marker where one of the
  same kind already stands, but that test can only be made against your own map. Five players
  walking into the same silver vein each wrote one honestly, none of them had synced, and when the
  maps met both survived -- different markers by id, and nothing downstream looked at where they
  were. The pair then sat one exactly on top of the other, invisible except that there were two of
  them to cross off, and crossing one left a bright marker wearing a red cross, because the cross
  belonged to the one underneath. Markers arriving from the shared map or another player are now
  folded against the ones already here, which is the only place that sees both. The survivor is
  chosen by something every map agrees on, so two clients fold a pair the same way round rather
  than each erasing the other's. What the losers knew is carried over first: crossed off if any of
  them was, and a name if the survivor had none. "Fold Markers Standing On One Another"
  (Recording) turns it off.
- A name keeps two markers apart. The same kind, close enough together, is not quite enough to
  call them one thing: two names side by side are two things somebody meant to tell apart, however
  near they sit. A name against a blank is still one thing, and the name carries onto the survivor.
- `tgm_dedupe` lists the markers standing on one another -- how many, how far apart, and who wrote
  each one -- and `tgm_dedupe apply` folds them. A map that has been shared for a while has a
  backlog of these, worth reading before anything is erased.
- **A marker's tooltip is headed by what the thing is, not by what kind it is.** A frost cave was
  headed "Dungeons", which is the name of its row in the legend and tells a player standing outside
  the cave nothing they did not know. It now reads "Frost Caves", and the line beneath it repeating
  the kind and the icon's name is gone.
- The tooltip keeps up when a marker is crossed off while it is open. It was built once and left
  alone, so clicking to mark something searched changed the map underneath a tooltip that still
  said otherwise.

## 1.8.1 — 2026-10-09

Client-side; works with a 0.2.x server.

- Comparing maps with another player is written to the log, with timings. Two players taking their
  maps out together exchange markers and explored area, and applying someone's explored area is the
  same work as reading a cartography table, on the main thread -- so a slow one is a frame the game
  did not draw. None of it left any trace before: the "Compared maps with" line is a message on
  screen and nowhere else, so there was nothing to look at afterwards. The log now says who, how
  many markers, how much explored area each way, and how long each part took.

## 1.8.0 — 2026-10-08

Client-side; works with a 0.2.x server.

- **Place this mod's own markers by hand.** The map button's list of kinds grows a small `+` on
  each row: press it, then right-click the map as you always would, and the marker you place is of
  that kind -- with its icon, its tooltip, and a place in the hide and crossed-off lists, shared
  with everyone like any other. Until now a hand-placed marker could only be one of vanilla's five
  plain icons, and this mod's kinds existed only for things the recorder classified itself.
  Nothing is detected or inferred: the player saw it, the player marks it, so the "found, not
  detected" rule is untouched.
- **A "Sightings" kind, for what the map can never work out by itself.** Nothing records one --
  there is nothing in the world to classify -- so it exists purely to be placed: a serpent
  surfacing, a bay that fishes well, where the trolls walk. It wears the serpent trophy by default,
  which is a setting like any other kind's, and it hides and groups as its own row. Because nothing
  records it, it has none of the recording settings the other kinds carry.
- A marker of one of this mod's kinds is never written to a cartography table, even where the
  table is set to carry player markers. Its pin type is a number this mod invented; a client
  without the mod finds no icon for it and indexes its visibility array past the end. Nothing is
  lost -- such a client could not draw them anyway -- and the table is the one place a marker of
  ours can reach somebody not running it.
- The arming is spent by the next marker placed and no other, so a kind chosen and forgotten does
  not quietly claim every pin placed afterwards.

## 1.7.6 — 2026-10-03

Client-side; works with a 0.2.x server.

- **No more words on the minimap.** The game draws marker labels on the large map only and turns
  them off everywhere else -- and it works that out at the end of laying the markers out, which is
  the moment before this mod's own styling pass ran. That pass showed each visible marker by
  switching on its icon *and* its label, undoing the decision a moment after it was made. Only
  portals showed it, because a portal is nearly the only marker that carries a label at all: their
  label spacing is 0 and almost everything else is -1. Hiding a marker still hides its label;
  showing one now leaves the label to the game, which decides afresh every pass.

## 1.7.5 — 2026-10-03

Client-side; works with a 0.2.x server.

- Two things that could put a label on the minimap, found while looking for the one 1.7.6 fixes.
  Lighting up the players restored every label as the map closed -- which is to say onto the
  minimap. And the minimap clipping added in 1.7.2 swept the label root as well as the marker root,
  so a label it had put out could be handed back; there are no labels on the minimap to clip, so it
  leaves them alone. Neither was the cause of the portal labels.

## 1.7.4 — 2026-10-02

Client-side; works with a 0.2.x server.

- **The legend edits the catalog.** Click any row and its icon appears in a box at the foot of the
  panel; type another and the list redraws with the new picture, in red if that name is not a real
  item. An entry your own catalog line does not have is added rather than ignored, and clearing the
  box takes the override off again. An icon can only really be judged by looking at it, so this is
  the shortest path between wondering and knowing.
- The legend says what each icon *is*, not only where it came from: every row now reads
  `prefab — IconKey (source)`, with the key in the form you would type.
- The legend lays itself out in two or three columns when you widen it, balanced by height, with a
  kind's heading repeated at the top of a column it spills into.
- **The legend remembers its size and place again.** It always saved them; it could not read them
  back, because the parser insisted on three numbers and a panel's size is two. Every open quietly
  fell back to 560x620 in the middle of the screen.
- **Settings are re-read when the file changes on disk**, so an edit made in a text editor while the
  game runs takes effect. BepInEx reads the file once at startup and writes it from memory after
  that, so an edit was not merely ignored, it was overwritten the next time anything saved.
- An icon name's capitals no longer have to be right. The game hashes the name, so `Softtissue`
  found nothing and fell back to the kind's own picture, which looks like a wrong icon rather than
  a wrong capital.
- The legend stops calling locations missing. Dungeons, camps, traders, boss altars and most
  structures are placed by the world rather than spawned, so they are not in ZNetScene at all, and
  asking it alone put twenty honest catalog lines under "not in this game".
- Putrid holes and charred spawners are known by name, with the morgen and charred melee trophies.
  A putrid hole was already marked, as an unlisted location with an interior, which makes it a
  dungeon with a plain dot and a prefab for a name.
- Standing stones, stone circles, dolmens, ship settings and waymarkers share the memorial pin.
  They are landmarks with nothing in them to search, and since crossed-off markers group by icon
  they are now one row in the list rather than lost among the buildings.
- Mountain graves and mountain wells are not marked at all. Note that dropping a catalog entry is
  not enough by itself: an outdoor location with no entry is still recorded as a structure, so
  these are in "Structures Exclude Prefixes" as well, which is what actually stops it.
- Giant remains and petrified swords are no longer marked as places. Each piece -- sword, helmet,
  ribs, skull, brain -- is already recorded where it stands, wearing what it yields, so the site
  marker was a house pin sitting among five better ones.
- Icons for things that had none: dragon eggs for drake nests, the greydwarf trophy for greydwarf
  camps, the seeker for infested trees, the dvergr trophy for all six dvergr structures, and the
  charred mage for fortress ruins. Each giant's remains are named for what they are -- sword,
  helmet, ribs, skull, brain -- rather than all seven reading "Petrified Bone", and wear what they
  yield: copper scrap, black marble, and soft tissue for the brain.
- Scrap piles wear iron scrap and petrified bone wears bone, instead of both coming out as copper
  ore. A deposit whose catalog entry carries no icon falls back to what it drops, and then to the
  kind's own fallback, which is copper -- so anything the game would not name a drop for looked
  like copper. The answers are now in the table the code keeps for exactly this, which means a
  catalog line saved before those icons existed gets them too, without editing the line. Silver
  veins are covered the same way.
- Frost caves go back to the cultist trophy. The freeze gland tried in 1.7.2 was not an improvement.
- Markers already written keep the icon they were written with. For dungeons, walking past the
  place with your map out corrects it; for deposits, `tgm_erase Ore 100` near one lets it be
  recorded afresh.

## 1.7.3 — 2026-10-02

Client-side; works with a 0.2.x server.

- Every marker now carries a name, even the ones that draw no label. Most markers are drawn without
  a word on the map on purpose -- that is what keeps a crowded map readable -- but the name was
  being thrown away rather than merely not drawn, which left other mods nothing to recognise them
  by. HUD Compass filters its band by pin name, so there was no way to keep campfires, berries or
  mushrooms off it. Naming them changes nothing on the map: the game draws a label only for a pin
  created with one, so this names them afterwards. Existing markers are covered too.

## 1.7.2 — 2026-10-02

Client-side; works with a 0.2.x server.

- **Markers stay inside a round minimap.** New setting "Clip Minimap Markers" (Display, on): any
  marker falling outside the circle is hidden, whoever drew it -- this mod's, the game's own deaths
  and pings, and other mods' markers such as HUD Compass's boats. Vanilla's minimap is square and
  everything on it belongs there, so none of this happens unless something has actually made the
  minimap round, as the Round Minimap mod does: the game carries on reasoning about the square it
  used to be, and markers in the corners end up on the HUD outside the circle with nothing under
  them. Markers are found in the roots they are drawn in rather than in the game's list of pins,
  since a mod can parent its own markers there and those are not pins. It only ever hides, and only
  ever shows again what it hid itself, so nothing another mod wanted hidden is disturbed.
- Frost caves wear a freeze gland instead of the cultist trophy. A trophy is a dark, busy little
  silhouette and it was lost among everything else on the map; the gland is one bright shape, and
  the name says frost. Markers already written keep the trophy, and still group as frost caves.
- Drake nests wear a dragon egg instead of the house pin every structure shares. A house on a
  mountain peak told you nothing about what was up there; the egg says it plainly. Markers already
  written keep the house -- an icon is stored with the marker -- so `tgm_erase Structures 100` near
  a nest lets it be recorded again.
- Crossed-off structures hide by type now that one of them has an icon of its own: the marker
  button's list grows a "Drake Nests" row and an "Other Structures" row. Whatever still wears the
  kind's fallback icon stays in the catch-all rather than being named after whichever catalog entry
  happened to come first, so this grows one row at a time as types are given icons.

## 1.7.1 — 2026-09-29

Client-side; works with a 0.2.x server.

- "Highlight Players" moves from P to T, for team. P is the busiest key in a modded Valheim -- The
  Greatest Portal, Portal Rules, Target Portal and others all want it -- so pressing it opened a
  portal list over the players you were looking for. If you had already set it yourself, your own
  choice stands.
- A keybind with no modifier no longer answers a press that has one. The check asked only that the
  modifiers a binding listed were held, so a plain key fired while Shift or Ctrl was down: Shift+Y
  for the legend also took the map out on plain Y, and a plain key of this mod's answered another
  mod's chord on the same letter. It now wants exactly the modifiers asked for and no others, which
  is what BepInEx's own shortcut check does.
- Marker labels no longer blink while the players are lit up. Zooming or panning makes the game
  build its markers afresh, and a new one starts visible, so they showed for the frame before the
  spotlight put them out again. They are put out as part of that same rebuild now.
- Marker labels no longer appear beside the minimap. Keeping markers drawn past the map's edge,
  added in 1.6.1, was applied to whichever map was asking -- and the minimap asks the same
  question. It has no mask of its own, so the markers kept past its edge drew their labels onto the
  HUD next to it. Only the large map, whose pin roots are masked, is generous now; the minimap is
  back to the game's own behavior.

## 1.7.0 — 2026-09-29

Client-side; works with a 0.2.x server.

- **The crops in your own base are not written down.** New setting "Record Plants In Your Base"
  (Recording, off): berries, mushrooms, herbs, seeds and plants standing inside one of your own
  bases are left unrecorded, so a farm no longer puts a marker on every carrot you planted. A base
  is the game's own idea of one, the ground a workbench or fire keeps monsters out of, so it covers
  the fields as well as the house. Ore deposits are not covered: one standing in your yard is still
  worth knowing about. Markers already written stay; `tgm_erase Seeds 60` clears the ones near you.
- **Barnacles are crossed off when the leviathan sinks.** A few hits and it dives, taking them with
  it, so the marker pointed at open ocean. Sinking now counts as mining them out: the markers are
  crossed off rather than erased, keeping the record of where a leviathan surfaced, and nothing is
  suppressed, so one that surfaces there again is recorded again.
- Crossed-off markers can be hidden by type for any kind that has types, not dungeons alone. A kind
  splits only once its types wear different icons -- a type is read from a marker's icon -- so
  structures stay one group until their icons tell them apart, and will split by themselves when
  they do.

## 1.6.1 — 2026-09-28

Client-side; works with a 0.2.x server.

- Markers and their labels stay inside the map, and stay drawn while any part of them is on it.
  The game keeps a marker only while the point it stands on is inside the visible map, and draws it
  centered on that point with its label beside it -- so a marker vanished whole while half of it was
  still showing, and its words ran on past the frame over whatever lay beyond. Markers are now kept
  while any of them is on the map, and whatever overhangs the frame is cut off at it. This was
  always so; a map pulled in small only makes it easier to see.
- Vanilla's icon buttons shrink to fit a small map. They are two panels of a fixed size anchored to
  the map's bottom edge, some 480 units tall between them, so a map pulled in smaller than that was
  shorter than its own buttons and they hung off both ends of it. They are scaled down, place and
  all, and put back the moment there is room.

## 1.6.0 — 2026-09-28

Client-side; works with a 0.2.x server.

- **The large map can be made bigger and moved.** The game draws it inside a fixed margin, which on
  a wide screen leaves a good deal of map going spare. Drag the gold grip in the map's lower-right
  corner to open it out, up to the full screen, or pull it in smaller than the game draws it, and the
  strip along its very top edge to move it. Double-click that strip to fill the screen, and again to
  go back to the size it was.
  Where you put it is remembered, in "Map Extra Size" and "Map Position" under Display.
  The map itself grows while the pin labels, the icon column and the buttons stay the size they
  were, so a bigger map is a less crowded one rather than a magnified copy of the same one. It
  cannot be dragged off the screen, and there is nowhere to move it once it fills the screen.
  Growing it shows more of the world rather than drawing the same world larger: the map's scale
  comes from its height, so the zoom is let out by however much the height grew. "Map Resize Shows
  More" (Display, on) turns that off and leaves the zoom where you set it.
- The map's buttons are one row in its top-right corner: the pause mark, a maximize square and the
  map pin, all the same size. The pin used to be drawn large and hung off vanilla's icon column,
  which is anchored to the map's bottom edge, so making the map taller carried it away downward.
  "Pause Button Offset X" and "Y" now move the whole row; "Marker Button Offset X" and "Y" still
  nudge the pin alone and default to 0. All three turn bright orange and grow a tenth under the
  pointer, and wear the pause mark's orange while what they control is on: the maximize square
  while the map is full, the map pin while its list is open. The biome name the
  game writes in that same corner is moved down to sit under the row and follows it, rather than
  the buttons having to dodge it.
- New console command `tgm_mapui`: what the large map's layout measures, how much of the screen it
  uses and how its parts are anchored.

## 1.5.0 — 2026-09-28

Client-side; works with a 0.2.x server.

- `P` on the large map lights up the other players and puts every other marker out. Their markers grow, take a color
  of their own and breathe gently; press again, or close the map, and the map comes back exactly as it was, whatever
  you had hidden yourself included. Nothing is written and nothing is shared. Only players sharing their position can
  be shown, which is all the game tells your client, and you are told plainly when nobody is rather than being left
  looking at an emptied map. New settings: "Highlight Players" (Keys, P), and in Display "Highlight Players Hides
  Others" (on), "Highlight Players Color" (#66E0FF) and "Highlight Players Size" (170%).

- Marker tooltips no longer appear for markers hidden behind TheGreatestPortal's portal list. The
  tooltip works from the map position under the cursor, which knows nothing about panels drawn on
  top of the map, so it was describing markers the pointer could not see.

- **Marker icon types stop short of 1000, which is reserved for other mods.** A pin type is only an
  int, and this mod hands them out as it meets icons, counting up from 100 — so a map that had
  recorded 900 kinds of thing would eventually reach a number another mod had taken. Taking a
  number is not harmless: registering an icon for a type removes whatever entry was there, and the
  "is there already a marker here?" check matches pins by type. TheGreatestPortal draws its portal
  pins at 1000 and this stops below it. Reaching the ceiling is now a warning in the log and a
  default pin, not a silent argument with another mod. Nothing is saved with a type in it — markers
  carry an icon key — so no map changes.

## 1.4.1 — 2026-09-26

Client-side; works with a 0.2.x server.

- A portal you just raised is drawn again when something else is marked beside it. "Map All Portals" skipped any portal that already had a marker of this mod's within 4 m, but it counted a marker of any kind, and a base puts a campfire, a bush and a portal within a few meters of each other. The portal was treated as already marked and nothing was drawn until the pocket map next came out and recorded it. It now looks for a portal marker, or a marker placed by hand wearing the portal icon. The same test is what tgm_portals reports.

## 1.4.0 — 2026-09-25

Client-side; works with a 0.2.x server.

- The map-pin button and the pause mark are moved out of the corner the map writes the biome name in while you point at it. The pin goes up and right; the pause mark stays at the right but drops below the name. Four new Display settings place them: "Marker Button Offset X" (33) and "Y" (15), "Pause Button Offset X" (0) and "Y" (-20), in pixels, positive right and up. They take effect as you change them with the map open, so you can drag them where you want them.
- The pause mark no longer measures its place from the map-pin button. It sat to that button's left, which meant moving the pin dragged the pause mark along with it.

## 1.3.0 — 2026-09-25

Client-side; works with a 0.2.x server.

- Yggdrasil roots are recorded, the Mistlands roots a sap extractor is built on, with the sap icon. They are the only source of sap and they never move, so they are worth writing down, and finding one again in the mist is the whole problem. They count as a deposit, so "Record Ore" covers them, and they are never crossed off: tapping one does not use it up.
- Things with no component that says what they are can now be recognized by name. A root is not mined, picked or broken, so nothing this mod looked for was on it; a catalog line naming any plain networked object now works, which is what made the root possible. It applies to berries, mushrooms, herbs, seeds, plants and deposits, not to locations or campfires, which have rules of their own.

## 1.2.0 — 2026-09-20

Client-side; works with a 0.2.x server. The settings below become the server's to set, so a player
still on 1.1.0 or older keeps deciding them for themselves until they update.

- New setting "Lock Configuration" (General, on, server-synced): only server admins may change the settings marked [Synced with Server]. Those settings were shared before, but nothing enforced who set them, so any player could change a rule of the shared map for the whole server and the last one to touch it won. Your own settings are untouched, and single player and hosting your own game are unaffected.
- The settings that cross a marker off for everyone are now the server's: "Cross Off Mined Copper", "Cross Off Mined Silver" and "Cross Off Structures When Searched". Whether a mined-out deposit is crossed off on everybody's map is a rule of the server, not a choice of whoever happened to swing the pick.
- "Apply Label Rules To Existing Markers" and "Repair Dungeon Icons" are the server's as well. Both rewrite markers other players wrote, and labels are only ever removed, never added back, so one player's setting quietly edited the shared map for everyone.

## 1.1.0 — 2026-09-20

Client-side; works with a 0.2.x server. Players still on 1.0.0 or older see campfire markers as plain dots.

- Campfires are marked. Building one notes it like any other find, and it is written down the next time your map comes out; a campfire another player built counts once you have seen it. The marker is the campfire's own build-menu picture, already the color of fire, with no label; should that picture be missing, vanilla's fire pin is used instead, painted orange. Taking a campfire down removes its marker for everyone. Fires the world puts in camps and villages are not counted. "Record Campfires" and the other per-kind settings cover it, and the list behind the map's marker button has a row for it.
- Marker icons can name a building piece, as piece:<prefab>, to use its build-menu picture.

## 1.0.0 — 2026-09-16

Client-side; works with a 0.2.x server.

Out of alpha. The mod has been in daily use on its authors' server, and the description no longer
calls it alpha. Nothing changes from 0.3.2 but the version number.

---

Versions before 1.0.0 are in [CHANGELOG-0.x.md](CHANGELOG-0.x.md), which is kept in the
repository rather than in the package: Thunderstore refuses a changelog over 100,000
characters, and this one had grown past it.
