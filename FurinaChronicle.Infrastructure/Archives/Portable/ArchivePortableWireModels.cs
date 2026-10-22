// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.History;
using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Services.Archives.Portable;
using FurinaChronicle.Services.HistoryDomains;

namespace FurinaChronicle.Infrastructure.Archives.Portable;

internal sealed record ArchiveCatalogWire(
    string Format,
    string FormatVersion,
    IReadOnlyList<ArchiveCatalogRoleIdentity> RoleIdentities,
    IReadOnlyList<ArchiveCatalogArchive> Archives)
{
    public ArchiveCatalog ToModel() => new(RoleIdentities, Archives);
}

internal sealed record ArchiveSettingsWire(
    string Format,
    string FormatVersion,
    bool RequireUniqueArchiveNames,
    IReadOnlyList<ArchivePortableArchiveSetting> Archives)
{
    public ArchivePortableSettings ToModel() =>
        new(RequireUniqueArchiveNames, Archives);
}

internal sealed record ArchiveGachaHistoryLine(
    string Kind,
    Guid AccountReference,
    string ExternalRecordId,
    IReadOnlyList<string>? ReasonCodes,
    ArchiveGachaStandardExtension? Extension,
    GachaRecord? Record,
    ArchiveGachaRevision? Revision);

internal sealed record ArchiveGachaStandardExtension(
    long OccurredAtUtcTicks,
    int OccurredAtOffsetMinutes,
    RecordProvenance Provenance);

internal sealed record ArchiveGachaRevision(
    Guid RevisionReference,
    EntityChangeKind ChangeKind,
    FactVersion? BeforeVersion,
    FactVersion? AfterVersion,
    GachaRecord? Before,
    GachaRecord? After,
    DateTimeOffset CreatedAt);

internal sealed record ArchiveCharacterWeaponDataset(
    string Format,
    string FormatVersion,
    Guid ArchiveReference,
    IReadOnlyList<ArchiveCharacterWeaponAccount> Accounts);

internal sealed record ArchiveCharacterWeaponAccount(
    Guid AccountReference,
    IReadOnlyList<ItemStateObservation> ItemObservations,
    IReadOnlyList<ManualAcquisitionState> ManualAcquisitions,
    IReadOnlyList<ManualAcquisitionRevisionItem> ManualRevisions);

internal sealed record ArchiveChallengeDataset(
    string Format,
    string FormatVersion,
    Guid ArchiveReference,
    IReadOnlyList<ArchiveChallengeAccount> Accounts);

internal sealed record ArchiveChallengeAccount(
    Guid AccountReference,
    IReadOnlyList<ChallengeObservation> ChallengeObservations);

internal sealed class ArchiveUigfDocument
{
    public ArchiveUigfInfo? Info { get; init; }

    public List<ArchiveUigfAccount?>? Hk4e { get; init; }
}

internal sealed class ArchiveUigfInfo
{
    public long ExportTimestamp { get; init; }

    public string? ExportApp { get; init; }

    public string? ExportAppVersion { get; init; }

    public string? Version { get; init; }
}

internal sealed class ArchiveUigfAccount
{
    public string? Uid { get; init; }

    public int Timezone { get; init; }

    public List<ArchiveUigfRecord?>? List { get; init; }
}

internal sealed class ArchiveUigfRecord
{
    public string? UigfGachaType { get; init; }

    public string? GachaType { get; init; }

    public string? ItemId { get; init; }

    public string? Count { get; init; }

    public string? Time { get; init; }

    public string? Name { get; init; }

    public string? ItemType { get; init; }

    public string? RankType { get; init; }

    public string? Id { get; init; }
}
