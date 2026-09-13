# Read-only status board for every mod in this repo.
#
# Per mod it answers: do the local version numbers agree (manifest.json, thunderstore.toml,
# hexium.toml, the BepInPlugin attribute), what version is live on Thunderstore and on Hexium,
# is the Release build newer than the source, is the working tree clean, and with -Server
# whether the DatHost server holds the exact DLL that was built here.
#
# Usage:
#   .\mod-status.ps1                            # local files + both registries
#   .\mod-status.ps1 -Server                    # also hash-check the DatHost server
#   .\mod-status.ps1 -NoRemote                  # local only, no network
#   .\mod-status.ps1 -Mod TheGreatestMap,PauseMyServer
#
# Nothing here builds, writes, uploads, publishes, or restarts anything.

param(
    [string[]]$Mod,
    [switch]$Server,
    [switch]$NoRemote,
    [string]$SecretsPath = (Join-Path $env:USERPROFILE ".dathost")
)

$repo = $PSScriptRoot
$skipDirs = @("bin", "obj", ".claude")

function Get-TomlValue($path, $key) {
    if (-not (Test-Path $path)) { return $null }
    $line = Select-String -Path $path -Pattern "^$key\s*=" | Select-Object -First 1
    if (-not $line) { return $null }
    return ($line.Line -replace "^$key\s*=\s*", "").Trim().Trim('"')
}

# The version baked into the DLL comes from the BepInPlugin attribute: either a ModVersion
# constant or a literal third argument.
function Get-PluginVersion($dir) {
    foreach ($f in Get-ChildItem $dir -Filter *.cs -File) {
        $text = Get-Content $f.FullName -Raw
        if ($text -notmatch 'BepInPlugin\(') { continue }
        $m = [regex]::Match($text, 'ModVersion\s*=\s*"([^"]+)"')
        if ($m.Success) { return $m.Groups[1].Value }
        $m = [regex]::Match($text, 'BepInPlugin\([^)]*?"([0-9][^"]*)"\s*\)')
        if ($m.Success) { return $m.Groups[1].Value }
    }
    return $null
}

# Thunderstore and Hexium run the same API, so one function serves both.
function Get-Published($repository, $namespace, $name) {
    if (-not $name) { return "n/a" }
    $uri = "$repository/api/experimental/package/$namespace/$name/"
    try {
        return (Invoke-RestMethod -Uri $uri -TimeoutSec 25).latest.version_number
    } catch {
        $code = 0
        if ($_.Exception.Response) { $code = [int]$_.Exception.Response.StatusCode }
        if ($code -eq 404) { return "none" }
        return "error"
    }
}

# A mod is a top-level folder with both a project and a manifest.  Test projects are skipped;
# the pattern is anchored so that a name like TheGreatestMap ("grea-test") is not caught.
$dirs = @(Get-ChildItem $repo -Directory | Where-Object {
    (Test-Path (Join-Path $_.FullName "manifest.json")) -and
    @(Get-ChildItem $_.FullName -Filter *.csproj -File).Count -gt 0 -and
    $_.Name -notmatch 'Tests?\d*$' -and $_.Name -ne 'TestMod'
} | Sort-Object Name)
if ($Mod) { $dirs = @($dirs | Where-Object { $Mod -contains $_.Name }) }
if ($dirs.Count -eq 0) { Write-Error "No mods matched."; exit 1 }

