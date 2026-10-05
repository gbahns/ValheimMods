# Make Gale notice a version that was just published.
#
# Gale does not query Thunderstore live. It reads a pre-built listing index
# (/c/<game>/api/v1/package-listing-index/), keeps its own copy of the result in
# <appdata>/com.kesomannen.gale/<game>/thunderstore_cache.json, and holds that copy for a while -
# long enough that a version published minutes ago is routinely invisible, which is the whole
# reason this script exists. Deleting the cache leaves it nothing to fall back on, so the next
# launch has to fetch.
#
# The catch, and the reason this waits rather than just deleting: that index is not rebuilt on
# publish. Measured on 2026-10-04, the copy being served was built at 01:02:55 UTC, four seconds
# before the version published landed at 01:02:59 - and it was still the copy being served half an
# hour later, with the CDN's own 5-minute s-maxage expiring and renewing against an origin blob
# that had not changed. So the wait here is mostly Thunderstore's rebuild cadence, not Gale's
# polling and not the CDN. Clearing Gale's cache before that rebuild re-caches a listing that does
# not contain what was just published, and reads as the clear not working.
#
# Hence: confirm the version is live in Thunderstore's database (instant - that endpoint is a real
# query), note when it landed, and wait for the index to be rebuilt after that moment before
# clearing anything. Two rebuilds were timed on 2026-10-04 - 01:02:55 and 01:32:48 UTC, 29m53s
# apart - so the cadence looks like 30 minutes. The default timeout allows for that plus slack.
# Treat 30 minutes as one measurement rather than a documented guarantee.
#
# If someone needs the mod *now*, do not wait for any of this - the direct download URL reads the
# live database and works the moment a publish finishes:
#   https://thunderstore.io/package/download/<namespace>/<name>/<version>/
#
# Usage:
#   .\refresh-gale.ps1 -Mod DiagnoseServerLag     # wait for the index, then clear Gale's cache
#   .\refresh-gale.ps1 -Mod X -NoWait             # clear now, without waiting (see the catch)
#   .\refresh-gale.ps1                            # clear now, no version to wait for
#   .\refresh-gale.ps1 -Mod X -WhatIf             # say what it would do
#
# Nothing here is needed to test your own build: dotnet build -c Release already deploys the DLL
# into the test profiles. This is for when other people have to install what you published.

param(
    [string]$Mod,
    [string]$Namespace,
    [string]$Name,
    [string]$Version,
    [string]$Game = "valheim",
    [int]$TimeoutSeconds = 2700,
    [switch]$NoWait,
    [switch]$Force,
    [switch]$WhatIf
)

$ErrorActionPreference = "Stop"

function Get-TomlValue($path, $key) {
    if (-not (Test-Path $path)) { return $null }
    $line = Select-String -Path $path -Pattern "^$key\s*=" | Select-Object -First 1
    if (-not $line) { return $null }
    return ($line.Line -replace "^$key\s*=\s*", "").Trim().Trim([char]34)
}

# ---- what are we waiting for -------------------------------------------------------------------

if ($Mod) {
    $modDir = Join-Path $PSScriptRoot $Mod
    if (-not (Test-Path $modDir)) { Write-Error "No such mod folder: $modDir"; exit 1 }
    $toml = Join-Path $modDir "thunderstore.toml"
    if (-not $Namespace) { $Namespace = Get-TomlValue $toml "namespace" }
    if (-not $Name)      { $Name      = Get-TomlValue $toml "name" }
    if (-not $Version) {
        $manifest = Join-Path $modDir "manifest.json"
        if (Test-Path $manifest) {
            $Version = (Get-Content $manifest -Raw | ConvertFrom-Json).version_number
        }
    }
    if (-not $Namespace -or -not $Name -or -not $Version) {
        Write-Error "Could not read namespace/name/version for $Mod"; exit 1
    }
}

$waiting = $Namespace -and $Name -and $Version -and -not $NoWait

# ---- wait for the index Gale actually reads -----------------------------------------------------

