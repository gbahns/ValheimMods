# Changelog

## 4.4.0 -- unreleased

- **A sync carries what changed instead of the whole map.** A cartography table used to
  send your map up and the shared map back, both in full, almost always to find that
  nothing had changed. The server now counts its own changes and answers with just the
  ones you have not heard; a map with nothing new of its own goes up empty. Nothing is
  ordered by any clock, so a machine whose clock is wrong cannot cause a marker to be
  skipped, and anything the server is unsure of still sends the whole map.

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

## 0.3.2 — 2026-09-16

Client-side; works with a 0.2.x server.

- Copper deposits and silver veins are no longer crossed off when mined out, unless the new settings "Cross Off Mined Copper" or "Cross Off Mined Silver" (Recording) are turned on. Much of either can be out of sight underground, so a player cannot always tell whether they have got all of it, and crossing it off for them told them something they could not know. Tin and plants are still crossed off, since there is no doubt when those are done. The two are told apart by what the deposit yields, so a renamed or fractured copy still counts.
- Hitting a fresh copper deposit or silver vein no longer counts as mining it out, for anyone who turns those settings on. The whole rock is destroyed by its first blow and the game puts a fractured copy in its place, which is the part you actually mine, and that first blow was being read as the deposit running out. Something replaced by a mineable copy of itself now counts as breaking open, and the fractured copy crosses the marker off when it is finished. Markers already crossed off this way stay crossed off; right-click one and choose Uncross.
- Searched places are hidden by type rather than all together. The marker button's list now has two columns, the kinds on the left and crossed-off markers on the right: cleared deposits, Structures, a switch for all searched dungeons, one per dungeon type (Burial Chambers, Sunken Crypts, Frost Caves, Troll Caves, Infested Mines, Bear Caves, Other Dungeons), and any other kind crossed off, each with a count. A dungeon's type is read from its icon, since the marker does not store it. A crossed-off marker's right-click menu offers its own type's switch, and "Hide all searched dungeons" on a dungeon.
- Mining a deposit out or picking the last of a plant that never grows back, before it is on your map, no longer makes your character forget it. It stays in memory like anything else found, and the next time the map comes out it is written down already crossed off, or, if a marker is already there, that marker is crossed off. Copper and silver are remembered and written down like any other find, and crossed off only if their settings say so. Finds remembered by this version are saved in a newer format, which an older version ignores.
- A folded-map icon shows among your status effects, beside Rested and Wet, while your pocket map is out, so a glance tells you whether what you find is being written down. "Map Out Status Icon" (Display) turns it off.
- "Show Searched Places", added in 0.3.1, is replaced by "Hidden Searched Places", the list of hidden types. If you had turned it off, searched places show again until you hide them in the new list.

## 0.3.1 — 2026-09-16

Client-side; works with a 0.2.x server.

- Markers you have crossed off can be hidden. "Show Cleared Deposits" already covered ore and plants that are used up; "Show Searched Places" now covers everything else, chiefly structures you have searched, so you can leave only the places you have not been to yet. The two are separate because they mean different things: a cleared deposit is gone, while a searched ruin is still standing and merely done with. The record is kept either way, and "Show all hidden" and `tgm_show` bring both back.
- Both switches are on the map screen, not only in the config: the marker button's list has a "Crossed off" section with "Hide cleared deposits" and "Hide searched places", each showing how many markers it covers, and that list's "Show all" turns them back on too. A crossed-off marker's right-click menu offers whichever of the two covers it.

## 0.3.0 — 2026-09-15

