// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.Records;

namespace FurinaChronicle.Services.Gacha.Refreshing;

public enum GachaRefreshSource
{
    SToken = 1,
    WindowsWebCache = 2,
    ManualUrl = 3
}

public enum GachaRefreshMode
{
    Incremental = 0,
    Full = 1
}

public sealed record GachaRefreshRequest(
    GameAccount GameAccount,
    GachaRefreshSource Source,
    GachaRefreshMode Mode = GachaRefreshMode.Incremental,
    Guid? PassportAccountId = null,
    string? ManualUrl = null,
    string? GameInstallationPath = null,
    PlayerArchive? ArchiveToCreate = null,
    GameAccount? AccountToCreate = null);

public sealed record GachaRefreshDiscoveryRequest(
    GachaRefreshSource Source,
    string? ManualUrl = null,
    string? GameInstallationPath = null,
    GameServerRegion ServerRegionHint = GameServerRegion.Unknown);

public sealed record GachaRefreshIdentity(
    string Uid,
    GameServerRegion ServerRegion);

public sealed record GachaRefreshResult(
    int FetchedCount,
    int InsertedCount,
    int UpdatedCount,
    int DuplicateCount,
    int PageCount,
    int ReachedLocalBoundaryCount,
    int SuppressedCount = 0);

public sealed record GachaRemoteRecord(
    string Uid,
    string ExternalRecordId,
    string? ItemName,
    string? ItemId,
    string? ItemType,
    int? RankType,
    string GachaType,
    DateTimeOffset Time,
    int Count = 1)
{
    public GachaRecord ToDomain(
        Guid gameAccountId,
        RecordProvenance? provenance = null)
    {
        return new GachaRecord(
            gameAccountId,
            ExternalRecordId,
            ItemName,
            RankType,
            Time)
        {
            ItemId = ItemId,
            ItemType = ItemType,
            GachaType = GachaType,
            UigfGachaType = GachaType == "400" ? "301" : GachaType,
            Count = Count,
            Provenance = provenance ?? RecordProvenance.Unknown
        };
    }
}

public sealed record GachaRemotePage(
    IReadOnlyList<GachaRemoteRecord> Records,
    string? NextEndId);

public sealed record GachaRefreshProgress(
    string GachaType,
    int Page,
    int FetchedCount);
