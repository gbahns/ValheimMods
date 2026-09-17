# Valheim Mod Project Brief

## Developer Context

- Using BepInEx 5 + Harmony patching (standard Valheim mod stack)
- Thunderstore Mod Manager (TMM) for mod management
- Target: Valheim (current stable, Ashlands/Bog Witch era)
- Language: C#
- IDE: VS Code with Claude Code extension

---

## Mod 1 — Hunger Pangs (or: Vital Signs)

### Concept
Notification-only mod. The player's character knows they're hungry/poisoned/freezing.
The player should too. This mod bridges the immersion gap without removing agency.
It does NOT auto-eat. It does NOT automate anything.

### Design Philosophy
Simulate the nervous system telling the brain something is wrong.
Escalating urgency. Diegetic where possible. Never spammy.

### Trigger: Food Expiry (3 tiers)

**Tier 1 — Peckish** (~60–90s before a food buff expires)
- Subtle stomach growl sound (diegetic audio, not UI ping)
- Food icon in buff bar gets soft amber pulse
- No screen intrusion

**Tier 2 — Hungry** (~30s before expiry, or one food slot already empty)
- Louder/more insistent growl sound
- Buff icon pulses faster with slight red tint
- Subtle peripheral vignette (screen edge only, barely visible)

**Tier 3 — Starving** (food slot fully expired / all slots empty)
- Distinct stomach-pain sound (distress, not just growl)
- Screen-edge vignette deepens, brief desaturation pulse
- Buff bar slot: flashing hollow slot instead of just disappearing
- Small non-dismissible icon near health bar until player eats

### Trigger: Other Status Effects

**Poison**
- Augment vanilla green tint with: intermittent sharp wince/gasp sound
- More visible screen-edge vignette, pulses in rhythm with damage ticks
- Poison buff icon pulses with damage tick timing

**Freezing / Cold** (Mountains, Deep North)
- Shiver audio loop + frost-edge vignette that grows with Freezing stacks
- Important: players forget they're taking passive cold damage

**Wet (in combat)**
- Drip sound loop + buff icon highlight when Wet is active during combat
- Reminder that Wet hurts fire resistance and stamina recovery

**Low Health** (below ~25%)
- Persistent low-health heartbeat sound + vignette between hits
- NOT just on-hit (vanilla already does that) — persistent between hits

### What It Does NOT Do
- No auto-eating
- No gameplay pausing or slowing
- Each tier triggers once then has a cooldown before repeating (no spam)
- Vignettes only — never center-screen overlays
- No HUD elements that block view

### Config Options
- Per-effect enable/disable toggles
- Warning threshold timers (when does each tier kick in)
- Sound volume multiplier (separate from master game audio)
- Vignette intensity slider (0–100%)
- "Hardcore mode" option: Tier 3 starvation adds a small actual debuff (reduced max stamina or health regen) to match the lore-realism framing

---

## Mod 2 — Forsaken Shrines

### Concept
A craftable, placeable personal shrine system that lets the player swap Forsaken Powers
at their base — but with real progression cost and lore-appropriate friction.
This is NOT a convenience mod. Existing mods (ChangeForsakenPower, ForsakenPowersPlus)
already do free hotkey swapping. This mod makes swapping *earned* and *immersive*.

### Core Mechanic
- Defeat a boss → unlocks recipe for that boss's personal shrine
- Build the shrine → requires a **second trophy** (consumed on craft) + biome materials
- Shrine must be placed on **natural ground** (no flooring/platforms — rooted to earth)
- Shrine must be exposed to sky (no roof directly above — same rule as world altar stones)
- Interact with shrine → equip that Forsaken Power (same activation animation as world altar)
- Swapping between shrines has a configurable cooldown (default 30–60s)

### Shrine Recipes