$rows = @()
foreach ($d in $dirs) {
    $dir      = $d.FullName
    $manifest = (Get-Content (Join-Path $dir "manifest.json") -Raw | ConvertFrom-Json)
    $tsToml   = Join-Path $dir "thunderstore.toml"
    $hxToml   = Join-Path $dir "hexium.toml"

    $pkgName = Get-TomlValue $tsToml "name"
    if (-not $pkgName) { $pkgName = $manifest.name }
    $ns = Get-TomlValue $tsToml "namespace"
    if (-not $ns) { $ns = "DeathMonger" }

    # Every place a version number lives.  All of them have to agree before a publish.
    $sources = [ordered]@{
        "manifest.json"     = $manifest.version_number
        "thunderstore.toml" = Get-TomlValue $tsToml "versionNumber"
        "hexium.toml"       = Get-TomlValue $hxToml "versionNumber"
        "BepInPlugin"       = Get-PluginVersion $dir
    }
    $found  = @($sources.GetEnumerator() | Where-Object { $_.Value })
    $agreed = @($found | ForEach-Object { $_.Value } | Sort-Object -Unique)

    # Is the Release DLL newer than everything it is built from?
    $dll = @("net48", "net462") |
        ForEach-Object { Join-Path $dir "bin\Release\$_\$($d.Name).dll" } |
        Where-Object { Test-Path $_ } | Select-Object -First 1
    $build = "not built"
    if ($dll) {
        $newest = Get-ChildItem $dir -Recurse -File |
            Where-Object {
                if ($_.Extension -notmatch "^[.](cs|csproj)$") { return $false }
                # bin, obj and the worktrees under .claude hold copies, not the source of record.
                $parts = $_.FullName.Substring($dir.Length).Split([IO.Path]::DirectorySeparatorChar)
                -not ($parts | Where-Object { $skipDirs -contains $_ })
            } |
            Sort-Object LastWriteTime -Descending | Select-Object -First 1
        $build = "current"
        if ($newest -and $newest.LastWriteTime -gt (Get-Item $dll).LastWriteTime) { $build = "STALE" }
    }

    $dirty = @(git -C $repo status --porcelain -- $d.Name 2>$null)

    $rows += [pscustomobject]@{
        Folder      = $d.Name
        Package     = $pkgName
        Namespace   = $ns
        Local       = $sources."manifest.json"
        Sources     = $sources
        Agreed      = ($agreed.Count -le 1)
        Listed      = (Test-Path $tsToml)
        HasHexToml  = (Test-Path $hxToml)
        Thunderstore= "-"
        Hexium      = "-"
        Build       = $build
        Dll         = $dll
        Dirty       = $dirty.Count
        ServerState = ""
    }
}

if (-not $NoRemote) {
    foreach ($r in $rows) {
        if (-not $r.Listed) { $r.Thunderstore = "unlisted"; $r.Hexium = "unlisted"; continue }
        $r.Thunderstore = Get-Published "https://thunderstore.io" $r.Namespace $r.Package
        $hexName = $r.Package
        if ($r.HasHexToml) { $hexName = Get-TomlValue (Join-Path $repo "$($r.Folder)\hexium.toml") "name" }
        else { $hexName = $null }
        $r.Hexium = Get-Published "https://valheim.hexium.gg" $r.Namespace $hexName
    }
}

