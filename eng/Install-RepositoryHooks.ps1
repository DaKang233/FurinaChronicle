# Copyright (c) 2026 DaKang233.
# SPDX-License-Identifier: MIT

[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = (& git rev-parse --show-toplevel 2>$null)
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($repositoryRoot)) {
    throw "This script must run inside the FurinaChronicle Git repository."
}

Push-Location $repositoryRoot.Trim()
try {
    & git config --local core.hooksPath .githooks
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to configure the repository hooks path."
    }
}
finally {
    Pop-Location
}

Write-Host "Configured core.hooksPath=.githooks for this clone."
