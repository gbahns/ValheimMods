# Changelog

## 0.2.1 - unreleased

- **Ownership is frozen near fighting.** The attacker rule already refused to move anything living,
  because a creature's target, path and alert timers are not all replicated and the new owner
  restarts its AI from whatever the ZDO holds - which in play is a boss that stops attacking. The
  yield rule had no such guard, which was an inconsistency rather than a decision: it works through
  a predicate that is handed a position and an owner and never learns which object it is being
  asked about, so it cannot tell a troll from a fence post.

  Greg reported exactly that symptom after a Gerhaffa fight, and it does not need the yielded player
  to have arrived first - ownership moves whenever an owner leaves an object's active area, so
  simply walking through was enough to acquire the boss and have it taken away again.

  Since the object cannot be identified, the guard is spatial: every damage RPC the server relays
  marks a place and a time, and ownership stops moving within 40 m of recent blows until 15 quiet
  seconds have passed. Coarse - it protects the fence posts in the fight too - but it errs the safe
  way, and a fight is the one moment when rebalancing has nothing to offer.

## 0.2.0

- **Every player is sent their world update each cycle, instead of one player per frame.** Vanilla
  serves exactly one peer per server frame, so each player hears from the server every
  (players + 1) frames. Measured on bahnsheim at its 30 Hz cap, and the model is exact at both
  ends: one player `(1+1) x 33.3 = 67 ms`, measured 67; five players `(5+1) x 33.3 = 200 ms`,
  measured 199, 200, 201 and 204.

  Five updates a second, with four other people in the world. Everything a player does not own -
  where everyone else is, what their creatures are doing, the ship they are standing on - arrives
  at that rate and is interpolated in between. It is the one cost that grows with the size of the
  group, and it is invisible to every other measurement: in that same capture the server held a
  perfect 33.3 ms tick on 17% of one core and stalled 3 times in 1800 seconds.

  The 50 ms gate in the vanilla loop never signifies, because `m_sendTimer` keeps accumulating
  during the serving frames and is always long past 0.05 by the end of a cycle. But it reads like
  an intended 20 updates a second, which the per-frame loop quietly turns into 20/N. This restores
  that intent: `Updates Per Second`, default 20, no longer divided by the player count.

  Safe because the real flow control is untouched. `SendZDOs` refuses outright when the socket's
  send queue is backed up and caps each package at what is left of 10 KB, so the socket's capacity
  still decides and the gain is self-limiting rather than a flood. On bahnsheim that queue sits at
  a median 3.7 KB against a refusal threshold near 8 KB, so expect real improvement rather than a
  clean six times.

## 0.1.4

- **A player being hit is no longer treated like a tree.** The attacker rule skipped creatures by
  looking for `BaseAI`, and a player does not have one - so when something hit a player, the damage
  RPC named that player's own ZDO and their character was handed to whoever swung. The receiving
  client then found it owned a `Player` that was not its local one and did what vanilla does in
  `Player.FixedUpdate`: logged "Destroying old local player" and destroyed it. The player's screen
  went black and they had to rejoin. The test is now `Character`, which every creature has too, so
  nothing living is ever moved.

- **A chest stays with the player who has it open.** Vanilla's container code assumes whoever has
  the window open owns the ZDO - `OnContainerChanged` saves only `if (IsOwner())`, and `Load()`
  repaints the panel whenever the data revision moves. Steering a yielded player's chest away
  mid-session broke both: the stack they dragged out reappeared in the chest a moment later while
  the one they took sat in their inventory. Nothing was ever duplicated and closing the chest
  cleared it, but it looked alarming. Open containers are now held with their user, the way a
  ship is held with its captain. The open request names the chest - it is routed through the
  server, and only travels at all when somebody else owns it - and the container's own `InUse`
  flag says when to let go.

## 0.1.3

