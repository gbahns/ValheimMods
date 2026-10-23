<#
.SYNOPSIS
  Fail the build when a mod's BepInPlugin version disagrees with its manifest.json.

.DESCRIPTION
  A mod carries its version twice.  manifest.json is the version of record -- Thunderstore and
  Hexium read it, package.ps1 publishes from it, and Directory.Build.props turns it into the
  assembly version, which is how mod-status.ps1 and the dashboard tell a deployed DLL apart from
  a stale one.  The BepInPlugin attribute needs the same number as a compile-time constant,
  because an attribute argument cannot be anything else, so a `const string ModVersion` has to
  repeat it.

  Two copies of one fact disagree eventually.  On 2026-10-23 they did: ModVersion went to 4.3.0
  and manifest.json stayed at 4.2.0, so the built DLL announced itself to BepInEx as 4.3.0 while
  its assembly version said 4.2.0.  The dashboard believed the assembly version, reported the
  server as up to date, and the newer build sat undeployed while two sessions disagreed about
  which number was real.

  The repeat cannot be removed, so it is checked instead: one number, verified at every build.
#>
[CmdletBinding()]
param(
    # The mod project's directory, which holds both manifest.json and the plugin source.
    [Parameter(Mandatory)] [string] $ProjectDir,

    # The version Directory.Build.props read out of manifest.json.
    [Parameter(Mandatory)] [string] $ManifestVersion
)

$ErrorActionPreference = 'Stop'

# Only the plugin's own declaration counts.  MinCompatibleVersion sits beside it and is a
# different number on purpose -- it is the floor, not the version -- so the pattern is anchored
# to the name rather than to any version-shaped string nearby.
$pattern = 'ModVersion\s*=\s*"([0-9][0-9.]*)"'

$found = @()
foreach ($file in Get-ChildItem -LiteralPath $ProjectDir -Filter *.cs -File -ErrorAction SilentlyContinue) {
    foreach ($m in [regex]::Matches((Get-Content -LiteralPath $file.FullName -Raw), $pattern)) {
        $found += [pscustomobject]@{ File = $file.Name; Version = $m.Groups[1].Value }
    }
}

# A mod that writes its version straight into the attribute has nothing to keep in step.
if ($found.Count -eq 0) { exit 0 }

$disagree = $found | Where-Object { $_.Version -ne $ManifestVersion }
if ($disagree.Count -eq 0) { exit 0 }

$where = ($disagree | ForEach-Object { "$($_.File) says $($_.Version)" }) -join '; '
Write-Error ("Version mismatch in $(Split-Path $ProjectDir -Leaf): manifest.json says " +
             "$ManifestVersion but $where. These must match -- manifest.json is the version of " +
             "record and becomes the assembly version, while ModVersion is what the mod reports " +
             "to BepInEx and what ServerSync compares. Bump both, or the built DLL will lie " +
             "about itself to one of them.")
exit 1
