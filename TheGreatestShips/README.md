# The Greatest Ships

New ships built on Valheim's own hulls. Install on the server and on every client.

| Ship | Based on | Top speed | Health | Turning | Storage | Width | Sail | Hull | Recipe (Workbench) |
|---|---|---|---|---|---|---|---|---|---|
| Karve *(vanilla)* | | 7.3 | 500 | normal | 4 | | | | |
| **Fast Karve** | Karve | about 10 | 400 | normal | 2 | 0.85× | blue | red stripes | Fine Wood 30, Ancient Bark 20, Bronze Nails 60, Troll Hide 8, Resin 20 |
| Longship *(vanilla)* | | 9.65 | 1000 | normal | 18 | | | | |
| **Fast Longship** | Longship | about 12 | 800 | normal | 12 | 0.85× | blue | red stripes | Fine Wood 40, Ancient Bark 40, Iron Nails 100, Linen Thread 20, Resin 30 |
| **Cargo Longship** | Longship | about 8.4 | 1500 | slower | 32 | 1.25× | amber | green stripes | Fine Wood 40, Ancient Bark 60, Iron Nails 180, Deer Hide 10, Core Wood 30 |

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

## Cargo Longship

A deep, heavy longship built to haul. It is slower and turns more slowly than a longship, but it
is sturdier and holds 32 stacks.

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

Updating from 0.9.0 moves a Fast Karve speed of 1.14 and the old Cargo Longship recipe to the new
defaults. Values you changed yourself are kept.
