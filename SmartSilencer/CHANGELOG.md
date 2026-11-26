# Changelog

## 1.3.0 — 2026-11-25

- The game no longer warns about area damage it has itself switched off. An object with triggers
  disabled but a collider still on it writes `AOE got OnTriggerStay but trigger damage is disabled
  in <name>` for every collider touching it, every physics tick — a pufferfish lying in the wrong
  place wrote 9,741 lines in four minutes. New setting "Quiet Disabled Trigger Damage" (Fixes, on).
  No damage changes.

## 1.2.1 — 2026-11-04

- The per-app level meters are now read on a thread of their own instead of from the game's frame. The poll was costing about 5 ms a second on the main thread, the most of any installed mod; the game now pays nothing for it. No change in behavior.

## 1.2.0 — 2026-10-15

- **The game stops asking for music it does not have.** A music volume names the track it wants and
  re-names it every tick you stand inside it, so when that track is not loaded the game writes
  `Location music missing: <name>` to the log at frame rate — thousands of lines for standing in one
  room. Valheim ships at least one of these: the frost cave sanctum asks for `frostcaves`, and no
  track of that name exists, nor any frost cave music anywhere in the game's audio. A name the game
  cannot play is now dropped before it is set. Nothing that could have played is silenced — the only
  names dropped are ones whose lookup can only fail — and if a future Valheim ships the missing
  track, the name is found and this stops doing anything. New setting, "Quiet Missing Location
  Music" (Fixes, on); turn it off to leave the game as it ships.

## 1.1.1 — 2026-09-22

- Show Messages now defaults to off, like Log Transitions: the on-screen messages are for confirming the mod works, not for every song. An existing config keeps whatever it has. The hotkey's on/off confirmation still shows regardless.

## 1.1.0 — 2026-09-22

- Two new triggers, both off by default: **Game Paused** (the single-player ESC pause, or a PauseMyServer pause on a server) and **Main Menu**.
- The console command takes `paused` and `menu` alongside the other trigger names.

## 1.0.0 — 2026-09-22

First release.

- The game's sound fades down while a music player (Spotify and friends) is playing, while a video plays (any browser, VLC and the like), or while another window is in front, and fades back up when that stops.
- Each of the three triggers has its own switch, plus an optional fourth for any other program making sound (off by default, so a voice call or a notification does not count).
- Music and video are told apart by program, through two editable lists; a third list of programs is always ignored.
- A hotkey and the `silencer` console command flip the whole thing while playing; `silencer apps` shows what is audible right now and how it is classified.
- On-screen messages and log lines for each transition are separate switches (Show Messages, Log Transitions); the log is off by default.
- Quiet Volume, fade times, the sound threshold, the trigger delay and the silence grace period are all configurable and take effect without a restart.
