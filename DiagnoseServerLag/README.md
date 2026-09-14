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

The readout is four lines — your frame time, ping, queue and the server's tick time — with the worst one colored. It is off by default, because watching numbers is the habit this mod exists to replace.

## Console commands

| Command | Does |
|---|---|
| `dsl` | Open or close the report |
| `dsl_why` | Print the verdict and its evidence as text |
| `dsl_now` | The last second measured on this machine |
| `dsl_server` | What the server last reported about itself, including the per-player table |
| `dsl_dump` | Write every measured second to a CSV under `BepInEx/config/DiagnoseServerLag/` |
| `dsl_reset` | Forget the history and rebuild the baseline |

`dsl_why` and `dsl_server` work on the dedicated server's own console too.

## Configuration

`BepInEx/config/DeathMonger.DiagnoseServerLag.cfg`. Every threshold is exposed and documented, because a verdict is only worth as much as the number it was decided with, and the right number depends on hardware nobody else can see.

The ones worth knowing about:

- **Stall Milliseconds** (100) — a frame longer than this counts as a stall.
- **Window Seconds** (10) — how many recent seconds the verdict is made from.
- **Baseline Seconds** (60) — how much history counts as "normal for this server".
- **Server Tick Warn / Severe Ms** (33 / 66) — a healthy dedicated server ticks in single-digit milliseconds, so these are deliberately generous.
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

Ping and connection quality come from Steam's own connection status. Valheim's plain TCP path reports both as zero, so a zero here means **not measurable**, not perfect — the report says "not measurable on this socket" rather than coloring it green.

Nothing inside the game can see the host machine's CPU being shared with other tenants. It can, however, see the consequence: a server tick time that is bad while the object count and traffic are normal is the signature of an oversubscribed host, and that is itself a finding you can act on.

## Compatibility

Reads public game API only and patches nothing that affects play. It should coexist with anything.

**BetterNetworking** changes how the protocol is compressed and paced. The measurements stay meaningful — queue sizes and tick times are the same numbers — but the byte rates are of compressed traffic, so they read lower than the uncompressed equivalent.

## Source

<https://github.com/gbahns/ValheimMods>
