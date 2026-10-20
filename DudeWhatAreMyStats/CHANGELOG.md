# Changelog

## 0.11.0 — unreleased

A kill count that knows what it killed: your own pets, the deer and the fish, and the things that
were actually trying to kill you, told apart and counted separately. Most of it works on the kills
you already made.

**Your Kills number will drop the day you update**, because it stops counting pets, deer, hares,
fish and chickens. That is the point of it. *Kills Column Hostile Only* off puts the column back to
the game's own total.

Plays with 0.10.1 either way: the stats message only gained sections on the end, which is the
contract 0.10.0 set up, so nobody is hidden from anybody. A player on an older version shows "-" in
the new columns rather than disappearing.

- **Hostile, tame, critters and bosses, worked out for kills you already made.** Valheim files a
  kill under the species and nothing else, but what sort of thing a species *is* never changes, and
  the answer is still in the game: the faction it belongs to, whether it carries an attack at all,
  whether it can be tamed. So the old tallies can be sorted without any record of the kills
  themselves. Nothing here is a list this mod maintains, which means a mod that adds creatures gets
  classified by the same rules.
- **K/D follows the Kills column**, so the ratio and the number beside it mean the same thing. The
  Details tab shows both figures side by side, labelled, and *Kills by type* accounts for the
  difference between them.
- **Two new columns, Tame and Critters**, and the panel is a little wider by default to fit them. A
  size you have already dragged is kept.
- **Tame against wild is the one thing the past cannot give up.** Whether the wolf was somebody's pet
  is a fact about that wolf, not about wolves, and the game recorded nothing: `Character.OnDeath`
  credits a kill without ever asking `IsTamed`. So *Assume Tame Share* says what to assume, per
  species, and ships assuming your old boar and wolf kills were pets - the right call for a
  long-running world with a pen full of animals, and wrong for a fresh one, which is why it is a
  setting. Write `Wolf:70` for a world that killed some of each.
- **Some of it is not an assumption at all.** A creature the world never spawns by itself and a
  player can tame only exists because somebody made it, so every one that died was a pet. In vanilla
  that is the hen and the chicken, which come from an egg and nowhere else; they are counted as
  yours without needing a setting, and a modded creature that works the same way gets it for free.
- **The assumption only ever touches kills nothing watched.** From this version the pet flag and the
  star level are recorded as kills happen, and the Details tab shows how many kills the game counted
  against how many this mod actually saw. The guessed share shrinks every session and never applies
  to a kill made from now on.
- **Kills by weapon**: unarmed, magic, ranged, melee. Valheim has been keeping these per creature
  since 1.0, in four slots beside the total that nothing has ever read. They are yours retroactively.
- **Toughness killed**, as the health of everything you fought added up, and the toughest single
  thing you have killed. Pets are left out of both; killing your own pig is not an achievement
  however much the pig weighed.
- **`dwams_creatures`** lists every creature in your world with how it was classified, its biome,
  its health, whether it can be tamed and what your setting assumes about it. Worth a look before
  trusting the numbers. `dwams_creatures all` prints the lot and copies it to the log.
- Settings under `[Kills]`: *Record Kills*, *Assume Tame Share*, *Kills Column Hostile Only*.
- Still not possible, and not pretended otherwise: how hard a kill was *at the time*. The old
  records carry no dates, so there is no way to know whether that troll died to a bronze axe or a
  blackmetal one. Star levels for old kills are gone for the same reason.

## 0.10.1

Different versions of this mod no longer hide players from each other.

Five players on two versions saw two separate scoreboards, each showing its own group and nobody
else, with nothing anywhere to say why. Both sides asked and both sides answered; only the reading
failed, so everything else looked perfectly normal.

- **Reads every version of the stats message this mod has ever written.** Refusing an old one bought
  nothing: the layout has only ever grown, apart from a single field added in 0.2.0, so an older
  snapshot reads fine and simply has no death record on it. An updated player now sees the whole
  table whoever else is running what.
- **The other direction cannot be fixed from here, so it is said out loud instead.** A game built
  before this change refuses anything but its own version, and that is baked into the copy they are
  running, so you will not appear on their scoreboard until they update. The panel says
  *2 players are on an older version, so you are not on their scoreboard until they update*,
  `dwams_status` prints the same, and the log says it once per player rather than every ten seconds.
- The same for the server's records. A store this version could not read left the board with no
  offline players at all, which looked exactly like a server that does not run the mod. It now reads
  those too, and says so if it ever cannot.
- No version is enforced anywhere and none ever will be. Nobody is refused a connection over a
  scoreboard; a version difference costs you rows on a table, not the game.

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
