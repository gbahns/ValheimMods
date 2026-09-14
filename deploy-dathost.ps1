# Pushes mods to a DatHost Valheim server over the DatHost REST API. Two modes:
#   -Mod <names>      the repo's own built DLLs (default: TheGreatestMap), each uploaded over the
#                     copy already under BepInEx/plugins
#   -Profile <name>   mirror a Gale profile's whole BepInEx/plugins tree: every file that is
#                     missing on the server or differs by content is uploaded. Nothing is ever
#                     deleted on the server. Packages that exist only locally are skipped and
#                     listed unless -IncludeLocalOnly is given.
#
# Both modes skip anything mods.json marks as client-only, and say which ones they skipped. Most
# of those carry [BepInProcess("valheim.exe")], which makes BepInEx skip the plugin entirely under
# valheim_server.exe, so the upload would only cost time and add one more thing to rule out when
# the server misbehaves. Add an entry to mods.json for any new mod.
#
# DatHost's stop is a hard kill: the server console never shows a shutdown save, and the
# Valheim dedicated server ignores console input, so the world cannot be saved through DatHost
# itself. TheGreatestMap (0.1.5+) running on the server saves the world when a file named
# "save-now" appears in BepInEx/config/TheGreatestMap/. This script:
#   1. counts completed world saves in the server console,
#   2. creates the save-now trigger over the file API and waits for a new save to complete,
#   3. stops the server, uploads, starts the server and waits until DatHost reports it running.
# If no save shows up the script stops before touching anything unless -SkipSave is given.
#
# Usage (from the repo root):
#   .\deploy-dathost.ps1                                  # deploys TheGreatestMap
#   .\deploy-dathost.ps1 -Mod TheGreatestMap,Armory       # several mods in one restart
#   .\deploy-dathost.ps1 -Profile "Default SD"            # mirror the whole profile's plugins
#   .\deploy-dathost.ps1 -Profile "Default SD" -IncludeLocalOnly
#   .\deploy-dathost.ps1 -Mod TheGreatestMap -Published   # deploy the version players downloaded
#   .\deploy-dathost.ps1 -Package Azumatt-AzuCraftyBoxes  # update one third-party package from the
#                                                         # profile (defaults to "Default SD")
#   .\deploy-dathost.ps1 -RestartOnly                     # restart with a verified save, no upload
#   .\deploy-dathost.ps1 ... -WhatIf                      # show the plan, change nothing
#   .\deploy-dathost.ps1 ... -NoRestart                   # upload only (server should be stopped)
#   .\deploy-dathost.ps1 ... -SkipSave                    # do not wait for the pre-stop save
#
# Secrets live in %USERPROFILE%\.dathost (never in the repo or a chat), a JSON file:
#   { "email": "you@example.com", "token": "<DatHost account password>", "server_id": "<id from the panel URL>" }
# DatHost has no API keys: the API is HTTP Basic auth with the account email and password. To
# keep the main password out of this file, invite a second DatHost account to the server
# (Account -> Shared Account Access) and use that account's login here. The server id is the
# long hex string in the control panel URL for the server.

param(
    [string[]]$Mod = @("TheGreatestMap"),
    [string]$Profile,
    [string[]]$Package,
    [switch]$IncludeLocalOnly,
    [string]$SecretsPath = (Join-Path $env:USERPROFILE ".dathost"),
    [switch]$NoRestart,
    [switch]$SkipSave,
    [switch]$Published,
    [switch]$RestartOnly,
    [switch]$TestBuild,
    [switch]$WhatIf
)

$ErrorActionPreference = "Stop"
$base = "https://dathost.net/api/0.1"
$repo = $PSScriptRoot

# -Package names third-party packages by their Gale folder, e.g. Azumatt-AzuCraftyBoxes. It is the
# profile mirror scoped to those packages, so they are compared and uploaded exactly as a full
# mirror would, and the profile copy came from Gale -- which for a third-party mod is the same
# thing -Published gets for ours: the artifact its author actually released.
if ($Package -and -not $Profile) { $Profile = "Default SD" }
$saveTrigger = "BepInEx/config/TheGreatestMap/save-now"
# Files never worth sending to a server: debug symbols, build sidecars, store metadata and docs.
$skipNames = @("manifest.json", "README.md", "CHANGELOG.md", "icon.png", "LICENSE", "LICENSE.md", "LICENSE.txt")
$skipPatterns = @('\.pdb$', '\.old-\d+$', '\.old$', '-disabled$', '\.cs$', '^Placeholder')

