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

# Which side of a dedicated-server setup each mod belongs on; see mods.json.
$sides = @{}
$sidesPath = Join-Path $repo "mods.json"
if (Test-Path $sidesPath) {
    $sidesDoc = Get-Content $sidesPath -Raw | ConvertFrom-Json
    foreach ($p in $sidesDoc.mods.PSObject.Properties) { $sides[$p.Name] = $p.Value.side }
}

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

# Thunderstore and Hexium run the same API, so one function serves both.  Both edge-cache this
# endpoint for minutes after a publish -- long enough to report the previous version as current
# and send you looking for a failure that did not happen -- so every request carries a
# cache-buster and a no-cache header.
function Get-Published($repository, $namespace, $name) {
    if (-not $name) { return [pscustomobject]@{ Version = "n/a"; Updated = $null } }
    $bust = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
    $uri  = "$repository/api/experimental/package/$namespace/$name/?cb=$bust"
    try {
        $r = Invoke-RestMethod -Uri $uri -TimeoutSec 25 -Headers @{ "Cache-Control" = "no-cache" }
        return [pscustomobject]@{ Version = $r.latest.version_number; Updated = $r.date_updated }
    } catch {
        $code = 0
        if ($_.Exception.Response) { $code = [int]$_.Exception.Response.StatusCode }
        if ($code -eq 404) { return [pscustomobject]@{ Version = "none"; Updated = $null } }
        return [pscustomobject]@{ Version = "error"; Updated = $null }
    }
}

# What the server is actually running, from the DLL itself rather than its hash.
# Directory.Build.props stamps each mod's manifest version into its assembly and the SDK appends
# the commit, so a DLL names both its release and the source it was built from.  That separates
# the three cases a hash cannot: an older version still deployed, the same version rebuilt, and
# the identical build.  DLLs built before that stamping all claim 1.0.0.0 and cannot be placed.
function Get-ServerDllState([byte[]]$bytes, [string]$localDll) {
    $tmp = Join-Path ([IO.Path]::GetTempPath()) ("modstatus-" + [Guid]::NewGuid().ToString("N") + ".dll")
    try {
        [IO.File]::WriteAllBytes($tmp, $bytes)
        $remote = [Diagnostics.FileVersionInfo]::GetVersionInfo($tmp)
        $local  = [Diagnostics.FileVersionInfo]::GetVersionInfo($localDll)
        $rv = $remote.FileVersion
        $lv = $local.FileVersion
        if ($rv -eq "1.0.0.0" -and $lv -ne "1.0.0.0") { return "unstamped" }
        $short = $rv -replace '\.0$', ''

        if ($rv -eq $lv) {
            # Same version. The informational version carries the commit; the hash catches a
            # recompile of that same commit.
            if ($remote.ProductVersion -ne $local.ProductVersion) { return "$short other commit" }
            $sha = [Security.Cryptography.SHA256]::Create()
            try { $hash = [BitConverter]::ToString($sha.ComputeHash($bytes)) -replace "-", "" }
            finally { $sha.Dispose() }
            if ($hash -eq (Get-FileHash $localDll -Algorithm SHA256).Hash) { return "$short exact" }
            return "$short rebuild"
        }

        try {
            if ([version]$rv -lt [version]$lv) { return "$short BEHIND" }
            return "$short ahead"
        } catch { return "$short differs" }
    } finally {
        if (Test-Path $tmp) { Remove-Item $tmp -Force }
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

    # [BepInProcess("valheim.exe")] makes BepInEx skip the plugin under valheim_server.exe, so a
    # mod carrying it cannot run on a dedicated server whatever mods.json claims.
    #
    # Line comments are stripped first: a mod that removed the gate is likely to explain why in a
    # comment naming the attribute, and matching that text would report the gate as still there.
    $clientGated = $false
    foreach ($f in Get-ChildItem $dir -Filter *.cs -File) {
        $code = (Get-Content $f.FullName) -replace '//.*$', '' -join "`n"
        if ($code -match 'BepInProcess\(\s*"valheim\.exe"\s*\)') { $clientGated = $true; break }
    }

    # The newest version the changelog names.  A changelog describing a version the version files
    # do not have means the notes and the number will ship out of step.
    #
    # A heading marked "unreleased" is the release being prepared, not one that shipped: the
    # convention here is to bump all four version files immediately after publishing, so the tree
    # normally carries a version the sites have never seen.  The version to compare the registries
    # and the server against is therefore the newest heading NOT marked unreleased.
    $clVersion = $null
    $clUnreleased = $false
    $clReleased = $null
    $changelog = Join-Path $dir "CHANGELOG.md"
    if (Test-Path $changelog) {
        foreach ($line in Get-Content $changelog) {
            if ($line -notmatch '^##\s+v?([0-9]+(\.[0-9]+)+)') { continue }
            $v = $Matches[1]
            $isUnreleased = $line -match '(?i)unreleased'
            if (-not $clVersion) { $clVersion = $v; $clUnreleased = $isUnreleased }
            if (-not $isUnreleased) { $clReleased = $v; break }
        }
    }


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
        TsUpdated   = $null
        Changelog   = $clVersion
        Unreleased  = $clUnreleased
        # What the sites and the server are expected to be holding right now.
        Expected    = $(if ($clUnreleased -and $clReleased) { $clReleased } else { $sources."manifest.json" })
        Side        = $(if ($sides.ContainsKey($d.Name)) { $sides[$d.Name] } else { $null })
        ClientGated = $clientGated
        Categories  = Get-TomlValue $tsToml "valheim"
        Build       = $build
        Dll         = $dll
        Dirty       = $dirty.Count
        ServerState = ""
    }
}

