# Copies a world off the DatHost server, and optionally puts it on the local server.
#
# Valheim 1.0 keeps a world as a folder, not a pair of files: one .chunk per map region plus a
# generation of metadata named _main.<n>.{chunks,db2,fwl2,ok}. The .ok file is written last and is
# what marks that generation complete, so it is also what tells us a save was not in progress when
# we looked. Downloading 60-odd files one at a time means a save can land in the middle of the
# copy and leave regions from two different generations mixed together, which is why this re-reads
# the listing afterwards and insists nothing moved.
#
# Reads are harmless; -Install is not. It stops the local server, sets the existing world aside
# under a dated name, and copies the downloaded one into its place. The old world is never deleted.
#
# Usage:
#   .\pull-dathost-world.ps1                          # download to a staging folder, verify, stop
#   .\pull-dathost-world.ps1 -Install                 # ... and put it on the local server
#   .\pull-dathost-world.ps1 -World Ashlands           # some other world on the server
#   .\pull-dathost-world.ps1 -Install -WhatIf          # say what it would do
#
# The DatHost server should be stopped before a cutover you intend to keep. While it is running it
# is still accepting players, and anything they do after this copy is taken is left behind on a
# world nobody is looking at any more.

param(
    [string]$World = "DeepNorth",
    [string]$SecretsPath = (Join-Path $env:USERPROFILE ".dathost"),
    [string]$Staging,
    [string]$LocalWorlds = "C:\ValheimServer\data\worlds_local",
    [string]$ServerScript = "C:\ValheimServer\scripts\valheim.ps1",
    [switch]$Install,
    [switch]$WhatIf
)

$ErrorActionPreference = "Stop"
$base = "https://dathost.net/api/0.1"

# ── credentials ────────────────────────────────────────────────────────────────
if (-not (Test-Path $SecretsPath)) { Write-Error "Secrets file not found: $SecretsPath"; exit 1 }
$cred = Get-Content $SecretsPath -Raw | ConvertFrom-Json
foreach ($k in @("email", "token", "server_id")) {
    if (-not $cred.$k) { Write-Error "Secrets file is missing '$k'"; exit 1 }
}
$basic = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("$($cred.email):$($cred.token)"))
$headers = @{ Authorization = "Basic $basic" }
$id = $cred.server_id
$remoteDir = "SaveDir/worlds_local/$World"

