# Diagnose Server Lag

Says which kind of lag you actually have, instead of guessing.

"The server is lagging" covers six unrelated failures. They feel identical while playing — the game goes sticky — and they have nothing in common but that. A starved server, a saturated link, a lossy connection, an overloaded base, a world still loading and a computer that cannot keep up each need a different fix, and picking the wrong one costs an evening. Worse, the measurement people reach for first actively misleads: a frame-rate counter reads perfectly fine while the server burns, because your machine has nothing to struggle with when the updates never arrive.

This mod measures **both ends** once a second, and names the cause.

## What it tells you

Press **F8** and the report opens with a verdict in plain language, the evidence it was decided from, and what is actually worth doing about it. For example:

> **The server is badly starved: 84 ms per tick sustained (12 per second).**
> Nothing on your machine will change this, and it is happening to everyone online at the same time.
>
> - tick 91.2 ms now, 84.4 ms median over the server's last 60s
> - worst single tick in that window 340 ms
> - 47 server stalls over the window
> - 18,402 networked objects in the world, 612/s sent to 3 players

or

> **Packets are being lost: connection quality 91.4%.**
> This is the network path, not the server and not your computer, so neither restarting the server nor turning settings down will touch it.

The six causes it separates:

| Cause | What proves it |
|---|---|
| **Server starved** | The server's own tick times, which a client cannot see at all |
| **Link saturated** | Bytes handed to the socket that have not left, and whether that number is climbing |
| **Connection quality** | Steam's measured packet-loss fraction, and ping jitter judged apart from latency |
| **Object churn** | Object updates per second against this server's own recent normal |
| **World loading** | Objects being built into the scene at the moment the frame stalled |
| **Your machine** | Frame times, but only claimed once the other five have been measured and ruled out |

More than one can be true at once and often is — a saturated link and heavy churn are usually one event seen from two ends — so the report ranks them and shows the rest under the headline.

## Why it needs the server

The server half is the point of the mod. A client watching itself can tell you the game feels bad; only the server's own tick times can tell you whether the server was keeping up at the time, and that is the single measurement that decides whether anything on your machine is worth changing.

Without it the mod still works and still finds link saturation, packet loss, churn and loading — it simply cannot rule the server in or out, and it says so rather than quietly guessing:

> The server is not running this mod, so it cannot be measured. Every verdict below is made from this machine's side alone, and "the server was fine" is the one thing it cannot tell you.

## Installation

Install on the **server and on every client** (Gale, r2modman or Thunderstore Mod Manager, or drop `DiagnoseServerLag.dll` into `BepInEx/plugins`). Keep the same version everywhere.

- Server without the mod: clients diagnose their own end and say the server half is missing. Nothing breaks.
- Client without the mod: that player gets no report. Everyone else is unaffected.
- Hosting the world yourself: both halves are the same process, and the server numbers are read directly with no asking involved.

Works on Windows and Linux dedicated servers. Requires BepInExPack for Valheim.

## Keys (configurable)

| Key | Does |
|---|---|
| **F8** | Open and close the report. Escape closes it too. |
| **Shift+F8** | Turn the small corner readout on and off. |

There is also a **pause toggle** in the report's top-right corner, the same mark used on the large map and GrabMaterials' inventory panel: gray when off, Valheim orange while the game really is paused, red with a slash when the pause was asked for and refused. It is off by default.

Pausing matters more here than on those other panels, because this one reports on the very thing a pause changes. **While the game is paused the mod stops recording.** A paused world simulates nothing, so frames get cheap and the link goes quiet — recording those seconds would let the report work its way round to "nothing wrong right now" while you sat reading it. Instead a pause freezes the evidence, which is what you want when you paused to read why the last minute was bad. The report says so while it is held.

The pause goes through vanilla's own calls, so it works by itself solo or hosting alone; on a dedicated server it takes Pause My Server, and the button tells you whether it actually took.

F8 is free in vanilla Valheim. It replaced F10 in 0.1.1, which clashed with AutomaticFuel's default; if you installed 0.1.0 the mod moves your config over for you, unless you had already chosen a key of your own.

The readout is six lines, on by default, with the worst one colored:

