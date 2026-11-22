# Performance: drawn markers are what the full map costs (DSL session, 2026-11-04)

Measured solo in **GeniusWorld**, 2,939 TGM markers, map open, panning and zooming, DSL 0.10.21.
Three windows of about a minute, `dsl_hotspots drill Minimap.UpdatePins` once and `dsl_hotspots
reset` between conditions.

| | all shown | Resources hidden | all mine hidden |
|---|---|---|---|
| visible TGM markers | 2,939 | 569 | 0 |
| **frame rate** | **13.9 fps** | **19.8 fps** | **27.1 fps** |
| frame time | 71.9 ms | 50.5 ms | 36.9 ms |
| Minimap.Update per call | 11.760 ms | 10.624 ms | 7.011 ms |
| TGM postfix per call | 6.467 ms | 3.904 ms | 1.585 ms |
| relayouts/s | 6.8 | 11.1 | 16.1 |

Ignore ms/s for this comparison: a faster frame buys proportionally more relayouts, so the
postfix reads 43.79 then 43.22 ms/s and looks flat when its per-call cost nearly halved.

Where the 35 ms of frame time went:

| per frame | all shown | all mine hidden | recovered |
|---|---|---|---|
| in DSL's counters | 14.96 ms | 9.65 ms | 5.3 ms |
| **outside every counter** | **57.0 ms** | **27.2 ms** | **29.8 ms** |

**85% of the win is in Unity's canvas pass** - layout, batching and draw over active marker
objects - which no patch can see or time. Subtracting the postfix from `Minimap.Update` leaves
5.29, 6.72, 5.43 ms per call across the three conditions: flat, because vanilla walks the same
2,939 pins however many are shown.

### Per-marker price

| work | per marker | how often |
|---|---|---|
| Unity canvas layout, batch, draw | **~10.1 us** | **every frame** |
| TGM styling tail (scale, fade, label, sprite) | ~1.66 us | per relayout |
| TGM decision chain, reaching the `continue` | ~0.54 us | per relayout |

So the canvas costs roughly 12x everything this mod does. The only input it responds to is how
many marker objects are active.

### There is no culling bug - this was checked

Vanilla `UpdatePins` destroys the marker for any pin failing `IsPointVisible`, and that tests the
live `uvRect` (the visible window at the current zoom and pan), not the whole-world texture.
`StylePins` then opens with `if (pin.m_uiElement == null) continue;`, and AfterLayoutMaster
(ClientPins.cs:935) both skips those and returns early when nothing changed. Every marker drawn at
condition 1 was genuinely on screen. **The map is slow because it is being asked to draw 2,939
markers, and it draws them.** Nothing is being wasted; there is nothing to reclaim by culling.

### Ultrawide pays double

`CenterMap`: `uvRect.width = LargeZoom * (rect.width / rect.height)`. At 5120x1440 the aspect is
3.56 against 1.78 for 16:9, so at the same zoom level Greg sees twice the world width and about
twice the markers, and reaches whole-world width at LargeZoom 0.28 where a 16:9 player needs 0.56.
That mattered for the zoom-gate design below that was abandoned: a threshold in LargeZoom would
have given ultrawide and 16:9 players different products. **Thinning needs no special handling**
- the TGM session derived the cell size from the live chain and both axes come to
LargeZoom / rect.height, so the scale is uniform: twice the world visible, twice the cells.
Fully zoomed out on 5120x1440 that is a 384 m cell and 53 x 15 = 795 cells.

## The design: thin out overlapping markers (Greg's idea, 2026-11-04)

**Implemented in a51dacf** - Thinning.cs plus a two-pass StylePins. Config: "Thin Overlapping
Markers", "Marker Spacing On Screen" (marker widths, default 2) and "Most Markers Drawn"
(default 800). `tgm_status` prints the cell size and the counts.

Zoomed out, berries, mushrooms and tin overlap into mush - the same marker drawn forty times over
one meadow. Draw one per occupied cell instead. This beats hiding whole kinds progressively:

- **It preserves the information.** Hiding a kind loses "there is tin here". Thinning keeps one of
  each cluster, so only the redundancy goes.
- **It is self-scaling.** No per-kind thresholds to tune and no new list only Claude can edit - one
  plain toggle. (See the config-complexity note: 121 settings is already too many.)
- **It bounds the cost forever.** Drawn markers become screen area / cell area, independent of
  world size and of how much has been explored.

