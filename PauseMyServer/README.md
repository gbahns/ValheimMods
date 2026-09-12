# Pause My Server

Pause a server-hosted Valheim game.

Vanilla only pauses when you host the world yourself and nobody else is connected. On a dedicated server the menu never pauses anything: the day rolls on, raids fire, your smelter finishes and the boar you were fighting keeps chewing. This mod gives you two ways to pause a server.

## Pause when you are alone

When you are the **only player online** on a dedicated server, opening the ESC menu pauses the world, exactly like solo:

- Your client freezes (monsters, ships, physics, crafting, everything you see).
- The server freezes its world clock, so day and night, weather, plant growth, fermenters, beehives and respawn timers stop too.
- Raids and other random events are held.
- A sleep time-skip in progress waits.

Closing the menu resumes the world instantly. If a second player joins while you are paused, the world resumes on its own (you will see a small message) and stays running until you are alone again and reopen the menu. If you log out while paused, the server resumes.

Nothing changes when two or more players are online, unless an admin pauses. Vanilla behaviour is kept for hosted (non-dedicated) games, where the host already pauses when alone.

## Admin pause

An **admin** (listed in the server's adminlist) presses the pause key, by default the keyboard's **Pause** key, to pause the whole server for everyone, even with other players online:

- Every client freezes, whether or not their menu is open.
- Players who join during the pause are frozen as soon as they spawn.
- The pause stays until **any** admin presses the key again. A non-admin who presses it gets a notice.
- The label reads "Game paused by <admin>", and everyone gets a "Game resumed by <admin>" message afterwards.
- Safety net: when the last player leaves, an admin pause is dropped, so the next player to log in is not frozen with nobody online to resume.

Console commands (F5): `pms_pause` toggles the admin pause like the key, and `pms_status` prints the state. On the dedicated server's own console `pms_pause` toggles directly, which is the way out if no admin is online.

The admin pause is designed for dedicated servers. On a hosted (non-dedicated) game the host freezes with everyone else, and a player joining during the pause may have to wait until it is lifted.

## The "Game paused" label

A persistent label is shown on screen while the game is paused, in solo games too. Text, position (top or bottom) and size are configurable, and it can be turned off.

When the ESC menu is up but the game keeps running because other players are online, the label instead reads **"Game Unpaused"** in bright red, so the menu never looks like a pause it is not.

## Installation

Install on the **server and on every client** (Gale, r2modman or Thunderstore Mod Manager, or drop `PauseMyServer.dll` into `BepInEx/plugins`). Keep the same version everywhere.

- Server without the mod: clients simply never pause, same as vanilla. Nothing breaks.
- Client without the mod: that player cannot pause and is not frozen by an admin pause; everyone else is.

Works on Windows and Linux dedicated servers. Requires BepInExPack for Valheim.

## Configuration

`BepInEx/config/DeathMonger.PauseMyServer.cfg` (client side):

| Section | Setting | Default | Meaning |
|---|---|---|---|
| General | Mod Enabled | true | Master toggle; disables every patch without removing the DLL. Restart required. |
| General | Show Messages | true | Top-left HUD message when the game resumes for a reason other than you closing your menu. |
| Admin | Pause Key | Pause | Admins only: toggle the server-wide pause. Ignored while typing in chat, the console or a text box. |
| Pause Message | Show Pause Message | true | Show the persistent on-screen label while paused. |
| Pause Message | Text | Game paused | The label text. |
| Pause Message | Admin Text | Game paused by {0} | The label text during an admin pause; {0} is the admin's name. |
| Pause Message | Position | Bottom | Centred at the Top or the Bottom of the screen. |
| Pause Message | Font Size | 40 | Label size at a 1920x1080 reference; scales with the screen. |
| Pause Message | Show Unpaused Warning | true | Red label while the menu is up but the game runs on because others are online. |
| Pause Message | Unpaused Text | Game Unpaused | The text of that warning. |

Label settings apply the next time the game is paused, no restart needed. The server needs no configuration.

## How it works

The server is the single authority. Clients only ever send wishes: "my menu is open", "my menu is closed", "toggle the admin pause". Every frame the server works out whether the world should be paused, either because exactly one player is connected and wants it or because an admin set the pause, and broadcasts the state to everybody when it changes. A client freezes only after the server confirms, so a server without the mod cannot leave a client frozen while the world runs on without it. A player who spawns asks for the current state, which is how a joiner during an admin pause is frozen too.

Client side, a handful of small Harmony patches: postfixes on `Game.Pause` and `Game.Unpause` track the menu, a postfix on `Game.IsPaused` reports "paused" once the server has confirmed and either the menu is still open or the pause is an admin pause, and a postfix on `Player.OnSpawned` requests the state. Vanilla's own `Game.UpdatePause` then sets the time scale to zero, the same code path solo uses. Closing the menu during a lone pause resumes locally at once, without waiting for the round trip.

Server side, three prefixes hold the parts of the world the server itself runs while its clients are frozen: `ZNet.UpdateNetTime` (the world clock), `RandEventSystem.FixedUpdate` (raids) and `EnvMan.UpdateTimeSkip` (sleeping). The server's own frame time is deliberately left running: at time scale zero the server would stop sending player lists and world data, and a joining player could never load in. Everything near a player is owned and simulated by that player's client, so freezing the clients freezes the rest.

While frozen, a client still answers the server's keep-alive pings (Valheim's receive loop does not depend on frame time), so nobody gets disconnected.

The "Game paused" label is drawn on the mod's own overlay canvas, above the menu, using the font of the game's own HUD messages. It shows whenever the game reports itself paused, which includes a solo host pausing.

## Compatibility

- Does not touch `Game.CanPause`, so mods that let you pause in debug mode still work.
- Mods that change `Time.timeScale` themselves (speed-up or slow-motion mods) may fight with the freeze.
- Autosave keeps running on the server while paused; that is harmless.

## Source

[github.com/gbahns/ValheimMods](https://github.com/gbahns/ValheimMods)