| Boss | Trophy Cost | Additional Materials |
|---|---|---|
| Eikthyr | ×2 Eikthyr Trophy | ×10 Fine Wood, ×5 Deer Hide |
| The Elder | ×2 Elder Trophy | ×10 Core Wood, ×5 Greydwarf Eye |
| Bonemass | ×2 Bonemass Trophy | ×10 Iron, ×5 Entrails |
| Moder | ×2 Moder Trophy | ×10 Obsidian, ×5 Dragon Egg (or scale) |
| Yagluth | ×2 Yagluth Trophy | ×10 Black Metal, ×5 Fuling Totem |
| The Queen | ×2 Queen Trophy | ×10 Carapace, ×5 Sap |
| Fader | ×2 Fader Trophy | ×10 Flametal, ×5 Charred Bone |

### Placement Rules
- Natural ground only (no placed flooring beneath it)
- Exposed to sky (no roof piece directly above)
- One shrine per ward (prevents full pantheon at every outpost)
- Minimum 5–8m spacing between shrines (sacred objects need room)
- Shrines have HP and can be damaged by raids (protecting them is part of base defense)

### Optional / Configurable Mechanics
- **Attunement cooldown**: after swapping, 5-minute cooldown before next swap (committed to your choice)
- **Weakened away from shrine**: power swapped via personal shrine has 80% of normal duration vs. world altar (world altar remains the "true" source)
- **Trophy cost toggle**: config option to require 1 trophy instead of 2 (for casual players)
- **Shrine HP toggle**: config option to make shrines indestructible

### Mod Name
**Forsaken Shrines** — describes exactly what you're building: a personal hall of boss shrines.

---

## Mod 3 — Distance HUD

### Concept
Simple HUD element showing the player's radial distance from world center (0,0).
`distance = sqrt(x² + z²)` displayed as a clean number.
Useful for gauging how far into dangerous outer-world territory you've ventured.

### Notes
- MapCoordinatesDisplay (aedenthorn) shows X/Y coords on the map — useful but requires
  opening the map and doing mental math. This surfaces it as a persistent HUD readout.
- Should be configurable: show on minimap, show on HUD always, show only when map is open.
- Optionally show a biome-danger color indicator (green < 1500m, yellow 1500–3000m, red > 3000m)
  based on approximate vanilla biome ring distances.

---

## Mod 4 — Comfortometer

### Concept
Dedicated HUD widget showing:
- Current comfort level (number)
- Resulting Rested duration in minutes
- List of active comfort-granting pieces nearby (toggleable)
- Optionally: "X more comfort for +1 min Rested" hint

### Notes
- ComfortCalculationTweaks (Smoothbrain) shows a raw piece list but it's debug-style, not a UI widget.
- This should be a polished, minimal HUD element — not a debug overlay.
- Should update in real time as player moves toward/away from comfort items.
- Config: position on screen, font size, show/hide piece list, hotkey to toggle.

---

## Mod 5 — Food-o-pedia (Cookbook)

### Concept
A food-specific reference panel accessible in-game.
Comparison view: all food items displayed in a sortable table by stat (health, stamina, eitr, duration, heal rate).
Think "BIS food planning" — what's the optimal 3-food combo for this playstyle/biome?

### Notes
- VNEI (Valheim Not Enough Items) catalogs all items including food, but it's an item browser,
  not a food comparison tool. No sorting by stat value, no combo planning.
- Key features:
  - Sortable columns: health grant, stamina grant, eitr grant, duration, heal/tick rate
  - Filter by biome tier / progression stage
  - "Best 3-food combo" calculator: pick a stat priority, show optimal combination
  - Shows ingredient source (biome, creature, farm)
- Accessible via keybind, not tied to a crafting station

---

## Mod 6 — D3-Style Armory

### Concept
Named gear loadout slots, similar to Diablo 3's Armory system.
Save/load full equipment sets with a single interaction.

