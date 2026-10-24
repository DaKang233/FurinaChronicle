// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.History;
using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Services.Archives.Portable;
using FurinaChronicle.Services.HistoryDomains;
using FurinaChronicle.Services.HistoryDomains.Portable;

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

internal sealed record ArchiveUigfRecord(
    string UigfGachaType,
    string GachaType,
    string ItemId,
    string Count,
    string Time,
    string Name,
    string ItemType,
    string RankType,
    string Id);

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
    IReadOnlyList<PortableManualAcquisitionRevision> ManualRevisions);

internal sealed record ArchiveChallengeDataset(
    string Format,
    string FormatVersion,
    Guid ArchiveReference,
    IReadOnlyList<ArchiveChallengeAccount> Accounts);

internal sealed record ArchiveChallengeAccount(
    Guid AccountReference,
    IReadOnlyList<ChallengeObservation> ChallengeObservations);

internal interface IArchivePackageContentSink
{
    void SetMetadata(ArchiveCatalog catalog, ArchivePortableSettings settings);

    void AddStandardExtension(
        Guid accountReference,
        string externalRecordId,
        ArchiveGachaStandardExtension extension);

    void AddGachaRecord(GachaRecord record);

    void AddGachaRevision(
        Guid accountReference,
        string externalRecordId,
        ArchiveGachaRevision revision);

    void AddUigfRecord(Guid accountReference, ArchiveUigfRecord record);

    void AddCharacterWeaponDataset(ArchiveCharacterWeaponDataset dataset);

    void AddChallengeDataset(ArchiveChallengeDataset dataset);
}
