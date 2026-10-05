# Build every csproj in the repo. Stops on first failure.
# Usage:
#   .\build-all.ps1                       # Release
#   .\build-all.ps1 -Configuration Debug

param(
    [string]$Configuration = "Release"
)

$projects = Get-ChildItem -Path $PSScriptRoot -Recurse -Filter *.csproj |
    Where-Object {
        $_.FullName -notmatch '\\(\.claude|bin|obj)\\' -and
        # Anchored to the end of the file name on purpose. The old pattern was a bare
        # 'Tests', and -notmatch is case insensitive, so TheGreatestShips - The-Grea-tests-hips
        # - matched it and was silently never built. A substring test on a project name is
        # too loose to be safe here.
        $_.Name -notmatch '(Tests\d*|TestsBroke|Broke)\.csproj$' -and
        $_.Name -notmatch '^TestMod\.csproj$'
    }

foreach ($csproj in $projects) {
    Write-Host "Building $($csproj.Name) ($Configuration)..." -ForegroundColor Cyan
    dotnet build $csproj.FullName -c $Configuration --nologo
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Build failed: $($csproj.Name)"
        exit 1
    }
}

Write-Host "All $($projects.Count) project(s) built." -ForegroundColor Green