### Features
- Named loadout slots (e.g. "Troll Sneaker Kit", "Swamp Tank", "Mage Build")
- Save current equipped gear to a slot
- Load a slot: equips all saved items (must be in inventory)
- Comparison overlay: currently equipped vs. saved loadout
- Accessible via a craftable in-world item (Armory Rack or similar)
- Items must be in inventory to equip from loadout — no free item generation

### Notes
- Nothing like this exists in the Valheim mod ecosystem
- Medium-high complexity: requires inventory state management + custom UI
- The craftable object approach fits Valheim's crafting-station philosophy

---

## Mod 7 — Practice Mode

### Concept
A safe sandbox mode for combat practice without stakes.
No skill loss, no death penalty, configurable enemies.

### Key Design Questions (to resolve before implementation)
- Is this a world flag / game mode modifier, or a placeable in-world object (training dummy)?
- Should skills still gain XP in practice mode? (probably yes, but no skill loss on death)
- Configurable enemy spawns: type, count, level
- Damage numbers displayed (like MMO combat text) for parry/block feedback

### Notes
- Lower implementation priority than mods 1–4
- Needs design clarity before coding begins

---

## Mod 8 — Shared Map (working title)

### Concept

**Status (2026-09-10):** built as `TheGreatestMap/` (v0.1.0 published 2026-09-11 as an early alpha on Thunderstore and Hexium; single-player tested, not yet
tested in-game). Greg and Marco had used Better Cartography Table before and hit pins that kept coming back
after being erased (vanilla re-imports every foreign pin on each table read). The fix here: a server-side
store is the only copy, clients never persist shared markers, and erasing a recorded marker leaves a
"suppressed spot" so the recorder does not put it back.
Make the map a genuinely shared, living document between players on a server.
Idea dump from Greg + Marco, 2026-09-10, refined the same day. More to come.

- New map marker types (e.g. berries) beyond the five vanilla icons
- Cartography table syncs automatically when you're in range (or via a keybind while in range)
  instead of the manual read/write interaction
- The cartography table is only for sharing *exploration* (fog). Map *markers* share instantly
  between players, since players can already do that manually with pings anyway
- A **Map** the character carries "in a pocket": it takes no inventory slot. A keybind takes it
  out, and it occupies both hands (map in the left, pencil in the right). Markers can only be
  written while it's out
- **Automatic recording, no detection.** While the map is out, the character writes down
  important things nearby that the player has *actually found*. No radar: nothing is recorded
  that the player didn't see or interact with. Greg tried a proximity auto-pin mod once and
  considers that a cheat

### Open Design Questions
- **What counts as "found"?** Candidate signals, strongest first: interacted with it (picked,
  mined, entered, read); it was the crosshair hover target within 5 m; it was on screen with clear
  line of sight within N m for a dwell time. Likely a two-phase model: a client-side "discovered"
  ledger fills during normal play, and taking the map out writes pins for discovered-but-unrecorded
  things within range after the ~3 s dwell.
- **Where do instantly-shared pins live?** Pure broadcast never reaches a player who logs in
  later. Options: (a) also write them into every cartography table's data, (b) a server-side store
  (custom ZDO or file) replayed to joining clients, (c) hybrid. ServerSideMap and
  OneMapToRuleThemAll both chose (b).
- **Table reads delete foreign pins.** Vanilla `AddSharedMapData` removes every pin with a foreign
  owner ID that isn't in the incoming table data. Instant-shared pins carrying the sender's owner
  ID vanish on the next table read unless they're also in the table, or that behaviour is patched.
- **Who receives shared pins?** Everyone, or only players who have taken the map out?
- **What is "important"?** Pickables (berries, mushrooms, thistle, flax), locations (crypts,
  caves, runestones, altars, trader), ore veins, boss altars, docked ships?
