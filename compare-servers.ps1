# Compares two DiagnoseServerLag captures side by side.
#
# The point of this script is to answer one question honestly: given the same world, which machine
# has more room left. Wall-clock tick time cannot answer it, because most dedicated servers run a
# frame limiter and a limiter holds the tick at its configured length whether the server is using a
# tenth of a core or all of it. CPU time per wall second is the measurement that survives the cap,
# and it is the only one here that compares meaningfully between two different machines.
#
# Produce a capture on each server's own console:
#
#     dsl_bench 300
#
# which writes BepInEx/config/DiagnoseServerLag/lag-<role>-<timestamp>.csv on that machine.
#
# Usage:
#   compare-servers.ps1 -A dathost -B "C:\path\to\local\lag-dedicated-....csv"
#   compare-servers.ps1 -A a.csv -B b.csv -LabelA bahnsheim -LabelB local
#   compare-servers.ps1 -List                       # what captures exist on the DatHost server
#
# "dathost" in place of a path fetches the newest capture off the DatHost server over its REST API,
# using the same credentials as deploy-dathost.ps1 (%USERPROFILE%\.dathost).

param(
    [string]$A,
    [string]$B,
    [string]$LabelA,
    [string]$LabelB,
    [switch]$List,
    [string]$SecretsPath = (Join-Path $env:USERPROFILE ".dathost")
)

$ErrorActionPreference = "Stop"
$base = "https://dathost.net/api/0.1"
$remoteDir = "BepInEx/config/DiagnoseServerLag"

function Get-DatHostHeaders {
    if (-not (Test-Path $SecretsPath)) { throw "Secrets file not found: $SecretsPath" }
    $cred = Get-Content $SecretsPath -Raw | ConvertFrom-Json
    foreach ($k in @("email", "token", "server_id")) {
        if (-not $cred.$k) { throw "Secrets file is missing '$k'" }
    }
    $basic = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("$($cred.email):$($cred.token)"))
    [pscustomobject]@{ Headers = @{ Authorization = "Basic $basic" }; Id = $cred.server_id }
}

function Get-RemoteCaptures($h) {
    $listing = Invoke-RestMethod -Method Get -Headers $h.Headers `
        -Uri "$base/game-servers/$($h.Id)/files?path=$remoteDir&hide_default_files=true"
    @($listing) | Where-Object { $_.path -match '\.csv$' -and -not $_.deleted } | Sort-Object path
}

# Fetches the newest capture off the server into the scratch folder and returns the local path.
function Get-RemoteNewest($h) {
    $files = Get-RemoteCaptures $h
    if (-not $files) {
        throw "No captures in $remoteDir on the server. Run 'dsl_bench 300' on its console first."
    }
    # Names carry a sortable yyyyMMdd-HHmmss stamp, so the last by name is the newest.
    $newest = $files[-1].path
    $local = Join-Path ([IO.Path]::GetTempPath()) ([IO.Path]::GetFileName($newest))
    # -UseBasicParsing matters: PowerShell 5.1 otherwise tries the IE parsing engine and blocks on a
    # prompt that can never be answered in a non-interactive shell.
    Invoke-WebRequest -UseBasicParsing -Method Get -Headers $h.Headers `
        -Uri "$base/game-servers/$($h.Id)/files/$remoteDir/$([IO.Path]::GetFileName($newest))" -OutFile $local | Out-Null
    Write-Host "Fetched $newest from the server." -ForegroundColor DarkGray
    $local
}

function Resolve-Capture([string]$spec) {
    if ($spec -eq "dathost") { return Get-RemoteNewest (Get-DatHostHeaders) }
    if (-not (Test-Path $spec)) { throw "Capture not found: $spec" }
    (Resolve-Path $spec).Path
}

# The file is a '#key,value' metadata block followed by ordinary CSV.
function Read-Capture([string]$path) {
    $lines = Get-Content $path
    $meta = @{}
    foreach ($line in $lines) {
        if ($line -notmatch '^#') { break }
        $bits = ($line -replace '^#\s*', '') -split ',', 2
        if ($bits.Count -eq 2 -and $bits[0]) { $meta[$bits[0].Trim()] = $bits[1].Trim() }
    }
    $rows = $lines | Where-Object { $_ -notmatch '^#' } | ConvertFrom-Csv
    [pscustomobject]@{ Path = $path; Meta = $meta; Rows = @($rows) }
}

function Median($values) {
    $v = @($values | Where-Object { $_ -ne $null } | ForEach-Object { [double]$_ } | Sort-Object)
    if ($v.Count -eq 0) { return $null }
    if ($v.Count % 2 -eq 1) { return $v[[int](($v.Count - 1) / 2)] }
    return ($v[$v.Count / 2 - 1] + $v[$v.Count / 2]) / 2
}

function Maximum($values) {
    $v = @($values | Where-Object { $_ -ne $null } | ForEach-Object { [double]$_ })
    if ($v.Count -eq 0) { return $null }
    ($v | Measure-Object -Maximum).Maximum
}

function Total($values) {
    $v = @($values | Where-Object { $_ -ne $null } | ForEach-Object { [double]$_ })
    if ($v.Count -eq 0) { return 0 }
    ($v | Measure-Object -Sum).Sum
}

