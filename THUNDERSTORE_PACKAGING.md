# Thunderstore Packaging Guide — Valheim BepInEx Mods

How to build and publish a Valheim mod to Thunderstore. Covers the required package structure, ZIP spec requirements, version bumping, and upload process.

---

## Required Package Contents

Every Thunderstore package is a ZIP containing exactly these four files at the root level:

| File | Notes |
|---|---|
| `manifest.json` | Package identity and metadata |
| `README.md` | Shown on the Thunderstore mod page |
| `icon.png` | Must be exactly **256×256 PNG** |
| `BepInEx/plugins/<AssemblyName>.dll` | The compiled plugin |

---

## manifest.json

```json
{
    "name": "ModNameNoSpaces",
    "version_number": "1.0.0",
    "website_url": "",
    "description": "One-sentence description shown in search results.",
    "dependencies": ["denikson-BepInExPack_Valheim-5.4.2202"]
}
```

**Rules:**
- `name` — alphanumeric and underscores only, no spaces or hyphens. This becomes part of the mod's Thunderstore URL: `thunderstore.io/c/valheim/p/<TeamName>/<name>/`
- `name` must match the existing listing exactly (including case) for an upload to be treated as an update rather than a new mod
- `version_number` — must follow semver (`major.minor.patch`); Thunderstore rejects re-uploads of the same version
- `dependencies` — use the full `TeamName-ModName-Version` format; `denikson-BepInExPack_Valheim-5.4.2202` is the standard BepInEx dependency

---

## ZIP Spec Requirement — Forward Slashes

**Always use forward slashes in ZIP entry names.** The ZIP specification (§4.4.17) requires forward slashes as path separators. Windows tools like `Compress-Archive` write backslashes by default, which breaks extraction on non-Windows systems.

**Wrong:** `BepInEx\plugins\MyMod.dll`
**Right:** `BepInEx/plugins/MyMod.dll`

Use `System.IO.Compression.ZipArchive` directly to control entry names:

```powershell
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$stream = [System.IO.File]::Open($zipPath, [System.IO.FileMode]::Create)
$zip    = New-Object System.IO.Compression.ZipArchive($stream, [System.IO.Compression.ZipArchiveMode]::Create)

function Add-ZipEntry($archive, $filePath, $entryName) {
    $entry       = $archive.CreateEntry($entryName, [System.IO.Compression.CompressionLevel]::Optimal)
    $entryStream = $entry.Open()
    $fileStream  = [System.IO.File]::OpenRead($filePath)
    $fileStream.CopyTo($entryStream)
    $fileStream.Dispose()
    $entryStream.Dispose()
}

Add-ZipEntry $zip "path\to\manifest.json"               "manifest.json"
Add-ZipEntry $zip "path\to\README.md"                   "README.md"
Add-ZipEntry $zip "path\to\icon.png"                    "icon.png"
Add-ZipEntry $zip "path\to\bin\Release\MyMod.dll"       "BepInEx/plugins/MyMod.dll"

$zip.Dispose()
$stream.Dispose()
```

---

## Version Bumping Checklist

When releasing a new version, update the version string in **all three places** — they must agree:

1. **`manifest.json`** — `"version_number": "1.0.1"`
2. **`<ModName>Mod.cs`** — `[BepInPlugin(ModGuid, "Display Name", "1.0.1")]`
3. **Package filename** — `MyMod-1.0.1.zip` (controlled by the `-Version` parameter in `package.ps1`)

Thunderstore will reject an upload if `version_number` in the manifest matches an already-published version.

---

## Auto-Deploy to All Local Thunderstore Profiles

The project's `.csproj` can auto-copy the built DLL (and PDB if present) into every Thunderstore Mod Manager profile on the local machine after every build, so you can test in-game without manually copying files. Add a new profile in Thunderstore and the next build picks it up automatically — no csproj edit needed.

Add to the `.csproj`:

```xml
<!-- Auto-deploy build output to every Thunderstore Mod Manager profile on this machine. -->
<PropertyGroup>
  <ThunderstoreProfiles>$(APPDATA)\Thunderstore Mod Manager\DataFolder\Valheim\profiles</ThunderstoreProfiles>
</PropertyGroup>
<Target Name="DeployToProfiles" AfterTargets="Build" Condition="Exists('$(ThunderstoreProfiles)')">
  <Exec Command="powershell -NoProfile -ExecutionPolicy Bypass -Command &quot;Get-ChildItem '$(ThunderstoreProfiles)' -Directory | ForEach-Object { %24plugins = Join-Path %24_.FullName 'BepInEx\plugins'; if (Test-Path %24plugins) { Copy-Item '$(TargetPath)' %24plugins -Force; if (Test-Path '$(TargetDir)$(TargetName).pdb') { Copy-Item '$(TargetDir)$(TargetName).pdb' %24plugins -Force }; Write-Host ('Deployed $(TargetName).dll to ' + %24_.Name + ' profile') } }&quot;" />
</Target>
```

How it works:
- The `Condition="Exists('$(ThunderstoreProfiles)')"` on the `<Target>` makes the whole target a no-op on machines without Thunderstore Mod Manager installed — safe to commit.
- The PowerShell inline `<Exec>` enumerates every directory under `…\profiles\` and copies into any that has a `BepInEx\plugins` subfolder, so profiles without BepInEx are quietly skipped.
- `%24` is MSBuild's escape for `$`, needed because PowerShell variables (`$plugins`, `$_`) would otherwise be eaten by MSBuild's own property expansion.

The standard pattern across this repo. See any of the mod `.csproj` files for a working example.

---

## Uploading to Thunderstore

1. Go to [thunderstore.io](https://thunderstore.io), log in, and navigate to your team.
2. Click **Upload** and select the zip file.
3. Thunderstore validates the manifest, icon dimensions, and zip structure before accepting.
4. If the `name` matches an existing mod in your team, the upload is treated as a new version of that mod.
5. If the `name` is new, a new mod listing is created.

---

## Deprecating / Redirecting a Mod

To redirect users from an old mod listing to a new one:

1. Create a new version of the old mod with:
   - An updated `README.md` that explains the rename and links to the new mod
   - Updated `description` in `manifest.json`
   - Updated `website_url` pointing to the new mod
   - The same `name` as the existing listing (to trigger an update, not a new listing)
2. Upload it — existing users will see the update notification and can read the redirect.

---

## package.ps1 Pattern

Each mod project should have a `package.ps1` in its root:

```powershell
param([string]$Version = "1.0.0")

$projectDir = $PSScriptRoot
$zipPath    = Join-Path $projectDir "MyMod-$Version.zip"

dotnet build "$projectDir\MyMod.csproj" -c Release
if ($LASTEXITCODE -ne 0) { Write-Error "Build failed."; exit 1 }

# ... ZipArchive block (see above) ...

Write-Host "Package ready: MyMod-$Version.zip"
```

Run with:
```powershell
powershell -ExecutionPolicy Bypass -File package.ps1 -Version "1.0.1"
```
