# Changelog

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
