# Captain's Log

Logs ship telemetry — speed, wind, sail efficiency, rudder, heading — to CSV while you sail, plus a live HUD. Built to gather empirical data on ship speed instead of relying on guessed/community-reported stats.

---

## Features

- Logs a CSV row every time the local player is aboard a moving ship (sample rate configurable, default 2/sec)
- Captures: timestamp, ship name, position, biome, forward speed (m/s and knots), throttle setting, propulsion mode (oars forward/reverse vs sail), rudder, heading, relative wind angle, sail wind-efficiency factor, wind intensity, absolute wind direction
- Small always-on HUD (visible only while aboard a ship) showing live speed, wind, and sail efficiency
- One CSV file per game session, written to `BepInEx/CaptainsLog/shiplog-<timestamp>.csv`

---

## Why these columns

Raw speed alone doesn't tell you much — a ship that's "fast" running downwind might be mediocre upwind. Logging **wind angle** and **sail efficiency** (`GetWindAngleFactor`, the game's own point-of-sail multiplier) alongside speed lets you separate "this ship is fast" from "this ship happened to have good wind" when comparing runs.

`WindAngleDeg` is signed and ranges -180° to +180°: **0° = tailwind** (wind at your stern, pushing you forward), **+/-180° = headwind** (wind on the nose). `WindAngleFactor` (0-1) is the game's own sail-efficiency curve for that angle — it bottoms out at 0 at +/-180°, sits around 0.7 at 0° (dead downwind), and peaks near a broad reach (roughly +/-90-100°).

`SpeedSetting` (`Stop`/`Back`/`Slow`/`Half`/`Full`) is the ship's five-step throttle. `Propulsion` translates that into what's actually moving the ship: `Idle`, `Oars-Reverse` (`Back`), `Oars-Forward` (`Slow`), or `Sail-Half`/`Sail-Full` (`Half`/`Full`). Note that the game's own `Ship.IsSailUp()` isn't an independent sail-raised sensor — it's literally defined as `SpeedSetting == Half || SpeedSetting == Full` — so we don't log it separately; `Propulsion`/`SpeedSetting` already carry that information.

---

## Configuration

The config file is created at `BepInEx/config/DeathMonger.CaptainsLog.cfg` on first run.

**\[Logging\]**
- `Enabled` (default: `true`) — write samples to CSV
- `Sample Interval (s)` (default: `0.5`) — minimum time between logged rows

**\[HUD\]**
- `Enabled` (default: `true`) — show the live speed/wind widget while aboard a ship
- `Offset X (px)` / `Offset Y (px)` — widget position, top-left origin

---

## Requirements

- [BepInEx 5](https://valheim.thunderstore.io/package/denikson/BepInExPack_Valheim/)
- [Jotunn](https://valheim.thunderstore.io/package/ValheimModding/Jotunn/)

---

## Compatibility

- Client-side only — does not need to be installed on the server
- Read-only — does not modify player state, world state, or any vanilla system
