# Builds the Release DLL and creates a Thunderstore-ready zip.
# Uses ZipArchive directly so entry names use forward slashes (ZIP spec 4.4.17).
# Output: SmartSilencer-<version>.zip in the project directory.
#
# Usage:
#   package.ps1 -Version "1.1.0"              # build + zip only
#   package.ps1 -Version "1.1.0" -Publish     # build + zip + upload to Thunderstore
#   package.ps1 -Version "x.y.z" -Hexium      # build + zip + upload to Hexium (valheim.hexium.gg)
#   (both switches may be combined)
#
# Publishing requires the TCLI_AUTH_TOKEN environment variable to be set.
# Get your token from: thunderstore.io -> Settings -> Teams -> Service Accounts

param(
    [string]$Version = "1.1.0",
    [switch]$Publish,
    [switch]$Hexium
)

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$projectDir = $PSScriptRoot
$zipPath    = Join-Path $projectDir "SmartSilencer-$Version.zip"

# Guard against metadata drift between the two repositories.  Only the repository URL
# and the category list are meant to differ between thunderstore.toml and hexium.toml;
# a version or description edited in one file and not the other would publish two
# listings that disagree.  manifest.json is checked too, since that is the version
# that actually ships inside the zip.
function Get-TomlValue($path, $key) {
    if (-not (Test-Path $path)) { return $null }
    $line = Select-String -Path $path -Pattern "^$key\s*=" | Select-Object -First 1
    if (-not $line) { return $null }
    return ($line.Line -replace "^$key\s*=\s*", "").Trim().Trim('"')
}

if ($Publish -or $Hexium) {
    $tsToml   = Join-Path $projectDir "thunderstore.toml"
    $hexToml  = Join-Path $projectDir "hexium.toml"
    $manifest = Join-Path $projectDir "manifest.json"

    if (Test-Path $hexToml) {
        foreach ($key in @("namespace", "name", "versionNumber", "description")) {
            $a = Get-TomlValue $tsToml $key
            $b = Get-TomlValue $hexToml $key
            if ($a -ne $b) {
                Write-Error "thunderstore.toml and hexium.toml disagree on '$key'. Fix both before publishing.`n  thunderstore.toml: $a`n  hexium.toml:       $b"
                exit 1
            }
        }
    }

    $manifestVersion = ((Get-Content $manifest -Raw) | ConvertFrom-Json).version_number
    $tomlVersion     = Get-TomlValue $tsToml "versionNumber"
    if ($manifestVersion -ne $tomlVersion) {
        Write-Error "manifest.json and thunderstore.toml disagree on the version. Fix both before publishing.`n  manifest.json:     $manifestVersion`n  thunderstore.toml: $tomlVersion"
        exit 1
    }
    if ($manifestVersion -ne $Version) {
        Write-Warning "The -Version parameter ($Version) only names the zip file; the published version is $manifestVersion from manifest.json."
    }
}

Write-Host "Building Release..."
dotnet build "$projectDir\SmartSilencer.csproj" -c Release
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

Add-ZipEntry $zip "$projectDir\manifest.json"                       "manifest.json"
Add-ZipEntry $zip "$projectDir\icon.png"                            "icon.png"
Add-ZipEntry $zip "$projectDir\README.md"                           "README.md"
if (Test-Path "$projectDir\CHANGELOG.md") { Add-ZipEntry $zip "$projectDir\CHANGELOG.md" "CHANGELOG.md" }
Add-ZipEntry $zip "$projectDir\bin\Release\net48\SmartSilencer.dll" "BepInEx/plugins/SmartSilencer.dll"

$zip.Dispose()
$stream.Dispose()

Write-Host "Package ready: SmartSilencer-$Version.zip"

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

if ($Hexium) {
    # Hexium runs a Thunderstore-compatible API, so the same zip and toml publish there.
    # The token is read from HEXIUM_AUTH_TOKEN, or from %USERPROFILE%.hexium_token, so it never
    # has to be typed into a shell or a chat.  Create it at valheim.hexium.gg -> Settings -> Teams
    # -> Service Accounts (team DeathMonger).
    $hexToken = $env:HEXIUM_AUTH_TOKEN
    $tokenFile = Join-Path $env:USERPROFILE ".hexium_token"
    if (-not $hexToken -and (Test-Path $tokenFile)) { $hexToken = (Get-Content $tokenFile -Raw).Trim() }
    if (-not $hexToken) {
        Write-Error "No Hexium token. Set HEXIUM_AUTH_TOKEN or put the token in $tokenFile"
        exit 1
    }
    Write-Host "Publishing to Hexium..."
    tcli publish --file "$zipPath" --config-path "$projectDir\hexium.toml" --repository "https://valheim.hexium.gg" --token $hexToken
    if ($LASTEXITCODE -ne 0) { Write-Error "Hexium publish failed."; exit 1 }
    Write-Host "Published to Hexium."
}