`m_pinSizeLarge` is 48 px. On 5120x1440, cells of one marker width cap the drawn count at
106 x 30 = **3,200**; cells of two marker widths (96 px, comfortable separation) cap it at
53 x 15 = **795**. At 10.1 us each that is **8.0 ms/frame of canvas instead of 29.7** - and it
still holds at 795 in Bahnsheim's 7,266, where today's cost would be ~73 ms/frame. Smaller screens
cap lower by themselves, so Brane's laptop gets a proportionally bigger win.

Design decisions worth making deliberately:

- **Per kind, with a global backstop.** One of *each* kind per cell preserves "tin and berries are
  both here", which is what the idea is for; a global cap (600-800 drawn) stops many co-located
  kinds from multiplying it back up.
- **Which marker survives a cell.** This line as first written contradicted the one above it: if
  every kind keeps its own cell, kinds never compete for one, so "rarer kinds win the cell" had
  nothing to decide. Resolved in a51dacf by putting Greg's count heuristic in the **cap** instead:
  when the cap binds, markers are given up from whichever kind has the most currently drawn,
  round-robin. That protects rare kinds without erasing common ones, which is what the heuristic
  was for. Within a cell: not-crossed-off first, then PinKey, so the same marker survives every
  relayout.
- **Exempt the near-radius reveal** (ClientPins.cs:1217). Markers close to the player always draw,
  so walking around is unaffected and the dandelion-doubt problem stays solved.
- **Quantize on a fixed world grid, not in screen space.** Cell world-size derives from zoom, but
  snapping to a stable world grid stops survivors flickering as the map is panned. Snap the cell
  size to discrete steps so zooming changes survivors in jumps rather than continuously.
- **Where it goes:** `SetMarkerActive` (ClientPins.cs:1442) is the single choke point and already
  handles the label. The gate costs one hash lookup per pin per relayout, about 0.3 ms at 2,939 -
  against ~4.9 ms of styling and ~22 ms/frame of canvas saved.
- The GameObject is instantiated by vanilla before the postfix runs, so thinning prevents the draw
  but not that one-time instantiate. `m_uiElement` stays non-null, so it is not re-instantiated.

### Secondary, worth about 2 ms/frame

`StylePins` re-styles every shown pin on every relayout. AfterLayoutMaster already shows the trick
- track the last state and touch only pins whose state is wrong. Do this third, or not at all.

### Bahnsheim result, 2026-11-04 (thinning on vs off, 7,286 markers)

**This measured the FIRST thinning build, which Greg found too aggressive and which had a real bug
in it - the cell was sized in Minimap's nominal 48-unit pin width rather than each kind's drawn
width, so it was 1.67x too big for the berries, mushrooms, herbs and ore that actually clump. Fixed
same day: per-kind cells sized to drawn width, cell = distance / root 2, ladder floors instead of
rounding, spacing default 2 -> 1. At this zoom the full-size cell went 1024 -> 256 m and berries to
192 m.** So the table below will not reproduce. Read it as **an upper bound on what thinning can
buy** - 501 of 7,286 drawn is close to the cheapest the map can be made - with the shipped rule
landing somewhere between this and nothing. The per-marker costs and the duty-cycle lesson still
hold; the fps win does not.

Solo, base, big map open and panning, drill on `Minimap.UpdatePins` in both windows. tgm_status:
"1024 m cells, 501 of 7286 drawn (6785 standing on one of their own kind)".

|                          | A: off     | B: on       |
|--------------------------|------------|-------------|
| frame rate               | 5.8 fps    | **16.4 fps** |
| frame time               | 172.4 ms   | 61.0 ms     |
| **panning duty cycle**   | **64%**    | **62%**     |
| in DSL's counters        | 20.7 ms    | 15.3 ms     |
| **outside every counter**| **151.7 ms** | **45.7 ms** |
| postfix per relayout     | 16.636 ms  | 12.864 ms   |

**+183%, with the duty cycles matched** - the first pair where they were. 106 ms/frame recovered in
the canvas pass. The per-marker model above predicted the unthinned postfix at 16.03 ms against
16.636 measured, 4% off, at 2.5x the markers it was built from.