# ── what is on the server ──────────────────────────────────────────────────────
# Sizes as well as names: the sizes are what let us tell a quiet world from one that saved while
# we were reading it.
function Get-WorldFiles {
    $all = Invoke-RestMethod -Method Get -Headers $headers -TimeoutSec 60 `
        -Uri "$base/game-servers/$id/files?hide_default_files=true"
    $want = @{}
    foreach ($e in @($all)) {
        if ($e.deleted) { continue }
        if ($e.path -notlike "$remoteDir/*") { continue }
        $leaf = $e.path.Substring($remoteDir.Length + 1)
        if ($leaf -match "/") { continue }        # a nested backup folder, not this world
        if (-not $e.size) { continue }            # the folder entry itself
        $want[$leaf] = [int64]$e.size
    }
    return $want
}

Write-Host "Reading $remoteDir ..."
$files = Get-WorldFiles
if ($files.Count -eq 0) { Write-Error "No files found at $remoteDir on the server."; exit 1 }

# The newest generation that actually finished writing.
$gen = $null
foreach ($leaf in $files.Keys) {
    if ($leaf -match "^_main\.(\d+)\.ok$") {
        $n = [int]$Matches[1]
        if ($gen -eq $null -or $n -gt $gen) { $gen = $n }
    }
}
if ($gen -eq $null) {
    Write-Error ("No _main.<n>.ok in $remoteDir, so no save there is known to be complete. " +
                 "Stop the server and try again.")
    exit 1
}
$totalMB = (($files.Values | Measure-Object -Sum).Sum) / 1MB
Write-Host ("  {0} files, {1:n1} MB, newest complete save generation {2}" -f $files.Count, $totalMB, $gen)

$srv = Invoke-RestMethod -Method Get -Uri "$base/game-servers/$id" -Headers $headers -TimeoutSec 30
if ($srv.on) {
    Write-Warning ("'{0}' is still running. The copy will be consistent or this will say so, but " -f $srv.name)
    Write-Warning "anything played there after now is left behind. Stop it before a cutover you mean to keep."
}

# ── download ───────────────────────────────────────────────────────────────────
if (-not $Staging) {
    $Staging = Join-Path ([IO.Path]::GetTempPath()) ("world-$World-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
}
if ($WhatIf) {
    Write-Host "WhatIf: would download $($files.Count) files to $Staging"
} else {
    New-Item -ItemType Directory -Path $Staging -Force | Out-Null
    $i = 0
    foreach ($leaf in ($files.Keys | Sort-Object)) {
        $i++
        $enc = [uri]::EscapeDataString("$remoteDir/$leaf")
        $out = Join-Path $Staging $leaf
        Invoke-WebRequest -Method Get -Uri "$base/game-servers/$id/files/$enc" -Headers $headers `
            -OutFile $out -TimeoutSec 300 -UseBasicParsing
        $got = (Get-Item $out).Length
        if ($got -ne $files[$leaf]) {
            Write-Error "$leaf came back $got bytes, expected $($files[$leaf]). Aborting."
            exit 1
        }
        if ($i % 10 -eq 0 -or $i -eq $files.Count) { Write-Host "  $i/$($files.Count)" }
    }

    # Did the world move under us? A save during the copy would change sizes or add a generation,
    # and a folder holding regions from two generations is not a world anybody should load.
    $after = Get-WorldFiles
    $drifted = @()
    foreach ($leaf in $files.Keys) {
        if (-not $after.ContainsKey($leaf)) { $drifted += "$leaf disappeared" }
        elseif ($after[$leaf] -ne $files[$leaf]) { $drifted += "$leaf changed size" }
    }
    foreach ($leaf in $after.Keys) { if (-not $files.ContainsKey($leaf)) { $drifted += "$leaf appeared" } }
    if ($drifted.Count -gt 0) {
        Write-Warning "The world was saved while it was being copied:"
        foreach ($d in $drifted | Select-Object -First 8) { Write-Warning "  $d" }
        Write-Error ("This copy mixes two saves and must not be installed. Stop the server, then " +
                     "run this again.")
        exit 1
    }
    Write-Host ("Downloaded to {0} - verified unchanged on the server." -f $Staging)
}

if (-not $Install) {
    Write-Host "Not installing (no -Install). Nothing on the local server was touched."
    return
}

# ── install ────────────────────────────────────────────────────────────────────
# The server must be stopped first. A running one holds the world in memory and writes it back on
# its next save, which would quietly undo this a few minutes later.
$target = Join-Path $LocalWorlds $World
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$aside = Join-Path $LocalWorlds "${World}_before-dathost-$stamp"

if ($WhatIf) {
    Write-Host "WhatIf: would stop the local server"
    if (Test-Path $target) { Write-Host "WhatIf: would move $target -> $aside" }
    Write-Host "WhatIf: would copy $Staging -> $target, then start the server"
    return
}

if (Test-Path $ServerScript) {
    Write-Host "Stopping the local server..."
    & $ServerScript stop -Force | Out-Null
} else {
    Write-Warning "No $ServerScript - make sure the local server is stopped before this runs."
}

if (Test-Path $target) {
    Move-Item -Path $target -Destination $aside
    Write-Host "Previous world kept at $aside"
}
New-Item -ItemType Directory -Path $target -Force | Out-Null
Copy-Item -Path (Join-Path $Staging "*") -Destination $target -Force
$placed = (Get-ChildItem $target -File).Count
Write-Host ("Installed {0} files into {1}" -f $placed, $target)
if ($placed -ne $files.Count) {
    Write-Warning "Expected $($files.Count) files but placed $placed. The old world is still at $aside."
}

if (Test-Path $ServerScript) {
    Write-Host "Starting the local server..."
    & $ServerScript start -Force
}
