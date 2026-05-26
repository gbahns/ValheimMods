# Distance From World Center

A small always-on HUD widget that shows your horizontal distance from the world center (X=0, Z=0) — useful for knowing how far you are from the world spawn / sacrificial stones, or for finding the biome rings (since Valheim's biomes are largely a function of distance from center).

---

## Features

- Always-on HUD widget in the upper-left of the screen
- Updates ~5 times per second
- Optional display of raw `(X, Z)` coordinates alongside the distance
- Position is configurable (pixel offset from the top-left corner)
- Toggle on/off without removing the mod

---

## Display

By default the widget shows:

```
World Center: 1437m
```

With **Show Coordinates** enabled it shows:

```
World Center: 1437m  (812, -1185)
```

Distance is the horizontal (X/Z) distance only — vertical altitude is ignored, so the value matches the in-game minimap scale.

---

## Configuration

The config file is created at `BepInEx/config/DeathMonger.DistanceFromWorldCenter.cfg` on first run.

**\[Distance HUD\]**
- `Enabled` (default: `true`) — show the widget at all
- `Show Coordinates` (default: `false`) — also include `(X, Z)` after the distance
- `Offset X (px)` (default: `10`) — horizontal offset from the upper-left corner
- `Offset Y (px)` (default: `10`) — vertical offset from the top of the screen (Y grows downward, like CSS)

Edits to the config take effect live — no game restart needed.

---

## Requirements

- [BepInEx 5](https://valheim.thunderstore.io/package/denikson/BepInExPack_Valheim/)
- [Jotunn](https://valheim.thunderstore.io/package/ValheimModding/Jotunn/)

---

## Compatibility

- Client-side only — does not need to be installed on the server
- Read-only — does not modify player state, world state, or any vanilla system
- Should be compatible with essentially any other mod

---

## Support

Join [my Discord server](https://discord.gg/eH7UfRj5mG) for questions, feedback, or bug reports.
