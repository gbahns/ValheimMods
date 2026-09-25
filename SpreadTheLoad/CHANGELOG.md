# Changelog

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
