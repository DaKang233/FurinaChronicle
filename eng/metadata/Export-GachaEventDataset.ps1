# Copyright (c) 2026 DaKang233.
# SPDX-License-Identifier: MIT

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $SourcePath,

    [Parameter(Mandatory = $true)]
    [string] $OutputPath,

    [Parameter(Mandatory = $true)]
    [string] $SourceRevision,

    [Parameter(Mandatory = $true)]
    [DateTimeOffset] $SourceUpdatedAt
)

$ErrorActionPreference = 'Stop'

function Get-PoolGroup([int] $gachaType) {
    switch ($gachaType) {
        301 { return 'CharacterEvent' }
        400 { return 'CharacterEvent' }
        302 { return 'WeaponEvent' }
        500 { return 'Chronicled' }
        default { throw "Unsupported gacha type: $gachaType" }
    }
}

function Get-PoolSlug([string] $poolGroup) {
    switch ($poolGroup) {
        'CharacterEvent' { return 'character-event' }
        'WeaponEvent' { return 'weapon-event' }
        'Chronicled' { return 'chronicled' }
        default { throw "Unsupported pool group: $poolGroup" }
    }
}

$sourceItems = @(Get-Content -LiteralPath $SourcePath -Raw | ConvertFrom-Json)
$normalized = foreach ($item in $sourceItems) {
    $from = [DateTimeOffset]::Parse(
        [string] $item.From,
        [Globalization.CultureInfo]::InvariantCulture)
    $to = [DateTimeOffset]::Parse(
        [string] $item.To,
        [Globalization.CultureInfo]::InvariantCulture)
    if ($to -lt $from) {
        throw "Invalid interval: $($item.Name)"
    }

    $poolGroup = Get-PoolGroup ([int] $item.Type)
    [pscustomobject]@{
        Name = [string] $item.Name
        Version = [string] $item.Version
        PhaseOrder = [int] $item.Order
        StartsAt = $from
        EndsAt = $to
        PoolGroup = $poolGroup
        GachaType = [int] $item.Type
        ImageUrl = [string] $item.Banner
        BackupImageUrl = [string] $item.Banner2
        UpFiveStarItemIds = @($item.UpOrangeList | ForEach-Object { [string] $_ })
        UpFourStarItemIds = @($item.UpPurpleList | ForEach-Object { [string] $_ })
    }
}

$periods = @($normalized |
    Group-Object {
        "$($_.Version)|$($_.PhaseOrder)|$($_.StartsAt.ToString('O'))|" +
        "$($_.EndsAt.ToString('O'))|$($_.PoolGroup)"
    } |
    ForEach-Object {
        $items = @($_.Group | Sort-Object GachaType)
        $first = $items[0]
        $poolSlug = Get-PoolSlug $first.PoolGroup
        $periodId = 'genshin-cn-{0}-{1}-{2}-p{3}' -f @(
            $poolSlug,
            $first.StartsAt.ToUnixTimeSeconds(),
            $first.EndsAt.ToUnixTimeSeconds(),
            $first.PhaseOrder)

        $banners = @($items | ForEach-Object {
            [pscustomobject][ordered]@{
                id = "$periodId-type$($_.GachaType)"
                name = $_.Name
                gacha_type = $_.GachaType
                image_url = $_.ImageUrl
                backup_image_url = $_.BackupImageUrl
                up_five_star_item_ids = @($_.UpFiveStarItemIds)
                up_four_star_item_ids = @($_.UpFourStarItemIds)
            }
        })

        [pscustomobject][ordered]@{
            id = $periodId
            version = $first.Version
            phase_order = $first.PhaseOrder
            pool_group = $first.PoolGroup
            starts_at = $first.StartsAt.ToString('O')
            ends_at = $first.EndsAt.ToString('O')
            banners = $banners
        }
    } |
    Sort-Object { [DateTimeOffset] $_.starts_at }, pool_group)

if (($periods | Select-Object -ExpandProperty id | Sort-Object -Unique).Count -ne
    $periods.Count) {
    throw 'Generated event period identifiers are not unique.'
}

$dataset = [pscustomobject][ordered]@{
    format = 'furina-gacha-events'
    format_version = 1
    game = 'genshin'
    region_scope = 'china'
    language = 'zh-CN'
    source = [pscustomobject][ordered]@{
        name = 'Snap.Metadata'
        url = 'https://github.com/SnapHutaoRemasteringProject/Snap.Metadata'
        revision = $SourceRevision
        updated_at = $SourceUpdatedAt.ToString('O')
        license = 'MIT'
        copyright = 'Copyright (c) 2022 DGP Studio'
    }
    source_record_count = $sourceItems.Count
    period_count = $periods.Count
    periods = $periods
}

$directory = Split-Path -Parent $OutputPath
if ($directory) {
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}
$json = $dataset | ConvertTo-Json -Depth 12
[IO.File]::WriteAllText(
    [IO.Path]::GetFullPath($OutputPath),
    $json + [Environment]::NewLine,
    [Text.UTF8Encoding]::new($false))

Write-Output "Exported $($sourceItems.Count) banners as $($periods.Count) periods."
