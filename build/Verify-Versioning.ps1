# Copyright (c) 2026 DaKang233.
# SPDX-License-Identifier: MIT

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
$projectPath = Join-Path $repositoryRoot 'FurinaChronicle.App\FurinaChronicle.App.csproj'
$windowsFramework = 'net10.0-windows10.0.19041.0'
$androidFramework = 'net10.0-android'
$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ("FurinaChronicle-Versioning-{0}" -f [Guid]::NewGuid().ToString('N'))

function Invoke-VersionProbe {
    param(
        [Parameter(Mandatory)]
        [string] $Framework,

        [Parameter(Mandatory)]
        [string] $GitRoot,

        [hashtable] $Properties = @{},

        [switch] $ExpectFailure
    )

    $arguments = @(
        'msbuild'
        $projectPath
        '-t:PrintFurinaVersion'
        "-p:TargetFramework=$Framework"
        "-p:FurinaRepositoryRoot=$GitRoot"
        '-verbosity:minimal'
    )

    foreach ($entry in $Properties.GetEnumerator()) {
        $arguments += "-p:$($entry.Key)=$($entry.Value)"
    }

    $output = (& dotnet @arguments 2>&1 | Out-String)
    $succeeded = $LASTEXITCODE -eq 0
    if ($ExpectFailure) {
        if ($succeeded) {
            throw "Version probe unexpectedly succeeded.`n$output"
        }

        return $null
    }

    if (-not $succeeded) {
        throw "Version probe failed.`n$output"
    }

    $line = ($output -split "`r?`n" | Where-Object { $_ -match 'FurinaVersion Full=' } | Select-Object -Last 1)
    if ([string]::IsNullOrWhiteSpace($line)) {
        throw "Version probe did not emit its diagnostic line.`n$output"
    }

    $result = @{}
    foreach ($match in [regex]::Matches($line, '(?<key>[A-Za-z]+)=(?<value>[^\s]+)')) {
        $result[$match.Groups['key'].Value] = $match.Groups['value'].Value
    }

    return $result
}

function Assert-Equal {
    param(
        [Parameter(Mandatory)] $Expected,
        [Parameter(Mandatory)] $Actual,
        [Parameter(Mandatory)] [string] $Message
    )

    if ($Expected -ne $Actual) {
        throw "$Message Expected '$Expected', actual '$Actual'."
    }
}

try {
    New-Item -ItemType Directory -Path $temporaryRoot | Out-Null
    $historyRepository = Join-Path $temporaryRoot 'history'
    New-Item -ItemType Directory -Path $historyRepository | Out-Null
    git -C $historyRepository init --quiet
    git -C $historyRepository -c user.name=FurinaVersionTest -c user.email=version-test.invalid commit --allow-empty --quiet -m first

    $first = Invoke-VersionProbe -Framework $windowsFramework -GitRoot $historyRepository
    $firstRepeated = Invoke-VersionProbe -Framework $windowsFramework -GitRoot $historyRepository
    Assert-Equal $first.Full $firstRepeated.Full 'The same commit must resolve to the same full version.'
    Assert-Equal '1' $first.Build 'The first fixture commit must have Build 1.'

    git -C $historyRepository -c user.name=FurinaVersionTest -c user.email=version-test.invalid commit --allow-empty --quiet -m second
    $second = Invoke-VersionProbe -Framework $windowsFramework -GitRoot $historyRepository
    Assert-Equal '2' $second.Build 'A new commit must increase Build.'

    $windows = Invoke-VersionProbe -Framework $windowsFramework -GitRoot $historyRepository
    $android = Invoke-VersionProbe -Framework $androidFramework -GitRoot $historyRepository
    Assert-Equal $windows.Full $android.Full 'Windows and Android must share the full version.'
    Assert-Equal $windows.Display $android.Display 'Windows and Android must share the display version.'
    Assert-Equal $windows.Build $android.Build 'Windows and Android must share Build/versionCode.'

    $nextMilestone = Invoke-VersionProbe -Framework $windowsFramework -GitRoot $historyRepository -Properties @{
        FurinaVersionPatch = 5
        FurinaPhaseLabel = '8C.0'
    }
    Assert-Equal '0.8.5' $nextMilestone.Display 'The next Phase 8 milestone must advance Patch rather than reset it.'
    Assert-Equal $windows.Build $nextMilestone.Build 'Changing Patch or PhaseLabel must not change Build.'

    $phaseOnly = Invoke-VersionProbe -Framework $windowsFramework -GitRoot $historyRepository -Properties @{
        FurinaPhaseLabel = 'diagnostic-only'
    }
    Assert-Equal $windows.Full $phaseOnly.Full 'Changing PhaseLabel alone must not change the numeric version.'

    $sourceArchive = Join-Path $temporaryRoot 'source-archive'
    New-Item -ItemType Directory -Path $sourceArchive | Out-Null
    $fallback = Invoke-VersionProbe -Framework $windowsFramework -GitRoot $sourceArchive
    Assert-Equal 'false' $fallback.Reliable 'A source archive must be marked as an unreliable development fallback.'
    Assert-Equal 'development-fallback' $fallback.Source 'A source archive must identify its fallback source.'

    $missingGit = Invoke-VersionProbe -Framework $windowsFramework -GitRoot $historyRepository -Properties @{
        FurinaGitExecutable = 'furina-git-command-that-does-not-exist'
    }
    Assert-Equal 'false' $missingGit.Reliable 'A missing Git executable must use the non-release fallback.'
    Assert-Equal 'development-fallback' $missingGit.Source 'A missing Git executable must identify its fallback source.'

    Invoke-VersionProbe -Framework $windowsFramework -GitRoot $sourceArchive -Properties @{
        FurinaRequireReliableVersion = 'true'
    } -ExpectFailure

    $explicit = Invoke-VersionProbe -Framework $windowsFramework -GitRoot $sourceArchive -Properties @{
        FurinaBuildNumber = 70001
        FurinaGitCommit = 'archive1'
        FurinaGitDirty = 'unknown'
        FurinaRequireReliableVersion = 'true'
    }
    Assert-Equal '70001' $explicit.Build 'An explicit CI Build must be honored.'
    Assert-Equal 'explicit' $explicit.Source 'An explicit CI Build must identify its source.'
    Assert-Equal 'true' $explicit.Reliable 'An explicit CI Build must be release-capable.'

    $shallowRepository = Join-Path $temporaryRoot 'shallow'
    $historyUri = ([Uri]$historyRepository).AbsoluteUri
    git clone --quiet --depth 1 $historyUri $shallowRepository
    $shallow = Invoke-VersionProbe -Framework $windowsFramework -GitRoot $shallowRepository
    Assert-Equal 'false' $shallow.Reliable 'A shallow clone must not use its incomplete commit count.'
    Assert-Equal 'true' $shallow.Shallow 'A shallow clone must be diagnosed explicitly.'

    Write-Host 'FurinaChronicle versioning verification passed.'
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
