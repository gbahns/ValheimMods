# Grab Materials — Packaging Reference

How to build, deploy, and package this mod for Thunderstore. See also [../THUNDERSTORE_PACKAGING.md](../THUNDERSTORE_PACKAGING.md) for the generic guide.

---

## Key Identifiers

| Field | Value |
|---|---|
| Thunderstore team | `MojoRyzen` |
| Thunderstore mod name | `GrabMaterials` |
| Thunderstore URL | `https://thunderstore.io/c/valheim/p/MojoRyzen/GrabMaterials/` |
| BepInEx GUID | `DeathMonger.GrabMaterialsMod` |
| Assembly name | `GrabMaterials` |
| Config file (in-game) | `BepInEx/config/DeathMonger.GrabMaterialsMod.cfg` |
| Target framework | `net462` |

Note: the GUID ends in `…Mod` (not just `…GrabMaterials`). Don't "fix" this — changing the GUID would orphan every existing user's config file. The Thunderstore listing name and assembly name are both `GrabMaterials`; only the GUID carries the legacy suffix.

---

## Version Bump Checklist

Update the version string in **all three places** before packaging a new release:

1. [GrabMaterialsMod.cs](GrabMaterialsMod.cs) — `[BepInPlugin(GrabMaterialsMod.ModGuid, "Grab Materials", "X.Y.Z")]`
2. [manifest.json](manifest.json) — `"version_number": "X.Y.Z"`
3. Pass `-Version "X.Y.Z"` to `package.ps1` (controls the output zip filename)

---

## Build, Deploy, and Package

Run from the project directory:

```powershell
powershell -ExecutionPolicy Bypass -File package.ps1 -Version "2.0.1"
```

This will:
1. Build the Release DLL (`bin\Release\net462\GrabMaterials.dll`)
2. Auto-deploy `GrabMaterials.dll` to every Thunderstore profile on this machine (via the csproj's `DeployToProfiles` target)
3. Create `GrabMaterials-2.0.1.zip` in the project directory

---

## What Goes in the ZIP

| ZIP entry | Source file |
|---|---|
| `manifest.json` | `manifest.json` |
| `README.md` | `README.md` |
| `icon.png` | `icon.png` (256×256 Thunderstore icon) |
| `BepInEx/plugins/GrabMaterials.dll` | `bin\Release\net462\GrabMaterials.dll` |

---

## Uploading to Thunderstore

1. Go to `https://thunderstore.io/c/valheim/p/MojoRyzen/GrabMaterials/`
2. Log in as MojoRyzen and click **Upload**
3. Select `GrabMaterials-X.Y.Z.zip`
4. Thunderstore will validate and publish; the new version appears immediately

---

## NexusMods Distribution

The `Nexus/` subfolder is leftover from an older NexusMods distribution flow. It currently holds a stale `GrabMaterials.dll` (no longer tracked in git). If you publish to NexusMods, do it manually from the same `bin\Release\net462\GrabMaterials.dll` the Thunderstore package uses — don't rebuild the artifact under `Nexus/`.

---

## Local Test Deploy

The `.csproj` auto-copies the DLL to every Thunderstore profile on every build (Release or Debug). No manual copy needed — just build and launch Valheim via Thunderstore Mod Manager.
