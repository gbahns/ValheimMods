# Changelog

## 1.0.0 — 2026-09-22

First release. (The same build went out as 0.1.0 a few minutes earlier; 1.0.0 is the one to install.)

- Continue buttons at the top of the main menu for the most recent character + world pairs (three by default), and for servers joined, dedicated or a friend's game.
- A world button starts the world with the hosting options it was last started with.
- A server button rejoins with the remembered password, when one was typed and Remember Passwords is on. Passwords are stored encrypted with Windows DPAPI, readable only by the same account on the same computer; a rejected password is forgotten and asked for again.
- Right-click any Continue button for the full list of remembered games, newest first with how long ago each was played; click to start, or forget one. An optional More... button (off by default) opens the same list.
- The Continue buttons are yellow with a thin divider under them, both configurable, so they stand apart from the vanilla menu.
- The menu list is lifted so its last button stays on screen however many buttons mods add to it.
- Settings take effect the moment they are changed, from a mod manager's config editor or in game.
- The vanilla Start button asks before loading a character into a world that character has never been in.