- **Vanilla clients.** Custom pin types in a table crash an unmodded client that reads it (null
  icon lookup, visibility array indexed past its end). Either require the mod on every client, or
  downgrade custom types to vanilla icons when writing to the table. Custom pin indices also
  collide between mods that each append to the enum (Asocial Cartography's README documents this).
- **Does auto-sync respect ward access?** Vanilla read and write both check `PrivateArea.CheckAccess`.

### Technical Notes (verified against assembly_valheim, 2026-09-10)
- **Table read/write is simple.** `MapTable.OnRead` pulls a compressed byte array from the table's
  ZDO (`ZDOVars.s_data`) and hands it to `Minimap.AddSharedMapData`. `OnWrite` reads first,
  serialises via `Minimap.GetSharedMapData`, then sends a `MapData` RPC to the ZDO owner, who
  stores it. Both are private; the publicized assembly exposes them. Auto-sync is "find MapTable
  components within radius every few seconds, call OnWrite with a null item".
- **Only saved, non-death pins are shared.** The serialiser writes owner ID, name, position, type
  (as int), checked flag and author (`PlatformUserID`). Pins within 1 m of an existing pin are
  de-duplicated on read.
- **Custom pin types are an int cast.** `PinType` is an enum (18 values, last is `Memorial`);
  icons come from `Minimap.m_icons` (a `List<SpriteData>` mapping type to sprite). Add entries for
  new int values and cast, then resize `m_visibleIconTypes`. Round-trips through the table.
- **Shared pins already render differently.** Non-zero owner ID pins draw grey-tinted, fade in via
  the `_SharedFade` shader param, and toggle with the shared-map button. Author names are
  supported since the `PinsAuthor` format version.
- **Instant sharing.** Register a `ZRoutedRpc` message (like ping/chat) carrying the pin fields
  and broadcast to all peers. Cheap and immediate.
- **"Found" signals available in vanilla.** `Player.FindHoverObject` raycasts 50 m from the camera
  on the interact mask but only sets `m_hovering` within `m_maxInteractDistance` (5 m); a mod can
  reuse the same raycast with a longer cutoff plus a dwell timer for "looked at it". Interaction
  hooks: `Pickable.Interact`, `MineRock5.Damage`, `RuneStone` / `Vegvisir.Interact`, dungeon entry.
  `Location.m_discoverLabel` is the vanilla "entered a location" hook; boss altars are revealed
  via `Game.DiscoverClosestLocation` / `Minimap.DiscoverLocation`.
- **Pocket map, no item.** Since it takes no slot, model it as player state (a flag or custom
  status effect) rather than an `ItemDrop`. The keybind toggles it; while out, both hand items are
  hidden and attacks suppressed, and map/pencil prefabs attach to the hand bones the way
  `VisEquipment` attaches held items. Needs a two-handed idle animation or a reuse of an existing
  one (e.g. the crouch/read pose).

### Prior Art (Thunderstore, checked 2026-09-10)
- **OneMapToRuleThemAll** (DrummerCraig, 2.8.0, updated 2026-09-10, ~9k downloads): closest
  overall. One server-wide map, fog merged in real time, player pins synced, plus proximity
  auto-pins and a client radar driven by catalog files. Its auto-pins are exactly the detection
  model we reject.
- **ServerSideMap** (Mydayyy, 1.3.13, 2025-03, ~190k): server-authoritative explored map and
  optional pins, synced on join. Incompatible with crossplay. Not updated since 2025-03.
- **DiscoveryPins** (Searica, 0.3.10, 2025-03, ~37k): the "found it" model. Auto-pins dungeons on
  entry, ore on damage, portals on build; a keybind pins things in your field of view. Client-side.
- **PinAssistant** (WxAaRoNxW, 1.8.3, 2025-11, ~11k): pins the object you look at, or auto-pins
  tracked object types. Local only.
- **ZenMap** (ZenDragon, 1.10.0, 2026-08, ~44k): low-map play. Map only at the table or via a
  crafted parchment copy (a static snapshot); pins are attached to signs in the world, not placed
  on the map. Map data is universal. Different philosophy, but the carried-map idea exists here.
- **Better Cartography Table** (nbusseneau, 0.8.1, 2025-08, ~77k): private/public/guild pins
  shared selectively via tables, with real-time multi-user table editing.
- **Asocial Cartography** (VentureValheim, 1.0.0, 2026-09-10, ~22k): toggle sharing/receiving
  player pins at the table; overlap radius to cut clutter.
- **AutoPinSigns** (shudnal, 2.0.1, 2026-09-10): pins from sign text, synced from the server.
- Icon packs: MoreMapPins, RavenwoodMapPins, PixelMapIcons.
- **Gap this mod fills:** nobody combines instant shared pins with an honest "only what you found"
  recorder and the pocket-map ritual. The closest pieces are OneMapToRuleThemAll's sharing and
  DiscoveryPins' triggers.

---

## Mod 9 — Dude, What Are My Stats?

### Concept

**Status (2026-09-13):** built as `DudeWhatAreMyStats/` (v0.1.0, not yet tested in-game or published).
Grew out of ComfyMods' ReportCard breaking on Valheim 1.0: its stats panel reads the player profile with
the pre-1.0 field shape, so it throws. By then MyLittleUI already covered the character-select screen and
AzuExtendedPlayerInventory the in-game list, so a straight replacement was not worth building. What
neither of them does is the part Greg wanted.

The three things this mod is for:

- **Quick access.** One key (default `I`, free in vanilla and in Greg's profiles) from anywhere. AzuEPI
  makes you open the inventory and click a clipboard button.
- **Pause while reading.** Solo and host-alone freeze; a busy server does not. Goes through the game's own
  `Game.Pause` / `Game.Unpause` so Pause My Server can extend it without this mod knowing.
- **A scoreboard, for trash-talking.** Every player online who also runs the mod, in a sortable table:
  kills, deaths, K/D, boss kills, active play time, best skill. A Details tab breaks one player out into
  foldable sections with skills and most-killed creatures.

### Design notes

- **Client-side only.** Nobody's stats live on the server. A routed RPC asks everyone; each client reads
  its own profile and answers the asker directly. `ZRoutedRpc.RPC_RoutedRPC` forwards by target peer id
  without looking at the method hash, so an unmodded server relays it; players without the mod never answer.
- **`m_playerStats[0]` is the lifetime total.** Valheim 1.0 keeps ten sets per character, one per
  `DifficultyRequirement`. `IncrementStat` always writes slot 0 (RawStats), then slot 1 and the current
  difficulty's slot when the run is achievement-eligible, so summing the array counts most events two or
  three times. `PlayerProfile.GetStat()` is not the answer either: it returns the current difficulty's slot.
- **"Played" is active play time.** `TimeInBase` + `TimeOutOfBase`, which `Player` only increments when you
  have moved more than a meter since the last check, so it runs behind wall-clock and AFK adds nothing.
- The panel clones the game's own text prompt for its frame, font and buttons, reusing `UiKit.cs` from
  The Greatest Portal. No Jotunn, no asset bundle.

### Possible follow-ups

- Explored map percentage by biome, the one ReportCard feature nothing else replaces. Greg does not need it
  ("nice to have, no need to base decisions on it"), so it was left out of 0.1.0. ReportCard's approach:
  walk `Minimap.m_explored` against `WorldGenerator.GetBiome` and tally.
- A per-session column ("this session" vs lifetime), which needs a baseline captured at login.
- An external dashboard, the GsValheimStats idea. Greg liked it but did not want to take it on now.

---

## Mod 10 — The Greatest Ships

### Concept

**Status (2026-09-16):** built as `TheGreatestShips/` and published as 0.9.0 the same day (Thunderstore and Hexium).
Started when OdinShipPlus 0.8.3 refused to run on the DatHost server: its KeyManager V2 license check
reported the server as "Pending" and shut it down. Rather than depend on a licensed mod, build our own
ships, one at a time.

Ships are clones of the vanilla hulls with retuned stats, so no Unity project or asset bundle is needed
yet. A custom model is a later step (Unity editor + asset bundle; nothing in the repo does that today).

### Fast Karve (first ship)

- Clone of `Karve`, prefab `DM_FastKarve`. Bronze-age hybrid: needs a trip to the Swamp for Ancient Bark
  but no iron.
- Top speed about 10: Karve 8.8, Longship 9.5, OdinShipPlus's Fast Ship Skuldelev 12.2 (the ceiling).
- 2 storage slots, blue sail, red-striped hull, 0.85× width.
- Recipe: Fine Wood 30, Ancient Bark 20, Bronze Nails 60, Troll Hide 8, Resin 20 (Workbench).

**Speed physics.** `Ship.CustomFixedUpdate` pushes with `m_sailForceFactor × wind` and drags with
`speed² × m_dampingForward` every physics step, so top speed ∝ √(m_sailForceFactor / m_dampingForward).
The speed multiplier is squared and applied to the sail force.

### Longship variants (added the same day)

- **Fast Longship** (`DM_FastLongship`): top speed 12.2, matching the Skuldelev; 9 slots (3×3); blue
  sail, red-striped hull, 0.85× width; Plains-era recipe (Linen Thread sail).
- **Cargo Longship** (`DM_CargoLongship`): top speed about 8.5; 32 slots (8×4); amber sail, green-striped hull, 1.25× width; Iron-age
  recipe with Core Wood.

The Fast Karve sail tint first did nothing because the Karve leaves `Ship.m_sailObject` unset; the sail
(`ship/mast/Karve_Sail/Karve_Sail`, material `sail_white`, shader `Custom/Vegetation`) is now found by
material name. Hull paint multiplies the tint into bands of a GPU read-back copy of the hull
texture; width scales the whole ship on X, which skews the sail and rudder slightly when they turn.

### 0.10.0 rebalance (2026-09-17)

The 8.8 / 9.5 / 12.2 speeds that 0.9.0 was tuned against came from the ValheimGuides ships page, and
the page had never measured them. Greg's CaptainsLog CSVs gave real numbers: steady full-sail rows
fit speed² = k × drive within ±2%. Results: Karve 7.3, Odin Cargo Ship 8.7, Fast Skuldelev 12.2,
Longship about 9.4 (estimated). Also compared against the real prefab stats of vanilla ships and
OdinShipPlus, read from their asset bundles.

- Fast Karve x1.37 (about 10), health 400.
- Fast Longship 12 slots (4×3), health 800.
- Cargo Longship health 1500, rudder 0.8, 180 Iron Nails.
- New synced Health and Rudder Speed settings; the floor was raised to 0.10.0 for the hold resize.

### Possible next ships

- War Drakkar (more health).

---

## Technical Stack Reference

- BepInEx 5.x (not 6)
- Harmony 2.x patches
- Jotunn library (for recipe registration, piece registration, UI helpers) — preferred where applicable
- Target framework: net48 / net462 (Valheim standard)
- No Nexus-only dependencies; prefer Thunderstore-available dependencies
- ServerSync pattern for any config that needs server-client consistency

---

## Priorities (suggested build order)

1. **Hunger Pangs** — highest player impact, contained scope, no new game objects needed
2. **Distance HUD** — smallest scope, good starter mod to establish project structure
3. **Comfortometer** — small scope, polished UI goal
4. **Forsaken Shrines** — medium complexity, high design value
5. **Food-o-pedia** — medium complexity, data-heavy
6. **D3 Armory** — most ambitious, save for last or parallel track
7. **Practice Mode** — needs design resolution first
8. **Shared Map** — design in progress with Marco; resolve the open questions first
9. **Dude, What Are My Stats?** — built 2026-09-13; needs in-game testing before publishing
10. **The Greatest Ships** — published 0.9.0 on 2026-09-16 with three ships; speeds not yet raced against the targets
