# Copies the mod settings - and the world-keyed data worth keeping - off the DatHost server onto
# the local one.
#
# BepInEx/config holds three different kinds of thing, and only the first is "settings":
#
#   settings   *.cfg and *.yml at the root. What this is for.
#   data       the group's shared map pins, their stats store, SpreadTheLoad's known-ids list.
#              Not settings, but losing them is worse than losing settings, so they come too.
#   litter     every capture dsl_bench ever wrote on that server (about 20 MB), runtime state
#              files that regenerate, and client-side PNG assets. Left behind.
#
# BepInEx.cfg is deliberately NOT copied. It configures the loader, not a mod, and the DatHost
# container and this machine are not the same environment.
#
# The server is stopped first. BepInEx reads config at startup and several mods rewrite their file
# on shutdown, so copying into a running server gets partly undone a few minutes later.
#
# Usage:
#   .\pull-dathost-config.ps1 -WhatIf      # list exactly what would be copied and skipped
#   .\pull-dathost-config.ps1              # do it
#   .\pull-dathost-config.ps1 -SettingsOnly   # leave the pins and stats alone

param(
    [string]$SecretsPath = (Join-Path $env:USERPROFILE ".dathost"),
    [string]$LocalConfig = "C:\ValheimServer\server\BepInEx\config",
    [string]$LocalSaveDir = "C:/ValheimServer/data",
    [string]$ServerScript = "C:\ValheimServer\scripts\valheim.ps1",
    [switch]$SettingsOnly,
    [switch]$WhatIf
)

$ErrorActionPreference = "Stop"
$base = "https://dathost.net/api/0.1"

if (-not (Test-Path $SecretsPath)) { Write-Error "Secrets file not found: $SecretsPath"; exit 1 }
$cred = Get-Content $SecretsPath -Raw | ConvertFrom-Json
$basic = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("$($cred.email):$($cred.token)"))
$headers = @{ Authorization = "Basic $basic" }
$id = $cred.server_id

# ── what to take ───────────────────────────────────────────────────────────────
function Classify([string]$rel) {
    # $rel is the path under BepInEx/config/
    if ($rel -eq "BepInEx.cfg") { return "skip: loader config, not a mod setting" }
    if ($rel -like "DiagnoseServerLag/*") { return "skip: old capture" }
    if ($rel -eq "PauseMyServer.state.json") { return "skip: runtime state, regenerates" }
    if ($rel -like "*.rowschema-*") { return "skip: schema backup" }
    if ($rel -like "*.png") { return "skip: client asset" }

    if ($rel -notlike "*/*") {
        if ($rel -like "*.cfg" -or $rel -like "*.yml") { return "settings" }
        if ($rel -eq "SpreadTheLoad-known-ids.txt") { return "data" }
        return "skip: not a settings file"
    }

    if ($rel -like "TheGreatestMap/*.pins.bin")        { return "pins" }
    if ($rel -like "DudeWhatAreMyStats/*.stats.bin")   { return "data" }
    if ($rel -like "shudnal.ConditionalConfigSync/*")  { return "settings" }
    if ($rel -like "KeyManager/*")                     { return "settings" }
    return "skip: not needed on the server"
}

