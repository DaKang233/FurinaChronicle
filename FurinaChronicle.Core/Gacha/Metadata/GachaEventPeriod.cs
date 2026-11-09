// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;

namespace FurinaChronicle.Core.Gacha.Metadata;

public sealed record GachaEventPeriod(
    string Id,
    GachaGame Game,
    string Version,
    int PhaseOrder,
    GachaPoolGroup PoolGroup,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    IReadOnlySet<GameServerRegion> ServerRegions,
    IReadOnlyList<GachaEventBanner> Banners,
    string Source,
    string SourceRevision)
{
    public bool Contains(DateTimeOffset time)
    {
        DateTimeOffset exclusiveEnd = EndsAt.Second == 0 &&
            EndsAt.Millisecond == 0
                ? EndsAt.AddMinutes(1)
                : EndsAt.AddTicks(1);
        return time >= StartsAt && time < exclusiveEnd;
    }

    public bool Supports(GameServerRegion region) =>
        region == GameServerRegion.Unknown || ServerRegions.Contains(region);
}

public sealed record GachaEventBanner(
    string Id,
    string Name,
    int GachaType,
    string? ImageUrl,
    string? BackupImageUrl,
    IReadOnlyList<string> UpFiveStarItemIds,
    IReadOnlyList<string> UpFourStarItemIds)
{
    public IReadOnlyList<GachaEventFeaturedItem> FeaturedItems { get; init; } = [];
}

public sealed record GachaEventFeaturedItem(
    string? ItemId,
    string Name,
    int RankType,
    string? ImageUrl);