**The decide pass costs ~1.11 us per candidate, not the ~0.1 us the design assumed.** Backing it
out: unthinned = 3.93 (chain) + 12.09 (style x7,286) = 16.03; thinned = 3.93 + X x 7,286 + 0.83
(style x501) = 12.864, so X = 1.11. That is why the postfix fell only 23% while the frame rose
183% - the canvas saving dwarfs the postfix either way. **Next lever if wanted:** cell assignment
is recomputed for every marker on every relayout, but a marker's cell changes only when the zoom
changes or the map pans by more than one cell. Caching it would be worth ~8 ms/relayout, about
5 ms/frame at this duty cycle. Even thinned, `Minimap.Update` is 17.7% of wall clock while panning
and the postfix is 74% of that.

**The earlier sublinearity finding is WITHDRAWN - it was wrong, from confounded windows.** The
first Bahnsheim pair had 11% panning against 62% (back-solved against the now-known 4.52 ms vanilla
baseline), so neither its +42% nor its 4.39 us/marker means anything. Clean windows give the
opposite sign:

    GeniusWorld  2,939 markers    29.7 ms recovered   10.1 us/marker
    Bahnsheim    6,785 markers   106.0 ms recovered   15.6 us/marker

1.54x the per-marker cost at 2.31x the markers - **superlinear**. Hold that loosely too: two points,
two worlds, different cell sizes. The honest position is that **there is no reliable scaling law
here yet**. In particular "be generous with Most Markers Drawn" is withdrawn; if anything cost
rises faster than marker count. The cap never bound in this run anyway - the 1024 m cells held it
to 501, under the 800 default - so Most Markers Drawn remains untested.

Reading caveat for the tables: at 172 ms frames anything that accumulates work over elapsed time is
inflated per call, so `ZNet.Update`, `ZNetScene.Update` and `Pathfinding` are not comparable between
windows of very different frame rates. `MonoUpdaters` is the exception, stable at ~2.2-2.8 across
every Bahnsheim window.

- **`Minimap.Update` ms/call is a duty cycle, not a cost.** It runs every frame; `UpdatePins` runs
  only on frames where the map is dirty (dragging, or the center moved). So its per-call figure
  averages panning and non-panning frames together, and two hand-panned windows have no reason to
  share a panning fraction. A 6.337 -> 9.709 "rise" with thinning on was that artifact: my own
  per-marker figures put the postfix at 16 ms per relayout, which cannot fit inside a 6.337 ms
  per-frame average unless ~60% of frames skipped it. **Always read UpdatePins calls/s alongside
  it** - the drill reports it, third column.

### Caveats and the test that would settle them

- One three-condition run, one world. The per-marker figures assume cost is linear in marker count.
- **The Bahnsheim run would test exactly that**: 7,266 markers, all shown, same procedure. If
  10.1 us/marker holds, the numbers above carry; if it is superlinear, the global cap matters more
  than the cell size.
- `MonoUpdaters.Update` per call moved between windows (1.679 / 1.469 / 1.457), so the windows were
  not perfectly matched.
- Timing adds a little to each call, so absolutes are slightly high. The ratios are the point.

---

[Info   :The Greatest Map] [TheGreatestMap] tgm_list: 9m [rec]  (piece:fire_pit) - icon piece:fire_pit, stored '', type 113, crossed no/no, not faded, no icon, hidden -- no reason found -- which is itself worth reporting

what is "hidden -- no reason found -- which is itself worth reporting" this text for?
overall what's the utility of the tgm_list command?  do we still need it?

notes:
AzuCraftyBoxes/GrabMaterials at 20, AzuAutoStore at 15



problems:


new features:

test:
* I think everything in the tree should be in a category, e.g. Dynamic Markers for ships, carts, portals, and players
* Sightings needs a major upgrade.  what we really need is the ability to place any icon, not just serpent icons.  need to think about this.  for starters, any icons already covered by auto-markers don't need to be available under Sightings. (first step: #92, a picture per thing — the fish, both serpents, bear, vile and gjall.)
* when pinging the location of markers make it draw them in front so it's easy to see them wave; their z-order can return to what it was before after; or if it's easier to just leave the z-order that behavior could make sense because then the player will still see whatever they were interested in most recently.  I don't know how the z-order is currently determined or if it's easy to control it.
* the vanilla markers should be covered in the list.  in fact, I can envision getting rid of the vanilla marker interface altogether in favor of the vanilla marker functionality being integrated into the TGM menu (this can be optional).
* when crossed-off markers are hidden how about keeping the "137 done" button hidden until hover just like the other buttons on the row?