function HumanBytes([double]$b) {
    if ($b -ge 1073741824) { return "{0:N1} GB" -f ($b / 1073741824) }
    if ($b -ge 1048576)    { return "{0:N1} MB" -f ($b / 1048576) }
    if ($b -ge 1024)       { return "{0:N1} KB" -f ($b / 1024) }
    "{0:N0} B" -f $b
}

# ── listing mode ────────────────────────────────────────────────────────────────
if ($List) {
    $h = Get-DatHostHeaders
    $files = Get-RemoteCaptures $h
    if (-not $files) { Write-Host "No captures on the server yet. Run 'dsl_bench 300' on its console." ; exit 0 }
    Write-Host "Captures in $remoteDir on the DatHost server:`n"
    $files | Select-Object path, size | Format-Table -AutoSize
    exit 0
}

if (-not $A -or -not $B) {
    Write-Error "Give two captures: -A <path|dathost> -B <path|dathost>. Use -List to see what is on the server."
    exit 1
}

$capA = Read-Capture (Resolve-Capture $A)
$capB = Read-Capture (Resolve-Capture $B)
if (-not $LabelA) { $LabelA = if ($A -eq "dathost") { "bahnsheim" } else { $capA.Meta["role"] } }
if (-not $LabelB) { $LabelB = if ($B -eq "dathost") { "bahnsheim" } else { $capB.Meta["role"] } }
if (-not $LabelA) { $LabelA = "A" }
if (-not $LabelB) { $LabelB = "B" }

# ── the numbers ─────────────────────────────────────────────────────────────────
function Summarize($cap) {
    $r = $cap.Rows
    $cores = [double]($cap.Meta["cores"]); if (-not $cores -or $cores -lt 1) { $cores = 1 }
    $cpu = Median ($r | ForEach-Object { $_.cpu_ms_per_sec })
    $measured = (Total ($r | ForEach-Object { $_.cpu_measured })) -gt 0
    $tickMed = Median ($r | ForEach-Object { $_.frame_avg_ms })
    $tickWorst = Maximum ($r | ForEach-Object { $_.frame_max_ms })
    $stalls = Total ($r | ForEach-Object { $_.stalls })
    $secs = [Math]::Max(1, $r.Count)
    [pscustomobject]@{
        Seconds   = $r.Count
        Cores     = $cores
        Cpu       = $cpu
        CpuOk     = $measured
        CpuPeak   = Maximum ($r | ForEach-Object { $_.cpu_ms_per_sec })
        CoreShare = if ($measured) { $cpu / 1000 } else { $null }
        MachShare = if ($measured) { $cpu / (1000 * $cores) } else { $null }
        Headroom  = if ($measured -and $cpu -gt 0.1) { 1000 / $cpu } else { $null }
        TickMed   = $tickMed
        TickWorst = $tickWorst
        Stalls    = $stalls
        # Same test the mod's verdict uses: a worst tick close to the median with no stalls is a
        # frame cap, which means the tick figure describes the configuration and not the load.
        Capped    = ($stalls -eq 0 -and $tickWorst -le ($tickMed * 1.5 + 2))
        Zdos      = Median ($r | ForEach-Object { $_.zdos })
        Peers     = Median ($r | ForEach-Object { $_.peers })
        Sent      = Median ($r | ForEach-Object { $_.zdos_sent_sec })
        Recv      = Median ($r | ForEach-Object { $_.zdos_recv_sec })
        Gc0       = (Total ($r | ForEach-Object { $_.gc0 })) / $secs * 60
        Gc1       = (Total ($r | ForEach-Object { $_.gc1 })) / $secs * 60
        Gc2       = (Total ($r | ForEach-Object { $_.gc2 })) / $secs * 60
        Heap      = Median ($r | ForEach-Object { $_.heap_bytes })
        Working   = Median ($r | ForEach-Object { $_.working_set_bytes })
    }
}

$sa = Summarize $capA
$sb = Summarize $capB

# [ordered] so the columns come out in this order, and a real name for the first one: PowerShell
# rejects an empty property name outright.
function Row($name, $a, $b) { [pscustomobject][ordered]@{ "measure" = $name; $LabelA = $a; $LabelB = $b } }
function Pct($x) { if ($null -eq $x) { "n/a" } else { "{0:N1}%" -f ($x * 100) } }
function Num($x, $fmt = "N0") { if ($null -eq $x) { "n/a" } else { "{0:$fmt}" -f $x } }