Write-Host "Reading BepInEx/config from the DatHost server..."
$all = Invoke-RestMethod -Method Get -Headers $headers -TimeoutSec 60 `
    -Uri "$base/game-servers/$id/files?hide_default_files=true"

$take = @()
$skip = @()
foreach ($e in @($all)) {
    if ($e.deleted) { continue }
    if ($e.path -notlike "BepInEx/config/*") { continue }
    if ($e.path.EndsWith("/")) { continue }
    if ($e.size -eq $null) { continue }
    $rel = $e.path.Substring("BepInEx/config/".Length)
    if ([string]::IsNullOrEmpty($rel)) { continue }
    $what = Classify $rel
    if ($what -eq "settings" -or (($what -eq "data" -or $what -eq "pins") -and -not $SettingsOnly)) {
        $take += [pscustomobject]@{ Rel = $rel; Size = [int64]$e.size; Kind = $what }
    } else {
        if ($what -eq "data" -or $what -eq "pins") { $what = "skip: -SettingsOnly" }
        $skip += [pscustomobject]@{ Rel = $rel; Size = [int64]$e.size; Why = $what }
    }
}

$takeMB = (($take | Measure-Object Size -Sum).Sum) / 1MB
$skipMB = (($skip | Measure-Object Size -Sum).Sum) / 1MB
Write-Host ("  taking  {0,3} files, {1,6:n1} MB" -f $take.Count, $takeMB)
Write-Host ("  leaving {0,3} files, {1,6:n1} MB" -f $skip.Count, $skipMB)


# ── the server's player lists: compared, never copied ──────────────────────────
#
# adminlist, permittedlist and bannedlist live in SaveDir, as siblings of worlds_local - one level
# above any world. One server applies the same lists to every world it hosts, so they belong to
# the destination server rather than to the world being moved, and copying them with a world would
# be actively wrong: it would overwrite the destination's own admins with the source's.
#
# But leaving them unmentioned is how somebody ends up without admin on the new server and nobody
# notices until a command is refused - dsl_bench's server capture needs admin, for one. So they
# are read and compared, and a difference is reported for a person to act on.
function Compare-PlayerLists {
    $remoteSaveDir = "SaveDir"
    $localSaveDir = $LocalSaveDir
    if (-not (Test-Path $localSaveDir)) {
        Write-Warning "No local save directory at $localSaveDir - skipping the player list check."
        return
    }
    Write-Host ""
    Write-Host "Player lists (server-level, not copied):"
    foreach ($leaf in @("adminlist.txt", "permittedlist.txt", "bannedlist.txt")) {
        $ids = @()
        try {
            $enc = [uri]::EscapeDataString("$remoteSaveDir/$leaf")
            $r = Invoke-WebRequest -Method Get -Uri "$base/game-servers/$id/files/$enc" `
                -Headers $headers -TimeoutSec 60 -UseBasicParsing
            $ids = @([Text.Encoding]::UTF8.GetString($r.Content) -split "`r?`n" |
                     ForEach-Object { $_.Trim() } |
                     Where-Object { $_ -and -not $_.StartsWith("//") })
        } catch {
            Write-Host ("  {0,-18} could not be read from the source server" -f $leaf)
            continue
        }

        $localPath = Join-Path $localSaveDir $leaf
        $mine = @()
        if (Test-Path $localPath) {
            $mine = @(Get-Content $localPath | ForEach-Object { $_.Trim() } |
                      Where-Object { $_ -and -not $_.StartsWith("//") })
        }
        $missing = @($ids | Where-Object { $mine -notcontains $_ })

        if (-not (Test-Path $localPath)) {
            Write-Warning ("  {0,-18} does not exist here; the source lists {1} id(s)" -f $leaf, $ids.Count)
        } elseif ($missing.Count -eq 0) {
            Write-Host ("  {0,-18} source {1}, here {2} - nothing missing" -f $leaf, $ids.Count, $mine.Count)
        } else {
            Write-Warning ("  {0,-18} {1} id(s) on the source are not here: {2}" -f $leaf, $missing.Count, ($missing -join ", "))
            Write-Warning ("  {0,-18} server-level, so add them by hand if they should apply here: {1}" -f "", $localPath)
        }
    }
}

Compare-PlayerLists

if ($WhatIf) {
    Write-Host ""
    Write-Host "WOULD COPY:"
    foreach ($t in ($take | Sort-Object Kind, Rel)) {
        $local = Join-Path $LocalConfig $t.Rel
        $note = if (Test-Path $local) { "replaces " + (Get-Item $local).Length + " b" } else { "new" }
        Write-Host ("  {0,-8} {1,9}  {2,-52} {3}" -f $t.Kind, $t.Size, $t.Rel, $note)
    }
    Write-Host ""
    Write-Host "WOULD SKIP (grouped):"
    foreach ($g in ($skip | Group-Object Why | Sort-Object Count -Descending)) {
        Write-Host ("  {0,3} files, {1,7:n1} MB  {2}" -f $g.Count, (($g.Group | Measure-Object Size -Sum).Sum / 1MB), $g.Name)
    }
    Write-Host ""
    Write-Host "WhatIf: nothing changed."
    return
}

