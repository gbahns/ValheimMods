---
name: mod-release
description: Release a Valheim mod from this repo — bump the version in every place it appears, update the changelog, build, package, publish to Thunderstore and Hexium, deploy to the DatHost server, and commit. Use when asked to release, publish, ship, or push a new version of a mod, or to get a mod up to date on both sites or on the server.
---

# Releasing a mod

One mod at a time. Steps 5 and 6 reach the outside world and need Greg's explicit go-ahead
each time — a published Thunderstore version cannot be withdrawn, and the server deploy stops
the server out from under whoever is playing.

Names below: `<Folder>` is the directory in this repo (e.g. `HungryViking`), `<Package>` is the
`name` in `thunderstore.toml` (e.g. `Hungry_Viking`). They are not always the same.

## 1. See where it stands

```powershell
.\mod-status.ps1 -Mod <Folder> -Server
```

Fix any `(!)` version disagreement before anything else, and don't re-publish a version that is
already live — Thunderstore rejects duplicate versions.

## 2. Pick the version

Use the one Greg names. If he hasn't: patch for a fix, minor for new behavior or config, and ask
rather than guess when it could be either.

## 3. Bump it everywhere

Four files hold the version, and all four have to move together:

- `<Folder>/manifest.json` → `version_number`
- `<Folder>/thunderstore.toml` → `versionNumber`
- `<Folder>/hexium.toml` → `versionNumber` (some mods have no hexium.toml; then it is Thunderstore only)
- `<Folder>/<Folder>Mod.cs` → the `BepInPlugin` version: a `ModVersion` constant in the newer
  mods, a literal third argument in the older ones

`package.ps1` refuses to publish when manifest.json and the two tomls disagree, and it also
compares the tomls field by field. Two exceptions to that safety net:

- `TheGreatestMap/package.ps1` is the older, shorter script and has no such guard — it will
  publish disagreeing metadata without complaint.
- Nothing but `mod-status.ps1` checks the `BepInPlugin` version. A stale one ships a DLL that
  reports the wrong version in the BepInEx log and to other mods.

## 4. Changelog and docs

Add the new version at the top of `<Folder>/CHANGELOG.md` in the format already there, and
update `README.md` if behavior or config keys changed. Both ship inside the zip. American
spelling everywhere a player can see it (favorite, center, color).

## 5. Build and package

```powershell
cd <Folder>
.\package.ps1 -Version <x.y.z>
```

`-Version` only names the zip file; the version that actually publishes comes from
manifest.json, and the script warns if the two differ.

Then, **after Greg confirms**, publish to both sites:

```powershell
.\package.ps1 -Version <x.y.z> -Publish -Hexium
```

`-Publish` needs `TCLI_AUTH_TOKEN` in the environment; `-Hexium` reads the token from
`%USERPROFILE%\.hexium_token`. Never print either token.

Hexium used to hide every version uploaded through the API until Greg unhid it on the site, so a
successful publish was not yet a visible listing. He found the setting that turns that off and
enabled it on 2026-09-14, so new versions should now appear by themselves. Don't assume either
way — `.\mod-status.ps1` reads each site's live `latest`, so let it confirm, and only send him to
valheim.hexium.gg if Hexium is still a version behind.

## 6. The server

Only if the server needs this mod at all. `mods.json` records which side each mod belongs on, and
`deploy-dathost.ps1` skips anything marked `client` — seven of the eleven mods here carry
`[BepInProcess("valheim.exe")]`, which makes BepInEx skip the plugin under `valheim_server.exe`,
so uploading them achieves nothing. If a new mod has no `mods.json` entry, add one.

**After Greg confirms** (it stops the server):

```powershell
.\deploy-dathost.ps1 -Mod <Folder>
.\deploy-dathost.ps1 -Mod <Folder> -WhatIf     # plan only, changes nothing
```

It triggers a world save through TheGreatestMap's `save-now` file and waits for the save to
complete before stopping, then uploads, starts, and waits until DatHost reports it running.
In `-Mod` mode it always plans an upload without comparing hashes, so use
`.\mod-status.ps1 -Mod <Folder> -Server` to know whether a deploy is actually needed.

Several mods in one restart: `-Mod A,B,C`.

## 7. Commit

Match the log's style — `<Mod name> <x.y.z>: <what changed, lowercase>`, e.g.
`PauseMyServer 1.3.0: any mod can ask for a pause, not just the ESC menu`. Include the
attribution line this session was given.

## 8. Confirm

```powershell
.\mod-status.ps1 -Mod <Folder> -Server
```

Registries can take a minute to report the new version.

## Bump the version straight after publishing, not before

The last step of a release is to open the next one: raise the version in all four files, add a
`## <next> — unreleased` heading to the changelog, and commit that on its own. Then the version
in the tree always names the release being prepared, never the one that shipped.

That is what makes the tree readable by a session that was not there. "Local version equals the
version live on the sites" becomes an error worth stopping for, rather than the normal state
right after a release, and `mod-status.ps1` already computes that comparison. Bump only at
release time and the healthy tree and a tree full of unpublished edits look identical, which has
already caused unreleased code to sit under a published number more than once.

At release time, replace `— unreleased` with the date. If the work turned out bigger than the
number you guessed, raise it again then; that is free, and publishing over a live number is not.

## TheGreatestMap needs three extra checks

It is the one mod here that genuinely runs on the server, so the usual "client-only, the server
can wait" assumption does not hold.

**Is the compatibility floor moving?** `MinCompatibleVersion` in `TheGreatestMapMod.cs` is what
ServerSync uses to refuse older clients. Compare it against the last released version before
publishing:

- unchanged (it has been `0.2.0` since that release) — a client-only release is fine and the
  server can lag behind.
- changed — the update is no longer optional for anyone. Say so at the top of the changelog
  entry in bold, deploy the server when nobody is playing, and tell Greg his players are locked
  out until they update, because they are kicked at the next connect.

**Does the server actually need this release?** The shared marker store lives on the server, and
the server is also the source of the live portal list. A release that touches either needs a
deploy, not just a publish. `.\mod-status.ps1 -Mod TheGreatestMap -Server` says whether the
server holds the current DLL.

**Every other mod's deploy depends on this one.** `deploy-dathost.ps1` gets its verified
pre-stop save from the `save-now` trigger file, and that trigger is implemented inside
TheGreatestMap (`ServerSave.cs`), watching `BepInEx/config/TheGreatestMap/`. DatHost's stop is a
hard kill, so without it a restart loses whatever the world had not autosaved. If a deploy of any
mod ever fails its save check, look first at whether the server's TheGreatestMap is present,
current and loading.

## Gotchas

- Folder name ≠ package name: `HungryViking` publishes as `Hungry_Viking`. Read `namespace` and
  `name` out of `thunderstore.toml`; never build a URL from the folder name.
- `StationExtensionGuard` has no tomls and has never been published. Don't publish it without asking.
- Never build `GrabMaterialsTests`, `GrabMaterialsTests2`, or `TestMod`; the test projects have
  never worked and `build-all.ps1` skips them.
- Jotunn 2.30.0 is the version that works on Valheim 1.0 — 2.29.2's PieceManager causes a spawn loop.
