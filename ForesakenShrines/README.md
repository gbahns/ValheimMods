# Forsaken Shrines

> **Alpha — Version 0.8.0**
> This mod has not been fully tested. Recipes, placement rules, and balance are subject to change. You may encounter bugs. Please report issues on the [GitHub page](https://github.com/gbahns/ValheimMods).

Build a permanent shrine to each Forsaken boss you have defeated. Interact with a shrine to channel that boss's power — no altar, no offering, no travel required.

The standard 20-minute guardian power cooldown still applies.

---

## Features

- Seven buildable shrines, one for each Forsaken boss (Eikthyr through Fader)
- Unlocked automatically when the corresponding boss is defeated — no relog required
- Placed with the Hammer under the **Forsaken Shrines** category; requires a nearby Stonecutter
- Shrines must be built on **natural terrain** and have **open sky** above them (no roof, no indoor placement)
- Visually match the vanilla boss stones found at the meadows starting area, scaled to each boss

---

## Shrines and Build Costs

Stone, Chain, and Iron are **returned** if the shrine is demolished. All trophies are **consumed** on build.

### Eikthyr — Meadows
*Unlocked by: defeating Eikthyr*

| Item | Amount |
|---|---|
| Stone | 20 |
| Chain | 2 |
| Iron | 2 |
| Bear Trophy | 2 |
| Deer Trophy | 5 |

---

### The Elder — Black Forest
*Unlocked by: defeating The Elder*

| Item | Amount |
|---|---|
| Stone | 30 |
| Chain | 2 |
| Iron | 2 |
| Brenna's Trophy | 1 |
| Troll Trophy | 4 |

*Brenna is the boss of the Smouldering Tomb (Hildir's Request dungeon).*

---

### Bonemass — Swamp
*Unlocked by: defeating Bonemass*

| Item | Amount |
|---|---|
| Stone | 40 |
| Chain | 3 |
| Iron | 2 |
| Wraith Trophy | 4 |
| Abomination Trophy | 4 |

---

### Moder — Mountain
*Unlocked by: defeating Moder*

| Item | Amount |
|---|---|
| Stone | 50 |
| Chain | 3 |
| Iron | 2 |
| Geirrhafa's Trophy | 1 |
| Fenring Trophy | 2 |

*Geirrhafa is the boss of the Whispering Caves (Hildir's Request dungeon).*

---

### Yagluth — Plains
*Unlocked by: defeating Yagluth*

| Item | Amount |
|---|---|
| Stone | 60 |
| Chain | 3 |
| Iron | 2 |
| Zil's Trophy | 1 |
| Thungr's Trophy | 1 |

*Zil and Thungr are the twin bosses of the Sealed Tower (Hildir's Request dungeon).*

---

### The Queen — Mistlands
*Unlocked by: defeating The Queen*

| Item | Amount |
|---|---|
| Stone | 70 |
| Chain | 5 |
| Iron | 2 |
| G'jall Trophy | 3 |
| Seeker Soldier Trophy | 5 |

---

### Fader — Ashlands
*Unlocked by: defeating Fader*

| Item | Amount |
|---|---|
| Stone | 60 |
| Chain | 5 |
| Iron | 2 |
| Fallen Valkyrie Trophy | 2 |
| Charred Warlock Trophy | 2 |

---

## Placement Rules

Shrines enforce two hard placement requirements:

**Natural terrain only** — no player-placed floor, platform, or foundation may be beneath the shrine. Shrines must be built directly on the ground.

**Open sky required** — the shrine cannot be placed indoors or under a roof. It must be exposed to the sky.

Both rules can be toggled off by a server admin in the configuration file.

Violating either condition blocks placement and shows a message explaining why.

---

## Configuration

All settings are server-synced — the server's values override clients.

The config file is created at `BepInEx/config/DeathMonger.ForesakenShrines.cfg` on first run.

**\[Placement\]**
- `RequireNaturalTerrain` (default: true) — require natural ground beneath the shrine
- `RequireSkyExposure` (default: true) — require open sky above the shrine

**\[Recipe.shrine_*\]** — one section per shrine, one entry per ingredient. Set any amount to 0 to remove that ingredient from the recipe.

---

## Requirements

- [BepInEx 5](https://valheim.thunderstore.io/package/denikson/BepInExPack_Valheim/)
- [Jotunn](https://valheim.thunderstore.io/package/ValheimModding/Jotunn/)

---

## Compatibility

- Multiplayer compatible — uses ServerSync
- Should be compatible with most other mods; no vanilla systems are permanently altered
- Shrines are build pieces; they persist through world saves and game restarts

---

## Notes

- You keep your boss trophies. Recipes use creature trophies and Hildir dungeon boss trophies — not the boss trophies you hang at the meadows altar.
- The Stonecutter must be within range when building. You do not need it nearby after the shrine is placed.
