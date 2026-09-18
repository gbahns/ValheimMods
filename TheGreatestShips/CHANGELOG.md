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
- Cargo Longship: health 1000 → 1500, and its rudder swings at 0.8 of a longship's speed, so it
  turns more slowly. Its recipe takes 180 Iron Nails instead of 150, and Troll Hide in place of Deer
  Hide, as the fast ships do. Top speed about 8.4.
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
  tougher and roomier: the **Busse** (36 slots, 1800 health) and the **Big Busse** (64 slots, 2200
  health).
- New "Hull Length" setting, alongside "Hull Width": stretches the ship bow-to-stern instead of
  side-to-side. The Busse (1.15×) and Big Busse (1.3×) use it so their bigger hold reads as a
  longer hull, not just a fatter one.
- **Experimental:** a **Small Byrding** -- a Longship with a rail-fenced pen built onto the deck --
  corner posts and two rails a side, no gate yet -- that keeps a frightened tamed animal from
  jumping overboard the way it can on a bare longship. First pass: the pen's position, size
  and deck height are estimates, not measured in-game, and it doesn't protect penned animals from
  outside damage (only from jumping off). Otherwise a vanilla Longship 10% slower, with 12 hold slots instead of 18, the rest going to fodder and tack. Named for the Norse trading vessel that carried livestock.
- **Experimental:** two bigger sizes of it. The **Byrding** is the whole ship scaled 1.25×, with a
  pen the length of its deck: room for a few boars, a longship's hold, 1250 health, 15% slower
  than a longship and a little slower to turn. The **Greater Byrding** is scaled 1.5×, its fence
  high enough for wolves: room for a lox, 24 hold slots, 1500 health, 20% slower, slower to turn. Its recipe adds 4 Lox Pelt, which makes it Plains-tier -- as a lox carrier is anyway.
- New "Hull Scale" setting: the whole ship in all three dimensions; "Hull Width" and "Hull Length"
  multiply on top of it.
- Recipes trimmed so their materials weigh at most 450 (one Megingjord load), or 600 for the Big
  Busse and Greater Byrding (two trips, or a Mead of Troll Endurance): less Wood on the Byrdings,
  less Ancient Bark and Core Wood on the Busse and Big Busse. The Fast Karve, Fast Longship and
  Knarr already fit and are unchanged.
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
