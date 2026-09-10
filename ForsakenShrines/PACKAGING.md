# Forsaken Shrines — Packaging Reference

How to build, deploy, and package this mod for Thunderstore. See also [../THUNDERSTORE_PACKAGING.md](../THUNDERSTORE_PACKAGING.md) for the generic guide.

---

## Key Identifiers

| Field | Value |
|---|---|
| Thunderstore team | _not yet published — set on first upload_ |
| Thunderstore mod name | `ForsakenShrines` |
| Thunderstore URL | _not yet published_ |
| BepInEx GUID | `DeathMonger.ForsakenShrines` |
| Assembly name | `ForsakenShrines` |
| Config file (in-game) | `BepInEx/config/DeathMonger.ForsakenShrines.cfg` |
| Target framework | `net48` (required by ServerSync's C# 12 features) |

---

## Version Bump Checklist

Update the version string in **all four places** before packaging a new release:

1. [ForsakenShrinesMod.cs](ForsakenShrinesMod.cs) — `public const string ModVersion = "X.Y.Z";`
2. [manifest.json](manifest.json) — `"version_number": "X.Y.Z"`
3. [thunderstore.toml](thunderstore.toml) — `versionNumber = "X.Y.Z"`
4. Pass `-Version "X.Y.Z"` to `package.ps1` (controls the output zip filename)

---

## Build, Deploy, and Package

Run from the project directory:

```powershell
powershell -ExecutionPolicy Bypass -File package.ps1 -Version "0.8.1"
```

This will:
1. Build the Release DLL (`bin\Release\net48\ForsakenShrines.dll`)
2. Auto-deploy `ForsakenShrines.dll` to every Gale profile on this machine (via the csproj's `DeployToProfiles` target)
3. Create `ForsakenShrines-0.8.1.zip` in the project directory

To also **upload to Thunderstore** in one step, add `-Publish`:

```powershell
powershell -ExecutionPolicy Bypass -File package.ps1 -Version "0.8.1" -Publish
```

See [Uploading to Thunderstore](#uploading-to-thunderstore) below for the one-time setup of `tcli` and the auth token.

---

## What Goes in the ZIP

| ZIP entry | Source file |
|---|---|
| `manifest.json` | `manifest.json` |
| `README.md` | `README.md` |
| `icon.png` | `icon.png` (256×256 Thunderstore icon) |
| `BepInEx/plugins/ForsakenShrines.dll` | `bin\Release\net48\ForsakenShrines.dll` |

---

## Icon Files

| File | Purpose |
|---|---|
| `icon.png` | 256×256 Thunderstore package icon |
| `logo.png` | Larger source/branding image (not included in package) |

---

## Uploading to Thunderstore

### Automated (recommended): `tcli` + `package.ps1 -Publish`

**One-time setup:**

1. Install the Thunderstore CLI:
   ```powershell
   dotnet tool install -g tcli
   ```
2. Get a Thunderstore service-account token: log in at [thunderstore.io](https://thunderstore.io) → **Settings** → **Teams** → pick your team → **Service Accounts** → create one, copy the token (`tss_...`).
3. Store the token as an environment variable (persists across sessions):
   ```powershell
   [Environment]::SetEnvironmentVariable("TCLI_AUTH_TOKEN", "tss_paste_your_token_here", "User")
   ```
   Open a new shell after running this so the variable is visible to subsequent `package.ps1` runs.

**Per-release:**

1. Bump the version in all four places (see [Version Bump Checklist](#version-bump-checklist)).
2. Run:
   ```powershell
   powershell -ExecutionPolicy Bypass -File package.ps1 -Version "X.Y.Z" -Publish
   ```
   This builds, zips, and uploads the new version to Thunderstore.

[thunderstore.toml](thunderstore.toml) drives the publish target (namespace, community, categories). It already points at the `DeathMonger` team and `valheim` community; first publish creates the listing on Thunderstore, subsequent publishes are treated as version updates.

### Manual fallback

1. Build the zip without `-Publish`.
2. Go to [thunderstore.io](https://thunderstore.io), log in, and navigate to your team.
3. Click **Upload** and select `ForsakenShrines-X.Y.Z.zip`.

---

## Local Test Deploy

The `.csproj` auto-copies the DLL to every Gale profile on every build (Release or Debug), using a rename-and-replace strategy that works even when Valheim has the DLL locked. No manual copy needed — just build and launch Valheim via r2modman.