# ── which side each mod belongs on ──────────────────────────────────────────────
# mods.json says which mods the server can actually use.  A mod marked "client" is never
# uploaded: most of them carry [BepInProcess("valheim.exe")], which makes BepInEx skip the
# plugin under valheim_server.exe, so the file would sit there unloaded -- extra upload time,
# extra restart risk and one more thing to rule out when the server misbehaves.
$sides = @{}
$sidesPath = Join-Path $repo "mods.json"
if (Test-Path $sidesPath) {
    $sidesDoc = Get-Content $sidesPath -Raw | ConvertFrom-Json
    foreach ($p in $sidesDoc.mods.PSObject.Properties)            { $sides[$p.Name] = $p.Value.side }
    foreach ($p in $sidesDoc.profilePackages.PSObject.Properties) { $sides[$p.Name] = $p.Value.side }
} else {
    Write-Warning "mods.json not found; uploading everything named, including client-only mods."
}

# "client" cannot run on a server; "excluded" could but is deliberately not wanted there. Both are
# skipped, so an excluded mod does not quietly return on the next profile mirror.
function Test-ClientOnly([string]$name) {
    return ($sides.ContainsKey($name) -and $sides[$name] -in @("client", "excluded"))
}

function Get-TomlValue($path, $key) {
    if (-not (Test-Path $path)) { return $null }
    $line = Select-String -Path $path -Pattern "^$key\s*=" | Select-Object -First 1
    if (-not $line) { return $null }
    return ($line.Line -replace "^$key\s*=\s*", "").Trim().Trim('"')
}

