# Changelog

## 1.1.0 — unreleased

- **Install it on a dedicated server to log everyone's sailing.** The server writes one CSV, `BepInEx/CaptainsLog/shiplog-server-<timestamp>.csv`, with a row for every ship that has someone at the helm or its sail or oars engaged. Speed is the velocity the sailing player's game reports, not the server's estimate.
- New `Player` column (second, after `Timestamp`) in both the client and server logs. On the server it names the helmsman, or whoever's game is running the ship when nobody is steering.
- HUD shows ship speed in knots by default, with a `Speed Units` setting for m/s or both.
- The wind line says where the wind comes from relative to the ship (`Wind: 39% from starboard quarter`) instead of a bare `@ 24°`. `Show Wind Angle` turns that part off. Wind stays a percentage, because Valheim wind has no speed.
- HUD sits under the hotbar by default, including AzuExtendedPlayerInventory's quick slot row, instead of on top of it. New `Position` setting: `BelowHotbar`, `Centered` (beside the ship's rudder icon, like Ship Stats) or `TopLeft`. `Offset X/Y` became `Nudge X/Y`, relative to that position. The HUD now has a dark background (`Background Opacity`) and hides with the rest of the HUD.
- HUD lines are clearer: `Sail: Full, 73% efficient` appears only while the sail is up (`Oars: Forward/Reverse` while rowing), and `Rudder` appears only while the rudder is turned.
- HUD's first line names the ship (Karve, Longship, or a modded ship's name). `Show Ship Name` hides it.
- `Rudder` (HUD and CSV) is now the rudder's position, -1 to 1. It used to be that frame's steering input, which read 0 unless a key was held.
- No longer limited to `valheim.exe`, so it loads on Windows and Linux dedicated servers. Clients need nothing new.

## 1.0.0 — 2026-09-09

First public release, built for Valheim 1.0.7.

- Logs a CSV row while you are aboard a moving ship (default twice a second): timestamp, ship, position, biome, speed in m/s and knots, throttle, propulsion mode, rudder, heading, relative wind angle, sail efficiency, wind intensity and direction.
- One CSV per session in `BepInEx/CaptainsLog/`.
- Small HUD, visible only aboard a ship, with live speed, wind and sail efficiency.
- Sample interval, HUD offsets and on/off switches are configurable.
