---
name: mod-status
description: Check whether every Valheim mod in this repo is up to date — local version numbers agreeing with each other, the versions live on Thunderstore and Hexium, whether the Release builds are current, and whether the DatHost server holds the DLLs that were built here. Use when asked what needs publishing, whether the mods or the server are current, or for an overview of the mods in this repo.
---

# Are the mods up to date?

Run the board. It is read-only: nothing is built, uploaded, published, or restarted.

```powershell
.\mod-status.ps1                    # local files + both registries
.\mod-status.ps1 -Server            # also hash-check the DatHost server
.\mod-status.ps1 -NoRemote          # local only, no network
.\mod-status.ps1 -Mod TheGreatestMap
```

Add `-Server` whenever the question involves the server, or when a release just happened.
It needs `%USERPROFILE%\.dathost` and takes ~30s longer because each DLL is downloaded to hash.

## Reading the columns

| Column | Means |
| --- | --- |
| `Local` | `version_number` from manifest.json. A trailing `(!)` means the version numbers in the mod's own files disagree — the "Needs attention" list names each one. A trailing `*` means CHANGELOG.md marks it unreleased, so it is the release being prepared (see below). |
| `Thunderstore` / `Hexium` | the latest version live on that site. `unlisted` = the mod has no `thunderstore.toml` and has never been published. |
| `Build` | `current`, `STALE` (a .cs or .csproj is newer than the Release DLL), or `not built`. |
| `Git` | uncommitted changes under that mod's folder. |
| `Server` | the version the server's own DLL reports, and how it compares to this repo's build. |

Server states, read out of the DLL rather than guessed from a hash (`Directory.Build.props`
stamps each mod's manifest version into its assembly, and the SDK appends the commit):

- `0.2.4 exact` — the identical build is deployed.
- `0.2.4 rebuild` — same version, same commit, recompiled. Not a problem; a build that was
  copied into a Gale profile and pushed from there looks like this.
- `0.2.4 other commit` — same version number, built from different source. Worth a look.
- `0.2.3 BEHIND` — the server is running an older version than this repo builds. Deploy.
- `0.2.5 ahead` — newer than this repo. Someone else built it; don't overwrite it blindly.
- `unstamped` — the DLL predates version stamping, so it cannot be placed. One deploy fixes it.
- `client-only` — `mods.json` says the server has no use for it, so nothing is compared.
- `client-only, still there` — as above, but a copy is sitting on the server doing nothing.
- `absent` — not on the server, and not declared client-only either.

`mods.json` is what makes those first two possible: it records whether each mod is `client`,
`server` or `both`, `deploy-dathost.ps1` skips the client ones, and this script cross-checks the
file against each mod's own `[BepInProcess("valheim.exe")]` attribute — the thing BepInEx actually
enforces — so a mod declared server-side that cannot load on a server gets called out, as does a
store listing that promises the same.

One blind spot: a mod whose real version *is* 1.0.0 cannot be told apart from an unstamped
build, since both report 1.0.0.0. Those rows fall through to the commit comparison.

## Versions being prepared

The convention in `mod-release` is to bump all four version files straight after publishing, so a
healthy tree normally carries a version the sites have never seen. Where CHANGELOG.md marks the
newest heading `unreleased`, the script compares the registries and the server against the newest
heading *below* it — the last version actually released — and marks the row with `*`. A server
holding that released version reads `0.2.5 released`, not `BEHIND`.

So `Local 0.2.6* / Thunderstore 0.2.5 / Server 0.2.5 released` is the steady state mid-development,
not three things to fix. Drop the `unreleased` marker at release time and the comparison moves.

The script ends with a "Needs attention" list; if it is empty everything agrees.

Two of those lines catch a trap the version columns cannot, because every column reads green
while it is happening — the mod's version files agree with each other *and* with both registries,
while the repo holds work that was never published:

- **"code committed since X went out, with no bump to carry it"** — .cs commits landed after the
  published version shipped, and none of them bumped manifest.json. That work is not in anyone's
  game. Publishing without a bump would either be refused by Thunderstore as a duplicate or, on
  Hexium, put different code under a number players already have.
- **"the version files say X but CHANGELOG.md's newest entry is Y"** — the notes and the number
  will ship out of step. This also catches the subtler habit of folding new work into the entry
  for a version that is already public, which tells anyone already on it that they have fixes
  they do not.

## After reading it

- Anything behind on a registry or on the server → offer the `mod-release` skill (`/mod-release <Mod>`), don't start publishing unprompted.
- `StationExtensionGuard` is unlisted on purpose. Report it as local-only, not as a problem.
- A `(!)` version disagreement must be fixed before any publish. `package.ps1` blocks on manifest.json vs the tomls, but nothing except this script checks the version in the `BepInPlugin` attribute.
- The script cannot see the one kind of drift that arrives on its own: a Valheim, BepInEx, or Jotunn update breaking an already-published mod. When Greg asks whether the mods are still *working* (not just current), that is a separate check — game version, the Libs refresh, and the mod pages' comments.
