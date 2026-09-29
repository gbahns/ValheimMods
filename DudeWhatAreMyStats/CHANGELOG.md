# Changelog

## 0.10.0

The mod now records what kills you. Valheim counts your deaths and sorts them by category, but it
has never recorded which creature did it; this watches your fights as they happen and keeps the
answer.

**Update together.** The message players' games use to exchange stats has grown, so a 0.9.0 game and
a 0.10.0 game will not show each other on the board, and a server on 0.9.0 starts its store over.
Nothing breaks and nobody is refused a connection. This is the last time: from this version the
format only ever gains sections on the end, and a build reads as much of a newer one as it
understands, so later additions cost nobody anything.

- **A Nemesis column** on the scoreboard: the creature that has killed you most, and how often. It
  reads "-" until this version has watched somebody die, because there is no old record to draw on.
- **Killed by** and **In the fight** tables in the Details tab. Deaths from before the mod was
  watching are shown as *Not recorded* rather than quietly leaving the table short of the death
  count two columns over.
- **The whole story of your last few deaths**, kept on your own machine and printed by the new
  `dwams_deaths` console command: the killing blow split by damage type, what the killer was holding
  and how many stars it had, the armor and weapon you had on and the effects you were under, the
  biome and the day, how long the fight ran, and a line for every creature that was in it. Add a
  number, as in `dwams_deaths 2`, for one death in full.
- **Everything in the fight is recorded, not just the killer.** Ten greydwarves that surround you and
  drain your stamina are part of why the troll got you, so each one is listed with what it actually
  did: the damage it dealt, the blows you blocked and what they cost you in stamina, the ones you
  dodged, and how long it had you in its sights. A creature that never landed a blow still appears,
  marked as having been there.
- Deaths nothing dealt are recorded too, so falls, drowning, lava and the edge of the world sit in
  the same table as the creatures. Poison and burning ticks carry no attacker at all, so they are
  credited to whoever last dealt that kind of damage and marked *(inferred)* rather than presented
  as certain. That is what puts a death by Blob poison down to the Blob after the Blob itself is
  dead.
- New settings under `[Deaths]`: *Record Deaths*, *Record Bystanders*, *Bystander Seconds*,
  *Fight Gap Seconds*, *Keep Reports*, *Top Death Causes* and *Log Deaths*. The reports live in
  `BepInEx/config/DudeWhatAreMyStats/`, one file per character, beside the server's store.
- Nothing new is needed on the server. Only your own game can see what hit you, so only your own
  game can record it, and the totals travel to everyone else exactly as the rest of your stats do.

## 0.9.0

Plays together with 0.2.0: the stats messages are unchanged, so players on either version see
each other, and a server still on 0.2.0 keeps remembering offline players for everyone.

- An always-visible player list with each player's death count, most deaths first. Off by default;
  turn it on with *Show Player List*, the console command `dwams_hud`, or a key of your choosing
  under *Toggle Key* (unbound by default). It stays up over the map and the inventory and never
  takes the mouse. It steps aside when the game hides its own HUD (Ctrl+F3), in cutscenes, behind
  the pause menu and the death or teleport fade, and while the stats panel is open. Settings under
  `[Player List]` choose the corner, the offset, the text size, how many rows, and whether offline
  players are included.
- Who counts as online now comes from the game's own list of connected players rather than from
  how recently someone answered. A player who had just died could briefly vanish from the
  scoreboard while respawning, right when their death count went up; they now stay listed.
- The open key no longer fires while you are typing. Typing a word containing an "i" into the
  hammer's build menu search box opened the stats panel; it now stands down while that box, or any
  other text box, has the keyboard. That covers sign text and other mods' search fields too, not
  just the build menu. With the panel already open, a focused text box elsewhere is left alone in
  the same way.

## 0.2.0

Players who are not online can now appear on the scoreboard.

**Update together.** The message players' games use to exchange stats changed shape, so a 0.1.0
game and a 0.2.0 game will not show each other on the board. Nothing breaks and nobody is refused
a connection; they simply do not see one another until both are on 0.2.0.

- Offline players appear, marked with how long ago they were last seen, such as *(2d ago)*. Your
  game hands the server its own stats every few minutes, when you open the panel and as you leave
  the world; the server keeps the newest record per character in
  `BepInEx/config/DudeWhatAreMyStats/`, one file per world, and hands the set to anyone who asks.
  A live answer always beats the stored copy, so nobody standing next to you is shown from an old
  record.
- This is the one thing the mod wants on the server, and it is optional. A server without the mod
  never answers the request, and the board shows whoever is online, exactly as in 0.1.0. Run
  `dwams_status` to see which case you are in.
- New server settings under `[Server]`, ignored on a client: *Server Store Enabled*,
  *Server Keep Days* (forget a character nobody has seen in that long) and *Server Max Characters*
  (the ceiling on how many are remembered, and on the size of the one message that carries them).
- New client setting *Push Minutes*. Set it to 0 to stop handing over your own stats, which keeps
  you off the board for anyone who was not online at the same time as you.
- *Remember Offline Players* is now *Show Offline Players*, since it governs both the players who
  log out while you are playing and the ones the server remembers from before you arrived.
- Turning off *Ask Other Players* now also tells the server to forget the record it already holds,
  so you leave other people's boards instead of lingering there until the keep window expires.
- Fixes a bug in Valheim itself: a treasure chest counted as newly found every time it was opened,
  because the game marks it discovered over a message it never registered a handler for. That also
  stops the `Failed to find rpc method 327122920` warnings in the log. Counts already inflated stay
  inflated; nothing rewrites stats the game has recorded. Off switch:
  *Fix Treasure Discovery Count*.

## 0.1.0

Early alpha, first release.

- Stats panel on a key press (default I), with the game paused while it is open as far as vanilla's
  own pause allows: solo and host-alone freeze, a busy server does not.
- Scoreboard of every player online running the mod, with kills, deaths, K/D, boss kills, active
  play time and best skill. Sort by any column; the choice is remembered.
- Details tab: one player's full stats sorted into foldable sections, their skills, and the
  creatures they have killed most. Times and distances are formatted rather than dumped as raw
  numbers.
- Other players' stats arrive over a routed RPC that each client answers for itself, so the mod is
  client-side only and needs nothing on the server.
- Reads slot 0 of Valheim 1.0's per-difficulty stats array, which is the raw lifetime tally.
  Adding the ten slots together would count most events two or three times.
- Movable and resizable panel, remembered between sessions. Console commands `dwams`,
  `dwams_refresh` and `dwams_status`.
