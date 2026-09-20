# The Greatest Ships

> **⚠ Pre-release, still changing.** This mod is new and hasn't reached 1.0. Cargo slots,
> default build materials, and other balance numbers **will** change as it's tuned against real
> sailing data. A future update can shrink a ship's hold or swap its recipe out from under you.
> Use it at your own risk, and expect to re-tune your own configs after an update.

New ships built on Valheim's own hulls. Install on the server and on every client.

| Ship | Based on | Top speed | Health | Turning | Storage | Width | Length | Sail | Hull | Recipe (Workbench) |
|---|---|---|---|---|---|---|---|---|---|---|
| Karve *(vanilla)* | | 7.3 | 500 | normal | 4 | | | | | |
| **Fast Karve** | Karve | 10.1 | 400 | normal | 2 | 0.85× | | blue | red stripes | Fine Wood 30, Ancient Bark 20, Bronze Nails 60, Troll Hide 8, Resin 20 |
| Longship *(vanilla)* | | 9.65 | 1000 | normal | 18 | | | | | |
| **Fast Longship** | Longship | 13.0 | 800 | normal | 12 | 0.85× | 1.1× | blue | red stripes | Fine Wood 40, Ancient Bark 40, Iron Nails 120, Deer Hide 10, Troll Hide 10 |
| **Knarr** | Longship | 8.6 | 1100 | slower | 32 | 1.25× | | amber | green stripes | Fine Wood 40, Ancient Bark 50, Iron Nails 120, Deer Hide 10, Troll Hide 10 |
| **Busse** | Longship | about 8.0 | 1400 | slower still | 36 | 1.3× | 1.15× | amber | green stripes | Fine Wood 50, Ancient Bark 60, Iron Nails 140, Deer Hide 15, Troll Hide 15 |
| **Big Busse** | Longship | about 7.5 | 1900 | slowest | 64 | 1.5× | 1.5× | amber | green stripes | Fine Wood 60, Ancient Bark 90, Iron Nails 180, Deer Hide 20, Troll Hide 20 |
| **Small Byrding** *(experimental)* | Longship | 8.6 | 1000 | normal | 12 | | | white | brown | Fine Wood 40, Ancient Bark 40, Iron Nails 100, Deer Hide 12, Wood 24 |
| **Byrding** *(experimental)* | Longship, 1.25× | 8.3 | 1400 | a little slower | 18 | | | white | brown | Fine Wood 55, Ancient Bark 55, Iron Nails 150, Deer Hide 15, Wood 36 |
| **Greater Byrding** *(experimental)* | Longship, 1.5× | about 7.7 | 1600 | slower | 24 | | | white | brown | Fine Wood 65, Ancient Bark 65, Iron Nails 200, Wood 51, Lox Pelt 20 |

Top speeds are in m/s, in full wind about 64° off the stern. They were worked out from logged
sailing with the Captain's Log mod.

Hulls are priced by their planking: a ship's wood and nails follow the area of its hull, taking
the vanilla Longship's 80 wood and 100 nails as the unit -- the cargo ships pay a small premium
(about 1.05×) and the Byrdings, mostly open deck, a little less. Health follows the wood in the
recipe, 1000 per 80, except the two fast ships, which are built light and take 20% less. Every
recipe's materials weigh 450 or less -- one load with the Megingjord -- except the Greater
Byrding, the biggest ship here, at 482: two trips, or one on a Mead of Troll Endurance. (The
vanilla Longship's materials weigh 220, the Drakkar's 260.)

