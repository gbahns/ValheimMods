# Changelog

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
