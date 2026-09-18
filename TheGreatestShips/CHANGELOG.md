# Changelog

## 0.9.1 — unreleased

**Breaking: the server and every player need 0.9.1. The Fast Longship's hold changed size, so
0.9.0 and 0.9.1 cannot share a server.**

Rebalanced against real top speeds, worked out from logged sailing: the Karve reaches 7.3, not
the 8.8 that 0.9.0 was tuned against, and the Longship 9.65, not the 9.5 it was tuned against.

- Fast Karve: top speed multiplier 1.14 → 1.37, so it reaches about 10 and outruns a longship as
  intended (at 1.14 it topped out at 8.4). Health 500 → 400.
- Fast Longship: 12 storage slots (4×3) instead of 9. Health 1000 → 800. Top speed multiplier
  1.28 → 1.342, which gives it exactly the Fast Ship Skuldelev's sail force: about 13. Its recipe
  is now all Swamp-tier and below -- 40 Fine Wood, 40 Ancient Bark, 120 Iron Nails, 10 Deer
  Hide, 10 Troll Hide, 10 Guck -- so it no longer waits on the Plains for Linen, and Resin (never
  a real cost) is gone; and it is 10% longer as well as 15% narrower.
- Cargo Longship: health 1000 → 1100, and its rudder swings at 0.8 of a longship's speed, so it
  turns more slowly. Its recipe takes 120 Iron Nails instead of 150, 30 Ancient Bark instead of
  60, 20 Core Wood instead of 30, and Troll Hide as well as Deer Hide, as the Fast Longship does.
  Top speed about 8.4.
- New per-ship settings "Health" and "Rudder Speed", synced with the server.
- Every ship names itself: its rudder reads "Use rudder (Fast Longship)" and its hold "Fast Longship
  Storage". The vanilla ships get the same ("Use rudder (Karve)", "Longship Storage", and the
  Drakkar and Raft), in the player's language, so you can tell which ship you are looking at. "Name Vanilla Ships" turns
  that off for the vanilla ones.
- A config from 0.9.0 still holding the old Fast Karve speed or the old Cargo Longship recipe is
  moved to the new defaults. Values changed by hand are kept.
- Fixed: the three ships never showed up on the map or compass with HUDCompass installed.
  HUDCompass decides what counts as a ship once, at the main menu, before this mod's ships exist
  yet -- so it never saw them. If HUDCompass is installed, this mod now asks it to look again
  once its own ships are ready.
- The Cargo Longship is renamed **Knarr**, after the real Norse cargo ship -- same stats, same
  hold, same recipe, nothing else changes. Its saved config moves across to the new section name
  automatically.
- Two new, bigger cargo tiers above the Knarr, both slower and clumsier than the one before but
  tougher and roomier: the **Busse** (36 slots, 1400 health) and the **Big Busse** (64 slots, 1900
  health).
- New "Hull Length" setting, alongside "Hull Width": stretches the ship bow-to-stern instead of
  side-to-side. The Busse (1.15×) and Big Busse (1.5×, and 1.5× wide) use it so their bigger
  hold reads as a longer hull, not just a fatter one -- and so the Big Busse's 64 slots fit its
  deck at the Knarr's cargo density rather than twice it.
- **Experimental:** a **Small Byrding** -- a Longship with a rail-fenced pen built onto the deck --
  corner posts and two rails a side, no gate yet -- that keeps a frightened tamed animal from
  jumping overboard the way it can on a bare longship. First pass: the pen's position, size
  and deck height are estimates, not measured in-game, and it doesn't protect penned animals from
  outside damage (only from jumping off). Otherwise a vanilla Longship 10% slower, with 12 hold slots instead of 18, the rest going to fodder and tack. Named for the Norse trading vessel that carried livestock.
- **Experimental:** two bigger sizes of it. The **Byrding** is the whole ship scaled 1.25×, with a
  pen the length of its deck: room for a few boars, a longship's hold, 1400 health, 15% slower
  than a longship and a little slower to turn. The **Greater Byrding** is scaled 1.5×, its fence
  high enough for wolves: room for a lox, 24 hold slots, 1600 health, 20% slower, slower to turn. Its recipe takes 20 Deer Hide and 10 Lox Pelt, which makes it Plains-tier -- as a lox carrier is anyway.
- New "Hull Scale" setting: the whole ship in all three dimensions; "Hull Width" and "Hull Length"
  multiply on top of it.
- Hulls priced by their planking: each ship's wood and nails follow the area of its hull, with the
  Longship's 80 wood and 100 nails as the unit. The cargo ships had been paying about 1.5× the
  wood and 2× the nails per unit of hull, so they come down to about 1.05× (Knarr 90 wood, 120
  nails; Busse 110 wood, 140 nails; Big Busse 150 wood, 180 nails); the scaled Byrdings had been
  paying less than a Longship per unit of hull, so they go up about 10% (Byrding 110 wood, 150
  nails; Greater Byrding 130 wood, 200 nails). The Byrdings' Wood is exactly what their pen's
  poles and beams cost (24 / 36 / 52). Every recipe's materials now weigh at most 450 (one
  Megingjord load) except the Greater Byrding's 494 (two trips, or a Mead of Troll Endurance).
- Health follows the wood in each recipe, 1000 per 80: Knarr 1100, Busse 1400, Big Busse 1900,
  Byrding 1400, Greater Byrding 1600. The fast ships keep their 20% less, built light for speed.
  Saved Health, Hull Width and Hull Length values still at an earlier default move to the new one.
- Fixed: selecting a ship with seven ingredients in the Hammer threw an error every frame (the
  build HUD has room for six plus the crafting station). The row now grows to fit, though every
  recipe here stays within six.

## 0.9.0 — 2026-09-16

First release.

- The Fast Karve: a Karve with about 14% more top speed (about 10, where the Longship reaches
  9.5), two storage slots, and a blue sail. Built from Fine Wood, Ancient Bark, Bronze Nails,
  Troll Hide and Resin.
- The Fast Longship: a Longship with a top speed of about 12.2, nine storage slots, and a blue
  sail. Built from Fine Wood, Ancient Bark, Iron Nails, Linen Thread and Resin.
- The Cargo Longship: a Longship slowed to about 8.5 with 32 storage slots and an amber sail.
  Built from Fine Wood, Ancient Bark, Iron Nails, Deer Hide and Core Wood.
- The fast ships have red-striped hulls, the Cargo Longship green-striped, and are 15% narrower; the Cargo Longship is 25% wider.
  "Hull Color", "Hull Stripes" and "Hull Width" change them.
