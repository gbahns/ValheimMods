# Changelog

## 0.8.4 — 2026-09-20

- New "Lock Configuration" setting (General, on by default, the server's): while on, the synced
  settings -- the placement rules and every shrine recipe -- can be changed only by the server's
  admins, from their own game, and every other player sees them read-only. Before this any
  player could change them for the whole server. Not a breaking change: 0.8.3 clients can still
  connect (this release stops pinning the required version to the current one).

## 0.8.3 — 2026-09-13

**Server and clients update together.** The version check in this mod has never been enforced,
because the plugin never loaded on a server to enforce it. Once the server runs 0.8.3, a client
still on 0.8.2 is refused at connect ("Forsaken Shrines needs to be at least version 0.8.3").
A client with no Forsaken Shrines at all is unaffected and can still join.

- **Fixed: the mod could not be installed on a dedicated server, and shrines built near the world center were being deleted.** The plugin carried `[BepInProcess("valheim.exe")]`, which makes BepInEx skip it entirely under `valheim_server.exe`. The shrines are new prefabs, so a server without the mod cannot resolve them, and Valheim destroys the ZDOs it cannot instantiate — permanently, and without telling anyone. The gate is gone; **install Forsaken Shrines on the server as well as on every client.**
- Server-synced settings now actually sync. With the plugin blocked from loading server-side there was no config for an admin to edit and nothing authoritative to push, so every client silently ran its own placement rules and recipes.
- The Thunderstore listing now says client-side as well as server-side. It only ever claimed server-side, which was both incomplete and, until this release, untrue.

## 0.8.2 — 2026-09-09

- **Fixed: spinning camera and character frozen in the wake-up pose on login (Valheim 1.0).** Jotunn 2.29.2's PieceManager patches `PieceTable.UpdateAvailable` through a field Valheim 1.0 removed. The patch throws, the game aborts player spawning part-way through loading whenever a Hammer is equipped, and retries every physics tick. Forsaken Shrines no longer touches Jotunn's PieceManager at all; pieces are configured and registered directly with the game, so the mod works on Jotunn 2.29.2 as well as 2.30.0.
- The separate **Forsaken Shrines** hammer tab is gone: Valheim 1.0 hard-limits build categories to the vanilla set, and Jotunn 2.30.0 does not support custom categories on 1.0 yet. Shrines now sit in the same tab as the vanilla stone (Stonecutter) pieces.
- New `[General] Mod Enabled` setting (client-side, not synced): set to false to switch the mod off without removing the DLL. Takes effect on the next launch.
- A shrine now appears in the Hammer as soon as its boss dies, and on spawn, instead of after the next inventory change.
- Rebuilt for Valheim 1.0.7 (Unity 6): implements the new `Hoverable.GetHoverOffset` (0.8.1's shrine component fails to load on 1.0 at all), adapts to the changed `Inventory.AddItem` signature, and registers the `ShrineSpawnAll` / `ShrineSpawnTrophies` dev commands directly with the game because Jotunn 2.29.2's command registration fails on 1.0.
- Bonemass shrine: Wraith trophy cost lowered from 4 to 2. Server-synced via `[Recipe.shrine_bonemass]`; a cfg that already has 4 written keeps 4 until updated.
- Dependencies bumped to BepInExPack 5.4.2350 and Jotunn 2.30.0.

**Note:** the spawn loop is a Jotunn 2.29.2 problem on Valheim 1.0, and any other mod that registers build pieces through Jotunn's PieceManager triggers it too. Jotunn 2.30.0 fixes it for those mods; update Jotunn if you still see it with Forsaken Shrines 0.8.2 installed.
