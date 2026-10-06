# Compiles a mod against the game's ORIGINAL assemblies instead of the publicized copies, so
# every private field, property or method the code reaches into shows up as a compiler error.
#
# The publicized references in Libs make private members compile, but Mono enforces access at
# runtime: a direct read throws FieldAccessException (or MethodAccessException) every time the
# line runs -- in a per-frame patch, every frame.  TheGreatestShips shipped that three times
# (InventoryGrid.m_elements in 0.9.1, InventoryGui.m_containerGrid in 0.9.8, Player.m_attachPoint
# in 0.9.10) before this check existed.  Anything listed here needs AccessTools.FieldRefAccess /
# AccessTools.Method, or a public accessor.
#
#   .\audit-private-access.ps1 -Mod TheGreatestShips
#
# Builds to a temp folder and deploys nowhere; the tree is left untouched.
param([Parameter(Mandatory)][string]$Mod)

$proj = Join-Path $PSScriptRoot "$Mod\$Mod.csproj"
if (-not (Test-Path $proj)) { throw "No project at $proj" }
$audit = Join-Path $PSScriptRoot "$Mod\$Mod.Audit.csproj"
$out   = Join-Path $env:TEMP "audit-$Mod"
try {
    (Get-Content $proj -Raw) -replace '_publicized', '' | Set-Content $audit -Encoding utf8
    $log = & dotnet build $audit -c Release -p:DeployProfiles=none -o $out 2>&1
    $errors = $log | Select-String -Pattern 'error CS\d+' | ForEach-Object { $_.Line } |
        ForEach-Object { if ($_ -match '([A-Za-z0-9_]+\.cs)\((\d+),\d+\): (error CS\d+: .*?) \[') { "$($Matches[1]):$($Matches[2])  $($Matches[3])" } } |
        Sort-Object -Unique
    if ($errors) {
        Write-Host "Private members reached directly in $Mod (fix with AccessTools.FieldRefAccess / AccessTools.Method):" -ForegroundColor Yellow
        $errors | ForEach-Object { Write-Host "  $_" }
        exit 1
    }
    Write-Host "$Mod reaches no private game members directly." -ForegroundColor Green
}
finally {
    Remove-Item $audit -ErrorAction SilentlyContinue
    Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
}
