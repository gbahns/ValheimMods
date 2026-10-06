# pinmerge

Merges two of TheGreatestMap's pin stores outside the game.

```
pinmerge <base.bin> <incoming.bin> <out.bin>
```

A pin store lives at `BepInEx/config/TheGreatestMap/<world>_<seed>.pins.bin`. `<incoming>` is
merged into `<base>`; the result is written to `<out>` and read back before the tool reports
success, because a store that cannot be reloaded is not a merge, it is a corrupt file.

## Why it exists

Moving a world between servers means moving three things, and the pin store is the one that cannot
simply be copied. Copy the world and the destination's pins are wrong. Copy the source's pins over
the destination's and every marker anyone placed on the destination since the last copy disappears
— which is exactly what players notice.

So the pins get merged, and `pull-dathost-config.ps1` calls this tool to do it.

## Why it is trustworthy

It calls the mod's own `MapStore.Merge`. `MapStore.cs` and `SharedPin.cs` are **linked** into this
project from `../../TheGreatestMap/`, not copied, so the rules here cannot drift from the rules the
game uses:

- a later change wins
- a tombstone beats an older pin, so nothing anyone deliberately erased comes back
- tombstones keep the later timestamp, whichever side it came from
- suppressions (erased spots, which stop a marker being re-recorded) are unioned

Merging is **idempotent** — merging a result with itself reports `added 0, updated 0` — so re-running
a transfer is harmless.

`build-all.ps1` compiles this project, which is the point: if a change to TheGreatestMap breaks it,
that surfaces on the next build rather than the next time a world is moved.

## The two stubs

The mod's store needs a handful of symbols that only exist inside the game. Two small files supply
them:

- **`CategoryStub.cs`** — the `Category` enum, copied verbatim so the member set matches. Only
  `SharedPin.KindCategory` parses it and merging never calls that; `Kind` travels as a string, so
  it cannot affect what is read or written.
- **`IconRegistryStub.cs`** — `Normalize` and `SameKey`, copied verbatim. `SameKey` decides whether
  two suppressions are the same during a merge, so a paraphrase could silently drop an erased spot.
  `LegacyKey` throws instead of guessing: it is only reachable for stores older than format
  version 2, and a wrong icon is worse than a refusal.

Everything else — `Catalog.cs`, `LocationIndex.cs`, the real `IconRegistry` — reaches into
`TgmConfig`, `Localization`, `Minimap`, `ZNet` and `Player`, and pulls in most of the mod.

## Runtime dependencies

The Unity assemblies are built against netstandard 2.1 while net48 ships only the 2.0 facade, so
`Libs/netstandard.dll` is required **beside the exe** or every run dies with
`Could not load file or assembly 'netstandard, Version=2.1.0.0'`. The project copies it, along with
`assembly_valheim`, `assembly_utils` and the UnityEngine assemblies, into the output folder. That
is the only reason those references are `Private=true`.

## Moving a world between servers

In order. Each step is safe to re-run.

1. **The world.** `.\pull-dathost-world.ps1 -Install`
   Takes the file listing, uses the newest save generation that has an `.ok` marker, verifies every
   file against its expected size, then re-reads the listing and refuses to install if anything
   changed — 60-odd sequential downloads leaves room for a save to land mid-copy, and a folder
   holding regions from two generations is not a loadable world. The world it replaces is moved
   aside under a dated name.

2. **The server-side mod settings, the shared map and the stats store.**
   `.\pull-dathost-config.ps1`
   Settings are replaced; the pin store is **merged** with this tool; the stats store and
   SpreadTheLoad's known-ids list are replaced. `BepInEx.cfg` is deliberately left alone — it
   configures the loader, not a mod, and the two machines are not the same environment. Old
   captures, runtime state and client-side assets are left behind. The whole previous config
   folder is copied to `_before-dathost-copy-<stamp>` first.

   It also **compares** the source's `adminlist.txt`, `permittedlist.txt` and `bannedlist.txt`
   against the destination's and reports any id the destination is missing. Those live in
   `SaveDir`, as siblings of `worlds_local` rather than inside a world, so one server applies the
   same lists to every world it hosts: they belong to the destination server, not to the world
   being moved, and copying them would overwrite that server's own admins with the source's. They
   are never copied - but an admin silently absent on the new server is not noticed until a
   command is refused, and `dsl_bench`'s server capture is one of the things that refuses.

3. **The mods themselves**, if the destination does not already have them:
   `.\deploy-dathost.ps1 -Mod <Folder> -Published` for DatHost, or
   `valheim.ps1 deploy -Dll <path>` for the local server.

Both scripts stop the destination server first, because BepInEx reads config at startup and
several mods rewrite their file on shutdown — copying into a running server gets partly undone a
few minutes later, and a running server writes its in-memory world back over a replaced one.

**Stop the source server before a cutover you mean to keep.** While it is up it is still accepting
players, and anything they do after the copy is left behind on a world nobody is looking at.

### Checking it worked

Comparing config files byte for byte will mislead you: BepInEx rewrites every file on startup in
its own canonical form — comment text, key order, and the key set of whichever mod version is
installed — so files can differ while every setting agrees, and can match while a value was lost.
Compare `key = value` pairs, not bytes.
