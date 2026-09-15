# Changelog

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
- Fixes a bug in Valheim itself: a treasure chest counted as newly found every time it was
  opened, because the game marks it discovered over a message it never registered a handler
  for. That also stops the `Failed to find rpc method 327122920` warnings in the log.
