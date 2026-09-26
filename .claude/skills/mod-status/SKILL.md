---
name: mod-status
description: Check whether every Valheim mod in this repo is up to date — local version numbers agreeing with each other, the versions live on Thunderstore and Hexium, whether the Release builds are current, and whether the DatHost server holds the DLLs that were built here. Use when asked what needs publishing, whether the mods or the server are current, or for an overview of the mods in this repo.
---

# Are the mods up to date?

Run the board. It is read-only: nothing is built, uploaded, published, or restarted.

```powershell
.\mod-status.ps1                    # local files + both registries
.\mod-status.ps1 -Server            # also the DatHost server, by version number
.\mod-status.ps1 -Server -Deep      # ... comparing the DLLs byte for byte
.\mod-status.ps1 -NoRemote          # local only, no network
.\mod-status.ps1 -Mod TheGreatestMap
```

Add `-Server` whenever the question involves the server, or when a release just happened. It needs
`%USERPROFILE%\.dathost` and costs about two seconds, from two cheap reads:

1. **the server's BepInEx log** — names every plugin it loaded, with its version, so one 70 KB fetch
   answers for every mod, and answers about what is *running*;
2. **the plugins listing** — path and size per file, which is what the server holds *on disk*.

Size is weak evidence of sameness (TheGreatestShips 0.9.3 and 0.9.4 are byte-for-byte the same
length, which once made a size-only comparison call two servers identical when they were not) but
strong evidence of difference. That asymmetry is what separates "never deployed" from "deployed and
waiting for a restart" — states that read alike and call for opposite actions.

Those are the only free facts the DatHost API offers. Tested 2026-09-26: the listing returns
`path`, `size`, `deleted` and nothing more under any query parameter; HEAD on a file returns neither
`Content-Length` nor `Last-Modified` nor an `ETag`; and byte-range requests are ignored, so a DLL's
version resource cannot be read without pulling the whole file. Anything beyond version numbers
therefore costs a download per mod, which is `-Deep`.

Trust those version numbers by default. `-Deep` downloads a DLL per mod and compares hashes, taking
around 30s instead, and earns that only when a version number is itself in doubt — two builds
wearing one version, or a published number that quietly covers changed source. Reach for it when a
row looks wrong rather than as a matter of course.

The fast path reports what the server **loaded**, which is a slightly different question from what
sits in its plugins folder, and usually the better one: a DLL uploaded without a restart still shows
its old version, because that is the code players are meeting. `on disk, not loaded` is that case
named outright — the file is there and no running plugin matches it.

## Reading the columns

| Column | Means |
| --- | --- |
| `Local` | `version_number` from manifest.json. A trailing `(!)` means the version numbers in the mod's own files disagree — the "Needs attention" list names each one. A trailing `*` means CHANGELOG.md marks it unreleased, so it is the release being prepared (see below). |
| `Thunderstore` / `Hexium` | the latest version live on that site. `unlisted` = the mod has no `thunderstore.toml` and has never been published. |
| `Build` | `current`, `STALE` (a .cs or .csproj is newer than the Release DLL), or `not built`. |
| `Git` | uncommitted changes under that mod's folder. |
| `Server` | the version the server is running, and how it compares to this repo's build. From the server's log by default; from the DLL itself under `-Deep`. |

Server states (`Directory.Build.props` stamps each mod's manifest version into its assembly, and
the SDK appends the commit, which is what lets `-Deep` tell two builds of one version apart):

- `0.2.4 matches` — the server is running the version this repo builds, and the file on disk is the
  size of that build. The default verdict.
- `0.2.4 disk differs` — running the right version, but the DLL on disk is a different size, so a
  restart would load something else. `-Deep` on that one mod says what.
- `0.2.3 BEHIND, uploaded` — running an older version while the file on disk is the size of this
  build: already deployed, waiting for a restart. Deploying again changes nothing.
- `0.2.4 exact` — `-Deep` only: the identical build, byte for byte.
- `0.2.4 rebuild` — `-Deep` only: same version, same commit, recompiled. Not a problem; a build that
  was copied into a Gale profile and pushed from there looks like this.
- `0.2.4 other commit` — `-Deep` only: same version number, built from different source. Worth a look.
- `0.2.3 BEHIND` — the server is running an older version than this repo builds. Deploy it — or, if
  it was already uploaded, restart the server, which the fast path cannot tell apart.
- `on disk, not loaded` — the DLL is in the plugins folder but no loaded plugin matches it: an
  upload waiting for a restart.
- `0.2.5 ahead` — newer than this repo. Someone else built it; don't overwrite it blindly.
- `-----------` — `mods.json` says the server has no use for it, so nothing is compared. A dashed
  rule rather than words, so the rows that do say something stand out.
- `client-only, still there` — as above, but a copy is sitting on the server doing nothing. This one
  keeps its words: it is a finding, not a blank.
- `absent` — not on the server, and not declared client-only either.

`mods.json` is what makes those first two possible: it records whether each mod is `client`,
`server` or `both`, `deploy-dathost.ps1` skips the client ones, and this script cross-checks the
file against each mod's own `[BepInProcess("valheim.exe")]` attribute — the thing BepInEx actually
enforces — so a mod declared server-side that cannot load on a server gets called out, as does a
store listing that promises the same.

One blind spot: a DLL built before versions were stamped reads `1.0.0.0`, exactly like a correctly
stamped 1.0.0, and nothing in the metadata separates them — only a hash against the published
artifact would. The script reads `1.0.0.0` as the version 1.0.0 rather than guessing: right when it
is one, and when it is genuinely an old build the row comes out `BEHIND`, which asks for the deploy
that fixes it either way.

## Versions being prepared

The convention in `mod-release` is to bump when a change needs it, in the same commit as the work,
so a mod level with its published version has nothing pending and one above it does. While a
release is being prepared, CHANGELOG.md marks the newest heading `unreleased`; the script then
compares the registries and the server against the newest heading *below* it — the last version
actually released — and marks the row with `*`. A server holding that released version reads
`0.2.5 released`, not `BEHIND`.

So `Local 0.2.6* / Thunderstore 0.2.5 / Server 0.2.5 released` is a release in progress, not three
things to fix. Drop the `unreleased` marker at release time and the comparison moves.

The `Server` comparison is DLL against DLL, so a **stale build** makes it meaningless: the DLL can
still carry a version the tree has moved off, and the row will claim the server is behind something
that was never released. Those lines say so and ask for a rebuild rather than a deploy — treat
`Build: STALE` as "this row is not evidence yet".

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
