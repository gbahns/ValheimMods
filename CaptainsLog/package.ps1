# Builds the Release DLL and creates a Thunderstore-ready zip.
# Uses ZipArchive directly so entry names use forward slashes (ZIP spec 4.4.17).
# Output: CaptainsLog-<version>.zip in the project directory.
#
# Usage:
#   package.ps1 -Version "1.0.0"              # build + zip only
#   package.ps1 -Version "1.0.0" -Publish     # build + zip + upload to Thunderstore
#
# Publishing requires the TCLI_AUTH_TOKEN environment variable to be set.
# Get your token from: thunderstore.io -> Settings -> Teams -> Service Accounts

param(
    [string]$Version = "1.0.0",
    [switch]$Publish
)

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$projectDir = $PSScriptRoot
$zipPath    = Join-Path $projectDir "CaptainsLog-$Version.zip"

Write-Host "Building Release..."
dotnet build "$projectDir\CaptainsLog.csproj" -c Release
if ($LASTEXITCODE -ne 0) { Write-Error "Build failed."; exit 1 }

if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

$stream = [System.IO.File]::Open($zipPath, [System.IO.FileMode]::Create)
$zip    = New-Object System.IO.Compression.ZipArchive($stream, [System.IO.Compression.ZipArchiveMode]::Create)

function Add-ZipEntry($archive, $filePath, $entryName) {
    $entry       = $archive.CreateEntry($entryName, [System.IO.Compression.CompressionLevel]::Optimal)
    $entryStream = $entry.Open()
    $fileStream  = [System.IO.File]::OpenRead($filePath)
    $fileStream.CopyTo($entryStream)
    $fileStream.Dispose()
    $entryStream.Dispose()
}

Add-ZipEntry $zip "$projectDir\manifest.json"                                       "manifest.json"
Add-ZipEntry $zip "$projectDir\icon.png"                                            "icon.png"
Add-ZipEntry $zip "$projectDir\README.md"                                           "README.md"
if (Test-Path "$projectDir\CHANGELOG.md") { Add-ZipEntry $zip "$projectDir\CHANGELOG.md" "CHANGELOG.md" }
Add-ZipEntry $zip "$projectDir\bin\Release\net462\CaptainsLog.dll"    "BepInEx/plugins/CaptainsLog.dll"

$zip.Dispose()
$stream.Dispose()

Write-Host "Package ready: $zipPath"

if ($Publish) {
    if (-not $env:TCLI_AUTH_TOKEN) {
        Write-Error "TCLI_AUTH_TOKEN is not set. Get your token from thunderstore.io -> Settings -> Teams -> Service Accounts"
        exit 1
    }

    Write-Host "Publishing to Thunderstore..."
    tcli publish --file "$zipPath" --config-path "$projectDir\thunderstore.toml"
    if ($LASTEXITCODE -ne 0) { Write-Error "Publish failed."; exit 1 }

    Write-Host "Published successfully."
}
