# Copyright (c) 2026 DaKang233.
# SPDX-License-Identifier: MIT

[CmdletBinding()]
param(
    [string] $OutputDirectory = "",
    [string] $Configuration = "Release",
    [string] $Runtime = "win-x64",
    [switch] $NoArchive
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = (& git rev-parse --show-toplevel 2>$null)
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($repositoryRoot)) {
    throw "This script must run inside the FurinaChronicle Git repository."
}
$repositoryRoot = $repositoryRoot.Trim()

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repositoryRoot "artifacts/windows-winui/$Runtime"
}
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)

$commit = (& git rev-parse HEAD).Trim()
$commitShort = (& git rev-parse --short HEAD).Trim()
$versionProps = Get-Content (Join-Path $repositoryRoot "Version.props") -Raw -Encoding utf8
$versionParts = @()
foreach ($part in @("Major", "Minor", "Patch")) {
    if ($versionProps -notmatch "<FurinaVersion$part[^>]*>([^<]+)<") {
        throw "FurinaVersion$part is missing from Version.props."
    }
    $versionParts += $Matches[1].Trim()
}
$versionPrefix = $versionParts -join "."
$phaseLabel = ""
if ($versionProps -match "<FurinaPhaseLabel[^>]*>([^<]+)<") {
    $phaseLabel = $Matches[1].Trim()
}

Write-Host "Publishing the default Windows host (WinUI 3, unpackaged, self-contained)…"
Write-Host "  version: $versionPrefix"
Write-Host "  commit : $commitShort"
Write-Host "  output : $outputPath"

if (Test-Path $outputPath) {
    Remove-Item $outputPath -Recurse -Force
}
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null

& dotnet publish (Join-Path $repositoryRoot "FurinaChronicle.Windows/FurinaChronicle.Windows.csproj") `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -m:1 `
    -p:WindowsPackageType=None `
    -p:WindowsAppSDKSelfContained=true `
    -p:FurinaRequireReliableVersion=true `
    -o $outputPath
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

Copy-Item (Join-Path $repositoryRoot "README.md") $outputPath -Force
Copy-Item (Join-Path $repositoryRoot "LICENSE.txt") $outputPath -Force
Copy-Item (Join-Path $repositoryRoot "THIRD-PARTY-NOTICES.txt") $outputPath -Force

$executable = Get-ChildItem $outputPath -File -Filter "FurinaChronicle.Windows.exe" |
    Select-Object -First 1
if (-not $executable) {
    throw "FurinaChronicle.Windows.exe was not produced under $outputPath."
}

$buildInfo = @(
    "Product: FurinaChronicle"
    "Host: FurinaChronicle.Windows (WinUI 3, default Windows host)"
    "Version: $versionPrefix (Phase $phaseLabel)"
    "Commit: $commit"
    "Configuration: $Configuration"
    "Runtime: $Runtime"
    "Packaging: unpackaged, self-contained"
    "Data: isolated UI-1 preview data until the production migration wizard is completed"
    "Note: production database migration must be confirmed by the user on first start."
) -join [Environment]::NewLine
Set-Content -LiteralPath (Join-Path $outputPath "BUILD-INFO.txt") -Value $buildInfo -Encoding utf8NoBOM

$hashLines = Get-ChildItem $outputPath -File |
    Where-Object { $_.Name -ne "SHA256SUMS.txt" } |
    Sort-Object Name |
    ForEach-Object {
        $hash = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $($_.Name)"
    }
Set-Content -LiteralPath (Join-Path $outputPath "SHA256SUMS.txt") -Value $hashLines -Encoding utf8NoBOM

if (-not $NoArchive) {
    $archivePath = "$outputPath.zip"
    if (Test-Path $archivePath) {
        Remove-Item $archivePath -Force
    }
    Compress-Archive -Path (Join-Path $outputPath "*") -DestinationPath $archivePath
    Write-Host "Archive: $archivePath"
}

Write-Host "Done: $outputPath"
Write-Host "Next: run FurinaChronicle.Windows.exe and follow the first-start migration prompt."