# -Published: deploy the artifact players actually download, by fetching the newest published
# version's zip from Thunderstore and taking the DLL out of it.
#
# bin\Release holds whatever the tree last built, and the release convention bumps all four
# version files immediately after publishing, so the tree normally builds a version that exists
# nowhere but this disk.  Deploying that puts unreleased code on a server whose players are all on
# the released version.  Fetching the published zip removes the question: the server runs the same
# bytes as every client, whatever state the working tree is in.
function Get-PublishedInfo([string]$mod) {
    $tsToml = Join-Path $repo "$mod\thunderstore.toml"
    $ns   = Get-TomlValue $tsToml "namespace"
    $name = Get-TomlValue $tsToml "name"
    if (-not $ns -or -not $name) { return $null }
    $bust = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
    try {
        return (Invoke-RestMethod -TimeoutSec 30 -Headers @{ "Cache-Control" = "no-cache" } `
            -Uri "https://thunderstore.io/api/experimental/package/$ns/$name/?cb=$bust").latest
    } catch { return $null }
}

$tempDownloads = @()
function Get-PublishedDll([string]$mod) {
    $info = [pscustomobject]@{ latest = (Get-PublishedInfo $mod) }
    if (-not $info.latest -or -not $info.latest.download_url) { return $null }

    $tmp = Join-Path ([IO.Path]::GetTempPath()) ("deploy-" + [Guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $tmp | Out-Null
    $script:tempDownloads += $tmp
    $zip = Join-Path $tmp "package.zip"
    Invoke-WebRequest -Uri $info.latest.download_url -OutFile $zip -TimeoutSec 180
    Expand-Archive -Path $zip -DestinationPath (Join-Path $tmp "x") -Force

    $dll = Join-Path $tmp "x\BepInEx\plugins\$mod.dll"
    if (-not (Test-Path $dll)) { return $null }
    return [pscustomobject]@{ Dll = $dll; Version = $info.latest.version_number }
}

# ── secrets ─────────────────────────────────────────────────────────────────────
if (-not (Test-Path $SecretsPath)) { Write-Error "Secrets file not found: $SecretsPath"; exit 1 }
$cred = Get-Content $SecretsPath -Raw | ConvertFrom-Json
foreach ($k in @("email", "token", "server_id")) {
    if (-not $cred.$k) { Write-Error "Secrets file is missing '$k'"; exit 1 }
}
$basic = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("$($cred.email):$($cred.token)"))
$headers = @{ Authorization = "Basic $basic" }
$id = $cred.server_id

Add-Type -AssemblyName System.Net.Http
$client = New-Object System.Net.Http.HttpClient
$client.Timeout = [TimeSpan]::FromMinutes(5)
$client.DefaultRequestHeaders.Authorization = New-Object System.Net.Http.Headers.AuthenticationHeaderValue("Basic", $basic)

function Get-Server {
    Invoke-RestMethod -Method Get -Uri "$base/game-servers/$id" -Headers $headers
}

# DatHost returns paths relative to the queried folder; keys here are full paths from the root.
function Get-ServerListing([string]$path) {
    $listing = Invoke-RestMethod -Method Get -Uri "$base/game-servers/$id/files?path=$path&hide_default_files=true" -Headers $headers
    $map = @{}
    foreach ($e in @($listing)) {
        if ($e.deleted) { continue }
        $p = if ($e.path) { $e.path } elseif ($e.name) { $e.name } else { "$e" }
        if ($p -match '/$') { continue }
        if ($path -and $p -notmatch ("^" + [regex]::Escape($path) + "/")) { $p = "$path/$p" }
        $map[$p] = [long]$e.size
    }
    $map
}

# Gale disables a mod by renaming its files to *.old; a hand-disabled DLL ends in -disabled.
# A package with no active DLL is disabled locally and must not be sent.
function Test-PackageActive([string]$folder) {
    $dlls = Get-ChildItem $folder -Recurse -File | Where-Object { $_.Name -match '\.dll$' }
    return ($dlls.Count -gt 0)
}

function Get-ServerFileHash([string]$path) {
    $uri = "$base/game-servers/$id/files/" + ($path -replace " ", "%20")
    $bytes = $client.GetByteArrayAsync($uri).Result
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { [BitConverter]::ToString($sha.ComputeHash($bytes)) -replace "-", "" } finally { $sha.Dispose() }
}

# Valheim 1.0 logs every world save as "World save (1/5) ... => Save number N" through
# "World save (5/5) done". Counting completed saves before and after an action shows whether
# the world was saved by it.
function Get-SaveEvidence {
    try {
        $c = Invoke-RestMethod -Method Get -Uri "$base/game-servers/$id/console?max_lines=400" -Headers $headers
        $lines = @($c.lines)
        $done = @($lines | Where-Object { $_ -match 'World save \(5/5\) done' })
        $numbers = @($lines | Where-Object { $_ -match 'Save number (\d+)' } | ForEach-Object { [int]([regex]::Match($_, 'Save number (\d+)').Groups[1].Value) })
        [pscustomobject]@{
            Saves = $done.Count
            LastSaveLine = $(if ($done.Count -gt 0) { $done[-1] } else { $null })
            LastNumber = $(if ($numbers.Count -gt 0) { ($numbers | Measure-Object -Maximum).Maximum } else { 0 })
        }
    } catch {
        [pscustomobject]@{ Saves = -1; LastSaveLine = $null; LastNumber = 0 }
    }
}

function Wait-ServerState([bool]$wantOn, [int]$timeoutSeconds = 180) {
    $deadline = (Get-Date).AddSeconds($timeoutSeconds)
    do {
        Start-Sleep -Seconds 3
        $s = Get-Server
        if ([bool]$s.on -eq $wantOn -and -not [bool]$s.booting) { return $s }
    } while ((Get-Date) -lt $deadline)
    Write-Error ("Server did not reach on={0} within {1} s" -f $wantOn, $timeoutSeconds); exit 1
}

function Send-ServerFile([byte[]]$bytes, [string]$name, [string]$target) {
    $content = New-Object System.Net.Http.MultipartFormDataContent
    $fileContent = New-Object System.Net.Http.ByteArrayContent(, $bytes)
    $fileContent.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse("application/octet-stream")
    $content.Add($fileContent, "file", $name)
    $uri = "$base/game-servers/$id/files/" + ($target -replace " ", "%20")
    $resp = $client.PostAsync($uri, $content).Result
    if (-not $resp.IsSuccessStatusCode) {
        $body = $resp.Content.ReadAsStringAsync().Result
        Write-Error ("Upload to {0} failed: {1} {2}" -f $target, [int]$resp.StatusCode, $body); exit 1
    }
}

try {
    # ── server state ────────────────────────────────────────────────────────────
    $server = Get-Server
    if ($server.game -ne "valheim") { Write-Error "Server '$($server.name)' is running '$($server.game)', not valheim."; exit 1 }
    if (-not $server.valheim_settings.enable_bepinex) { Write-Error "BepInEx is not enabled on '$($server.name)'."; exit 1 }
    Write-Host ("Server '{0}': on={1}, booting={2}" -f $server.name, $server.on, $server.booting)
    $serverFiles = Get-ServerListing "BepInEx/plugins"

    # ── plan ────────────────────────────────────────────────────────────────────
    $plan = @()
    if ($RestartOnly) {
        # Files deleted on the server stay loaded in the running process until it restarts, and
        # BepInEx only reads config at startup. This gives that restart the same verified world
        # save the uploads get, instead of re-uploading a file just to trigger one.
        Write-Host "Restart only: nothing will be uploaded."
    } elseif ($Profile) {
        $root = Join-Path $env:APPDATA "com.kesomannen.gale\valheim\profiles\$Profile\BepInEx\plugins"
        if (-not (Test-Path $root)) { Write-Error "Profile plugins folder not found: $root"; exit 1 }
        foreach ($wanted in @($Package)) {
            if ($wanted -and -not (Test-Path (Join-Path $root $wanted))) {
                Write-Error "Package '$wanted' is not installed in profile '$Profile'."; exit 1
            }
        }
        $serverPackages = @{}
        foreach ($p in $serverFiles.Keys) {
            $rel = $p -replace '^BepInEx/plugins/', ''
            if ($rel -match '^([^/]+)/') { $serverPackages[$Matches[1]] = $true }
        }
        $skippedPackages = @{}
        $disabledPackages = @{}
        $clientOnlyPackages = @{}
        $checked = 0
        foreach ($f in Get-ChildItem $root -Recurse -File) {
            if ($skipNames -contains $f.Name) { continue }
            if ($skipPatterns | Where-Object { $f.Name -match $_ }) { continue }
            # XML next to a same-named DLL is compiler documentation, not data.
            if ($f.Extension -eq ".xml" -and (Test-Path (Join-Path $f.DirectoryName ($f.BaseName + ".dll")))) { continue }
            $rel = $f.FullName.Substring($root.Length + 1) -replace '\\', '/'
            $top = if ($rel -match '^([^/]+)/') { $Matches[1] } else { $null }
            # Scoped to named packages: filter before every other rule, so the notices below
            # describe only what was asked for rather than the whole profile.
            if ($Package -and (-not $top -or $Package -notcontains $top)) { continue }
            if ($top -and -not $disabledPackages.ContainsKey($top) -and -not (Test-PackageActive (Join-Path $root $top))) { $disabledPackages[$top] = $true }
            if ($top -and $disabledPackages.ContainsKey($top)) { continue }
            # Declared client-only in mods.json: the server cannot use it, so don't send it.
            # This repo's own mods sit in the profile as loose DLLs at the plugins root with no
            # package folder, so keying only on $top would let every one of them through and undo
            # a cleanup.  Fall back to the file's own base name, which is how mods.json names them.
            $unit = if ($top) { $top } else { [IO.Path]::GetFileNameWithoutExtension($f.Name) }
            if (Test-ClientOnly $unit) { $clientOnlyPackages[$unit] = $true; continue }
            if ($top -and -not $serverPackages.ContainsKey($top) -and -not $IncludeLocalOnly) { $skippedPackages[$top] = $true; continue }
            $target = "BepInEx/plugins/$rel"
            $reason = $null
            if (-not $serverFiles.ContainsKey($target)) { $reason = "new" }
            elseif ($serverFiles[$target] -ne $f.Length) { $reason = "size differs" }
            else {
                $checked++
                $localHash = (Get-FileHash $f.FullName -Algorithm SHA256).Hash
                if ((Get-ServerFileHash $target) -ne $localHash) { $reason = "content differs" }
            }
            if ($reason) {
                $plan += [pscustomobject]@{ Mod = $(if ($top) { $top } else { $f.Name }); Version = ""; Local = $f.FullName; Target = $target; New = ($reason -eq "new"); Reason = $reason }
            }
        }
        Write-Host ("Profile '{0}': {1} files compared by hash, {2} to upload." -f $Profile, $checked, $plan.Count)
        if ($disabledPackages.Count -gt 0) { Write-Host ("Disabled locally, not sent: " + (($disabledPackages.Keys | Sort-Object) -join ", ")) -ForegroundColor Yellow }
        if ($clientOnlyPackages.Count -gt 0) { Write-Host ("Client-only per mods.json, not sent: " + (($clientOnlyPackages.Keys | Sort-Object) -join ", ")) -ForegroundColor DarkGray }
        if ($skippedPackages.Count -gt 0) { Write-Host ("Local-only packages skipped (add -IncludeLocalOnly to send them): " + (($skippedPackages.Keys | Sort-Object) -join ", ")) -ForegroundColor Yellow }
        $localPackages = @{}
        Get-ChildItem $root -Directory | ForEach-Object { $localPackages[$_.Name] = $true }
        $serverOnly = @($serverPackages.Keys | Where-Object { -not $localPackages.ContainsKey($_) } | Sort-Object)
        if ($serverOnly.Count -gt 0) { Write-Host ("Server-only packages left untouched: " + ($serverOnly -join ", ")) -ForegroundColor Yellow }
    } else {
        $clientOnly = @($Mod | Where-Object { Test-ClientOnly $_ })
        if ($clientOnly.Count -gt 0) {
            # Named explicitly, so say so rather than dropping them silently: a mod asked for by
            # name and then skipped is confusing unless the reason is on screen.
            Write-Host ("Client-only per mods.json, not sent: " + ($clientOnly -join ", ")) -ForegroundColor DarkGray
        }
        $Mod = @($Mod | Where-Object { -not (Test-ClientOnly $_) })
        if ($Mod.Count -eq 0) { Write-Host "Every mod named is client-only; nothing to deploy."; exit 0 }

        foreach ($m in $Mod) {
            if ($Published) {
                $pub = Get-PublishedDll $m
                if (-not $pub) { Write-Error "Could not fetch a published package for $m. Is it on Thunderstore?"; exit 1 }
                $dll = $pub.Dll
                $ver = $pub.Version
                Write-Host ("Using the published $m $ver from Thunderstore, not bin\Release.") -ForegroundColor DarkGray
            } else {
            $candidates = @("net48", "net462") | ForEach-Object { Join-Path $repo "$m\bin\Release\$_\$m.dll" }
            $dll = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
            if (-not $dll) { Write-Error "No Release build found for $m (looked in $($candidates -join ', ')). Build it first."; exit 1 }
            $manifest = Join-Path $repo "$m\manifest.json"
            $ver = if (Test-Path $manifest) { (Get-Content $manifest -Raw | ConvertFrom-Json).version_number } else { (Get-Item $dll).VersionInfo.FileVersion }

            # A local build that is ahead of what is published is an unreleased version, and
            # putting one on a shared server locks out every player who can only get the released
            # one -- their mod refuses the connection over the version difference, and stays
            # refused until the new version is published. That is an outage for other people, not
            # a private test, so it takes -TestBuild to say it is wanted.
            if (-not $TestBuild) {
                $live = (Get-PublishedInfo $m).version_number
                if ($live) {
                    $ahead = $false
                    try { $ahead = ([version]$ver -gt [version]$live) } catch { $ahead = ($ver -ne $live) }
                    if ($ahead) {
                        Write-Error ("$m $ver is not published (Thunderstore has $live). Deploying it would lock " +
                                     "every other player out of the server until $ver is published.`n" +
                                     "  Publish first, then:  .\deploy-dathost.ps1 -Mod $m -Published`n" +
                                     "  Or test it locally instead of on the shared server.`n" +
                                     "  -TestBuild overrides this when nobody else is playing.")
                        exit 1
                    }
                }
            }
            }
            $existing = $serverFiles.Keys | Where-Object { $_ -match ("(^|/)" + [regex]::Escape("$m.dll") + "$") } | Select-Object -First 1
            $target = if ($existing) { $existing } else { "BepInEx/plugins/$m.dll" }
            $plan += [pscustomobject]@{ Mod = $m; Version = $ver; Local = $dll; Target = $target; New = (-not $existing); Reason = $(if ($existing) { "update" } else { "new" }) }
        }
    }

    Write-Host "Plan:"
    if ($plan.Count -eq 0) { Write-Host "  nothing to upload; the server already matches." }
    $plan | ForEach-Object { Write-Host ("  {0} {1} -> {2}  ({3})" -f $_.Mod, $_.Version, $_.Target, $_.Reason) }
    if ($WhatIf) { Write-Host "WhatIf: nothing changed."; exit 0 }
    if ($plan.Count -eq 0 -and -not $RestartOnly) { exit 0 }

    $wasOn = [bool]$server.on

    # ── save the world before stopping (DatHost's stop does not) ────────────────
    if ($wasOn -and -not $NoRestart) {
        if ($SkipSave) {
            Write-Warning "SkipSave: not forcing a world save. Anything since the last save is lost on stop unless you ran /save in-game."
        } else {
            $before = Get-SaveEvidence
            Write-Host ("Asking the server to save (last save number so far: {0})..." -f $before.LastNumber)
            Send-ServerFile ([byte[]]@()) "save-now" $saveTrigger
            $saved = $false
            $deadline = (Get-Date).AddSeconds(60)
            do {
                Start-Sleep -Seconds 4
                $now = Get-SaveEvidence
                if ($now.Saves -gt $before.Saves -or $now.LastNumber -gt $before.LastNumber) { $saved = $true; break }
            } while ((Get-Date) -lt $deadline)
            if (-not $saved) {
                Write-Error "No world save appeared within 60 s of the save-now trigger. Either the server does not yet run a TheGreatestMap version with the trigger (0.1.5+), or saving failed. Nothing was changed. Save by hand with /save in-game as an admin, then re-run with -SkipSave."
                exit 1
            }
            Write-Host ("World saved: {0}" -f ($now.LastSaveLine -replace '\s+', ' ')) -ForegroundColor Green
        }

        Write-Host "Stopping server..."
        Invoke-RestMethod -Method Post -Uri "$base/game-servers/$id/stop" -Headers $headers | Out-Null
        Wait-ServerState $false | Out-Null
        Write-Host "Stopped."
    } elseif ($wasOn) {
        Write-Warning "Server is running and -NoRestart was given: files are replaced now but the running game keeps the old code until its next restart."
    }

    # ── upload ──────────────────────────────────────────────────────────────────
    $n = 0
    foreach ($p in $plan) {
        Send-ServerFile ([IO.File]::ReadAllBytes($p.Local)) (Split-Path $p.Local -Leaf) $p.Target
        $n++
        Write-Host ("Uploaded {0}/{1}: {2} -> {3}" -f $n, $plan.Count, $p.Mod, $p.Target)
    }
} finally {
    $client.Dispose()
    # Downloaded packages are scratch; don't leave them in %TEMP%.
    foreach ($t in $tempDownloads) { if (Test-Path $t) { Remove-Item $t -Recurse -Force -ErrorAction SilentlyContinue } }
}

if ($wasOn -and -not $NoRestart) {
    Write-Host "Starting server..."
    Invoke-RestMethod -Method Post -Uri "$base/game-servers/$id/start" -Headers $headers | Out-Null
    $s = Wait-ServerState $true
    Write-Host ("Running again ('{0}')." -f $s.name)
}
Write-Host "Done."
