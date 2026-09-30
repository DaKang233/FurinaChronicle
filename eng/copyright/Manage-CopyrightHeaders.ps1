# Copyright (c) 2026 DaKang233.
# SPDX-License-Identifier: MIT

[CmdletBinding()]
param(
    [ValidateSet("Apply", "Check")]
    [string] $Mode = "Check",

    [switch] $StagedOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$copyrightText = "Copyright (c) 2026 DaKang233."
$spdxText = "SPDX-License-Identifier: MIT"
$lineCommentExtensions = [System.Collections.Generic.HashSet[string]]::new(
    [string[]] @(".cs"),
    [System.StringComparer]::OrdinalIgnoreCase)
$xmlCommentExtensions = [System.Collections.Generic.HashSet[string]]::new(
    [string[]] @(
        ".xaml",
        ".xml",
        ".csproj",
        ".props",
        ".targets",
        ".resx",
        ".slnx",
        ".plist",
        ".appxmanifest",
        ".manifest",
        ".xcprivacy"),
    [System.StringComparer]::OrdinalIgnoreCase)

function Get-RepositoryRoot {
    $root = (& git rev-parse --show-toplevel 2>$null)
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($root)) {
        throw "This script must run inside the FurinaChronicle Git repository."
    }

    return [System.IO.Path]::GetFullPath($root.Trim())
}

function Get-CandidatePaths {
    param(
        [Parameter(Mandatory)]
        [bool] $OnlyStaged
    )

    if ($OnlyStaged) {
        $paths = @(& git diff --cached --name-only --diff-filter=ACMR)
    }
    else {
        $paths = @(& git ls-files --cached --others --exclude-standard)
    }

    if ($LASTEXITCODE -ne 0) {
        throw "Unable to enumerate Git files."
    }

    return $paths |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Sort-Object -Unique
}

function Test-IsGeneratedPath {
    param(
        [Parameter(Mandatory)]
        [string] $RelativePath
    )

    return $RelativePath -match '(^|[\\/])(bin|obj|Generated)([\\/]|$)' -or
        $RelativePath -match '(?i)\.(g|g\.i|designer)\.cs$'
}

function Read-TextFilePreservingEncoding {
    param(
        [Parameter(Mandatory)]
        [string] $Path
    )

    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $preambleLength = 0

    if ($bytes.Length -ge 3 -and
        $bytes[0] -eq 0xEF -and
        $bytes[1] -eq 0xBB -and
        $bytes[2] -eq 0xBF) {
        $encoding = [System.Text.UTF8Encoding]::new($true)
        $preambleLength = 3
    }
    elseif ($bytes.Length -ge 2 -and $bytes[0] -eq 0xFF -and $bytes[1] -eq 0xFE) {
        $encoding = [System.Text.UnicodeEncoding]::new($false, $true)
        $preambleLength = 2
    }
    elseif ($bytes.Length -ge 2 -and $bytes[0] -eq 0xFE -and $bytes[1] -eq 0xFF) {
        $encoding = [System.Text.UnicodeEncoding]::new($true, $true)
        $preambleLength = 2
    }
    else {
        $encoding = [System.Text.UTF8Encoding]::new($false)
    }

    $text = $encoding.GetString($bytes, $preambleLength, $bytes.Length - $preambleLength)
    return [pscustomobject] @{
        Text = $text
        Encoding = $encoding
    }
}

function Get-NewLine {
    param(
        [Parameter(Mandatory)]
        [AllowEmptyString()]
        [string] $Text
    )

    if ($Text.IndexOf("`r`n", [System.StringComparison]::Ordinal) -ge 0) {
        return "`r`n"
    }

    return "`n"
}