- **A tree, rock or ore vein now goes to whoever is hitting it.** Vanilla never does this - TreeBase,
  TreeLog, Destructible and MineRock5 all open their damage handler with
  `if (!m_nview.IsOwner()) return;` and nothing anywhere calls ClaimOwnership - so the machine that
  loaded a tree keeps it and every swing anyone else makes travels there and back, for that tree and
  the next one. A chopping session is hundreds of interactions against a handful of objects, which
  makes this the commonest way a group feels somebody else's frame time.

  Resources only. A creature carries live AI state that is not all replicated, so moving one
  mid-fight can make it re-acquire its target or re-path - a real hitch in the least welcome moment,
  left alone until it can be measured rather than reasoned about.

  Three details that matter: the transfer waits 0.4s so the swing that triggered it lands first
  (the old owner's handler checks ownership, and changing it immediately drops that hit); a dwell
  time stops two players bouncing one tree between them; and it never hands an object to a player
  work is being steered *away* from, which would only churn.

- **Ship prefabs are found in the right registry.** The scan read `ZNetScene.m_prefabs`, the list
  serialised with the scene, and found the five vanilla hulls and none of TheGreatestShips'. Mods
  register into `m_namedPrefabs`, which is what `GetPrefab` reads. Now 13 hulls including every
  `DM_*` ship.

## 0.1.2

- **The yield list is switched off with the mod.** `IsYielding` did not check `Enabled`, so with
  the mod turned off but names still configured it answered yes about a rule that was not in force.
  The ship pass consults it, so this was a real bug rather than only a display one.
- **A small public API**, `SpreadTheLoadApi`, so DiagnoseServerLag can grey the rows of players
  work is being steered away from. This mod stays server-only and gains no client half; DSL's
  server half reads it and sends the answer down with its own report.

## 0.1.1

Both fixes come from the first run on a real server.

- **The "NOT WORKING" alarm was a false positive.** It fired on a healthy server with no
  conflicting mod installed. IsInPeerActiveArea is only reached from the second half of
  `(!zdo.HasOwner() || !IsInPeerActiveArea(...))`, so an ownerless object short-circuits past it and
  an object owned by the peer being processed takes the other branch entirely. It is consulted only
  when one player's active area contains an object somebody *else* owns - which never happens while
  everyone is off in their own corner. Silence now only corroborates the Harmony inspection instead
  of accusing on its own.
- **Modded ships were missed.** The prefab scan ran once at startup and found the five vanilla hulls
  and none of TheGreatestShips', because mods register their prefabs after ZNetScene exists. It now
  re-scans whenever the prefab list grows, which self-corrects however late a mod registers.

## 0.1.0

- First build. Server-side only; nothing to install on clients.
- **Steers object ownership away from named players.** Valheim simulates each object on the one
  machine that owns it, gives ownership to whoever was in range first, and never rebalances - so
  one struggling client's frame time becomes everybody else's input delay. This hands those
  objects to another player who is already in range, and hands back the ones already held.
  Anything only the named player is near stays theirs, so nothing is ever left unsimulated.
- **Players are identified by network id, not by character name.** A name is used once to find
  somebody; their id is remembered in `SpreadTheLoad-known-ids.txt` and used from then on, so the
  setting survives them playing a different character.
- **Optionally finds struggling machines by itself**, off by default. Not by ping or connection
  quality - the machine this was written for had a 24 ms ping and ran at 16 fps - but by timing the
  gaps between the updates each client sends. Rate saturates around 20 Hz and tells you nothing; a
  479 ms hole in the stream tells you plenty. Clearing a flag is deliberately harder than earning
  one, and the last healthy player is never flagged.
- **Ships follow the helm.** Vanilla only reassigns a ship when its owner is not aboard, and then
  picks an arbitrary passenger instead of the captain, while steering is batched to the owner every
  0.2s and the physics runs only there - so the person steering often waits a quarter second for
  every turn. Ship prefabs are found by component, so modded hulls are covered without a list.
  Whether a yielding player keeps the helm is a setting, defaulting to yes.
- **Config changes take effect without a restart.** BepInEx keeps the value parsed at startup, so
  without a file watcher an edited setting would do nothing until the process restarted - which on
  a dedicated server means disconnecting everybody to change who is being steered away from. The
  file is watched and reloaded, and every setting is read fresh each pass, so a change lands within
  a second.
- **Says so when it is not working.** ValheimPerformanceOptimizations replaces the vanilla method
  this mod works through, which would otherwise leave it silently inert. It checks both by asking
  Harmony who else patched that method and by noticing its own decision was never consulted while
  players were connected.
