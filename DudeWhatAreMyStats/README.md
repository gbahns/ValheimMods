# Dude, What Are My Stats?

**Early alpha.** Your stats on one key press, and a scoreboard of everyone you play with.

- Press **I** anywhere to open the panel. No inventory, no menus, no clicking through a compendium.
- The **game pauses while you read**, exactly as far as the ESC menu would.
- The **Scoreboard** tab ranks everyone who runs this mod, online now or not. Click a column to sort by it.
- The **Details** tab breaks one player's stats into sections, with their skills and the creatures
  they have killed most.
- It **records what kills you** -- which creature, what it was holding, and everyone else who was
  in the fight. Valheim never writes that down.
- Optionally, a small **always-on player list** with everyone's death count.

Install it on your own game and the server needs nothing: everyone online who also runs the mod
shows up. Install it on the server as well and the board also remembers players who are **not**
online, each marked with how long ago they were last seen. Players without the mod never appear.

## The scoreboard

| Column | What it is |
|---|---|
| Player | Your own row is gold and marked *(you)*. Anyone not online is marked with how long ago they were last seen, such as *(2d ago)*. |
| Kills | Every creature you have killed. |
| Deaths | Every death. |
| K/D | Kills per death. With no deaths yet this is simply your kill count, the way scoreboards usually show it. |
| Bosses | Boss kills. |
| Played | Active play time. Valheim only counts this while you are moving about, so it runs behind wall-clock time, and standing in a base AFK adds nothing. |
| Nemesis | The creature that has killed you most, and how often. Empty until the mod has watched a death: see below. |
| Best skill | Your highest skill and its level. |

Click any row to open that player's Details. Click a column header to sort by it, and again to
flip the direction. The sort is remembered between sessions.

**Offline rows say how old they are.** A player who is online answers for themselves, so their
numbers are current. Anyone else is shown from the server's last record of them, marked *(2d ago)*
or similar, so nobody argues over a number that turns out to be a fortnight stale. If the server
does not run the mod there is nowhere to keep those records, and the board shows only the players
online to answer.

## What killed you

Valheim counts your deaths and sorts them by category -- enemy hit, fall, drowning -- but it never
records **which** creature. So this watches your fights as they happen, and when you lose one it
writes down what happened.

The **Nemesis** column and the **Killed by** table in Details are the short version: a running tally
of what has killed this character, most often first. Falls, drowning, lava and the edge of the world
are in the same table, so it accounts for every death it saw. It starts empty, since there is no old
record to read: deaths from before this was installed show as *Not recorded* rather than leaving the
table quietly short.

The long version is the last few deaths in full, printed by `dwams_deaths`:

```
just now - 1-star Troll (Troll club), with 12 others in the fight (day 84, Black Forest)
  blow: 94 damage (blunt 94), EnemyHit, unblockable
  you: 125 max health, 41 armor, holding Iron sword, Rested + Wet
  fight: 38s, 9 hit(s) taken, worst 94, 61 stamina blocking
  killed you: 1-star Troll: 94 damage, 1 hit(s)
  Greydwarf: 22 damage, 2 hit(s), 4 blocked, 31 stamina
  Greydwarf Shaman: 14 damage (inferred), 1 hit(s)
  Greydwarf: 6 blocked, 18 stamina
  Greydwarf: on you 22s
```

**Everything in the fight is in there, not just the killer.** Ten greydwarves that surround you and
drain your stamina are part of why the troll got you, so each is listed with what it actually did:
damage dealt, blows you blocked and what they cost in stamina, blows you dodged, and how long it had
you in its sights. One that never landed a blow still appears, marked as having been there. The
**In the fight** table in Details counts those over time, so you can see which creature keeps turning
up at your deaths without ever being the one to finish you.

A blow you blocked to nothing credits the creature with the swing and with none of the damage, which
is the distinction the whole record rests on. Poison and burning ticks are a genuine gap: the game
deals them from the effect rather than the creature and attaches no attacker at all, so they are
credited to whoever last dealt that kind of damage and marked *(inferred)*. That is what puts a death
by Blob poison down to the Blob, several seconds after the Blob itself is dead.

**The reports stay on your machine.** The tallies are small and travel with the rest of your stats,
which is what fills in the Nemesis column for everyone else. A full report is far too big to hand
around, so `dwams_deaths` shows your own. Settings live under `[Deaths]`: *Keep Reports* is how many
to keep, *Record Bystanders* turns off the part that looks around for creatures that never hit you,
*Bystander Seconds* is how long something must have you in its sights to count, *Fight Gap Seconds*
is how long the quiet has to last before the next fight starts fresh, and *Log Deaths* copies each
report into the BepInEx log. *Record Deaths* turns the whole thing off.

Nothing goes on the server for this. Only your own game can see what hit you, so only your own game
could record it.

## The details tab

Everything Valheim tracks for one character, which is over two hundred numbers, sorted into
sections you can fold away: Summary, Skills, Combat, Deaths, Exploration, Building, Crafting,
Gathering, Creatures, Forsaken powers, Other, and Creatures killed. Times read as `3h 12m`,
distances as `4.2 km`, everything else as a count. Stats still at zero are hidden unless you turn
on *Show Zero Stats*.

Left and Right arrows step through the players. Tab switches between the two views.

## The player list

