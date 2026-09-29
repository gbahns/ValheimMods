# The Obituaries

Every death on the server, announced the way Quake announced a frag.

> **Greg** accepted the Draugr's shaft
> **Marco** was sliced in half by a Draugr
> **Anna** got her head bashed in by a Skeleton
> **Greg** was swatted by a 2-star Troll's log
> **Marco** was skewered by a Fuling
> **Anna** sank like a rock
> **Greg** does a back flip into the lava
> **Marco** was mauled by Fluffy the Wolf
> **Anna** was ax-murdered by **Greg**
> **Greg** tried to leave

The lines come from Quake, Quake II and Quake III (*cratered*, *sank like a rock*, *was railed by*, *accepted his shaft*, *should have used a smaller gun*, *became one with Shub-Niggurath*) and are mapped onto what killed you in Valheim: the creature and what it was holding, the fall, the water, the lava, the tree, the edge of the world. An archer draugr reads differently from an axe draugr, a spear fuling from a torch fuling, a troll with a log from one without. Bosses get their own lines. Starred creatures are named by their stars, tamed ones by their pet name. His or her comes from your character's body type.

Kill another player and you get Quake III's **You fragged Marco** across the middle of your screen.

## What you see

- **In the chat window**, in orange and a little bigger than chat, and the window stays up half a minute for it.
- **In the center of the screen**, for fifteen seconds, which is all of the ten you lie there before the respawn starts. Can be moved to the small top-left messages, or turned off.
- **In the BepInEx log**, plain text, for the record.

The dead player's name is yellow, the killer's red; the line, both names and all the timings are configurable.

## Installing

Client-side. Install it on every computer that should see the obituaries; nothing goes on the server. A player without the mod dies and kills as usual and simply sees no obituaries, and their deaths are not announced (their game is the only one that knows what killed them).

## Trying it without dying

The console (F5) has `obituary`:

- `obituary` previews a random death line, on your screen only.
- `obituary fall`, `obituary drowning`, `obituary troll`, `obituary pvp` preview one cause. Add a weapon to pick the wording: `obituary draugr bow`, `obituary troll log`, `obituary pvp club`. `obituary causes` lists everything.
- `obituary all` runs through every hit type at once.

Nothing from the command is sent to other players.

## When a creature reads wrong

The game does not say what a creature's weapon is; the mod reads it off the weapon's name and the damage it did. Each death writes one line to the BepInEx log with what the game said about the killing hit: the creature, the weapon it held, the damage types, and what the mod made of it. If a line is off, send that log line along with what it should have said.

## Configuration

`BepInEx/config/DeathMonger.TheObituaries.cfg`. Every setting takes effect immediately.

| Setting | Default | What it does |
| --- | --- | --- |
| Mod Enabled | true | Master switch: your deaths are announced and others' obituaries shown. |
| Log Obituaries | true | Write every obituary you see to the BepInEx log. |
| Log Killing Hit | true | Write what the game said about your killing hit to the BepInEx log. |
| Show In Chat | true | Add the obituary to the chat window. |
| Chat Seconds | 30 | How long the chat window stays up for it (the game's own messages get 10). |
| Text Size | 115 | Size of the line in the chat window, as a percentage of chat text. |
| On Screen | Center | Also show it as an on-screen message: Center, TopLeft or Off. |
| Center Seconds | 15 | How long a center message stays (the game's own fade out over 4). You lie there 10 seconds before the respawn starts; the rest is under the loading screen. |
| You Fragged | true | "You fragged <name>" in the center of your screen when you kill a player. |
| Pronouns | Auto | His/her from the character's body type, or He, She, They. |
| Level Stars | true | "a 2-star Troll" rather than "a Troll". |
| Tame Names | true | "Fluffy the Wolf" rather than "a Wolf". |
| Line Color | #ff8c1a | Color of the whole line. Empty for chat's normal white. |
| Victim Color | #ffe66d | Color of the dead player's name. |
| Killer Color | #ff5e5e | Color of the killer's name. |

## How it works

The game only tells the dying player's own client what hit it last, so that client composes the line and sends it to everyone over a routed RPC. Every client with the mod shows it; the server relays it without needing the mod, and a client without the mod drops it as an unknown message. Every hit you take is remembered for two minutes along with what dealt it, so a killer that despawned before you died is still named.

## Source

[github.com/gbahns/ValheimMods](https://github.com/gbahns/ValheimMods/tree/main/TheObituaries)
