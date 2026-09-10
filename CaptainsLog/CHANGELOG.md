# Changelog

## 1.0.0 — 2026-09-09

First public release, built for Valheim 1.0.7.

- Logs a CSV row while you are aboard a moving ship (default twice a second): timestamp, ship, position, biome, speed in m/s and knots, throttle, propulsion mode, rudder, heading, relative wind angle, sail efficiency, wind intensity and direction.
- One CSV per session in `BepInEx/CaptainsLog/`.
- Small HUD, visible only aboard a ship, with live speed, wind and sail efficiency.
- Sample interval, HUD offsets and on/off switches are configurable.
