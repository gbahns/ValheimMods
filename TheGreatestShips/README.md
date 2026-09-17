# The Greatest Ships

New ships built on Valheim's own hulls. Install on the server and on every client.

| Ship | Based on | Top speed | Storage | Width | Sail | Hull | Recipe (Workbench) |
|---|---|---|---|---|---|---|---|
| Karve *(vanilla)* | | 8.8 | 4 | | | | |
| **Fast Karve** | Karve | about 10 | 2 | 0.85× | blue | red stripes | Fine Wood 30, Ancient Bark 20, Bronze Nails 60, Troll Hide 8, Resin 20 |
| Longship *(vanilla)* | | 9.5 | 18 | | | | |
| **Fast Longship** | Longship | about 12.2 | 9 | 0.85× | blue | red stripes | Fine Wood 40, Ancient Bark 40, Iron Nails 100, Linen Thread 20, Resin 30 |
| **Cargo Longship** | Longship | about 8.5 | 32 | 1.25× | amber | green stripes | Fine Wood 40, Ancient Bark 60, Iron Nails 150, Deer Hide 10, Core Wood 30 |

Each ship sits in the Hammer right after the vanilla ship it is based on. As with every build
piece, it shows up once you have picked up each of its materials.

## Fast Karve

A light racing karve with an oversized sail. It outruns a longship but has only two storage slots.

## Fast Longship

A lean longship under a linen sail: the fastest ship here, with half a longship's hold.

## Cargo Longship

A deep, heavy longship built to haul. Slower than a karve, but it holds 32 stacks.

## Configuration

`BepInEx/config/DeathMonger.TheGreatestShips.cfg` has one section per ship, named after it:

| Setting | | |
|---|---|---|
| Recipe | Comma-separated `ItemName:Amount` prefab names | Synced with the server |
| Top Speed Multiplier | Relative to the vanilla ship it is based on | Synced with the server |
| Sail Color | Multiplied into the sail. White leaves it as it is | Needs a restart |
| Hull Color | Paint multiplied into the hull planks, not the mast or rudder | Needs a restart |
| Hull Stripes | Number of painted bands; 0 paints the hull solid | Needs a restart |
| Hull Width | Width relative to the vanilla ship. Keep it the same for everyone on a server | Needs a restart |
