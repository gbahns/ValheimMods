# Changelog

## 0.9.1 — unreleased

**Breaking: the server and every player need 0.9.1. The Fast Longship's hold changed size, so
0.9.0 and 0.9.1 cannot share a server.**

Rebalanced against real top speeds, worked out from logged sailing: the Karve reaches 7.3, not
the 8.8 that 0.9.0 was tuned against, and the Longship 9.65, not the 9.5 it was tuned against.

- Fast Karve: top speed multiplier 1.14 → 1.37, so it reaches about 10 and outruns a longship as
  intended (at 1.14 it topped out at 8.4). Health 500 → 400.
- Fast Longship: 12 storage slots (4×3) instead of 9. Health 1000 → 800. Top speed about 12.
- Cargo Longship: health 1000 → 1500, and its rudder swings at 0.8 of a longship's speed, so it
  turns more slowly. Its recipe takes 180 Iron Nails instead of 150. Top speed about 8.4.
- New per-ship settings "Health" and "Rudder Speed", synced with the server.
- Every ship names itself: its rudder reads "Use rudder (Fast Longship)" and its hold "Fast Longship
  Storage". The vanilla ships get the same ("Use rudder (Karve)", "Longship Storage", and the
  Drakkar and Raft), in the player's language, so you can tell which ship you are looking at. "Name Vanilla Ships" turns
  that off for the vanilla ones.
- A config from 0.9.0 still holding the old Fast Karve speed or the old Cargo Longship recipe is
  moved to the new defaults. Values changed by hand are kept.

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