if (-not $NoRemote) {
    foreach ($r in $rows) {
        if (-not $r.Listed) { $r.Thunderstore = "unlisted"; $r.Hexium = "unlisted"; continue }
        $ts = Get-Published "https://thunderstore.io" $r.Namespace $r.Package
        $r.Thunderstore = $ts.Version
        $r.TsUpdated    = $ts.Updated
        $hexName = $r.Package
        if ($r.HasHexToml) { $hexName = Get-TomlValue (Join-Path $repo "$($r.Folder)\hexium.toml") "name" }
        else { $hexName = $null }
        $r.Hexium = (Get-Published "https://valheim.hexium.gg" $r.Namespace $hexName).Version
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
                # A client-only mod is not meant to be there. Say so instead of downloading and
                # comparing a DLL the server would never load -- and if a copy is still sitting
                # there from before, that is worth naming.
                if ($r.Side -eq "client") {
                    $r.ServerState = if ($target) { "client-only, still there" } else { "client-only" }
                    continue
                }
                if (-not $target) { $r.ServerState = "absent"; continue }
                if (-not $r.Dll)  { $r.ServerState = "present (no local build)"; continue }
                $bytes = $client.GetByteArrayAsync("$base/game-servers/$id/files/" + ($target -replace " ", "%20")).Result
                $r.ServerState = Get-ServerDllState $bytes $r.Dll
            }
        } catch {
            Write-Warning "Server check failed: $($_.Exception.Message)"
        }
    }
}

# A server holding the newest released version while the tree prepares the next one is correct,
# not behind.  Relabel before reporting so neither the table nor the action list cries wolf.
foreach ($r in $rows) {
    if ($r.Unreleased -and $r.ServerState -match 'BEHIND') {
        $srvVer = ($r.ServerState -split ' ')[0]
        if ($srvVer -eq $r.Expected) { $r.ServerState = "$srvVer released" }
    }
}

