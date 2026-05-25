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

Update the version string in **all three places** before packaging a new release:

1. [ForsakenShrinesMod.cs](ForsakenShrinesMod.cs) — `public const string ModVersion = "X.Y.Z";`
2. [manifest.json](manifest.json) — `"version_number": "X.Y.Z"`
3. Pass `-Version "X.Y.Z"` to `package.ps1` (controls the output zip filename)

---

## Build, Deploy, and Package

Run from the project directory:

```powershell
powershell -ExecutionPolicy Bypass -File package.ps1 -Version "0.8.1"
```

This will:
1. Build the Release DLL (`bin\Release\net48\ForsakenShrines.dll`)
2. Auto-deploy `ForsakenShrines.dll` to every Thunderstore profile on this machine (via the csproj's `DeployToProfiles` target)
3. Create `ForsakenShrines-0.8.1.zip` in the project directory

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

1. Go to [thunderstore.io](https://thunderstore.io), log in, and navigate to your team.
2. Click **Upload** and select `ForsakenShrines-X.Y.Z.zip`.
3. Thunderstore validates manifest, icon dimensions, and zip structure before accepting.
4. First upload creates the listing; subsequent uploads with the same `name` are treated as updates.

---

## Local Test Deploy

The `.csproj` auto-copies the DLL to every Thunderstore profile on every build (Release or Debug). No manual copy needed — just build and launch Valheim via Thunderstore Mod Manager.