# ── download to staging, then swap ─────────────────────────────────────────────
# Downloaded in full before anything local is touched, so a failure half way through leaves the
# server exactly as it was.
$staging = Join-Path ([IO.Path]::GetTempPath()) ("cfg-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
New-Item -ItemType Directory -Path $staging -Force | Out-Null
$i = 0
foreach ($t in $take) {
    $i++
    $enc = [uri]::EscapeDataString("BepInEx/config/" + $t.Rel)
    $out = Join-Path $staging $t.Rel
    New-Item -ItemType Directory -Path (Split-Path $out -Parent) -Force | Out-Null
    Invoke-WebRequest -Method Get -Uri "$base/game-servers/$id/files/$enc" -Headers $headers `
        -OutFile $out -TimeoutSec 300 -UseBasicParsing
    $got = (Get-Item $out).Length
    if ($got -ne $t.Size) { Write-Error "$($t.Rel) came back $got bytes, expected $($t.Size). Aborting."; exit 1 }
    if ($i % 20 -eq 0 -or $i -eq $take.Count) { Write-Host "  $i/$($take.Count)" }
}
Write-Host "Downloaded to $staging"

if (Test-Path $ServerScript) {
    Write-Host "Stopping the local server..."
    & $ServerScript stop -Force | Out-Null
} else {
    Write-Warning "No $ServerScript - make sure the local server is stopped."
}

# The whole existing config set is kept, not just the files being replaced: that way one folder is
# the complete 'before', and nothing has to be reasoned about file by file to undo this.
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$backup = Join-Path $LocalConfig "_before-dathost-copy-$stamp"
New-Item -ItemType Directory -Path $backup -Force | Out-Null
foreach ($item in (Get-ChildItem $LocalConfig | Where-Object { $_.Name -notlike "_before-dathost-copy-*" })) {
    Copy-Item -Path $item.FullName -Destination $backup -Recurse -Force
}
Write-Host "Previous config copied to $backup"

# The pin store is merged, never replaced. Replacing it is what would make a transfer visible to
# players: every marker someone placed on the destination between the last copy and the cutover
# would vanish. tools/pinmerge runs the mod's own MapStore.Merge, so later changes win and a
# tombstone still beats an older pin - nothing anyone erased comes back. Merging is idempotent, so
# a re-run of this script is harmless.
$pinmerge = Join-Path $PSScriptRoot "tools/pinmerge/bin/Release/net48/pinmerge.exe"
if (($take | Where-Object { $_.Kind -eq "pins" }) -and -not (Test-Path $pinmerge)) {
    Write-Host "Building tools/pinmerge..."
    dotnet build (Join-Path $PSScriptRoot "tools/pinmerge/pinmerge.csproj") -c Release --nologo | Out-Null
}

$n = 0
$pinsSkipped = @()
foreach ($t in $take) {
    $src = Join-Path $staging $t.Rel
    $dst = Join-Path $LocalConfig $t.Rel
    New-Item -ItemType Directory -Path (Split-Path $dst -Parent) -Force | Out-Null

    if ($t.Kind -eq "pins" -and (Test-Path $dst)) {
        if (-not (Test-Path $pinmerge)) {
            Write-Warning "pinmerge did not build; leaving $($t.Rel) as it is rather than replacing it."
            $pinsSkipped += $t.Rel
            continue
        }
        $merged = "$src.merged"
        Write-Host "Merging map pins: $($t.Rel)"
        & $pinmerge $dst $src $merged
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path $merged)) {
            # Keeping what is already here beats installing a half-merge, and the rest of the
            # config is still worth copying.
            Write-Warning "The pin merge failed; leaving $($t.Rel) as it is."
            $pinsSkipped += $t.Rel
            continue
        }
        Copy-Item -Path $merged -Destination $dst -Force
        $n++
        continue
    }

    Copy-Item -Path $src -Destination $dst -Force
    $n++
}
if ($pinsSkipped.Count -gt 0) {
    Write-Warning ("Not updated: " + ($pinsSkipped -join ", "))
}
Write-Host ("Copied {0} files into {1}" -f $n, $LocalConfig)

if (Test-Path $ServerScript) {
    Write-Host "Starting the local server..."
    & $ServerScript start -Force
}