- A destroyed portal's marker goes at once, and stays gone. Two things were keeping it: taking a piece down with the hammer only runs the game's destroy step on whoever owns it, which need not be the player holding the hammer, so the moment was often missed entirely; and the server's own portal list can still hold one briefly, so its next sweep put the marker straight back. A portal this client watched come down is now remembered as gone for a minute, and any list arriving in the meantime is read with that in mind. The map is also redrawn there and then rather than on the next tick.
- Everyone else still learns from the server, which now sweeps every two seconds instead of five.
- A portal rebuilt under a new name is recorded under the new name. Something you have found but not yet written down is remembered by where it is, and seeing it again only restarted the clock rather than taking a fresh look, so a portal torn down and raised again on the same spot was written later under the name it had the first time. Everything about a find is now taken afresh each time it is seen.
- Portal markers stand down while TheGreatestPortal's picker is open, rather than sitting on top of the pins it draws for the same portals. Two labels over one place is unreadable, and that mod owns portals in its own overlay. The rest of the map stays, which is the point of showing it there.
- Destroying and rebuilding a portal now behaves. The client briefly ignored the spot where it had watched one come down, to stop the server's slightly stale list resurrecting the marker, but the server only sends its list again when something changes, so ignoring one entry left the client wrong about that portal indefinitely: a rebuilt portal stayed invisible and a renamed one kept its old name. The server's list is simply trusted now, and destroying or building a portal asks for a fresh one immediately rather than waiting for the next sweep.
- A destroyed portal stays destroyed. Removing the marker did not remove the memory of having seen the portal, and that memory lasts half an hour, so the next time anything was written down the marker came back. A pending portal is now checked against the world's own list before it is written: gone means the sighting is forgotten rather than recorded, and still there means the name is taken as it is now rather than as it was. It is the one thing on the map that changes by itself.
- A portal marker left over from a previous session is cleared at login. The server sends its list as soon as the client asks, which can be before the character's map has finished loading, and it only sends another when something changes, so a stale marker could wait for an unrelated portal to be built or torn down somewhere in the world. The work is now remembered and done once the map is there.

- Walking into a portal shows the map you already know. While TheGreatestPortal's destination
  picker was up, this mod hid every marker it had drawn, because that picker used to clear the
  map for itself. It no longer does, so hiding them only made the picker's map look like a
  different map. The draw-only pins from "Map All Portals" still stand down while the picker is
  up, since it draws a pin for every portal itself and they would otherwise be doubled.

- Portal markers no longer flash white while you walk. The game lays its pins out again whenever the map moves, which on the minimap is every step, and a freshly built icon starts at plain white. The color was being applied once a frame, which won or lost depending on which ran first, so it held while standing still and lost while moving. It is now applied immediately after each rebuild as well.

