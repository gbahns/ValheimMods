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
| **Fast Longship** | Longship | 12.35 | 800 | normal | 12 | 0.85× | | blue | red stripes | Fine Wood 40, Ancient Bark 40, Iron Nails 100, Linen Thread 20, Resin 30 |
| **Knarr** | Longship | 8.6 | 1500 | slower | 32 | 1.25× | | amber | green stripes | Fine Wood 40, Ancient Bark 60, Iron Nails 180, Deer Hide 10, Core Wood 30 |
| **Busse** | Longship | about 8.0 | 1800 | slower still | 36 | 1.3× | 1.15× | amber | green stripes | Fine Wood 55, Ancient Bark 85, Iron Nails 250, Deer Hide 15, Core Wood 45 |
| **Big Busse** | Longship | about 7.5 | 2200 | slowest | 64 | 1.4× | 1.3× | amber | green stripes | Fine Wood 80, Ancient Bark 130, Iron Nails 350, Deer Hide 25, Core Wood 70 |
| **Stable Ship** *(experimental)* | Longship | 9.65 | 1000 | normal | 18 | | | white | brown | Fine Wood 40, Ancient Bark 40, Iron Nails 100, Deer Hide 10, Wood 80 |

Top speeds are in m/s, in full wind about 64° off the stern. They were worked out from logged
sailing with the Captain's Log mod.

Every ship names itself on its rudder ("Use rudder (Fast Longship)") and its hold ("Fast Longship
Storage"), and so do the vanilla ships ("Use rudder (Karve)", "Karve Storage"), so you can always tell which ship you are looking at.

Each ship sits in the Hammer right after the vanilla ship it is based on. As with every build
piece, it shows up once you have picked up each of its materials.

## Fast Karve

A light racing karve with an oversized sail. It outruns a longship, but its hull is lighter and it
has only two storage slots.

## Fast Longship

A lean longship under a linen sail: the fastest ship here, with a lighter hull and two thirds of a
longship's hold.

## Knarr, Busse and Big Busse

Three tiers of deep, heavy longship built to haul, named for real Norse cargo vessels. Each is
slower and clumsier than a longship, but sturdier, and holds progressively more: the Knarr 32
stacks, the Busse 36, the Big Busse 64. The Busse and Big Busse are also stretched bow-to-stern
(1.15× and 1.3×), not just wider, so their bigger hold reads as a longer hull, not just a fatter
one.

## Stable Ship *(experimental)*

A longship with a fenced pen on deck, built from real solid wood walls and a gate, tall enough
that a frightened tamed animal can't jump overboard the way it can on a bare longship. Otherwise
identical to a vanilla Longship. This is a first pass: the pen's position, size and deck height
are estimates rather than measured in-game, and it only stops animals from escaping -- it does
nothing to protect them from outside damage (monsters, the ship hitting rocks). Expect it to need
retuning.

## Configuration

`BepInEx/config/DeathMonger.TheGreatestShips.cfg` has one section per ship, named after it:

| Setting | | |
|---|---|---|
| Name Vanilla Ships (General) | Label the vanilla ships' rudders and holds too | Needs a restart |
| Recipe | Comma-separated `ItemName:Amount` prefab names | Synced with the server |
| Top Speed Multiplier | Relative to the vanilla ship it is based on | Synced with the server |
| Health | Maximum hull health | Synced with the server |
| Rudder Speed | How quickly the rudder swings; the vanilla ships use 1 | Synced with the server |
| Sail Color | Multiplied into the sail. White leaves it as it is | Needs a restart |
| Hull Color | Paint multiplied into the hull planks, not the mast or rudder | Needs a restart |
| Hull Stripes | Number of painted bands; 0 paints the hull solid | Needs a restart |
| Hull Width | Width relative to the vanilla ship. Keep it the same for everyone on a server | Needs a restart |
| Hull Length | Bow-to-stern length relative to the vanilla ship. Keep it the same for everyone on a server | Needs a restart |

Updating from 0.9.0 moves a Fast Karve speed of 1.14 and the old Cargo Longship recipe to the new
defaults. The Cargo Longship's own section also moves across under its new name, Knarr. Values you
changed yourself are kept either way.
