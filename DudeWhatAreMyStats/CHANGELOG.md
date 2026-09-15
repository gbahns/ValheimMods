# Changelog

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
