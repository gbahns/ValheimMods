# Hungry Viking — Packaging Reference

How to build, deploy, and package this mod for Thunderstore. See also `../THUNDERSTORE_PACKAGING.md` for the generic guide.

---

## Key Identifiers

| Field | Value |
|---|---|
| Thunderstore team | `DeathMonger` |
| Thunderstore mod name | `Hungry_Viking` |
| Thunderstore URL | `https://thunderstore.io/c/valheim/p/DeathMonger/Hungry_Viking/` |
| BepInEx GUID | `DeathMonger.HungryViking` |
| Assembly name | `HungryViking` |
| Config file (in-game) | `BepInEx/config/DeathMonger.HungryViking.cfg` |

---

## Version Bump Checklist

Update the version string in **all three places** before packaging a new release:

1. [HungryVikingMod.cs](HungryVikingMod.cs) — `[BepInPlugin(ModGuid, "Hungry Viking", "X.Y.Z")]`
2. [manifest.json](manifest.json) — `"version_number": "X.Y.Z"`
3. Pass `-Version "X.Y.Z"` to `package.ps1` (controls the output zip filename)

---

## Build, Deploy, and Package

Run from the project directory:

```powershell
powershell -ExecutionPolicy Bypass -File package.ps1 -Version "1.0.1"
```

This will:
1. Build the Release DLL (`bin\Release\net462\HungryViking.dll`)
2. Auto-deploy `HungryViking.dll` to the Thunderstore Default profile (if it exists on this machine)
3. Create `HungryViking-1.0.1.zip` in the project directory

---

## What Goes in the ZIP

| ZIP entry | Source file |
|---|---|
| `manifest.json` | `manifest.json` |
| `README.md` | `README.md` |
| `icon.png` | `icon.png` (256×256, wood-background Thunderstore icon) |
| `BepInEx/plugins/HungryViking.dll` | `bin\Release\net462\HungryViking.dll` |

Note: `status_icon.png` (the transparent-background in-game icon) is **embedded inside the DLL** as a managed resource — it is not a separate ZIP entry.

---

## Icon Files

| File | Purpose |
|---|---|
| `icon.png` | 256×256 Thunderstore package icon (wood background) |
| `status_icon.png` | 256×256 in-game HUD status icon (transparent background, embedded in DLL) |
| `HungerPangsLogo.png` | Original 2048×2048 source image (wood background) |
| `HungryViking-ClearBG.png` | 2048×2048 source image (blue chroma-key background, used to generate `status_icon.png`) |

To regenerate `status_icon.png` from `HungryViking-ClearBG.png` (e.g. after a new AI-generated image):
1. The source image has a solid blue background — use the chroma-key PowerShell block in session history, or re-run the approach: sample the corner pixel color, flood-fill transparent within tolerance ~40, resize to 256×256.
2. Check the bottom-right corner for watermarks and erase that patch (last ~35×35 pixels) if present.
3. Rebuild and repackage so the new icon is embedded in the DLL.

---

## Uploading to Thunderstore

1. Go to `https://thunderstore.io/c/valheim/p/DeathMonger/Hungry_Viking/`
2. Log in as DeathMonger and click **Upload**
3. Select `HungryViking-X.Y.Z.zip`
4. Thunderstore will validate and publish; the new version appears immediately

---

## Local Test Deploy

The `.csproj` auto-copies the DLL to the Thunderstore **Default** profile on every build (Release or Debug), as long as that profile folder exists on the machine. No manual copy needed — just build and launch Valheim via Thunderstore Mod Manager.
