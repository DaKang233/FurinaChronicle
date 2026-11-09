# Copyright (c) 2026 DaKang233.
# SPDX-License-Identifier: MIT

[CmdletBinding()]
param([Parameter(Mandatory = $true)][string] $EvidenceDirectory)

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$evidenceRoot = [IO.Path]::GetFullPath($EvidenceDirectory)
[IO.Directory]::CreateDirectory($evidenceRoot) | Out-Null
$framework = 'net10.0-windows10.0.19041.0'
$project = Join-Path $repositoryRoot 'FurinaChronicle.App\FurinaChronicle.App.csproj'
$outputRoot = Join-Path $evidenceRoot 'build'
$results = [Collections.Generic.List[object]]::new()

# Deliberately share obj and skip clean: this reproduces the unsafe incremental mode switch.
# None of these programs is launched. Each build goes to a separate evidence output root.
foreach ($mode in @('production', 'phase8c', 'ui1', 'production')) {
    $phase8c = $mode -eq 'phase8c'
    $ui1 = $mode -eq 'ui1'
    $expectedId = switch ($mode) {
        'phase8c' { 'com.companyname.furinachronicle.validation.phase8c6' }
        'ui1' { 'com.companyname.furinachronicle.preview.ui1' }
        default { 'com.companyname.furinachronicle.app' }
    }
    $expectedTitle = switch ($mode) {
        'phase8c' { 'FurinaChronicle 8C.6 Validation' }
        'ui1' { 'FurinaChronicle UI-1 Preview' }
        default { 'FurinaChronicle.App' }
    }
    $index = $results.Count + 1
    $arguments = @('build', $project, '-f', $framework, '-c', 'Release', '--no-restore', '-m:1', '-v:minimal',
        "-p:BaseOutputPath=$outputRoot\", "-p:Phase8CValidationMode=$phase8c", "-p:Ui1PreviewMode=$ui1", '-p:Phase8BValidationMode=false')
    & dotnet @arguments *> (Join-Path $evidenceRoot "$index-$mode.log")
    if ($LASTEXITCODE -ne 0) { throw "Build failed for $mode. Inspect the corresponding evidence log." }
    $generated = Join-Path $repositoryRoot "FurinaChronicle.App\obj\Release\$framework\win-x64\FurinaChronicle.App.AssemblyInfo.cs"
    $text = [IO.File]::ReadAllText($generated)
    $packageMatch = [regex]::Match($text, 'AppInfo\.PackageName", "([^"]+)"')
    $titleMatch = [regex]::Match($text, 'AppInfo\.Name", "([^"]+)"')
    $dll = Join-Path $outputRoot "Release\$framework\win-x64\FurinaChronicle.App.dll"
    $dllText = [Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes($dll))
    $correct = $packageMatch.Groups[1].Value -eq $expectedId -and $titleMatch.Groups[1].Value -eq $expectedTitle -and $dllText.Contains($expectedId)
    Copy-Item -LiteralPath $generated -Destination (Join-Path $evidenceRoot "$index-$mode-AssemblyInfo.cs.txt")
    $results.Add([pscustomobject]@{Mode=$mode;Package=$packageMatch.Groups[1].Value;Title=$titleMatch.Groups[1].Value;Pass=$correct;DllSHA256=(Get-FileHash $dll).Hash})
    if (-not $correct) { throw "Incremental build identity mismatch in $mode; do not run this artifact." }
}
$results | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $evidenceRoot 'results.json') -Encoding UTF8
$results | Format-Table Mode,Package,Pass -AutoSize
