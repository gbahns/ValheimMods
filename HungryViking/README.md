# Hungry Viking

**Your character knows they're hungry. You should too.**

This mod was inspired by a humiliating and infuriating death that ended a zero-death solo run.  I was in my base cooking when all of a sudden - YOU DIED!  Why did I die?  All my food buffs had expired and I didn't notice, and I was getting smoked by the campfire as I cooked.  And this is to say nothing of many of us who have needlessly died because of entering battle with a critical food buff missing.

Some will say I should have noticed and it's on me - fair enough - but I say it's not realisic that you can be starving and not notice it.  Hence the design of this mod - it doesn't eat for you automatically - it just makes sure you notice when you're hungry.

## What This Mod Does

Adds escalating visual warnings when your food buffs approach expiry, and overlays for smoke and poison exposure. It does not eat for you. It does not automate anything. It respects your agency completely — it just makes sure *you know what your character knows.*

### Hunger

- **Screen vignette** — an amber glow creeps in from the screen edges as your hungriest food slot ticks down. Intensity and extent are configurable. The vignette oscillates to draw your eye: slow pulse at first, faster as urgency increases.
- **On-screen label** — "You are starting to feel hungry." → "You are getting hungry." → "You are hungry." The text color shifts from orange toward red as urgency rises.
- **HUD status icon** — a Hungry icon appears in the same row as Rested and Shelter, so you can spot hunger state at a glance without reading the label.

All hunger effects are off when you have 3 full food slots. Warnings begin when your hungriest slot crosses the configurable threshold (default 90 seconds remaining).

### Smoke

- **Center vignette** — a light grey haze appears from the center of the screen outward when you are standing in smoke. Fades in over 1 second and fades out over 2 seconds.
- **On-screen label** — "You can't breathe in the smoke!" The text oscillates from grey toward red.
- No extra HUD icon — the game already shows one.

### Poison *(coming soon — not yet implemented)*

- **Center vignette** — a green haze appears from the center of the screen outward when you are poisoned. Same fade behavior as smoke.
- **On-screen label** — "You are poisoned." The text oscillates from green toward red.
- No extra HUD icon — the game already shows one.

## Configuration

Open `BepInEx/config/DeathMonger.HungryViking.cfg` to adjust settings. Changing Vignette Intensity or Vignette Extent in any section triggers a 2-second preview so you can dial in the values without needing to be hungry, smoked, or poisoned.

**[General]**

| Setting | Default | Description |
|---|---|---|
| Enabled | `true` | Master toggle for all mod effects |

**[Hunger]**

| Setting | Default | Description |
|---|---|---|
| Show Status Icon | `true` | Show/hide the Hungry HUD icon |
| Hunger Threshold (seconds) | `90` | Seconds remaining when warnings begin |
| Vignette Intensity | `0.20` | Max edge opacity (0–1) |
| Vignette Extent | `0.50` | How far the vignette reaches toward center (0–1) |

**[Smoked]**

| Setting | Default | Description |
|---|---|---|
| Vignette Intensity | `0.25` | Max center opacity (0–1) |
| Vignette Extent | `0.55` | How far the vignette reaches from center outward (0–1) |

**[Poisoned]**

| Setting | Default | Description |
|---|---|---|
| Vignette Intensity | `0.25` | Max center opacity (0–1) |
| Vignette Extent | `0.55` | How far the vignette reaches from center outward (0–1) |

---

## Console Commands

Open the console with **F5**. Commands marked **(cheat)** require `devcommands` to be active first. Visual test toggles do not.

| Command | Cheat? | Description |
|---|---|---|
| `hv_testhunger` | No | Toggle hunger vignette on/off for visual testing |
| `hv_testsmoked` | No | Toggle smoke vignette on/off for visual testing |
| `hv_testpoisoned` | No | Toggle poison vignette on/off for visual testing |
| `hv_foodstatus` | Yes | Print name and remaining time for each food slot |
| `hv_drainfood <slot> [seconds]` | Yes | Subtract seconds from one slot (default 60s) |
| `hv_drainfoods [seconds]` | Yes | Subtract seconds from all slots (default 60s) |
| `hv_setfoodtime <slot> <seconds>` | Yes | Set a slot to exactly X seconds remaining |
| `hv_clearfood` | Yes | Instantly remove all food buffs |
| `hv_selist` | Yes | List all active status effects and their hashes |

Slot numbers are 1, 2, or 3.

**Example test flow:**
1. Eat 3 foods, then `hv_foodstatus` to confirm all slots are tracked.
2. `hv_setfoodtime 1 91` — slot 1 is 1 second above the threshold.
3. Wait ~2 seconds — amber vignette and label should fade in.
4. `hv_clearfood` — all slots empty, full-urgency vignette and "You are hungry." label appear immediately.

## Compatibility

- Client-side only — does not need to be installed on the server
- Compatible with food-adding mods (detects expiry on any food buff)

## Manual Installation

1. Install [BepInExPack Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/)
2. Extract `HungryViking.dll` into `Valheim/BepInEx/plugins/`
3. Launch the game — config generates on first run


## Support
Join [my discord server](https://discord.gg/2gnsrZSN) to ask questions or provide feedback.
