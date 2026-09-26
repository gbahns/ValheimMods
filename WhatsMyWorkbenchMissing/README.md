# What's My Workbench Missing?

Which upgrades is this workbench missing? Hover its level number and see.

Open any crafting station and rest the mouse on the level badge next to its name. The tooltip lists the station's upgrades you have not built yet, split into the ones you could put down right now with what you are carrying and the ones you know but are short of materials for. Each shows what it costs; an amount you are short of is red and says how many you have, as in `4/10 Flint`.

> **Workbench level 2**
>
> Ready to build:
>   Tanning rack   10 Wood, 15 Flint, 20 Leather scraps, 5 Deer hide
>
> Missing materials:
>   Adze   10 Fine wood, 3 Bronze
>
> 1 more upgrade not yet discovered.

Upgrades you have not discovered yet are counted, not named, so the tooltip does not spoil what a new material unlocks. Turn the count off if you would rather not know it is there.

The upgrades come from the build tools' own piece tables, so it works for every station that has extensions, including ones other mods add, and whether an upgrade counts as built is decided exactly the way the game decides the station's level.

## Installing

Client-side. Install it on the computers that should see the tooltip; nothing goes on the server.

## Configuration

`BepInEx/config/DeathMonger.WhatsMyWorkbenchMissing.cfg`. Every setting takes effect immediately.

| Setting | Default | What it does |
| --- | --- | --- |
| Mod Enabled | true | Master switch. Off means the level number shows no tooltip. |
| Show Materials | true | List each upgrade's materials after its name, with shortfalls in red. |
| Show Undiscovered Count | true | Add a line counting the upgrades you have not discovered yet, without naming them. |
| Show Built | false | Also list the upgrades already attached to the station, dimmed. |

## Notes

- The tooltip is a mouse tooltip, like the ones on the recipe list. It does not show for gamepad navigation.
- An upgrade counts as built when it is attached to this station: close enough and connected, as the game's level count sees it. A tanning rack that is too far away is listed as missing, which is exactly when you want to know.

## Source

https://github.com/gbahns/ValheimMods/tree/main/WhatsMyWorkbenchMissing
