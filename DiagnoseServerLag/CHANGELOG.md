# Changelog

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
