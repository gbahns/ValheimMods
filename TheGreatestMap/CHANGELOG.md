# Changelog

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