# ── report ──────────────────────────────────────────────────────────────────────
$table = $rows | ForEach-Object {
    $local = $_.Local
    if ($_.Unreleased) { $local = "$local*" }
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
if ($rows | Where-Object { $_.Unreleased }) {
    Write-Host ("* CHANGELOG.md marks this version unreleased, so it is the one being prepared. " +
                "The sites and the server are compared against the newest released version instead.")
}

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
        if ($live -ne $r.Expected) {
            $actions += "$($r.Folder): $name has $live, and $($r.Expected) is the newest released version."
        }
    }
    # The changelog and the version files have to name the same release, or the notes ship under
    # the wrong number -- and Thunderstore only refuses a duplicate version, so the two sites can
    # end up holding different code under one number.
    if ($r.Changelog -and $r.Changelog -ne $r.Local) {
        $actions += ("$($r.Folder): the version files say $($r.Local) but CHANGELOG.md's newest entry is " +
                     "$($r.Changelog). Publishing now ships one release's notes under the other's number.")
    }

    # Code committed after the published version went out, with no bump to carry it: the state
    # where a publish would put new code under a number players already have.  Three filters make
    # this signal rather than noise:
    #   * only .cs -- a toml or package.ps1 edit sweeping every mod is not a release;
    #   * git's --since needs a date it can parse, and silently ignores the whole filter on the
    #     microsecond ISO stamp the registries return, which returns the mod's entire history;
    #   * commits that also touched manifest.json are the release commits themselves.  Releases
    #     here are published first and committed second, so a release's own commit always lands
    #     after its publish; the version bump inside it is what tells it apart from later work,
    #     whose subject often still carries the old version number.
    if ($r.Thunderstore -eq $r.Local -and $r.TsUpdated) {
        $since = ([datetimeoffset]$r.TsUpdated).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
        $bumps = @(git -C $repo log --format=%h --since=$since -- "$($r.Folder)/manifest.json" 2>$null)
        $code  = @(git -C $repo log --format="%h %s" --since=$since -- ":(glob)$($r.Folder)/**/*.cs" 2>$null |
                   Where-Object { $bumps -notcontains ($_ -split " ")[0] })
        if ($code.Count -gt 0) {
            $shown = $code | Select-Object -First 2 |
                ForEach-Object { $_.Substring(0, [Math]::Min(64, $_.Length)) }
            $more = ""
            if ($code.Count -gt 2) { $more = " (+$($code.Count - 2) more)" }
            $actions += ("$($r.Folder): code committed since $($r.Local) went out, with no bump to carry it - " +
                         ($shown -join "; ") + "$more")
        }
    }

    # mods.json against the mod's own code. The attribute is what BepInEx enforces, so a mod
    # declared server-side while carrying the client gate would be deployed and never load.
    if ($r.Side -and $r.Side -ne "client" -and $r.ClientGated) {
        $actions += ("$($r.Folder): mods.json says '$($r.Side)' but the code carries " +
                     "[BepInProcess(`"valheim.exe`")], so BepInEx will not load it on the server. " +
                     "One of the two is wrong.")
    }
    if (-not $r.Side) {
        $actions += "$($r.Folder): no entry in mods.json, so deploys cannot tell whether the server needs it."
    }
    # The store page is a promise to other people: a server-side category on a client-gated mod
    # tells strangers to install it on their server, where it will never load.
    if ($r.ClientGated -and $r.Categories -and $r.Categories -match 'server-side') {
        $actions += ("$($r.Folder): thunderstore.toml lists the server-side category, but the code carries " +
                     "[BepInProcess(`"valheim.exe`")] and cannot load on a server. The listing tells people wrong.")
    }
    if ($r.ServerState -eq "client-only, still there") {
        $actions += "$($r.Folder): client-only, but a copy is still on the server doing nothing. Safe to delete there."
    }

    if ($r.Build -eq "STALE")     { $actions += "$($r.Folder): source is newer than the Release DLL - rebuild before packaging." }
    if ($r.Build -eq "not built") { $actions += "$($r.Folder): no Release build in bin\Release." }
    if ($r.ServerState -match 'BEHIND') {
        $actions += ("$($r.Folder): the server is running " + ($r.ServerState -replace ' BEHIND', '') +
                     " and this repo builds $($r.Local) - .\deploy-dathost.ps1 -Mod $($r.Folder)")
    }
    if ($r.Unreleased -and $r.Expected -eq $r.Local) {
        $actions += ("$($r.Folder): CHANGELOG.md marks $($r.Local) unreleased but there is no released " +
                     "heading under it to compare the sites against.")
    }
    if ($r.ServerState -match 'ahead') {
        $actions += ("$($r.Folder): the server is running " + ($r.ServerState -replace ' ahead', '') +
                     ", newer than this repo's $($r.Local) - someone else built it.")
    }
    if ($r.ServerState -eq "unstamped") {
        $actions += "$($r.Folder): the server's DLL was built before versions were stamped, so its version cannot be read. One deploy replaces it with an identifiable build."
    }
}
if ($actions.Count -eq 0) {
    Write-Host "Everything agrees: local versions, both registries, builds and the working tree." -ForegroundColor Green
} else {
    Write-Host "Needs attention:" -ForegroundColor Yellow
    $actions | ForEach-Object { Write-Host "  - $_" }
}
