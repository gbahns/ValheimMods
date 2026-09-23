# Changelog

## 1.0.2 — 2026-09-22

- A server's name is also taken from the favorites and recent-servers list, where the game keeps the name it saw when it queried the server, and a server remembered without a name picks one up as soon as the game learns it. An address standing in for a name is not taken as one. A server on this computer reads "localhost" (Local Server Label can prefer its name, when the game can learn one; a server started with -public 0 never gives it out). A server joined by address is queried directly for its name. Two different servers with the same name show their address after it.

## 1.0.1 — 2026-09-22

- The divider under the Continue buttons is now a copy of the orange ornament the menu draws above itself, the same width and style, flipped to face the vanilla menu, instead of a thin yellow line.

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