* still a bit messy at the bottom of the map menu.  we've got the marker tree in pretty good shape now, but then you get to the bottom and it still needs some work:
* other markers - I don't know what this is - it says "3 done" but when I toggle it I'm not seeing what it's hiding/unhiding.  there's no ping button on this row to help me.
* Hide everything crossed off - this function is ok I guess, but it's quite a blunt-force tool.  I might like to get rid of this in favor of just hiding crossed-off things within the tree.  We can always add it back.
* Show erased markers is more of an admin-troubleshooting type tool, isn't it?  we can move it within the gear menu.
* ping a spot out loud - why do we need this?  is it covering a vanilla function that TGM otherwise suppresses?  I don't think so... middle-click still pings on the map.
* the hide all checkbox at the top is really an override for the regular hiding and showing in the tree, right?  and it only hides TGM markers (correct me if I'm wrong).  let's move this to the gear menu, and then take the "Show everything hidden - n types" button from the bottom and move it where the hide all checkbox is currently - make it say just "show all" and hide it when nothing is hidden.


a little problem is that when markers are hidden (think of dandelions as an example), when I'm exploring I don't know if those things are undiscovered or just currently hidden.  this results in me tending towards leaving everything visible to remove the doubt.  the downside to this is that my map view is perpetually covered with relatively insignificant things like raspberries, blueberries, and mushrooms. what could help with this:
* temporarily show newly discovered markers (10m?  30m?  configurable)
* show markers for things that are close to the player (50m?)
maybe the 2nd one is the way to go without the 1st one, because newly discovered markers will naturally be shown at first just by being close to the player, and they'll naturally be hidden again based on distance rather than time (just like pre-existing markers near the player)

done:
* if I click Berries to hide all berries, then click Raspberries, it's "hiding" raspberries even though they're already hidden; it should instead SHOW raspberries.
* the top "Hide all markers" doesn't hide everything - looks like it doesn't hide vanilla markers, or the built-in traders or bosses or Hildir quest dungeons.
* the hide all toggle at the top hides everything and the text changes to "show all", but then clicking "show all" doesn't actually show all; it just toggles of "hide all".  this seems like proper behavior, but the text needs to be accurate.  we could change it to a check box with "hide all" and then you just check and uncheck the box.
* the text above the tree says "Showing all 2662 markers".  I click on Resources (2279) to hide all of them, but the message still says "Showing all 2662 markers".
* draw an icon for dungeons and spawners like you did for berries, mushrooms, and herbs (unless you can find a suitable existing one to use, like the pickaxe for Ore)
* change "the order the types come in" to "Marker Order"
* change "What the map writes beside a marker" to "Labels"
* add an option (checkbox) to show/hide counts
* when a subset of a category is shown, instead of "Resources (2279)" it can say "Resources (1056 of 2279)
* ability to change the label on placed markers
* under Resource, put Farm last (after Ore)


I see "Seeds (4)" on my list.  it doesn't indicate what kind of seeds they are.  by zooming in and pinging I've figured out that they're all carrot seeds.  why isn't carrot seeds listed as a child underneath seeds?  some thoughts: 
* onion seeds are only in chests, so they'd never be marked on the map.  
* turnip seeds are found in the swamp like onion seeds.
* other pickable/plantable items don't have seeds but otherwise are similar to carrots and turnips: barley, flax, magecap and jotun puffs.
* the key thing that distinguishes these is that they're plantable
* the fact that they're technically mushrooms is less relevant in my mind than the fact that you 

the creature spawners in the game are:
* Greydwarf nest
* Evil bone pile
* Body pile (draugrs)
* Monument of Torment
* Monument of Torment (Elite)
* Effigy of Malice (no need to mark because they're in fortresses)


tgm_survey 3000 Spawners,Structures,Camps apply
tgm_survey 3000 Spawners apply
tgm_survey 3000 Structures apply
tgm_survey 3000 Camps apply

"Mark what you would travel to; don't mark what you walk past. A structure earns a marker if it holds something you'd come back for"
Another reason to mark something is to know you've cleared it so you DON'T waste time going back to it.  The obvious cases for this are dungeons like burial chambers, sunken crypts, caves, fuling villages.  A less obvious case for this is abandoned houses in the meadows; the reason for those is if you're trying to get early axes - you want to know which houses you've already checked.  Log Cabins are worth marking for this reason, because (if I'm not mistaken) they're in the mountains you can find onion seeds there - very important to find as soon as you can.  It's worth knowing which log cabins you've already searched so you don't bother again.

I think what you stated as "the rule I'd cut by" is correct except let's mark abandoned houses, for the reason I stated.  Cut Swamp Hut and Charred Ruins.