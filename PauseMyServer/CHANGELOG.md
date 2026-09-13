# Changelog

## 1.2.0 — 2026-09-13

- **Everyone in the menu pauses the game.** The lone-player rule is now the general one: the world pauses when every player online has the ESC menu open. Anyone closing their menu, or a new player arriving, resumes it. Alone, nothing changes.
- The red "Game Unpaused" label shows how many are in: "Game Unpaused (1 of 3 players paused)" (text configurable). Against a 1.1.0 server it falls back to the plain text.
- Compatible both ways with 1.1.0: older clients still pause when everyone is in the menu, they just do not see the counts.
- With the large map open (possible during an admin pause) the label moves into the strip between the map's edge and the screen edge instead of sitting on the map.

## 1.1.0 — 2026-09-12

- **Admin pause.** An admin presses the pause key (default: the keyboard's Pause key, configurable) to pause the whole server for everyone, even with other players online. Players who join meanwhile are frozen as soon as they spawn. It stays until any admin presses the key again. Non-admins who press it get a notice.
- The label reads "Game paused by <admin>" during an admin pause (configurable), and everyone gets a "Game resumed by <admin>" message when it is lifted.
- Safety net: when the last player leaves, an admin pause is dropped, so the next player to log in is not frozen with nobody online to resume.
- Console commands: `pms_pause` (toggle, same as the key; on the dedicated server's own console it toggles directly) and `pms_status`.
- A persistent "Game paused" label is shown on screen while the game is paused (in solo too). Text, position (top or bottom) and font size are configurable; the label can be turned off. The top-left "World paused" message is gone.
- When the ESC menu is up but the game keeps running because other players are online, the label reads "Game Unpaused" in bright red instead (configurable, can be turned off).

## 1.0.0 — 2026-09-11

First release. For Valheim 1.0. Install on the server and all clients.

- The ESC menu pauses the world when you are the only player online on a dedicated server: your client freezes like solo, and the server holds the world clock, raids and sleep time-skips.
- Closing the menu resumes instantly. Another player joining, or the pausing player logging out, resumes the world on its own.
- Server-authoritative: a client only freezes after the server confirms, so a server without the mod behaves like vanilla.
- Config: master toggle and a HUD message toggle (client side).