if ($waiting) {
    $pkgUrl = "https://thunderstore.io/api/experimental/package/$Namespace/$Name/"
    $idxUrl = "https://thunderstore.io/c/$Game/api/v1/package-listing-index/"
    $noCache = @{ "Cache-Control" = "no-cache" }
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $landed = $null

    Write-Host "Waiting for Thunderstore to serve $Namespace-$Name $Version ..."

    # 1. Is it in the live database yet, and when did it land? A publish that has not settled
    #    reports an older 'latest', so this polls rather than assuming.
    while ($true) {
        $live = $null
        try { $live = Invoke-RestMethod -Uri $pkgUrl -Headers $noCache -TimeoutSec 30 } catch { }
        if ($live -and $live.latest.version_number -eq $Version) {
            $landed = [datetime]::Parse($live.latest.date_created,
                                        [Globalization.CultureInfo]::InvariantCulture,
                                        [Globalization.DateTimeStyles]::AdjustToUniversal)
            Write-Host ("  database has {0}, published {1:HH:mm:ss} UTC" -f $Version, $landed)
            break
        }
        if ($live) { Write-Host ("  database still says {0}; waiting" -f $live.latest.version_number) }
        else       { Write-Host "  could not read the package endpoint; retrying" }
        if ((Get-Date) -gt $deadline) {
            Write-Error "$Version never appeared in Thunderstore's database. Nothing cleared."
            exit 1
        }
        Start-Sleep -Seconds 15
    }

    # 2. The index is a CDN blob. Wait until the copy being served was built after the version
    #    landed - otherwise clearing the cache just re-reads a listing that predates it.
    while ($true) {
        $built = $null
        $age = ""
        try {
            $head = Invoke-WebRequest -Uri $idxUrl -Method Head -Headers $noCache -UseBasicParsing -TimeoutSec 30
            $lm = $head.Headers["Last-Modified"]
            if ($lm) {
                $built = [datetime]::Parse($lm,
                                           [Globalization.CultureInfo]::InvariantCulture,
                                           [Globalization.DateTimeStyles]::AdjustToUniversal)
            }
            if ($head.Headers["Age"]) { $age = " (edge copy {0}s old)" -f $head.Headers["Age"] }
        } catch { }

        if ($built -and $built -ge $landed) {
            Write-Host ("  listing index rebuilt {0:HH:mm:ss} UTC - it now carries {1}" -f $built, $Version)
            break
        }
        if ($built) {
            Write-Host ("  listing index still from {0:HH:mm:ss} UTC, before the publish{1}" -f $built, $age)
        } else {
            Write-Host "  could not read the listing index headers; retrying"
        }
        if ((Get-Date) -gt $deadline) {
            Write-Error ("The listing index was not rebuilt within {0}s. Nothing cleared - clearing now " +
                         "would re-cache a listing that predates {1}. Re-run in a few minutes." -f $TimeoutSeconds, $Version)
            exit 1
        }
        Start-Sleep -Seconds 20
    }
}
elseif ($Namespace -and $Name -and $Version) {
    Write-Host "-NoWait: not checking whether Thunderstore serves $Version yet."
}

# ---- clear the cache ---------------------------------------------------------------------------

$galeDir = Join-Path $env:APPDATA "com.kesomannen.gale"
$gameDir = Join-Path $galeDir $Game
if (-not (Test-Path $gameDir)) {
    Write-Warning "No Gale data for game '$Game' at $gameDir - nothing to clear."
    exit 0
}

# Gale keeps the listing in memory as well as on disk, so a running instance goes on showing the
# old version whatever this does to the file. It has to be restarted either way.
$running = Get-Process gale -ErrorAction SilentlyContinue
if ($running -and -not $Force) {
    $pids = ($running | ForEach-Object { $_.Id }) -join ", "
    Write-Warning "Gale is running (pid $pids)."
    Write-Warning "It holds the listing in memory, so clearing the file now would not change what it shows."
    Write-Warning "Close Gale and re-run, or pass -Force to clear anyway and restart it yourself."
    exit 1
}

$cleared = @()
foreach ($leaf in @("thunderstore_cache.json", "hexium_cache.json")) {
    $path = Join-Path $gameDir $leaf
    if (-not (Test-Path $path)) { continue }
    $size = (Get-Item $path).Length
    if ($WhatIf) {
        Write-Host ("would delete {0} ({1:n0} bytes)" -f $path, $size)
    } else {
        Remove-Item $path -Force
        Write-Host ("cleared {0} ({1:n0} bytes)" -f $leaf, $size)
    }
    $cleared += $leaf
}

if ($cleared.Count -eq 0) {
    Write-Host "Gale had no cached listing for '$Game' - it will fetch on next launch anyway."
} elseif (-not $WhatIf) {
    Write-Host "Start Gale and it will fetch a fresh listing."
}