function Get-XmlHeaderInsertionIndex {
    param(
        [Parameter(Mandatory)]
        [AllowEmptyString()]
        [string] $Text
    )

    if (-not $Text.StartsWith("<?xml", [System.StringComparison]::OrdinalIgnoreCase)) {
        return 0
    }

    $declarationEnd = $Text.IndexOf("?>", [System.StringComparison]::Ordinal)
    if ($declarationEnd -lt 0) {
        throw "The XML declaration is not terminated."
    }

    $index = $declarationEnd + 2
    if ($Text.Length -ge $index + 2 -and $Text.Substring($index, 2) -eq "`r`n") {
        return $index + 2
    }

    if ($Text.Length -gt $index -and $Text[$index] -eq "`n") {
        return $index + 1
    }

    return $index
}

function Get-HeaderState {
    param(
        [Parameter(Mandatory)]
        [AllowEmptyString()]
        [string] $Text,

        [Parameter(Mandatory)]
        [ValidateSet("Line", "Xml")]
        [string] $Style
    )

    $newLine = Get-NewLine -Text $Text
    if ($Style -eq "Line") {
        $header = "// $copyrightText$newLine// $spdxText"
        return [pscustomobject] @{
            Present = $Text.StartsWith($header, [System.StringComparison]::Ordinal)
            UpdatedText = "$header$newLine$newLine$Text"
        }
    }

    $xmlHeader = "<!--$newLine  $copyrightText$newLine  $spdxText$newLine-->"
    $insertionIndex = Get-XmlHeaderInsertionIndex -Text $Text
    $present = $Text.Substring($insertionIndex).StartsWith(
        $xmlHeader,
        [System.StringComparison]::Ordinal)
    $prefix = $Text.Substring(0, $insertionIndex)
    $separator = if ($prefix.Length -gt 0 -and
        -not $prefix.EndsWith("`n", [System.StringComparison]::Ordinal)) {
        $newLine
    }
    else {
        ""
    }
    $suffix = $Text.Substring($insertionIndex)

    return [pscustomobject] @{
        Present = $present
        UpdatedText = "$prefix$separator$xmlHeader$newLine$newLine$suffix"
    }
}

$repositoryRoot = Get-RepositoryRoot
$missingPaths = [System.Collections.Generic.List[string]]::new()
$updatedPaths = [System.Collections.Generic.List[string]]::new()

Push-Location $repositoryRoot
try {
    foreach ($relativePath in Get-CandidatePaths -OnlyStaged $StagedOnly.IsPresent) {
        if (Test-IsGeneratedPath -RelativePath $relativePath) {
            continue
        }

        $extension = [System.IO.Path]::GetExtension($relativePath)
        $style = if ($lineCommentExtensions.Contains($extension)) {
            "Line"
        }
        elseif ($xmlCommentExtensions.Contains($extension)) {
            "Xml"
        }
        else {
            continue
        }

        $fullPath = Join-Path $repositoryRoot $relativePath
        if (-not [System.IO.File]::Exists($fullPath)) {
            continue
        }

        $file = Read-TextFilePreservingEncoding -Path $fullPath
        $headerState = Get-HeaderState -Text $file.Text -Style $style
        if ($headerState.Present) {
            continue
        }

        $missingPaths.Add($relativePath)
        if ($Mode -eq "Apply") {
            [System.IO.File]::WriteAllText(
                $fullPath,
                $headerState.UpdatedText,
                $file.Encoding)
            $updatedPaths.Add($relativePath)

            if ($StagedOnly) {
                & git add -- $relativePath
                if ($LASTEXITCODE -ne 0) {
                    throw "Unable to stage updated header for '$relativePath'."
                }
            }
        }
    }
}
finally {
    Pop-Location
}

if ($Mode -eq "Check" -and $missingPaths.Count -gt 0) {
    Write-Error (
        "Missing or non-canonical copyright headers in {0} file(s):`n{1}" -f
        $missingPaths.Count,
        ($missingPaths -join "`n"))
    exit 1
}

if ($Mode -eq "Apply") {
    Write-Host ("Updated {0} file(s)." -f $updatedPaths.Count)
}
else {
    Write-Host "All eligible C# and XML-family source files have canonical copyright headers."
}
