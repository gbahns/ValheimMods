# Changelog

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