- Portal markers default to two shades of orange (#FF6600 fading to #FF8800) rather than gold fading to violet. Two shades of one color read as a gentle pulse; two different colors were far too loud. #A05BDD with #B07CFF is a good violet alternative, and the config help now says so.

- **A deposit is recorded for what it yields, not because its name is on a list.** The object already carries both facts the mod needs: the display name the game shows when you point at it, and the table of what mining it gives you. Asking those is simpler than maintaining a list of every prefab, and it cannot fall out of step with the game the way a list does. Every deposit bug found today, a renamed object, a fractured twin, a mismatched capital letter, came from that list disagreeing with the world; none of them can happen now, and an ore added by a later update or another mod works without anyone editing anything. A plain boulder yields only rubble, so it is still not marked, and the catalog still decides what a deposit is called and which icon it wears.
- New command `tgm_deposits` lists every deposit prefab in the game with what it yields and whether the catalog knows it, which is how the change above was checked rather than assumed.

- A deposit's icon is the thing you went there for, not the rubble alongside it. A copper vein drops stone as well as ore and the stone comes first in its table, so a copper deposit was being marked with a lump of stone. Stone, wood and the like are now passed over when a deposit yields anything else, and a plain boulder still shows stone because that is all it gives.

- New command `tgm_admin` asks the server whether it treats you as an admin, and reports the player id it sees for you. Only the server can answer that: admin status is a line in its adminlist.txt matched against the id on your connection, so nothing a client believes about itself counts. The id comes back because "which one do I put in the file" is the next question. **Needs the server on this version**, since it is the server that replies.

- Removing a marker now depends on what the marker is. One you placed by hand is your own note, tied to nothing in the world, so you can always delete it whether or not the server allows erasing; anyone else needs that permission, since to them it is someone else's writing. A recorded marker stands for something really out there, and the world does not forget: erase it and the next player past records it again. Those stay behind "Allow Erasing Markers", which is best thought of as a repair tool for markers that are wrong, with hiding being the everyday way to not see something.

No longer an early alpha. The mod has been in steady use on its authors' server for a week, and
the version and the description say so: alpha, with changes still expected between versions.
Client-side; works with a 0.2.x server.

- **Fixed the odd deposit going unrecorded while its neighbours were fine.** Valheim ships a fractured twin of many breakables, named with a "_frac" suffix, and a world uses one or the other: `rock4_copper` and `rock4_copper_frac` are both copper and look identical to a player. Only the first was catalogued. A variant suffix now falls back to the base name, so cataloguing one covers both, and the same goes for tin, silver, obsidian and everything else with a fractured twin.
- **Fixed the mod reading a thing's identity from a name the game rewrites.** Every deposit renames its own object the moment it loads, to "___MineRock5 m_meshFilter", because the game sets a name on its mesh filter and in Unity that renames the object it sits on. Anything reading identity from that name was reading something the game rewrites. The prefab is now asked of the network object, which keeps what it was spawned from and cannot drift. This covers deposits, plants, destructibles and locations alike, and it is what made the fractured-variant problem above visible at all.
- Catalog lookup by prefab name is no longer case-sensitive, which is almost certainly why the odd copper deposit or other known thing went unrecorded while its neighbours were fine. Matching a name prefix already ignored case; matching a whole name did not, so a catalog line differing only in capitalization looked exactly like no line at all.
- `tgm_look` says what the catalog makes of the object: which component it found, the prefab name that component sits on, and whether the catalog has an entry for it. Finding the component is only half the job, and a name mismatch used to report as "nothing recordable", which pointed the blame in the wrong direction. It also prints the object chain from the hit upward with the components at each level.
- The kind list's last row reads "Show all" rather than "Show every kind".
- `tgm_look` now reports the duplicate check exactly as the recorder performs it, around the same center and over the same radius, rather than an approximation of it, and lists every marker of that kind nearby with its icon and whether it is crossed off. A marker that exists but wears a different icon, or is crossed off as cleared, looks just like a missing marker on screen and showed up in neither check before.
- Seeds and plants are their own kinds, split out of herbs, which they never were. Carrot, turnip and onion seeds are Seeds; wild flax, wild barley and fiddleheads are Plants; thistle and dandelion stay Herbs. Each kind gets its own show, size and minimap switches, so seeds can be hidden without losing thistle.
- Traders and boss altars are no longer recorded by default: the game marks both by itself the moment you find them, so ours was a second pin saying the same thing. Switch either back on in Recording if you want them.
- Frost caves use the cultist trophy, greydwarf nests the brute trophy, silver veins the silver ore icon and scrap piles the iron scrap icon.
- The legend now flags a catalog entry for something this game does not have at all, which is how you spot a line that can never mark anything. Onion seeds are the example: they come out of chests rather than the ground.
- Bear caves use the bear trophy, pinned by its real id, `TrophyBjorn`. The bear's own name is Bjorn, so searching the item database for "bear" could never have found its trophy; the only thing it matched was PulledBear, a meal. A creature is not always named after the English word for it, so a known id now beats any search.
- A marker whose icon is wrong is corrected the moment you see the place, rather than waiting for the map to come out. Correcting what the map says about somewhere already on it is not recording a discovery, so it should not have been behind that gate.
- Sunken crypts use the draugr trophy instead of the swamp key, which suits what lives in them. Nothing uses the swamp key now, and the dungeons' fallback icon, which was the key, is a plain dot: there is no such thing as a crypt in general, so a wrong picture was worse than a plain one. Markers already written keep the key until you next see the place, which corrects them.

- A catalog entry added in a new version now reaches players who already have a config file. The catalog was read only from that file, so a line saved by an older build hid everything added since, which is why bear caves stayed unknown after being catalogued, and burial chambers before them. The list this version ships is consulted when the player's own list has no entry; anything written by hand still wins, including deleting a line.
- New command `tgm_items <word>` lists the game's items whose name contains a word, with a note on any that are skipped, so a marker icon can be chosen by looking rather than guessed.
- Item search prefers a trophy, then an item named after the creature ("BearHide") over one that merely mentions it ("PulledBear", a meal).

- The legend can be moved by dragging its title and resized by its corner grip, and both are remembered between sessions ("Legend Size", "Legend Position"). The grip pulls the bottom-right corner, so the opposite corner stays where you put it, and the panel's middle is kept on screen so one dragged too far can always be dragged back.
- The legend takes the mouse pointer and the wheel while it is open, so it can be scrolled and clicked without zooming the view behind it, and carries the same pause toggle as the map.
- The marker tooltip also shows the raw icon key, which is the field that actually goes wrong: a marker drawn as a plain dot is usually one whose key names an item this game does not have, and nothing else on screen said so.
- Fixed the bear cave icon resolving to a beard. The item search matched "Bear" inside "Beard1", then picked it for being short. Matching is now word-aware, so "Bear" hits "BearHide" and "TrophyBear" but not "Beard1"; customization items such as beards and hair are skipped outright; and an item whose icon cannot actually be read is rejected rather than accepted and drawn as a dot.

- A legend, on the `L` key or `tgm_legend`: every marker the mod can draw and the thing it stands for, grouped by kind and scrollable. Each row shows the icon resolved exactly the way recording resolves it, plus where that icon came from, the catalog, what the thing gives or drops, a guess from its name, or the kind's fallback. A row whose icon names an item this game does not have is called out in red, which is the failure that silently produces a plain dot.

- The bear cave icon is found by asking the game which items it actually has, rather than guessing prefab names. There is no "TrophyBear", and the bear-ish strings in the game's asset files turn out to be sounds and meshes, so the guess resolved to nothing and the marker came out a plain dot. The item database is now searched for a bear item, preferring a trophy, and what it settles on is written to the log.

- Hovering a marker on the large map names it: what it is, its icon, whether it has been cleared or searched, and who recorded it and when. Most markers carry no label on purpose, which is what keeps a map with hundreds of them readable, so this is where that information lives now. Config "Marker Tooltips".

- Every kind that has markers is back in the map's kind list and in a marker's right-click menu, structures, portals, camps and boss altars included. Vanilla's icon buttons also hide those, so it is redundant, but looking for the switch and not finding it with the rest was worse. The two differ in a useful way: vanilla's button hides every pin with that icon, ours hides only the markers this mod recorded.

- A dungeon marker with the wrong icon is corrected when you next see the place: the classifier knows what the cave is, so the marker is fixed rather than a second one written beside it. This is what actually repairs bear and troll caves already on your map, because the name-based repair below cannot reach them. Dungeons carry no label by default and the label rules blank the name, so a marker written before its cave was in the catalog has nothing left to identify it from the map alone.
- Dungeon markers wearing the swamp crypt key because their cave was not in the catalog when they were written, bear caves above all, are now corrected automatically: when your map loads and after each merge, so markers arriving from players still on an older version are caught too. Only markers with that icon whose name says otherwise are touched, so a real sunken crypt keeps it. Config "Repair Dungeon Icons"; `tgm_reicon` still does it on demand.

## 0.2.7 — 2026-09-15

Client-side; works with a 0.2.x server.

- Bear caves are recognized (prefab `BearCave`), named and given a bear icon: the bear trophy where the game has one, otherwise a bear paw, chosen at runtime so it works whichever the case. They are new enough that they were among the caves showing a swamp crypt key.
- A dungeon the catalog does not list no longer borrows the swamp crypt key, which made every unlisted cave claim to be a burial chamber. The icon is now guessed from the name (troll caves get the troll head), anything still unrecognized gets a plain dot rather than a wrong picture, and the prefab name is written to the log once so it can be added to the catalog properly. New command `tgm_reicon` repairs markers already written this way, for everyone; a real sunken crypt keeps its key.
- The three-second wait after taking the map out is gone (the setting remains, defaulting to zero). Taking the map out is already the deliberate act, and a delay that writes nothing just looks broken. In its place is a rule with a reason behind it: you cannot write while something is hitting you ("Cannot Write Under Attack", on, for five seconds after the last hit). Enemies merely being nearby do not stop you, unlike resting, so you can stand your ground and write if you choose.
- Optionally you must also hold still to write: "Cannot Write While" is Never, Running or Moving. Off by default.
- `tgm_look` now opens with whether you can write at all, and why not.
- A pause toggle in the top-right corner of the large map, the same mark GrabMaterials puts on its inventory panel: two bars, gray when pausing is switched off, orange while the game really is paused, and red with a slash when the pause was asked for but refused, which is what happens on a server with other players online or one without Pause My Server. Config "Pause Button On Map".
- Mining a deposit out crosses its marker off, for everyone, rather than erasing it: tin, copper, silver and the rest leave nothing behind, but which ground has already been worked is worth knowing. New option "Show Cleared Deposits" (on) keeps them drawn with their cross; off hides them once cleared, and the record is kept either way. Structures are untouched by that switch, since their cross-off means searched rather than gone.
- Picking something that never grows back crosses its marker off the same way. Carrot, turnip and onion seeds do not respawn, so a marker that still promised one was wrong, and they are no longer recorded once picked. Berries, mushrooms, thistle and the rest do respawn and keep their markers as before. `tgm_look` reports which is which for whatever you are pointing at, straight from the object in front of you.

## 0.2.6 — 2026-09-13

Client-side; works with a 0.2.x server.

- Building something now counts as finding it, so a portal you raise goes on your map without you having to look at it afterwards. It is written under the usual rules, which in practice means the next time you take the map out, since both hands are busy while you are building. Only things the mod already records are noted this way; a house you build is not one, because a structure has to belong to a location the world generated.
- Portal markers have their own color, and by default fade slowly between pale gold and violet so they stand out on a map crowded with plants. Both colors and the speed are configurable ("Portal Marker Color", "Portal Pulse Color", "Portal Pulse Seconds"); clear the pulse color for a steady one, and either accepts a hex value such as #B07CFF or a name such as violet.

## 0.2.5 — 2026-09-13

Client-side, but the server must run this version too for portals to be corrected: the server is
where the list of portals comes from. Works with a 0.2.x server otherwise.

- **Portals are no longer remembered, they are known.** Every other kind of marker is a memory of something that stays put, but a portal gets renamed, torn down and rebuilt, so a marker written once goes wrong within days. The server now reads the game's own list of every portal and tells the clients, and each client corrects the portal markers it holds: renamed when the portal is renamed, erased when it is gone. The erasure travels like any other, so stale portals disappear for everyone, and the spot is not suppressed, so a portal rebuilt there is recorded again. Requires the server to run this version; single player and hosted games are their own server.
- New option "Map All Portals" (on by default) also draws every other portal that currently exists, not only the ones someone has seen. Portals are built by the players, so on a server where everyone is in the same group this just keeps the map honest; turn it off on a public server and portals are learned like everything else. Those extra pins are drawn only, never written to your map and never shared, so turning it off leaves nothing behind.
- TheGreatestMap's markers now stand down while TheGreatestPortal's portal picker is open over the map. That overlay hides the saved pins for itself, but this mod's styling pass ran afterwards and switched its own back on, which is why portals appeared twice there, in white and in gold.

## 0.2.4 — 2026-09-13

Client-side; works with a 0.2.x server.

- The per-kind buttons from 0.2.3 are replaced by a single map-pin button above vanilla's icon buttons on the right edge of the large map. Right-clicking it hides or shows every marker from this mod at once, the same gesture that hides and shows one icon's pins on each vanilla button, and it covers markers with no kind, which the per-kind switches never did. Left-clicking it opens the list of kinds that have markers on your map, each with its icon, for hiding them one at a time without leaving the map; that list also carries the hide-all row. The pin is gold while markers are shown and gray while they are hidden. The config option "Kind Buttons On Map" is replaced by "Marker Button On Map" (on by default) and "Show Markers".
- A marker you have just recorded shows on the minimap and the large map for thirty seconds even when its kind or its icon is hidden, so writing something down always shows you what you wrote ("Reveal New Markers Seconds", 0 turns it off). Markers that arrive from another player's map do not do this, and the reveal never overrides the hide-all switch or a marker you hid by hand.
- Structures, portals, camps and boss altars are no longer offered in this mod's own hide and show controls, because they draw on vanilla's map icons (house, portal, campfire, boss) and vanilla's icon buttons already hide and show them, including pins you placed by hand with the same icon. One control for those instead of two that half overlapped. Runestones keep ours, since the memorial pin has no vanilla button.

## 0.2.3 — 2026-09-13

Client-side; works with a 0.2.x server.

- Right-clicking one of this mod's markers on the map now opens a small menu instead of erasing it: "Hide this marker", "Hide all Dandelion markers" (that icon), "Hide all herbs" (that kind), "Cross off" / "Uncross", "Erase for everyone" (dimmed unless the server allows erasing, as before), and "Show all hidden" once anything is hidden. Shift + right-click is still the quick erase where erasing is allowed. Vanilla pins keep their vanilla right-click.
- Markers hidden one at a time are remembered with the character; icon and kind choices go straight into the Display settings ("Hidden Icons", "Show <Kind>"). Hidden markers stay on the map and keep syncing.
- New console command `tgm_show` brings every hidden marker back in one go.
- Kind buttons on the map: beside vanilla's icon buttons on the right edge of the large map there is now one button per kind that has markers on your map (berries, herbs, ore, structures and so on), showing the kind's icon. A click, left or right, hides or shows that kind, and a hidden kind's button turns gray, the same as vanilla's own filter buttons. Config "Kind Buttons On Map" (on by default).
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