```
frames      10 ms  103/s
stalls      0 in 10s
cpu         45% of a core
round trip  18 ms
simulating  41/44  (Marco has 3)
server      33.3 ms  412669 obj
```

**`simulating` is the one to watch.** Valheim runs a creature's AI only on the machine that owns it, ownership falls to whoever was in range first, and it is never rebalanced — so if several of you pile into one zone, one person ends up computing everyone's fights on a machine nobody chose. This is the only warning you get. Spreading out, or letting the strongest machine enter a zone first, is the lever.

## Console commands

| Command | Does |
|---|---|
| `dsl` | Open or close the report |
| `dsl_why` | Print the verdict and its evidence as text |
| `dsl_now` | The last second measured on this machine |
| `dsl_server` | What the server last reported about itself, including the per-player table |
| `dsl_bench [seconds]` | Summarize the last N seconds (default 120) on **this machine** and write a CSV |
| `dsl_bench_server [seconds]` | Ask the **server** to capture itself and print its reply here (admin only) |
| `dsl_cpu` | What this process is costing the machine, and how much headroom is left |
| `dsl_dump` | Write every measured second to a CSV under `BepInEx/config/DiagnoseServerLag/` |
| `dsl_reset` | Forget the history and rebuild the baseline |

A Valheim dedicated server has no console to type into — it reads nothing from stdin — so every command here runs on a **client**. To measure the server itself, an admin runs `dsl_bench_server`, which asks the server to capture itself and prints its reply in your console.

## Diagnosing a session, not just a machine

`dsl_bench_server` captures the server **and every connected client over the same seconds**, then says the one thing no single capture can:

```
  server           tick   33.3 ms   stalls   0   CPU  20.3% of a core   412669 objects
  Death            frame   8.7 ms   stalls   4   CPU  45.1% of a core   rt 18 ms
  Marco            frame  14.2 ms   stalls   0   CPU  22.0% of a core   rt 41 ms

ALONE: stalls nobody else had, which are local to that machine.
  Death            4 second(s) stalling by itself
  No second had two machines stalling together, so nothing here points at the server.
```

Stalls in the **same wall-clock second** on two or more machines are one shared event — the server, or the path everyone crosses. The same stalls at different seconds are separate local problems. The numbers look identical; only the alignment separates them.

A `group-<timestamp>.csv` is written on the server, one row per machine per second.

Each client decides whether to answer via **Share My Performance** (default on). It sends frame times, stalls, CPU share and collection counts — performance numbers only. Clients that do not answer are counted and named, so a partial picture is never mistaken for a complete one.

## Comparing two servers

Tick time cannot tell you how a server is doing if it runs a frame limiter, and most dedicated servers do. A server pinned to 33.3 ms might be using a tenth of a core or all of it — the tick is held at its configured length either way, and only starts moving once the server has already failed.

What survives the cap is **CPU time consumed per second of wall clock**, so that is what the mod records. It also gives you a headroom figure: how many times the current load the server could carry before one core is full. One core, not the machine — Valheim's simulation is effectively single threaded, so spare cores do not raise the ceiling.

For your own machine, or a server you are hosting, run:

```
dsl_bench 300
```

For a dedicated server, which has no console of its own, an admin runs this from a connected client:

```
dsl_bench_server 300
```

Either prints a summary and writes `BepInEx/config/DiagnoseServerLag/lag-<role>-<timestamp>.csv`. Then compare the two:

```powershell
.\compare-servers.ps1 -A dathost -B "C:\path\to\local\lag-dedicated-....csv"
.\compare-servers.ps1 -List      # what captures are on the DatHost server
```

`-A dathost` fetches the newest capture off the DatHost server over its REST API, using the same credentials as `deploy-dathost.ps1`.

**Put the same world on both machines first.** A capture of a 340,000-object world against one holding 12,000 compares worlds, not servers — the script says so and refuses to draw a conclusion, falling back to a rough per-object normalization.

## Configuration

`BepInEx/config/DeathMonger.DiagnoseServerLag.cfg`. Every threshold is exposed and documented, because a verdict is only worth as much as the number it was decided with, and the right number depends on hardware nobody else can see.

The ones worth knowing about:

- **History Minutes** (60) — how much per-second history to keep, and so the longest capture `dsl_bench` can summarize. Up to 180. Longer is not automatically better: the summary reports medians over the whole window, so match the capture to the activity rather than maximizing it.
- **Stall Milliseconds** (100) — a frame longer than this counts as a stall.
- **Window Seconds** (10) — how many recent seconds the verdict is made from.
- **Baseline Seconds** (60) — how much history counts as "normal for this server".
- **Server Tick Warn / Severe Ms** (50 / 100) — how slow the server's tick has to get before it is called slow, and then badly starved.
- **Steady Tick Ratio** (1.5) — how close the server's worst tick must be to its median before the tick rate is read as a deliberate frame cap rather than a struggle. Many dedicated servers run a limiter (30 ticks a second is 33.3 ms), and a capped server is not a slow one: a limiter holds every tick to nearly the same length, while a machine that cannot keep up produces variance. Below this ratio, with no stalls, the server is left alone however slow the number looks — up to the severe threshold, above which nothing is forgiven.
- **Queue Warn / Severe Bytes** (16 KB / 64 KB) — queued bytes that have not reached the wire.
- **Pause While Open** (off) — pause the game while the report is open. The corner toggle changes this.
- **Show Pause Button** (on) — whether that toggle is drawn.
- **Churn Factor** (3) — how many times this server's own normal rate of object updates counts as churn.

Server-side only, read by whichever machine runs the server:

- **Answer Clients** (true) — turn off and the server becomes indistinguishable from one without the mod.
- **Share Peer Detail** (true) — include the per-player table (each player's ping, quality, queued bytes and distance from the world center) in the answer to everyone. The aggregate numbers that diagnose the server always go to everyone; this is the part that names who is on a bad line. Admins receive it either way.

## How it works

Once a second each end records its frame times, the game's own network counters, its socket's queue and send rate, and how many objects it knows about and is exchanging. Ten minutes of those seconds are kept in a ring buffer on each side.

Two kinds of test run against them. Measurements with a physical meaning — packet loss, ping jitter, server tick time — are judged against absolute thresholds, because a connection losing packets is losing packets whatever it did a minute ago. Everything whose normal value depends on the world and the hardware is judged against this server's own recent median instead, because there is no universal right answer for how many objects should be changing near a large base.

Clients ask the server for its numbers once a second while the report is open and every five seconds otherwise; a server with nobody looking sends nothing at all. Every reading is a counter read or a dictionary count — nothing walks the object graph — because a diagnostic that cost a millisecond a frame would become a cause of the thing it is trying to explain.

One deliberate omission: Valheim's `ISocket.GetAndResetStats` would give cleaner byte totals and is never called, because it zeroes the counters the game keeps for its own bandwidth display.

## Reading the blind spots

**Latency on a dedicated server is measured by the mod, not by the game.** No socket Valheim gives a dedicated server can report a ping: the PlayFab socket inherits its connection-quality method from a stub that hardcodes zero, and the Steam socket reaches for the client Steam interface, which a server process never initializes because that lives in the client-only `SteamManager`. Nothing is missing from your host and there is nothing to install — it is how the game is built. So the mod times its own request to the server and reports that round trip instead, labeled `rt` so it is never confused with a socket ping. It includes a frame of server processing, which is arguably the more useful number: it is how long an action takes to be acknowledged.

Connection quality has no such fallback. A round trip says nothing about packet loss, so where the socket cannot report quality the report says "not measurable" rather than inventing one.

On a client, ping and connection quality come from Steam's own connection status where the connection is a Steam one. Valheim's plain TCP path reports both as zero, so a zero here means **not measurable**, not perfect — the report says "not measurable on this socket" rather than coloring it green.

Nothing inside the game can see the host machine's CPU being shared with other tenants. It can, however, see the consequence: a server tick time that is bad while the object count and traffic are normal is the signature of an oversubscribed host, and that is itself a finding you can act on.

## Compatibility

Reads public game API only and patches nothing that affects play. It should coexist with anything.

**BetterNetworking** changes how the protocol is compressed and paced. The measurements stay meaningful — queue sizes and tick times are the same numbers — but the byte rates are of compressed traffic, so they read lower than the uncompressed equivalent.

## Source

<https://github.com/gbahns/ValheimMods>