Every ship names itself on its rudder ("Use rudder (Fast Longship)") and its hold ("Fast Longship
Storage"), and so do the vanilla ships ("Use rudder (Karve)", "Karve Storage"), so you can always tell which ship you are looking at.

Each ship sits in the Hammer right after the vanilla ship it is based on. As with every build
piece, it shows up once you have picked up each of its materials.

## Fast Karve

A light racing karve with an oversized sail. It outruns a longship, but its hull is lighter and it
has only two storage slots.

## Fast Longship

A long, lean longship under an oversized sail, braced with troll hide and extra iron and sealed
with swamp guck: the fastest ship here, with a lighter hull and two thirds of a longship's hold.

## Knarr, Busse and Big Busse

Three tiers of deep, heavy longship built to haul, named for real Norse cargo vessels. Each is
slower and clumsier than a longship, but sturdier, and holds progressively more: the Knarr 32
stacks, the Busse 36, the Big Busse 64. The Busse and Big Busse are also stretched bow-to-stern
(1.15× and 1.5×), not just wider, so their bigger hold reads as a longer hull, not just a fatter
one; each holds about 1.4× as much per square meter of deck as a longship, which is what a hull
with no rowing benches should manage.

## Small Byrding, Byrding and Greater Byrding *(experimental)*

A longship with a rail-fenced pen on deck -- corner posts and two rails a side, no gate yet --
that keeps a frightened tamed animal from jumping overboard the way it can on a bare longship. Named for the Norse trading vessel that carried exactly this kind of cargo. Otherwise a vanilla Longship with one row of the hold given over to fodder and tack (12 slots instead of 18). This is a first pass: the pen's position,
size and deck height are estimates rather than measured in-game, and it only stops animals from
escaping -- it does nothing to protect them from outside damage (monsters, the ship hitting
rocks). Expect it to need retuning.

That is the **Small Byrding**: a boar or two. The **Byrding** is the whole ship scaled 1.25×, with a
pen the length of its deck, mast and all: room for a few boars, and a longship's hold. The
**Greater Byrding** is scaled 1.5×, with a fence high enough that a wolf can't clear it: room for a
lox, or a pack of wolves. Each is slower than the last: 10%, 15% and 20% under a longship, and
each turns a little more slowly too. The Wood in each recipe is exactly what its pen's poles and
beams would cost to build by hand: 24, 36 and 51. Each pen holds about one boar per two square
meters -- three, ten and fifteen -- and past that the animals stand on one another and can be
lost over the side. Under way, an invisible lip along the top of the rails keeps a jostled animal
in; at rest it is gone, so animals can be dropped in over the rails from a dock. Each pen also has
a gate on its starboard side: look at it and Use, and it folds outboard into a ramp from the pen
floor up over the gunwale, so with the ship alongside a dock at rail height animals can walk off,
or on.

## Configuration

`BepInEx/config/DeathMonger.TheGreatestShips.cfg` has one section per ship, named after it:

| Setting | | |
|---|---|---|
| Name Vanilla Ships (General) | Label the vanilla ships' rudders and holds too | Needs a restart |
| Recipe | Comma-separated `ItemName:Amount` prefab names, at most five: the build HUD has room for five plus the crafting station (a sixth is shown, but the station's icon hangs off the panel) | Synced with the server |
| Top Speed Multiplier | Relative to the vanilla ship it is based on | Synced with the server |
| Health | Maximum hull health | Synced with the server |
| Rudder Speed | How quickly the rudder swings; the vanilla ships use 1 | Synced with the server |
| Sail Color | Multiplied into the sail. White leaves it as it is | Needs a restart |
| Hull Color | Paint multiplied into the hull planks, not the mast or rudder | Needs a restart |
| Hull Stripes | Number of painted bands; 0 paints the hull solid | Needs a restart |
| Hull Width | Width relative to the vanilla ship. Keep it the same for everyone on a server | Needs a restart |
| Hull Length | Bow-to-stern length relative to the vanilla ship. Keep it the same for everyone on a server | Needs a restart |
| Hull Scale | Size of the whole ship in all three dimensions; Width and Length multiply on top. Keep it the same for everyone on a server | Needs a restart |

Updating from 0.9.0 moves a Fast Karve speed of 1.14 and the old Cargo Longship recipe to the new
defaults. The Cargo Longship's own section also moves across under its new name, Knarr. Values you
changed yourself are kept either way.
