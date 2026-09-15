# Dude, What Are My Stats?

**Early alpha.** Your stats on one key press, and a scoreboard of everyone else online.

- Press **I** anywhere to open the panel. No inventory, no menus, no clicking through a compendium.
- The **game pauses while you read**, exactly as far as the ESC menu would.
- The **Scoreboard** tab ranks every player online who also runs this mod. Click a column to sort by it.
- The **Details** tab breaks one player's stats into sections, with their skills and the creatures
  they have killed most.

Client-side only. Install it on your own game; the server needs nothing. Players without the mod
simply do not appear on the scoreboard.

## The scoreboard

| Column | What it is |
|---|---|
| Player | Your own row is gold and marked *(you)*. A player who logs out while you are still playing keeps their last known row, marked *(offline)*. |
| Kills | Every creature you have killed. |
| Deaths | Every death. |
| K/D | Kills per death. With no deaths yet this is simply your kill count, the way scoreboards usually show it. |
| Bosses | Boss kills. |
| Played | Active play time. Valheim only counts this while you are moving about, so it runs behind wall-clock time, and standing in a base AFK adds nothing. |
| Best skill | Your highest skill and its level. |

Click any row to open that player's Details. Click a column header to sort by it, and again to
flip the direction. The sort is remembered between sessions.

**The scoreboard only knows who has answered you.** Nothing is stored anywhere, so the board is
built fresh each time you load a world: it starts with just you and fills in as people reply. A
player who logs out mid-session stays on it until you leave the world, but someone who is offline
when you start, or who never plays at the same time as you, does not appear at all. Comparing
numbers works while you are both online.

## The details tab

Everything Valheim tracks for one character, which is over two hundred numbers, sorted into
sections you can fold away: Summary, Skills, Combat, Deaths, Exploration, Building, Crafting,
Gathering, Creatures, Forsaken powers, Other, and Creatures killed. Times read as `3h 12m`,
distances as `4.2 km`, everything else as a count. Stats still at zero are hidden unless you turn
on *Show Zero Stats*.

Left and Right arrows step through the players. Tab switches between the two views.

## Keys

| Key | What it does |
|---|---|
| I | Open or close the panel. Configurable. |
| Escape | Close the panel. |
| Tab | Switch between Scoreboard and Details. |
| Left / Right | Previous / next player, in Details. |

Drag the title to move the panel and the bottom-right corner to resize it. Both are remembered.

## About the pause

The pause goes through Valheim's own pause, the one the ESC menu uses, so the game decides whether
it takes:

- **Solo, or hosting with nobody else connected:** the game freezes while the panel is open.
- **On a server with other players online:** nothing freezes. Vanilla refuses to pause a shared
  world, which is what you want.
- **With [Pause My Server](https://thunderstore.io/c/valheim/p/DeathMonger/PauseMyServer/)
  installed:** that mod hooks the same two calls and may grant the pause anyway, under its own
  rules. This mod knows nothing about it and needs no special handling either way.

Turn the whole thing off with *Pause While Open* if you would rather the world kept turning.

## How other players' stats get here

Nobody's stats are stored on the server. When you open the panel it asks everyone online, and each
player's own game reads its own profile and answers straight back to you. Valheim forwards those
messages whether or not the server knows what they are, which is why no server install is needed.

While the panel is open it asks again every 10 seconds, or press **Refresh**. Set *Refresh Seconds*
to 0 to ask only when you open the panel and when you press the button.

*Ask Other Players* works both ways. Turn it off and you stop asking, and you also stop answering,
so you drop off everyone else's scoreboard as well as emptying your own. It is an opt-out, not a
quiet way to watch without being watched.

## Console commands

| Command | What it does |
|---|---|
| `dwams` | Open or close the panel. |
| `dwams_refresh` | Ask everyone online for their stats again. |
| `dwams_status` | Print the scoreboard to the console. |

## Configuration

`BepInEx/config/DeathMonger.DudeWhatAreMyStats.cfg`, or press F1 in game if you have a
configuration manager. Notable settings: the open key, *Pause While Open*, *Ask Other Players*,
*Refresh Seconds*, *Remember Offline Players*, *Show Zero Stats*, *Top Creature Count* and
*Fix Treasure Discovery Count*.

## A vanilla bug it fixes

Valheim counts a treasure chest as newly found every single time you open it. The chest tells its
owner it has been discovered over a message the game never registered a handler for, so the mark
that would keep it from counting twice is never written, and `Failed to find rpc method 327122920`
goes in the log instead. This mod registers the missing handler, so a chest counts once and the
treasure numbers on the panel mean the number of chests you have actually found.

It works while your own game owns the chest, which in a dungeon it usually does; a chest held by a
player without the mod still miscounts for them. Counts already inflated stay inflated, since
nothing here rewrites stats the game has already recorded. Turn it off with *Fix Treasure Discovery
Count* to leave the game exactly as it ships.

## Known limits

- Only players running this mod appear on the scoreboard, and only while they are online to answer.
  There is no stored history, so you cannot look up someone who is not playing when you are.
- Valheim keeps ten sets of stats per character, one per difficulty. This mod reads the raw
  lifetime set, so the numbers include every run regardless of difficulty, and cheated runs too.
- `Food Eaten` is bugged in vanilla and does not count up.
- The panel is not a controller UI. It expects a mouse and keyboard.

## Credits

Built by DeathMonger with Claude Code. The panel borrows the game's own frame, font and buttons,
so it needs no asset bundle and no Jotunn.
