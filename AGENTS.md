# ValheimMods

## Local builds deploy to BOTH test profiles

A plain `dotnet build -c Release` copies the mod's DLL into **two** Gale profiles:

- `Default SD Test`
- `Default HD Test`

Greg plays in either one and moves between them, so a build that reaches only one of them means
half his play sessions run code nobody changed. Both must always get the DLL. Asked for
2026-10-02, after only the SD profile had been receiving builds.

The list lives in one place, `DeployProfiles` in [Directory.Build.targets](Directory.Build.targets);
[deploy-to-profile.ps1](deploy-to-profile.ps1) does the copying. Don't add per-project copies of
this, and don't narrow the list to one profile.

- Just run `dotnet build -c Release`. It deploys to both by itself — no extra step, no manual copy.
- `-p:DeployProfiles=none` builds without copying anywhere. Only use it when you specifically do
  not want the DLL in the game, and say so; it silently leaves both profiles stale.
- `Default SD` is the production profile. **Never deploy there.** It gets mods from Gale the way
  players do, which is what makes it worth comparing against.
- A profile that has the mod installed from Thunderstore/Hexium gets the DLL overwritten inside
  that package folder, so one build never leaves two DLLs claiming the same plugin GUID.

Check what a build actually did: it prints one `Deployed <Mod> to <profile>` line per profile.
Two lines, or the deploy did not do its job.
