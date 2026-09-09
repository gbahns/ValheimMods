# Builds the final "moved to DeathMonger" release for the legacy MojoRyzen/GrabMaterials
# listing.  Same DLL as the main package; manifest/README/CHANGELOG come from
# redirect-MojoRyzen/.  See PACKAGING.md -> "Legacy MojoRyzen Listing".
#
# Usage:
#   package-redirect.ps1 -Version "2.0.0"            # build + zip only
#   package-redirect.ps1 -Version "2.0.0" -Publish   # + upload (token must belong to MojoRyzen)

param(
    [string]$Version = "2.0.0",
    [switch]$Publish
)

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$projectDir  = $PSScriptRoot
$redirectDir = Join-Path $projectDir "redirect-MojoRyzen"
$zipPath     = Join-Path $projectDir "GrabMaterials-MojoRyzen-redirect-$Version.zip"

Write-Host "Building Release..."
dotnet build "$projectDir\GrabMaterials.csproj" -c Release
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

Add-ZipEntry $zip "$redirectDir\manifest.json"                        "manifest.json"
Add-ZipEntry $zip "$projectDir\icon.png"                              "icon.png"
Add-ZipEntry $zip "$redirectDir\README.md"                            "README.md"
Add-ZipEntry $zip "$redirectDir\CHANGELOG.md"                         "CHANGELOG.md"
Add-ZipEntry $zip "$projectDir\bin\Release\net462\GrabMaterials.dll"  "BepInEx/plugins/GrabMaterials.dll"

$zip.Dispose()
$stream.Dispose()

Write-Host "Package ready: $zipPath"

if ($Publish) {
    if (-not $env:TCLI_AUTH_TOKEN) {
        Write-Error "TCLI_AUTH_TOKEN is not set."
        exit 1
    }
    Write-Host "Publishing to Thunderstore (MojoRyzen)..."
    tcli publish --file "$zipPath" --config-path "$redirectDir\thunderstore.toml"
    if ($LASTEXITCODE -ne 0) { Write-Error "Publish failed."; exit 1 }
    Write-Host "Published successfully."
}