A small list in the corner of the screen showing each player and how many times they have died,
most deaths first. Your own row is gold. It is **off by default**; turn it on with
*Show Player List*, the console command `dwams_hud`, or a key you choose under *Toggle Key*.

Once on, it stays up for the whole of play, over the map and the inventory too. It steps aside only
where it would otherwise draw on top of something it shouldn't: when the game hides its own HUD
(Ctrl+F3), during cutscenes, behind the pause menu and the black fade on death or teleport, and
while the stats panel is open, which shows the same numbers and more. It never takes the mouse, so
nothing underneath it stops being clickable.

A player who has just died stays on the list while they respawn. Who counts as online comes from
the game's own list of connected players, so the respawn, when a player's game briefly can't answer,
doesn't make them vanish at the moment their count goes up. One side effect: two characters with
the same name show as online together whenever either one is.

By default it lists only the players online now, which is what an always-visible list is usually
for; *Include Offline Players* adds the ones the server remembers. It asks the others for their
numbers every 30 seconds while the stats panel is closed, since deaths change rarely. Your own
count is always current.

It starts on the left, below the hotbar. If it lands on top of something in your setup, move it
with *Corner* and *Offset*: the offset is how far in from that corner, in pixels at 1080p, both
numbers counted inward whichever corner you pick. *Font Size* and *Max Rows* do what they say.

The toggle key is unbound by default because nearly every letter is already taken by the game or
another mod. Pick one that is free in your own setup.

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

**Players who are online answer for themselves.** When you open the panel it asks everyone, and
each player's own game reads its own profile and replies straight back to you. Valheim forwards
those messages whether or not the server knows what they are, which is why this half needs no
server install at all.

While the panel is open it asks again every 10 seconds, or press **Refresh**. Set *Refresh Seconds*
to 0 to ask only when you open the panel and when you press the button.

**Players who are not online are remembered by the server.** Your game hands the server your own
stats every few minutes, when you open the panel, and as you leave the world. The server keeps the
newest record per character and hands the lot to anyone who asks, which is what puts people on the
board who were not playing when you were. A live answer always wins over the stored copy, so
someone standing next to you is never shown from an old record.

This half needs the mod on the server, which is the only thing it is needed for. Without it,
nothing breaks: the request goes unanswered and you see the players who are online. Run
`dwams_status` in the console to see whether your server is answering.

Server settings live in the same config file under `[Server]` and are ignored on a client.
*Server Store Enabled* turns the record keeping off entirely. *Server Keep Days* forgets a
character nobody has seen in that long, so a public world does not collect one-time visitors
forever. The file sits in `BepInEx/config/DudeWhatAreMyStats/`, one per world.

*Server Max Characters* bounds how many the server remembers, dropping the least recently seen
past that. The whole set travels to a client in one message, so this is a real ceiling rather than
housekeeping.

*Push Minutes* is the client side of that. Set it to 0 and you stop handing over your own stats,
which keeps you off the board for anyone who was not online at the same time as you.

*Ask Other Players* works both ways. Turn it off and you stop asking, and you also stop answering,
so you drop off everyone else's scoreboard as well as emptying your own. It is an opt-out, not a
quiet way to watch without being watched. Turning it off also tells the server to forget the
record it already holds, so you come off other people's boards rather than lingering there until
the keep window runs out.

## Console commands

| Command | What it does |
|---|---|
| `dwams` | Open or close the panel. |
| `dwams_hud` | Show or hide the always-on player list. |
| `dwams_refresh` | Ask everyone online, and the server, for stats again. |
| `dwams_deaths` | What has killed you, and the last few deaths in full. Add a number for one of them. |
| `dwams_status` | Print the scoreboard, and say whether the server is keeping records. |

## Configuration

`BepInEx/config/DeathMonger.DudeWhatAreMyStats.cfg`, or press F1 in game if you have a
configuration manager. Notable settings: the open key, *Pause While Open*, *Ask Other Players*,
*Refresh Seconds*, *Show Offline Players*, *Push Minutes*, *Show Zero Stats*, *Top Creature Count*
and *Fix Treasure Discovery Count*, the `[Deaths]` section for the death record, the `[Player List]`
section for the always-on list, plus *Server Store Enabled* and *Server Keep Days* on a server.

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

- Only players running this mod appear on the scoreboard.
- Seeing players who are not online needs the mod on the server too. Without it the board shows
  only whoever is online to answer for themselves.
- The server records what each player's own game tells it about itself, the same as the live
  answers, so it is a scoreboard among friends rather than an audited one.
- A character is only remembered once they have played with the mod installed on both ends, so the
  board fills in over the first few sessions rather than arriving complete.
- What killed you is recorded from the moment this version is installed and no earlier. Nothing can
  reconstruct it: the game kept no record to read.
- A fight can hold two dozen creatures in the record. A raid bigger than that is noted as capped.
- Valheim keeps ten sets of stats per character, one per difficulty. This mod reads the raw
  lifetime set, so the numbers include every run regardless of difficulty, and cheated runs too.
- `Food Eaten` is bugged in vanilla and does not count up.
- The panel is not a controller UI. It expects a mouse and keyboard.

## Credits

Built by DeathMonger with Claude Code. The panel borrows the game's own frame, font and buttons,
so it needs no asset bundle and no Jotunn.