$table = @(
    Row "captured seconds" (Num $sa.Seconds) (Num $sb.Seconds)
    Row "cores"            (Num $sa.Cores)   (Num $sb.Cores)
    Row "cpu"              $capA.Meta["cpu_name"] $capB.Meta["cpu_name"]
    Row "" "" ""
    Row "world objects"    (Num $sa.Zdos)    (Num $sb.Zdos)
    Row "players"          (Num $sa.Peers)   (Num $sb.Peers)
    Row "objects out/s"    (Num $sa.Sent)    (Num $sb.Sent)
    Row "" "" ""
    Row "tick median"      ("{0:N1} ms{1}" -f $sa.TickMed, $(if ($sa.Capped) { " (capped)" } else { "" })) `
                           ("{0:N1} ms{1}" -f $sb.TickMed, $(if ($sb.Capped) { " (capped)" } else { "" }))
    Row "tick worst"       ("{0:N0} ms" -f $sa.TickWorst) ("{0:N0} ms" -f $sb.TickWorst)
    Row "stalls"           (Num $sa.Stalls)  (Num $sb.Stalls)
    Row "" "" ""
    Row "CPU per second"   ("{0:N0} ms" -f $sa.Cpu) ("{0:N0} ms" -f $sb.Cpu)
    Row "  of one core"    (Pct $sa.CoreShare) (Pct $sb.CoreShare)
    Row "  of the machine" (Pct $sa.MachShare) (Pct $sb.MachShare)
    Row "CPU peak (core)"  (Pct $(if ($sa.CpuOk) { $sa.CpuPeak / 1000 })) (Pct $(if ($sb.CpuOk) { $sb.CpuPeak / 1000 }))
    Row "headroom"         ($(if ($sa.Headroom) { "{0:N1}x" -f $sa.Headroom } else { "n/a" })) `
                           ($(if ($sb.Headroom) { "{0:N1}x" -f $sb.Headroom } else { "n/a" }))
    Row "" "" ""
    Row "GC gen0 /min"     (Num $sa.Gc0)     (Num $sb.Gc0)
    Row "GC gen1 /min"     (Num $sa.Gc1)     (Num $sb.Gc1)
    Row "GC gen2 /min"     (Num $sa.Gc2 "N1") (Num $sb.Gc2 "N1")
    Row "heap"             (HumanBytes $sa.Heap)    (HumanBytes $sb.Heap)
    Row "working set"      (HumanBytes $sa.Working) (HumanBytes $sb.Working)
)

Write-Host ""
$table | Format-Table -AutoSize
Write-Host ""

# ── what it means, and when it means nothing ────────────────────────────────────
#
# A comparison of two servers carrying different amounts of world is a comparison of worlds, not of
# servers, and saying so is more useful than printing a ratio that looks authoritative. The check is
# loud on purpose: it is the single easiest way to draw a confident wrong conclusion from this table.
$comparable = $true
$zA = [double]$sa.Zdos; $zB = [double]$sb.Zdos
if ($zA -gt 0 -and $zB -gt 0) {
    $ratio = [Math]::Max($zA, $zB) / [Math]::Min($zA, $zB)
    if ($ratio -gt 1.1) {
        $comparable = $false
        Write-Host ("NOT COMPARABLE: the two captures hold different worlds - {0:N0} objects vs {1:N0}, {2:N1}x apart." -f $zA, $zB, $ratio) -ForegroundColor Yellow
        Write-Host "  CPU differs because the work differs. Put the same world on both before drawing a conclusion" -ForegroundColor Yellow
        Write-Host "  about the hardware, or read the per-object figures below as a rough normalization." -ForegroundColor Yellow
        if ($sa.CpuOk -and $sb.CpuOk) {
            Write-Host ("  CPU per 100k objects: {0} {1:N0} ms/s   {2} {3:N0} ms/s" -f `
                $LabelA, ($sa.Cpu / $zA * 100000), $LabelB, ($sb.Cpu / $zB * 100000)) -ForegroundColor Yellow
        }
        Write-Host ""
    }
}

if (-not $sa.CpuOk -or -not $sb.CpuOk) {
    Write-Host "CPU was not measurable on at least one side, so headroom cannot be compared." -ForegroundColor Yellow
    Write-Host "  Tick time alone cannot stand in for it on a capped server - that is the whole reason CPU is here." -ForegroundColor Yellow
}
elseif ($sa.Headroom -and $sb.Headroom) {
    $faster = if ($sa.Cpu -lt $sb.Cpu) { $LabelA } else { $LabelB }
    $factor = [Math]::Max($sa.Cpu, $sb.Cpu) / [Math]::Max(0.1, [Math]::Min($sa.Cpu, $sb.Cpu))
    # Only a matched pair earns the "same work" claim. Saying it about captures of different worlds
    # would contradict the warning printed immediately above it, and the per-object figures there
    # can point the opposite way - a small world on fast hardware still costs more per object.
    if ($comparable) {
        Write-Host ("{0} does the same work for {1:N1}x less CPU." -f $faster, $factor)
    }
    else {
        Write-Host ("{0} used {1:N1}x less CPU, but for a different amount of work - see the warning above." -f $faster, $factor) -ForegroundColor Yellow
    }
    Write-Host ("Headroom before one core is full: {0} {1:N1}x, {2} {3:N1}x{4}." -f $LabelA, $sa.Headroom, $LabelB, $sb.Headroom,
        $(if ($comparable) { "" } else { ", each against its own world" }))
    # Valheim's simulation is effectively single threaded, so one core is the ceiling that matters
    # and spare cores do not raise it.
    Write-Host "Measured against one core, which is the ceiling that matters: the simulation is effectively single threaded."
}
Write-Host ""
