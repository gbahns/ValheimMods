# Spec: ship and cart pins in TheGreatestMap

Status: **implemented in 2.0.0**, 2026-10-19. Written 2026-10-19 by the lag-diagnosis session.
Kept for the reasoning behind the judgment calls -- the non-goals especially, which are decisions
rather than omissions. `Transports.cs` is the implementation; it follows this document, with the
sweep carried across frames one prefab at a time and the signature rounded to 4 m as suggested.

## Summary

Add map pins for ships and carts, drawn from a server-side sweep, modelled closely on the existing
`Portals.cs`. Two new categories, `Ship` and `Cart`, each with its own icon and visibility setting.
Pins are **drawn only** — never written to the pin store, never shared, gone when the setting is off.

This replaces a feature currently provided by the third-party mod **HUDCompass**
(`neobotics.valheim_mod.hudcompass`), which Greg intends to stop relying on.

## Why

Three reasons, in order of weight.

**1. It belongs here.** A cart someone parked or a longship moored at an outpost is exactly what a
shared group map should show. HUDCompass is a HUD overlay that happens to also write map pins;
TGM is the mod that owns the group's map.

**2. HUDCompass's version is limited to what the client has loaded.** It learns about vessels by
hooking `Ship.Awake` and `Vagon.Awake`, so it only sees things that spawn while it is watching —
roughly a 160 m bubble at the server's current simulation distance. A server-side sweep sees every
ship and cart in the world. Greg also reported a re-join bug to that author last year where portals
stopped appearing after rejoining; the `Awake`-hook design is the likely cause and the same class
of bug would apply to vessels. A server sweep plus a client request-on-join avoids it entirely.

**3. HUDCompass has a severe performance bug that Greg is working around.** Its `Hud.Update`
postfix walks **every** map pin every frame, doing `Vector3.ToString()`, `List<string>.Remove` and
`Transform.Find` per pin, with the distance test *after* all three. At Greg's 3,536 pins that
measured **19.9 ms per frame** — 99.4% of the whole `Hud.Update` chain, and two thirds of his frame.
Disabling the compass took him from 28 fps to 173 fps. That bug is in the compass display, not in
the map-pin path, so it does not directly affect this work — but it is why he is reducing his
dependence on the mod, and why the implementation here must not repeat the pattern.

## Scope

**In:**

- Two new `Category` values: `Ship`, `Cart`.
- Server-side periodic sweep for ship and cart ZDOs.
- Broadcast to clients when the result changes, same shape as `Portals`.
- Client draws them as pins with `m_save: false`.
- Per-category config: show/hide, icon choice, consistent with the other categories.
- Client asks the server for the list on join, and periodically if it has none.

**Out (explicit non-goals):**

- **No compass.** TGM does not draw a HUD compass and this change does not add one. That was
  considered and deferred.
- **No awareness of OdinHorse.** `Raelaziel.OdinHorse` draws its own pin for the Odin Cart, which
  is a `Vagon` underneath and so will also be picked up here. Greg has decided TGM should *not*
  special-case it: if a player sees a double pin they can set `HideCartPin = true` in OdinHorse.
  Keep this simple; do not detect, exclude or coordinate with that mod.
- **No writes to the pin store.** These are live positions, not discoveries. Nothing persists.
- **No sharing.** Each client draws from the server's broadcast; nothing enters `MapStore`,
  nothing syncs through `SyncEngine`, nothing leaves a tombstone.
- **No "found, not detected" gate.** Decided deliberately: a vessel is the group's property, not a
  discovery, so knowing where your own ship is does not break the rule that governs ore and
  dungeons. Everything is shown, subject to the config toggle.

## Design

Follow `Portals.cs`. It already implements this exact shape and the comments there explain the
reasoning. The differences are noted below.

```
server, every N seconds     sweep the world for ship/cart ZDOs
                            if the signature changed, broadcast to every client
client, on join / every 10s if it has no list, ask the server for one
client, on receipt          redraw its drawn-pin set
```

`Portals.cs` is ~470 lines but roughly half of it is the *marker reconciliation* half — correcting
and erasing stored portal markers whose portal moved or vanished. **Transports need none of that**,
because nothing is stored. Expect the new file to be substantially smaller.

### Enumerating ships and carts

There is no vanilla convenience list. `Portals` gets `ZDOMan.GetPortalList()` for free; nothing
equivalent exists for vessels. The available API is:

```csharp
public bool GetAllZDOsWithPrefabIterative(string prefab, List<ZDO> zdos, ref int index)
```

Verified in `assembly_valheim` (Valheim 1.0.20). Important properties:

- It takes a **prefab name string**, hashes it internally with `GetStableHashCode()`.
- It walks `m_objectsBySector` and processes **400 sectors per call**, then returns `false`.
- It returns `true` when the sweep is complete, having also covered `m_objectsBySector[0]` and
  `m_portalObjects`, and having run `zdos.RemoveAll(InvalidZDO)`.

So a full sweep of a large world takes several calls. Greg's world currently holds **1,066,241
ZDOs**; do not try to do this synchronously in one frame. Carry the `index` across frames and
treat a sweep as complete only when the method returns `true`.

Budget note: the server runs at a 30 Hz cap and currently sits at ~24% of one core with that world
size, so there is headroom — but one resumable sweep per prefab, spread across frames, is the
right shape regardless.

### Discovering which prefabs to sweep

**Do not hardcode a prefab list.** Two reasons:

- The conventional names (`Raft`, `Karve`, `VikingShip`, `VikingShip_Ashlands`, `Cart`) appear as
  strings in the game's asset bundles but are **not** literals in `assembly_valheim`, so they are
  unverified by inspection. Confirm them at runtime rather than trusting this document.
- Greg runs his own **TheGreatestShips**, which adds ship prefabs. A hardcoded list would silently
  miss them, which is the worst kind of bug here — a ship that exists and never appears.

Instead, build the list once at startup by walking `ZNetScene.instance.m_prefabs` and keeping any
whose hierarchy contains a `Ship` or a `Vagon` component. That handles vanilla, modded and future
vessels with no maintenance, and it tells you which category each prefab belongs to at the same
time.

Cache the resulting names; rebuild on `ZNetScene` re-creation (world load / rejoin).

### Drawing

Copy the `Portals.Redraw()` approach exactly:

```csharp
foreach (var pin in _drawn) if (Access.Pins(map).Contains(pin)) map.RemovePin(pin);
_drawn.Clear();
...
var pin = map.AddPin(pos, (Minimap.PinType)type, label, false, false, 0L);
//                                                      ^^^^^ m_save: false
if (pin != null) _drawn.Add(pin);
```

`m_save: false` is the load-bearing part — it keeps these out of the saved profile and out of
anything shared. Note that vanilla's `GetSharedMapData` filters on `m_save`, so false also keeps
them out of cartography-table syncs, which is what we want.

### Label

HUDCompass shows the vessel type ("Raft", "Karve", "Cart"). Matching that is reasonable. The
prefab name is available from the ZDO, so the label can be derived without extra lookups; prefer
a localized or prettified name if TGM already has a helper for that.

## Configuration

Follow the existing per-category pattern (`TgmConfig.CategoryIcon`, the show/hide settings used by
`Portals` and the other categories) so these appear in the same place as everything else.

Add one setting that `Portals` does not have:

- **Sweep interval**, server-side, default **5 seconds**. Portals use 2 s, but portals do not move
  and vessels do — a longship under sail covers ~20 m in 2 s, so a tighter interval buys little and
  costs a full ZDO sweep. 5 s was chosen as a starting point; make it configurable so it can be
  tuned without a rebuild. These pins answer "where did I leave it", not "where is it right now".

## Gotchas

- **`Hud` is rebuilt on every login.** Not directly relevant here since this draws map pins rather
  than HUD elements, but if any part of the work caches child objects, clear those caches on
  rebuild. This has bitten two mods in this repo already (see `HUD panel dies with the HUD`).
- **Wire versioning.** If an RPC payload shape is ever changed, keep reading the old shape. A
  version check that *refuses* an old message partitions players into groups that cannot see each
  other. Decide which shape to write; never which to accept.
- **ServerSync locking.** If any of these settings are server-synced, they must be registered with
  `AddLockingConfigEntry`. An unlocked synced setting accepts changes from any client, not just
  admins. Three mods in this repo had to be fixed for this.
- **Signature comparison.** `Portals.Signature()` rounds positions to integers before building the
  comparison string, so a stationary portal does not re-broadcast on float jitter. Vessels move, so
  a naive signature will differ on almost every sweep. Consider rounding more coarsely (e.g. to
  2–5 m) so a moored ship bobbing at anchor does not cause a broadcast every 5 seconds to every
  client.

## Acceptance

- A ship and a cart anywhere in the world appear on the map within one sweep interval.
- They move as the vessel moves, at sweep granularity.
- Turning the category off removes the pins immediately and leaves nothing behind.
- Logging out and back in restores the pins without further action — the specific failure mode that
  motivated this.
- Nothing appears in `MapStore`, in a cartography-table sync, or in another player's shared map as
  a result of this feature.
- A modded ship (TheGreatestShips) is pinned without any change to this code.
