# Pause My Server

The ESC menu pauses a server-hosted Valheim game the way it pauses a solo game.

Vanilla only pauses when you host the world yourself and nobody else is connected. On a dedicated server the menu never pauses anything: the day rolls on, raids fire, your smelter finishes and the boar you were fighting keeps chewing. This mod fixes that for the most common case first.

## Version 1.0: pause when you are alone

When you are the **only player online** on a dedicated server, opening the ESC menu pauses the world, exactly like solo:

- Your client freezes (monsters, ships, physics, crafting, everything you see).
- The server freezes its world clock, so day and night, weather, plant growth, fermenters, beehives and respawn timers stop too.
- Raids and other random events are held.
- A sleep time-skip in progress waits.

Closing the menu resumes the world instantly. If a second player joins while you are paused, the world resumes on its own (you will see a small message) and stays running until you are alone again and reopen the menu. If you log out while paused, the server resumes.

Nothing changes when two or more players are online. Vanilla behaviour is kept for hosted (non-dedicated) games, where the host already pauses when alone.

## Planned for 1.1

An admin will be able to pause the server for everyone, even with other players online.

## Installation

Install on the **server and on every client** (Gale, r2modman or Thunderstore Mod Manager, or drop `PauseMyServer.dll` into `BepInEx/plugins`).

- Server without the mod: clients simply never pause, same as vanilla. Nothing breaks.
- Client without the mod: that player cannot pause; other clients still can when alone.

Works on Windows and Linux dedicated servers. Requires BepInExPack for Valheim.

## Configuration

`BepInEx/config/DeathMonger.PauseMyServer.cfg` (client side):

| Setting | Default | Meaning |
|---|---|---|
| Mod Enabled | true | Master toggle; disables every patch without removing the DLL. Restart required. |
| Show Messages | true | Top-left HUD message when the world pauses, and when it resumes because another player came online while your menu was still open. |

The server needs no configuration.

## How it works

The server is the single authority. A client only ever tells the server "my menu is open" or "my menu is closed" (one routed RPC when that changes). Every frame the server checks whether exactly one player is connected and wants the pause, and broadcasts the state to everybody when it changes. A client freezes only after the server confirms, so a server without the mod cannot leave a client frozen while the world runs on without it.

Client side, three tiny Harmony patches: postfixes on `Game.Pause` and `Game.Unpause` track the menu, and a postfix on `Game.IsPaused` reports "paused" once the server has confirmed and the menu is still open. Vanilla's own `Game.UpdatePause` then sets the time scale to zero, the same code path solo uses. Closing the menu resumes locally at once, without waiting for the round trip.

Server side, three prefixes hold the parts of the world the server itself runs while its lone client is frozen: `ZNet.UpdateNetTime` (the world clock), `RandEventSystem.FixedUpdate` (raids) and `EnvMan.UpdateTimeSkip` (sleeping). The server's own frame time is deliberately left running: at time scale zero the server would stop sending player lists and world data, and a joining player could never load in. Everything near a player is owned and simulated by that player's client, so freezing the client freezes the rest.

While frozen, the client still answers the server's keep-alive pings (Valheim's receive loop does not depend on frame time), so nobody gets disconnected.

## Compatibility

- Does not touch `Game.CanPause`, so mods that let you pause in debug mode still work.
- Mods that change `Time.timeScale` themselves (speed-up or slow-motion mods) may fight with the freeze while the menu is open.
- Autosave keeps running on the server while paused; that is harmless.

## Source

[github.com/gbahns/ValheimMods](https://github.com/gbahns/ValheimMods)
