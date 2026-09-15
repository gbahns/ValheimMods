# Changelog

## 0.2.2

- **Stopped accusing a healthy server.** A real server reported 33.3 ms per tick now, 33.3 ms
  median and a 34 ms worst tick, with no stalls — and the verdict called it starved. That is not a
  server in trouble; it is a server pinned to exactly 30 ticks a second by a frame limiter, and the
  old 33 ms threshold sat precisely on top of that cap. The rule now looks at the *spread* rather
  than the level: a frame limiter holds every tick to nearly the same length, while a machine that
  genuinely cannot keep up produces variance, because the work that overruns is not the same work
  every tick. A worst tick close to the median, with no stalls, is read as a cap and left alone.
  This is not a list of known cap values — a server capped at 20 or 60 is recognized the same way,
  with nobody having to enumerate them.
- Above the severe threshold a steady tick no longer earns the benefit of the doubt: a server
  holding a metronomic 200 ms is still far too slow to run the game, however even it is.
- Thresholds raised accordingly — warn 33 → 50 ms, severe 66 → 100 ms — with a new **Steady Tick
  Ratio** setting controlling how close the worst tick must be to the median to read as a cap.
- The server section now shows a **steadiness** row explaining which of the two it is, so the
  number that caused the confusion carries its own explanation.
- **The value column was unreadable.** It used the shared row helper, whose right-hand column is a
  fixed 96 pixels — right for a scoreboard's short numbers, hopeless for these, so every value
  ellipsized to a few characters: "9.7 ms (103…", "42 ms in th…". Measurement rows now use a layout
  built for them, with a narrow fixed label column and the value taking all the remaining width.
- **Negative queue sizes are clamped.** A real session reported "-21294 B queued". The game's own
  `GetSendQueueSize` only ever sums non-negative values, so whatever Steam is reporting through
  that struct, a negative backlog is not a measurement — and it was being fed to the saturation
  rule as well as printed.

## 0.2.1

- **The report now takes the mouse.** It had no Harmony patches at all, so Valheim never treated it
  as a UI screen: the cursor stayed locked to the camera, mouse movement kept turning the character
  behind the panel, and nothing in it could be clicked — including 0.2.0's new pause button. The
  panel now reports itself through `TextInput.IsVisible`, which is the flag
  `GameCamera.UpdateMouseCapture` consults to release the cursor, and the same one vanilla already
  uses to hold back movement, the hotbar, the map key, the ESC menu and chat. Escape still closes
  the report without also opening the game menu, because `Menu.Update` gates on that same flag.
- The mouse wheel no longer zooms the camera behind the panel while you scroll the evidence.
  GameCamera's zoom check looks at chat, the console, the inventory and several other screens, but
  not at text prompts.
- Leaving a world now closes the report, drops any pause and clears the history from
  `ZNet.Shutdown`, rather than a frame later from the plugin's own Update. A pause is a global flag,
  so letting a shutdown race it could leave the game frozen behind a panel that no longer exists.

## 0.2.0

- **Fixed a crash.** The report threw `NullReferenceException` out of TextMeshPro on every layout
  pass whenever it showed a wrapped paragraph — including the "the server is not running this mod"
  notice, so it hit anyone on an unmodded server almost as soon as they opened it. The paragraphs
  were built by hand and never got a font: adding a TextMeshProUGUI to a live GameObject makes TMP
  look up Unity's default font, which Valheim does not ship. They now go through the same helper as
  every other label, which assigns the game's font while the object is still inactive. The corner
  readout built its label the same way and no longer does.
- **Pause button**, top-right of the report, matching the ones on the large map and GrabMaterials'
  inventory panel: gray when off, Valheim orange while the game really is paused, red with a slash
  when the pause was asked for and refused. Off by default — a diagnostic should not change how the
  game runs the first time you open it.
- **Nothing is recorded while the game is paused.** A paused world simulates nothing, so frames get
  cheap and the link goes quiet; recording those seconds would let the report talk itself round to
  "nothing wrong right now" while you sat reading it, which is the exact opposite of what pausing
  to read a diagnosis is for. Pausing now freezes the evidence, and the report says so.
- The pause survives another mod dropping it. Vanilla keeps a single pause flag rather than a
  count, so any mod calling `Game.Unpause()` releases everyone's — the sibling mods only act on a
  transition and quietly believe they still hold a pause that is gone. This re-asserts when a pause
  it actually had stops being in effect, and stays silent when the request was simply refused.

## 0.1.1

- Default key moved from **F10 to F8**, and the readout toggle from Shift+F10 to Shift+F8. F10 is
  AutomaticFuel's default and F9 cycles the controller layout, so 0.1.0 shipped into a conflict
  with a neighbor that was no escape either. F8 is free in vanilla and unused by every other mod
  in this repo.
- A config still holding 0.1.0's F10 is moved to F8 automatically. BepInEx writes every default
  into the .cfg on first run, so without this the new default would have reached nobody who
  already had the mod. A key you chose yourself is never touched.

## 0.1.0

Early alpha, first release. Not yet tested against a real lag event on the DatHost server.

- Measures both ends of the connection once a second and names which of six unrelated causes is
  behind the lag: a starved server, a saturated link, packet loss or jitter, object churn, the
  world still loading, or the local machine.
- Server half reports its own tick times, stall count, world object count and per-player socket
  state over a routed RPC. That is the one measurement a client cannot make for itself, and
  without it "the server was fine" is unprovable.
- A server without the mod never answers; clients say so plainly and fall back to diagnosing their
  own end rather than guessing at the half they cannot see.
- Report on a key press (default F10): verdict, the evidence behind it, what is worth doing, then
  the live numbers for this machine, the server and every connected player.
- Optional four-line corner readout (Shift+F10), off by default, with the worst measurement
  colored.
- Thresholds with a physical meaning - packet loss, ping jitter, server tick time - are judged
  absolutely. Everything whose normal value depends on the world and the hardware is judged
  against this server's own recent median instead. Every threshold is configurable and documented.
- Per-player detail (ping, quality, queued bytes, distance from the world center) goes to admins
  always and to everyone when the server's Share Peer Detail is on.
- Console commands `dsl`, `dsl_why`, `dsl_now`, `dsl_server`, `dsl_dump` and `dsl_reset`.
  `dsl_dump` writes every measured second to a CSV.
- Reads public game API only and patches nothing that affects play. `ISocket.GetAndResetStats` is
  deliberately never called: it would zero the counters Valheim keeps for its own bandwidth
  display.
