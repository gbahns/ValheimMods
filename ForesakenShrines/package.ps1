# Builds the Release DLL and creates a Thunderstore-ready zip.
# Uses ZipArchive directly so entry names use forward slashes (ZIP spec 4.4.17).
# Output: ForesakenShrines-<version>.zip in the project directory.

param(
    [string]$Version = "0.8.0"
)

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$projectDir = $PSScriptRoot
$zipPath    = Join-Path $projectDir "ForesakenShrines-$Version.zip"

Write-Host "Building Release..."
dotnet build "$projectDir\ForesakenShrines.csproj" -c Release
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

Add-ZipEntry $zip "$projectDir\manifest.json"                            "manifest.json"
Add-ZipEntry $zip "$projectDir\icon.png"                                 "icon.png"
Add-ZipEntry $zip "$projectDir\README.md"                                "README.md"
Add-ZipEntry $zip "$projectDir\bin\Release\net48\ForesakenShrines.dll"  "BepInEx/plugins/ForesakenShrines.dll"

$zip.Dispose()
$stream.Dispose()

Write-Host "Package ready: $zipPath"
