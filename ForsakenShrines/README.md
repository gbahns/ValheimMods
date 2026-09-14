# Forsaken Shrines

> **Alpha — Version 0.8.3**
> This should be considered an early release version of this mod.  It seems to be working but you might encounter issues.  Recipes, placement rules, and balance are subject to change.  I wanted to make it available for people to use and also to get feedback.  If you're using it without issue let me know; that feedback will help me confirm it's working and promote it to v1.0.0. Join [my discord server](https://discord.gg/eH7UfRj5mG) to ask questions, provide feedback, or report issues.

Build a shrine to each Forsaken boss you have defeated, then mount that boss's trophy on it to channel the guardian power remotely — no return trip to the world-center altar required for activation.

Each shrine is a remote access point that channels the Forsaken Power through the earth from the original sacrificial stones. The boss's trophy must be mounted in **both** places for the channel to work.

---
## Features
- Seven buildable shrines, one for each Forsaken boss (Eikthyr through Fader)
- Each shrine is unlocked when the corresponding boss is defeated
- Placed with the Hammer, in the same build tab as the vanilla stone (Stonecutter) pieces; requires a nearby Stonecutter
- Shrines must be built on **natural terrain** and have **open sky** above them (no roof, no indoor placement)

---

## Shrines and Build Costs

Stone, Chain, and Iron are **returned** if the shrine is demolished. The trophies listed below are **consumed** at build time. The main boss trophy is **not** in the build recipe — it is consumed separately when you mount it on the placed shrine.

| Shrine | Stone | Chain | Iron | Trophy 1 | Trophy 2 |
|---|---|---|---|---|---|
| Eikthyr | 20 | 2 | 2 | 2 Bear | 5 Deer |
| Elder | 30 | 2 | 2 | 1 Brenna | 4 Troll |
| Bonemass | 40 | 3 | 2 | 2 Wraith | 4 Abomination |
| Moder | 50 | 3 | 2 | 1 Geirrhafa | 2 Fenring |
| Yagluth | 60 | 3 | 2 | 1 Zil | 1 Thungr |
| Queen | 70 | 5 | 2 | 3 G'jall | 5 Seeker Soldier |
| Fader | 60 | 5 | 2 | 2 Fallen Valkyrie | 2 Charred Warlock |

*Brenna, Geirrhafa, Zil, and Thungr are mini-bosses from Hildir's Request dungeons (Smouldering Tomb, Whispering Caves, and Sealed Tower respectively).*

---

## Placement Rules

Shrines enforce two hard placement requirements:

**Natural terrain only** — no player-placed floor, platform, or foundation may be beneath the shrine. Shrines must be built directly on the ground.

**Open sky required** — the shrine cannot be placed indoors or under a roof. It must be exposed to the sky.

Both rules can be toggled off by a server admin in the configuration file.

Violating either condition blocks placement and shows a message explaining why.

---

## Configuration

All settings except `Mod Enabled` are server-synced — the server's values override clients, as
long as the mod is installed on the server.

The config file is created at `BepInEx/config/DeathMonger.ForsakenShrines.cfg` on first run.

**\[General\]**
- `Mod Enabled` (default: true) — master switch. Set to false to turn the mod off without removing the DLL: no shrine pieces, no placement rules, no Harmony patches. Not server-synced. Takes effect on the next game launch; shrines already placed in the world are hidden until you re-enable it.

**\[Placement\]**
- `RequireNaturalTerrain` (default: true) — require natural ground beneath the shrine
- `RequireSkyExposure` (default: true) — require open sky above the shrine

**\[Recipe.shrine_*\]** — one section per shrine, one entry per ingredient. Set any amount to 0 to remove that ingredient from the recipe.

---

## Requirements

- [BepInEx 5](https://valheim.thunderstore.io/package/denikson/BepInExPack_Valheim/)
- [Jotunn](https://valheim.thunderstore.io/package/ValheimModding/Jotunn/)

## Installing on a dedicated server

**Install on the server as well as on every client.** The shrines are new prefabs, and a server
that does not have them deletes the ones it cannot recognize — a shrine built within simulation
range of the world center will vanish for good. The server is also what makes the settings below
authoritative; without it, every client just runs its own.

Single-player and a player-hosted world need nothing extra.

---

## Compatibility

- Multiplayer compatible — uses ServerSync; install on the server and on every client
- Should be compatible with most other mods; no vanilla systems are permanently altered
- Shrines are build pieces; they persist through world saves and game restarts
- Built against Valheim 1.0.7 and Jotunn 2.30.0. Jotunn is used only to look up and clone vanilla prefabs; the mod does not use Jotunn's PieceManager, so it also runs on Jotunn 2.29.2

---

## Notes

- The Stonecutter must be within range when building. You do not need it nearby after the shrine is placed.

---

## Troubleshooting

**You want the mod out of the picture quickly:** set `Mod Enabled = false` in `BepInEx/config/DeathMonger.ForsakenShrines.cfg` and restart the game. Placed shrines stay in the world save and reappear when you re-enable it.

**Camera spins on login and your character is stuck in the wake-up pose:** on Valheim 1.0 with Jotunn 2.29.2 or older, this happens when any installed mod registers build pieces through Jotunn's PieceManager. Forsaken Shrines 0.8.1 did this; 0.8.2 no longer does. If it still happens with 0.8.2, another mod in your list is the trigger, and updating Jotunn to 2.30.0 or newer fixes it.

---

## Console Commands

For testing or admin use. Requires `devcommands` to be active. Both commands aggregate items across all seven shrines so each item type is granted in a single batch:

- **`ShrineSpawnAll`** — adds every material needed to build all seven shrines (Stone, Chain, Iron, plus all the creature and sub-boss trophies in the recipes).
- **`ShrineSpawnTrophies`** — adds only the trophy items needed across all shrines (skips Stone, Chain, Iron). Useful when you already have the bulk materials but need the rare drops.

Neither command grants the boss's *main* trophy — that's a separate post-build mount step (see above), and you have to earn it by killing the boss.
