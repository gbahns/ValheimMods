# The Obituaries

Every death on the server, announced the way Quake announced a frag.

> **Greg** was railed by a Deathsquito
> **Marco** sank like a rock
> **Anna** does a back flip into the lava
> **Greg** was smashed by a 2-star Troll
> **Marco** was mauled by Fluffy the Wolf
> **Anna** was ax-murdered by **Greg**
> **Greg** tried to leave
> **Marco** joins the Draugr

The lines come from Quake, Quake II and Quake III (*cratered*, *sank like a rock*, *was railed by*, *tried to invade personal space*, *should have used a smaller gun*, *became one with Shub-Niggurath*) and are mapped onto what killed you in Valheim: the creature, the weapon, the fall, the water, the lava, the tree, the edge of the world. Bosses get their own lines. Starred creatures are named by their stars, tamed ones by their pet name.

Kill another player and you get Quake III's **You fragged Marco** across the middle of your screen.

## What you see

- **In the chat window**, like a chat line. The window pops up for it the way it does for a message.
- **Top-left**, as one of the small messages next to the minimap. Can be moved to the center of the screen, or turned off.
- **In the BepInEx log**, plain text, for the record.

The dead player's name and the killer's name are colored (orange and red by default; both configurable).

## Installing

Client-side. Install it on every computer that should see the obituaries; nothing goes on the server. A player without the mod dies and kills as usual and simply sees no obituaries, and their deaths are not announced (their game is the only one that knows what killed them).

## Trying it without dying

The console (F5) has `obituary`:

- `obituary` previews a random death line, on your screen only.
- `obituary fall`, `obituary drowning`, `obituary troll`, `obituary deathsquito`, `obituary pvp` preview one cause. `obituary causes` lists them.
- `obituary all` runs through every hit type at once.

Nothing from the command is sent to other players.

## Configuration

`BepInEx/config/DeathMonger.TheObituaries.cfg`. Every setting takes effect immediately.

| Setting | Default | What it does |
| --- | --- | --- |
| Mod Enabled | true | Master switch: your deaths are announced and others' obituaries shown. |
| Log Obituaries | true | Write every obituary you see to the BepInEx log. |
| Show In Chat | true | Add the obituary to the chat window. |
| On Screen | TopLeft | Also show it as an on-screen message: TopLeft, Center or Off. |
| You Fragged | true | "You fragged <name>" in the center of your screen when you kill a player. |
| Victim Color | #ffa640 | Color of the dead player's name. Empty for none. |
| Killer Color | #ff5e5e | Color of the killer's name. Empty for none. |
| Level Stars | true | "a 2-star Troll" rather than "a Troll". |
| Tame Names | true | "Fluffy the Wolf" rather than "a Wolf". |

## How it works

The game only tells the dying player's own client what hit it last, so that client composes the line and sends it to everyone over a routed RPC. Every client with the mod shows it; the server relays it without needing the mod, and a client without the mod drops it as an unknown message.

## Source

[github.com/gbahns/ValheimMods](https://github.com/gbahns/ValheimMods/tree/main/TheObituaries)
