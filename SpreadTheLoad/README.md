# SpreadTheLoad

Server-side performance optimization for Valheim multiplayer. It moves object ownership off a
struggling client so that everybody else stops inheriting that machine's lag.

**Install on the dedicated server only.** No client needs it, including players running no mods at
all. There is no prefab, no RPC and no version check, so nobody is locked out.

## The problem

Valheim simulates each creature, ship and workbench on exactly one machine: whichever client
*owns* it. Ownership goes to whoever was in range first, and vanilla never rebalances it — whoever
loads a zone keeps it until they walk away.

That is fine until the owner is the slowest machine in the group. Then every arrow you fire at a
creature it owns, and every swing at a tree it loaded, travels to that machine and back before
anything happens. Its frame time becomes your input delay, and the lag you feel has nothing to do
with your own hardware or your ping to the server.

Measured on a three-player server: two clients averaging 18 ms a frame, one at 63 ms, and a round
trip through the slow one of 144 ms against an 18 ms ping to the server.

## What it does

Name the players whose machines should not be handed shared work, and the server stops giving them
objects that somebody else is also standing near — and hands back the ones they are already
holding, which vanilla will not do on its own.

They still own anything **only they** are near, so nothing is ever left unsimulated and no creature
freezes. When they are the only player in a zone, nothing changes at all.

## What it does *not* do

**It will not improve the frame rate of the machine it steers away from.** Ownership costs that
machine CPU, and a struggling client is usually short of something else — on the server this was
built for, the slow client was using *less* CPU than the healthy ones while rendering a third as
many frames. Measurement there found ownership made no difference to its frame time either way.

This mod protects the *other* players. If you want the slow machine itself to run better, that is a
graphics settings and hardware question, and [DiagnoseServerLag][dsl] will tell you which.

## Configuration

`BepInEx/config/DeathMonger.SpreadTheLoad.cfg`, generated on first run.

| Setting | Default | Meaning |
| --- | --- | --- |
| `Enabled` | `true` | Turn off for stock behaviour without removing the mod. |
| `Yield Players` | *(empty)* | Who to steer work away from. Empty means the mod does nothing. |
| `Remember Ids` | `true` | Learn and remember network ids, so names keep working. |
| `Log Activity` | `false` | Occasional summary line; never one line per object. |
| `Auto Detect Struggling Players` | `false` | Find struggling machines without naming anyone. |
| `Stalls Per Minute` | `6` | How many stalls a minute before flagging someone. |
| `Assign Ship To Captain` | `true` | Give a ship to whoever is steering it. |
| `Yielding Player Retains Boat Helm Ownership` | `true` | Whether a yielding player keeps the helm when they steer. |

### Naming players

`Yield Players` takes Steam ids or character names, comma separated.

A character name is not an identity — the same person on a second character stops matching, and
somebody else picking that name starts matching. So a name is used **once**, to find the player;
their network id is then written to `SpreadTheLoad-known-ids.txt` beside the config, and from then
on they are matched by id whatever they call their character. Delete a line from that file to
forget someone.

Steam ids are the reliable thing to enter. The server logs them as
`Got connection SteamID 7656119...` whenever somebody joins.

## Detecting struggling players automatically

`Yield Players` requires you to know who is struggling. `Auto Detect Struggling Players` works it
out instead — off by default, because deciding on its own to move work away from someone is a
judgement you should opt into.

**It does not use ping or connection quality**, and that matters. The client this mod was written
for had a 24 ms ping — better than one of the healthy players — a clean connection, and 16 frames
a second. Every network-level measurement the server can take said it was fine.

What the server can see instead is *timing*. A client's ZDO updates come from its own update loop,
so they arrive at whatever pace that machine manages. The rate is useless — the sender is gated at
about 20 Hz, so 30 fps and 200 fps look identical — but the **gaps** are not. A 479 ms frame, the
worst measured on that machine, is a 479 ms hole in the stream, and no healthy client produces one.

So a stall is a gap over 0.3 s, and a player is flagged above `Stalls Per Minute` averaged over two
minutes. Clearing the flag takes five clean minutes — deliberately harder than earning it, so a
borderline machine does not flap ownership back and forth.

Honest limits:

- A gap says that machine stopped sending, not *why*. A frozen client and a hiccuping connection
  look the same. That's acceptable, since routing other players' work through either is a bad idea.
- The last healthy player on the server is never flagged. If everyone is struggling there is nobody
  to hand work to, and flagging everyone would only churn ownership.
- Flags live in memory only. They are never written to the known-ids file and are dropped when the
  player disconnects — that file is for identities you chose, not guesses the mod made.

## Ships

A ship is simulated by its owner, and steering is sent to that owner in 0.2 s batches, so a captain
who does not own the hull waits roughly a quarter second for every turn. Vanilla only reassigns a
ship when its owner is *not* aboard, and then hands it to an arbitrary passenger rather than to the
captain - so the wrong person often owns it.

With `Assign Ship To Captain`, the ship follows the helm.

Note this can *give* a yielding player an object the rest of the mod would take away, whenever they
are the one steering. That is deliberate, and switchable. `Yielding Player Retains Boat Helm
Ownership` decides it:

- **On (default)** - they keep the helm. Their steering is responsive; the hull lurches for everyone
  aboard whenever their machine stalls past Unity's catch-up limit.
- **Off** - the ship goes to a capable player. Smoother for passengers, but the captain steers
  through a delay, and a captain fighting a mushy helm is the one who puts the boat into a rock.

Which is better is an open question - reasoned about, not measured. Try both.

An unattended ship has no captain, so it falls back to the ordinary yield rules and moves off a
struggling machine like anything else.

## Compatibility

Known conflict: **ValheimPerformanceOptimizations** replaces `ZDOMan.ReleaseNearbyZDOS` with its own
implementation, which is the vanilla method the yielding half of this mod works through. With it
installed, `Yield Players` has no effect. Helm ownership is unaffected, because the ship pass runs
on its own rather than through that method.

Rather than fail quietly, it checks twice and says so in the server log — once by asking Harmony who
else has patched that method, and once by noticing that its own decision was never consulted while
players were connected. If you see a `NOT WORKING` line, believe it.

**ServersideQoL_MultiplayerTweaks** also reassigns ownership, but by proximity — it gives objects to
the *closest* player. That directly contradicts this mod whenever the closest player is the one you
are steering away from. Run one or the other.

## Credits

The conflict-detection approach is borrowed from `balrond_core_optimizer`, which checks Harmony
ownership of its targets and stands down rather than trusting patch order.

[dsl]: https://thunderstore.io/c/valheim/p/DeathMonger/DiagnoseServerLag/
