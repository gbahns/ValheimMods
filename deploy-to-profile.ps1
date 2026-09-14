# Copies a freshly built mod DLL (and its .pdb) into the Gale profiles that take dev builds.
#
# Which profiles those are is decided in Directory.Build.targets, not here: by default only
# "Default SD Test". "Default SD" is deliberately NOT one of them -- it is the production
# profile and gets these mods from Gale the way players do, so that what is played there is
# what was actually published.
#
# Invoked from the build; run it by hand only to test the copying itself:
#   .\deploy-to-profile.ps1 -TargetName TheGreatestMap -TargetPath .\TheGreatestMap\bin\Release\net48\TheGreatestMap.dll

param(
    [Parameter(Mandatory = $true)][string]$TargetName,
    [Parameter(Mandatory = $true)][string]$TargetPath,
    [string]$Profiles = "Default SD Test",
    [string]$GaleProfiles = (Join-Path $env:APPDATA "com.kesomannen.gale\valheim\profiles")
)

$wanted = @($Profiles -split ';' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
if ($wanted.Count -eq 0) { exit 0 }
if (-not (Test-Path $GaleProfiles)) { exit 0 }

$pdb = [IO.Path]::ChangeExtension($TargetPath, ".pdb")

# Copy, and when the file is locked because Valheim is running, move the locked one aside and
# copy over the gap.  Windows allows that rename because .NET's Assembly.LoadFile opens the
# file with FILE_SHARE_DELETE.  The sidecar is swept on a later build, when nothing holds it.
function Copy-Plugin([string]$src, [string]$dest, [string]$profile) {
    if (-not (Test-Path $src)) { return }
    try { Copy-Item $src $dest -Force -ErrorAction Stop; return } catch { }
    try {
        $aside = "$dest.old-" + (Get-Random)
        Move-Item $dest $aside -Force -ErrorAction Stop
        Copy-Item $src $dest -Force -ErrorAction Stop
        Write-Host ("  shadow-replaced (was locked): " + (Split-Path $dest -Leaf))
    } catch {
        Write-Warning ("  deploy failed: $profile " + (Split-Path $dest -Leaf) + " - " + $_.Exception.Message)
    }
}

foreach ($name in $wanted) {
    $plugins = Join-Path (Join-Path $GaleProfiles $name) "BepInEx\plugins"
    if (-not (Test-Path $plugins)) {
        Write-Host "Profile '$name' not found - nothing deployed. Create it in Gale first."
        continue
    }

    # Gale installs each package into BepInEx\plugins\<Team>-<Name>\.  If this mod is installed
    # there from Thunderstore or Hexium, overwrite the DLL inside that package folder and clear
    # any stale loose copy at the plugins root, so one build does not leave two DLLs claiming the
    # same plugin GUID.  Otherwise deploy to the root, which BepInEx loads just as happily.
    $dests = @(Get-ChildItem $plugins -Directory -ErrorAction SilentlyContinue |
        Where-Object { Test-Path (Join-Path $_.FullName "$TargetName.dll") } |
        ForEach-Object { $_.FullName })

    if ($dests.Count -gt 0) {
        Get-ChildItem $plugins -File -Filter "$TargetName.*" -ErrorAction SilentlyContinue |
            Remove-Item -Force -ErrorAction SilentlyContinue
    } else {
        $dests = @($plugins)
    }

    foreach ($d in $dests) {
        Get-ChildItem $d -Filter "*.old-*" -ErrorAction SilentlyContinue |
            Remove-Item -Force -ErrorAction SilentlyContinue
        Copy-Plugin $TargetPath (Join-Path $d "$TargetName.dll") $name
        Copy-Plugin $pdb        (Join-Path $d "$TargetName.pdb") $name
        Write-Host ("Deployed $TargetName to $name (" + (Split-Path $d -Leaf) + ")")
    }
}
