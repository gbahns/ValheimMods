# Changelog

## 0.4.0

- **"Your machine hitched" is no longer the end of the conversation.** A new verdict attributes
  stalls to **garbage collection** when the evidence supports it. A collection pause is invisible
  to every other measurement here — network fine, server fine, frame average barely moving, one
  frame in a hundred taking a quarter of a second — which is exactly the shape people report as
  random stuttering.
  Coincidence alone is not treated as evidence. A large heap collects gen0 constantly, so "there
  was a collection during the stall" is nearly always true and proves nothing. The rule asks
  whether collections are *disproportionately* concentrated in the seconds that stalled compared
  with the seconds that did not, and stays silent when the rate is merely high throughout. Gen0 is
  ignored entirely; only gen1 and gen2 actually pause.
- The report now shows **game CPU, collections per minute, heap and working set** for your own
  machine. They were recorded from 0.3.0 and only written to the CSV, so the panel could say the
  machine hitched while showing nothing about what the machine was doing.
- When the verdict is still "this machine", it now carries that CPU and memory evidence instead of
  naming the machine and stopping.

### Also in this release

- **A throwing method was discarding measurements that worked.** The three per-socket figures were
  read inside one `try`, so when one threw the other two went with it. That cost real data on a
  real server: `ZPlayFabSocket.GetCurrentSendRate()` throws `NotImplementedException` outright,
  and it was taking the send queue size down with it — the one per-player number genuinely
  measurable there, and the one that detects saturation. Each figure is now read on its own.
- **The mod measures latency itself now.** No socket Valheim gives a dedicated server can report a
  ping: `ZPlayFabSocket` inherits `GetConnectionQuality` from `ZNetStats`, the stub that hardcodes
  ping and quality to zero, and `ZSteamSocket` reaches for the client Steam interface, which a
  server process never initializes because `SteamAPI.Init` lives in the client-only `SteamManager`.
  So the request the mod already sends every second now carries a sequence number, the report
  echoes it back, and the gap is a real round trip.
- Clients pass their measured round trip up with the next request, so the server's per-player table
  shows a real latency for everyone running the mod — something no socket on a dedicated server
  can provide. Shown as `ms rt` to keep it distinct from a socket ping.
- A round trip is **labeled as a round trip**, not passed off as a ping. It includes a frame of
  server processing, which arguably makes it the more useful figure — it is how long an action
  takes to be acknowledged — but it is not the same number.
- Connection quality no longer claims to be measured just because a latency exists. A round trip
  says nothing about packet loss, and the two had shared one "is this measurable" flag.
- Report layout 2 adds both fields after the peer block, so an older client reads every field it
  knows and never notices the trailing bytes.

## 0.3.1

- **`dsl_bench` could not be run on a dedicated server — the machine it was written for.** A Valheim
  dedicated server has no console to type into: commands registered with `Terminal.ConsoleCommand`
  only ever reach the in-game console, which needs a client, and the server process reads nothing
  from stdin. Verified against the real server by sending it a plain `save` and watching it do
  nothing.
- **`dsl_bench_server [seconds]`** fixes it. An admin runs it from a connected client; the server
  captures itself, writes its CSV, and sends the summary back to be printed in that client's
  console. Admin only, because it makes the server do work and write a file.
- Corrected the README, which had claimed since 0.1.0 that `dsl_why` and `dsl_server` work on the
  dedicated server's own console. They never could.

## 0.3.0

- **Measures what the server costs the machine, not just how long its ticks are.** Tick time cannot
  measure a capped server, and most dedicated servers are capped: bahnsheim holds 33.3 ms with
  almost no variance, and that single number is equally consistent with 3 ms of work plus 30 of
  sleep or with 33 ms of work and nothing left over. Those are the same measurement and opposite
  situations. The mod now records CPU time consumed per wall second, which survives the cap, says
  how much room is left *before* the server is in trouble, and is the one figure that compares
  honestly between two different machines.
- Also records garbage collections by generation — a gen2 pause is a stall the tick average hides —
  along with managed heap and working set.
- **`dsl_bench [seconds]`** prints a summary built for comparison (role, cores, world, tick, CPU
  share, headroom, GC rate, memory, traffic) and writes the same window to a CSV. It reads the
  history already in the ring, so it returns immediately rather than starting a timer.
- **`dsl_cpu`** prints the current CPU share and headroom on its own.
- The CSV now carries a `#` metadata header — mod version, role, cores, CPU model, OS, world size,
  player count — so a capture can be identified later without anyone remembering which machine it
  came from, and gained columns for CPU, GC and memory.
- **`compare-servers.ps1`** puts two captures side by side. `-A dathost` fetches the newest capture
  off the DatHost server over its REST API; `-List` shows what captures are on it. It refuses to
  draw a conclusion from captures of different worlds — it says so loudly and falls back to
  per-object normalization, because comparing two servers carrying different amounts of world is
  comparing worlds, not servers.
- Headroom is reported against **one** core, not the machine. Valheim's simulation is effectively
  single threaded, so idle cores next to it do not raise the ceiling.

## 0.2.3

- **Stopped flooding the server console.** On the real server, 452 of the last 600 console lines
  were one warning from this mod — "Could not read a peer socket: Steamworks is not initialized" —
  repeated once per peer per second, with nobody connected. A diagnostic mod was making the server
  harder to diagnose.
  The cause: `GetConnectedPeers()` returns everything in the peer list, including sockets still
  being set up or torn down, and calling into a socket like that throws. Vanilla's own
  `GetNetStats` walks the same list but touches a socket only when `IsReady()` — "has a uid yet" —
  and that guard is the entire reason vanilla never hits this. The peer walk now has it too.
- **The per-player table was empty on every real server.** The row was built *after* the socket
  call, so any peer that threw was dropped before it was ever added. That silently emptied one of
  the things the mod exists to show, and left the server's worst-queue figure sitting at a
  reassuring `0 B` that nothing had measured. The row is now built from the peer's identity first
  and kept whatever the socket does; only the socket figures are left as not measurable.
- **"2 connected" over an empty list** — the player count included peers that had not finished
  handshaking. It now counts the same peers the table shows.
- Any socket that still cannot be read is reported **once per session** instead of once a second.
  The reading itself is not disabled: the failure is per-peer and transient, and switching it off
  for everyone because one stale peer threw would trade a noisy bug for a silent one.

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
