<#
.SYNOPSIS
    Generates latest.json (cc-switch compatible format) for GitHub Releases auto-update.

.DESCRIPTION
    Reads version from AssemblyInformationalVersionAttribute in the app project,
    generates a latest.json that matches the cc-switch/tauri-plugin-updater format.

.PARAMETER Version
    Semantic version string (e.g. "1.2.3"). If omitted, reads from the app assembly.

.PARAMETER ReleaseNotes
    Release notes text. If omitted, defaults to "Release v{version}".

.PARAMETER DownloadUrl
    Full download URL for the Windows MSI/installer. If omitted, constructs from GitHub Releases.

.PARAMETER Signature
    Ed25519 signature (base64) for the installer. Required for verified updates.

.PARAMETER OutputDir
    Directory to write latest.json. Default: current directory.

.EXAMPLE
    .\generate-latest-json.ps1 -Version "1.2.3" -ReleaseNotes "Bug fixes" -DownloadUrl "https://github.com/ViewSuSu/BlackGoldAncientSword/releases/download/v1.2.3/Setup.msi"

.EXAMPLE
    .\generate-latest-json.ps1 -Version "1.2.3" -OutputDir ".\publish"
#>

param(
    [string]$Version,
    [string]$ReleaseNotes,
    [string]$DownloadUrl,
    [string]$Signature = "",
    [string]$OutputDir = "."
)

# Ensure output directory exists
if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
}

# Resolve version if not provided
if (-not $Version) {
    # Try to read from git tag
    $gitTag = git describe --tags --abbrev=0 2>$null
    if ($gitTag) {
        $Version = $gitTag -replace '^v', ''
    } else {
        Write-Error "Version not specified and no git tag found."
        exit 1
    }
}

# Ensure version doesn't have 'v' prefix
$Version = $Version -replace '^v', ''

# Set release notes
if (-not $ReleaseNotes) {
    $ReleaseNotes = "Release v$Version"
}

# GitHub repo info
$RepoOwner = "ViewSuSu"
$RepoName = "BlackGoldAncientSword"

# Generate download URL if not provided
if (-not $DownloadUrl) {
    # Default: MSI installer named as Setup-{version}.msi
    $DownloadUrl = "https://github.com/$RepoOwner/$RepoName/releases/download/v$Version/Setup-v$Version.msi"
}

# Build latest.json matching cc-switch format
$latestJson = @{
    version  = $Version
    notes    = $ReleaseNotes
    pub_date = [DateTime]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
    platforms = @{
        "windows-x86_64" = @{
            signature = $Signature
            url       = $DownloadUrl
        }
    }
} | ConvertTo-Json -Depth 4

# Write UTF-8 without BOM (required for GitHub)
$outputPath = Join-Path $OutputDir "latest.json"
$utf8NoBom = [System.Text.UTF8Encoding]::new($false)
[System.IO.File]::WriteAllText($outputPath, $latestJson, $utf8NoBom)

Write-Host "Generated: $outputPath"
Write-Host "Version: $Version"
Write-Host "Platforms: windows-x86_64"

# Summary for GitHub Release upload
Write-Host "`n=== Upload to GitHub Release ==="
Write-Host "Attach this file to release v$Version as: latest.json"
Write-Host "URL: https://github.com/$RepoOwner/$RepoName/releases/tag/v$Version"
Write-Host ""
Write-Host "Expected runtime URL: https://github.com/$RepoOwner/$RepoName/releases/latest/download/latest.json"

# Done
exit 0
