# Captain's Log

Logs ship telemetry — speed, wind, sail efficiency, rudder, heading — to CSV while you sail, plus a live HUD. Built to gather empirical data on ship speed instead of relying on guessed/community-reported stats.

---

## Features

- Logs a CSV row every time the local player is aboard a moving ship (sample rate configurable, default 2/sec)
- Captures: timestamp, player, ship name, position, biome, forward speed (m/s and knots), throttle setting, propulsion mode (oars forward/reverse vs sail), rudder, heading, relative wind angle, sail wind-efficiency factor, wind intensity, absolute wind direction
- Small HUD, visible only while aboard a ship: speed in knots (or m/s), wind strength and where it comes from, sail efficiency while the sail is up, and the rudder while it's turned
- The HUD sits under your hotbar, including a second row added by AzuExtendedPlayerInventory, or beside the ship's rudder icon like Azumatt's Ship Stats
- One CSV file per game session, written to `BepInEx/CaptainsLog/shiplog-<timestamp>.csv`
- A `Player` column on every row, so logs from different people can be merged
- Optional server install: a dedicated server logs **every** player's sailing into one file

---

## Server logging

Install the mod on a dedicated server and it writes `BepInEx/CaptainsLog/shiplog-server-<timestamp>.csv` for as long as the server runs. It logs every ship that has someone at the helm or its sail or oars engaged, at the configured sample interval per ship.

- `Player` is the helmsman. If nobody is steering (a sail left up), it's the player whose game is running the ship.
- Speed is exact, not estimated. The sailing player's game runs the ship's physics and sends its velocity to the server, and the server logs that velocity.
- Players don't need the mod for their sailing to be logged. The server log has no HUD, and the `[HUD]` settings are ignored there.
- A busy server writes about 2 rows a second per ship being sailed, roughly 0.7 MB an hour each.

---

## Why these columns

Raw speed alone doesn't tell you much — a ship that's "fast" running downwind might be mediocre upwind. Logging **wind angle** and **sail efficiency** (`GetWindAngleFactor`, the game's own point-of-sail multiplier) alongside speed lets you separate "this ship is fast" from "this ship happened to have good wind" when comparing runs.

`Rudder` is where the rudder sits, from -1 (full left) to 1 (full right), which is what actually steers the ship.

Valheim wind has no speed, only a strength from 0 to 1 (`WindIntensity`), which scales sail force from 25% to 100%. Sail force doesn't depend on how fast the ship is already going, so a ship can easily outpace any "wind speed" you might derive from it, and the HUD shows wind as a percentage for that reason. (Azumatt's Ship Stats converts it to knots by treating full strength as 10 m/s, which is only a convention.)

On the HUD, `from ...` is where the wind comes from relative to the hull, not a compass direction: `astern`, a `quarter` (within 67.5° of astern), the `beam`, the `bow`, or `ahead`, on the `port` or `starboard` side. The `efficient` figure on the Sail line is `WindAngleFactor` (below), which depends only on that angle.

`WindAngleDeg` is signed and ranges -180° to +180°: **0° = tailwind** (wind at your stern, pushing you forward), **+/-180° = headwind** (wind on the nose). `WindAngleFactor` (0-1) is the game's own sail-efficiency curve for that angle (`Ship.GetWindAngleFactor`): 0.7 with the wind dead astern, rising to 1.0 with the wind exactly on the beam (+/-90°), falling back to about 0.78 at +/-139°, then cut to 0 by +/-143°. Within about 37° of dead ahead the sail gives no push at all.

`SpeedSetting` (`Stop`/`Back`/`Slow`/`Half`/`Full`) is the ship's five-step throttle. `Propulsion` translates that into what's actually moving the ship: `Idle`, `Oars-Reverse` (`Back`), `Oars-Forward` (`Slow`), or `Sail-Half`/`Sail-Full` (`Half`/`Full`). Note that the game's own `Ship.IsSailUp()` isn't an independent sail-raised sensor — it's literally defined as `SpeedSetting == Half || SpeedSetting == Full` — so we don't log it separately; `Propulsion`/`SpeedSetting` already carry that information.

---

## Configuration

The config file is created at `BepInEx/config/DeathMonger.CaptainsLog.cfg` on first run.

**\[Logging\]**
- `Enabled` (default: `true`) — write samples to CSV
- `Sample Interval (s)` (default: `0.5`) — minimum time between logged rows (per ship, on a server)

**\[HUD\]**
- `Enabled` (default: `true`) — show the live speed/wind widget while aboard a ship
- `Position` (default: `BelowHotbar`) — `BelowHotbar` sits under the lowest hotbar row, including a second row from a mod like AzuExtendedPlayerInventory; `Centered` sits beside the ship's rudder icon and follows the ship; `TopLeft` is a fixed spot in the corner
- `Nudge X (px)` / `Nudge Y (px)` (default: `0`) — move the widget from that position (positive = right / down)
- `Background Opacity` (default: `0.5`) — the dark panel behind the text; `0` hides it
- `Speed Units` (default: `Knots`) — `Knots`, `MetersPerSecond`, or `KnotsWithMetersPerSecond` (m/s in parentheses); ship speed only
- `Show Wind Angle` (default: `true`) — add where the wind comes from relative to the ship, e.g. `Wind: 39% from starboard quarter`
- `Show Ship Name` (default: `true`) — a first line naming the kind of ship you're aboard (ships have no individual names in Valheim)

---

## Requirements

- [BepInEx 5](https://valheim.thunderstore.io/package/denikson/BepInExPack_Valheim/)
- [Jotunn](https://valheim.thunderstore.io/package/ValheimModding/Jotunn/)

---

## Compatibility

- Client-side by default. Installing it on the server is optional and only adds the server log; clients don't need it to join
- Read-only — does not modify player state, world state, or any vanilla system