# ── DatHost server ──────────────────────────────────────────────────────────────
# Read-only: lists BepInEx/plugins, downloads each mod's DLL and compares its hash to the
# local Release build.  The server is never stopped, started, or written to.
if ($Server) {
    if (-not (Test-Path $SecretsPath)) {
        Write-Warning "No DatHost secrets at $SecretsPath; skipping the server check."
    } else {
        try {
            $cred  = Get-Content $SecretsPath -Raw | ConvertFrom-Json
            $basic = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("$($cred.email):$($cred.token)"))
            $base  = "https://dathost.net/api/0.1"
            $id    = $cred.server_id
            $headers = @{ Authorization = "Basic $basic" }

            Add-Type -AssemblyName System.Net.Http
            $client = New-Object System.Net.Http.HttpClient
            $client.Timeout = [TimeSpan]::FromMinutes(2)
            $client.DefaultRequestHeaders.Authorization =
                New-Object System.Net.Http.Headers.AuthenticationHeaderValue("Basic", $basic)

            # Not $server: that name belongs to the -Server switch parameter.
            $srv = Invoke-RestMethod -Uri "$base/game-servers/$id" -Headers $headers -TimeoutSec 30
            Write-Host ("Server '{0}': on={1}, booting={2}" -f $srv.name, $srv.on, $srv.booting)

            $listing = Invoke-RestMethod -Headers $headers -TimeoutSec 60 `
                -Uri "$base/game-servers/$id/files?path=BepInEx/plugins&hide_default_files=true"
            $paths = @()
            foreach ($e in @($listing)) {
                if ($e.deleted) { continue }
                $p = $e.path
                if (-not $p) { $p = $e.name }
                if (-not $p) { continue }
                if ($p -match '/$') { continue }
                if ($p -notmatch '^BepInEx/plugins/') { $p = "BepInEx/plugins/$p" }
                $paths += $p
            }

            foreach ($r in $rows) {
                $target = $paths | Where-Object { $_ -match ("(^|/)" + [regex]::Escape("$($r.Folder).dll") + "$") } |
                    Select-Object -First 1
                if (-not $target) { $r.ServerState = "absent"; continue }
                if (-not $r.Dll)  { $r.ServerState = "on server (no local build)"; continue }
                $bytes = $client.GetByteArrayAsync("$base/game-servers/$id/files/" + ($target -replace " ", "%20")).Result
                $sha = [System.Security.Cryptography.SHA256]::Create()
                try { $remote = [BitConverter]::ToString($sha.ComputeHash($bytes)) -replace "-", "" }
                finally { $sha.Dispose() }
                if ($remote -eq (Get-FileHash $r.Dll -Algorithm SHA256).Hash) { $r.ServerState = "matches build" }
                else { $r.ServerState = "DIFFERS" }
            }
        } catch {
            Write-Warning "Server check failed: $($_.Exception.Message)"
        }
    }
}

# ── report ──────────────────────────────────────────────────────────────────────
$table = $rows | ForEach-Object {
    $local = $_.Local
    if (-not $_.Agreed) { $local = "$local (!)" }
    $out = [ordered]@{
        Mod          = $_.Folder
        Local        = $local
        Thunderstore = $_.Thunderstore
        Hexium       = $_.Hexium
        Build        = $_.Build
        Git          = $(if ($_.Dirty -gt 0) { "$($_.Dirty) changed" } else { "clean" })
    }
    if ($Server) { $out.Server = $_.ServerState }
    [pscustomobject]$out
}
Write-Host ""
$table | Format-Table -AutoSize

# ── what to do about it ─────────────────────────────────────────────────────────
$actions = @()
foreach ($r in $rows) {
    if (-not $r.Agreed) {
        $detail = ($r.Sources.GetEnumerator() | Where-Object { $_.Value } |
            ForEach-Object { "$($_.Key)=$($_.Value)" }) -join ", "
        $actions += "$($r.Folder): version numbers disagree ($detail) - fix all of them before publishing."
    }
    if (-not $r.Listed) {
        $actions += "$($r.Folder): no thunderstore.toml, so it has never been published. Local only."
        continue
    }
    if (-not $r.HasHexToml) { $actions += "$($r.Folder): no hexium.toml, so it publishes to Thunderstore only." }
    foreach ($site in @(@("Thunderstore", $r.Thunderstore), @("Hexium", $r.Hexium))) {
        $name = $site[0]; $live = $site[1]
        if ($live -in @("-", "n/a", "error", "unlisted")) { continue }
        if ($live -eq "none") { $actions += "$($r.Folder): not on $name yet (local $($r.Local))." ; continue }
        if ($live -ne $r.Local) { $actions += "$($r.Folder): $name has $live, local is $($r.Local)." }
    }
    if ($r.Build -eq "STALE")     { $actions += "$($r.Folder): source is newer than the Release DLL - rebuild before packaging." }
    if ($r.Build -eq "not built") { $actions += "$($r.Folder): no Release build in bin\Release." }
    if ($r.ServerState -eq "DIFFERS") { $actions += "$($r.Folder): the DatHost server has a different DLL - .\deploy-dathost.ps1 -Mod $($r.Folder)" }
}
if ($actions.Count -eq 0) {
    Write-Host "Everything agrees: local versions, both registries, builds and the working tree." -ForegroundColor Green
} else {
    Write-Host "Needs attention:" -ForegroundColor Yellow
    $actions | ForEach-Object { Write-Host "  - $_" }
}
