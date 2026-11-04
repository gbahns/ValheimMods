# Changelog

## 4.8.0 — unreleased

- "You cannot write on your map while under attack" stays on the HUD and off the console.
- A leviathan is spotted at 150 m rather than 40 m, so its barnacles can be marked from the ship.
- Leviathans are in the catalog, so their markers have a row in the list instead of counting as orphans.
- A sync now sends only the markers that changed here, not the whole map: 96 KB became under a kilobyte.
- A marker recorded just before an autosave is no longer left on your map alone.
- A map too big for one message is sent in several, so the shared map can keep growing: it used to
  have a ceiling of roughly 38,000 markers, past which players were disconnected.
- Explored ground travels in a message of its own rather than behind the markers.

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
