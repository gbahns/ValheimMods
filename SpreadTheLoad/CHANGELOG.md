# Changelog

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
