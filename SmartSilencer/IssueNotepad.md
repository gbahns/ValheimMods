# SmartSilencer notepad

> **Resolved 2026-11-03, SmartSilencer 1.2.1 (unreleased, built and deployed to both test profiles).**
> The poll now runs on its own background thread, which owns the COM objects, the name cache and
> the timers (Stopwatch, no Unity calls); each poll is published as a fresh array and Update only
> counts polls. `silencer apps` reads the same array. Still to do: the acceptance test below
> in-game (`dsl_hotspots mods`, then video / alt-tab checks).
>
> The first build of this (2026-11-04) stayed silent after Spotify was paused: Update told polls
> apart by array reference, and an empty poll is Mono's shared empty array every time, so once
> the last other program closed its stream no poll ever looked new and the verdict froze. Greg
> reproduced it (play Spotify, pause it, no sound back; toggling the mod cleared it). Fixed the
> same day with a poll counter; needs a game restart to take effect.

## The audio poll costs 5.1 ms/s on the main thread

Found 2026-11-03 by the DSL/STL session, in the first ever `dsl_hotspots mods` run (solo, in the
base, 813 s window at 35 fps):

```
[DeathMonger.SmartSilencer] SmartSilencerMod.Update      5.13 ms/s   0.146 ms/call   35.1 calls/s
[DeathMonger.SmartSilencer] SmartSilencerMod.LateUpdate  0.64 ms/s   0.018 ms/call   35.1 calls/s
```

**SmartSilencer is the most expensive mod on the machine**, out of 53 plugins and 49 timed
per-frame methods. For comparison, in the same window: TheGreatestMap 3.63 ms/s, AzuAutoStore
2.20, DiagnoseServerLag 1.23, and every other plugin together about 2.7. All 53 plugins combined
come to 15.5 ms/s, so SmartSilencer alone is a third of it.

### Where it goes

Not the hotkey, and not a per-frame poll - `Update` already gates the poll behind
`PollInterval` (0.25 s). The arithmetic says it is the poll itself:

    5.13 ms/s ÷ 4 polls/s = 1.28 ms per poll

`AudioSessions.TryPoll` rebuilds everything on each call: `GetSessionEnumerator`, `GetCount`, then
per session `GetSession`, `GetState`, two `QueryInterface`, `GetProcessId`, `GetPeakValue` and
three `Release`. That is roughly eight cross-process COM calls per audio session, times however
many sessions Windows currently has open - browser tabs, Discord, Spotify, system sounds. 1.28 ms
is an entirely reasonable price for that; the problem is only that it is paid on the main thread.

### Raising Poll Interval is not the fix

`Sound Delay Seconds` is 0.3 s, so polling slower than that just adds latency before the game goes
quiet. 0.25 s is already matched to the debounce. The cost has to move, not stretch.

### The fix: poll on a background thread

COM is already initialized `COINIT_MULTITHREADED` and `AudioSessions.Initialize` says in a comment
that everything it uses is free-threaded, so the interfaces can be called off the main thread. A
worker that polls on its own schedule and publishes a verdict to a volatile field, with `Update`
doing nothing but read that field, takes the whole 5.13 ms/s out of the frame - and would then be
free to poll *faster* than 0.25 s, since nobody is waiting on it.

**The one gotcha:** `TryPoll` currently reads `Time.unscaledTime` for the manager-refresh and
name-cache timers, and Unity APIs are not safe off the main thread. Those two timers need a
`Stopwatch` or `DateTime.UtcNow` before any of this moves. It will not necessarily throw if you
forget - it may just return garbage - so change it first.

Also check `Classify`/`_names`: if the name cache is read by the worker and written by the main
thread, it needs a lock or needs to live entirely on the worker.

### Acceptance test

```
dsl_hotspots on
dsl_hotspots mods
   ... play five minutes ...
dsl_hotspots
```

`SmartSilencerMod.Update` should come back near zero per call instead of 0.146 ms. Then verify the
behavior still works: start a video, confirm the game goes quiet within about a second; stop it,
confirm sound returns after the grace period; alt-tab and back.

### How much this is worth - read before investing

0.146 ms out of a 30.9 ms base frame is **0.5%**. Outdoors at 82 fps it is 0.06 ms of a 12 ms
frame. This is housekeeping, not a performance fix, and nothing being chased on the lag side
depends on it.

It is worth doing anyway for two reasons. A mod that mutes audio has no business being the most
expensive plugin on the machine, and this is a fixed cost paid on every frame forever - the kind
that is invisible in any before-and-after comparison and only ever shows up in an absolute
measurement like this one. That is exactly how HUDCompass went unnoticed at 26 ms a frame.
