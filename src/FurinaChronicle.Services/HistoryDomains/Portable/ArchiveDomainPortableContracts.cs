// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.History;
using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Services.History;

namespace FurinaChronicle.Services.HistoryDomains.Portable;

public enum ArchiveDomainDatasetKind
{
    CharacterWeaponHistory = 1,
    ChallengeHistory = 2,
}

public sealed record PortableManualAcquisitionRevision(
    Guid RevisionId,
    EntityChangeKind ChangeKind,
    FactVersion? BeforeVersion,
    FactVersion? AfterVersion,
    ManualAcquisition? Before,
    ManualAcquisition? After,
    DateTimeOffset CreatedAt);

public sealed record ArchiveDomainPortableAccount(
    Guid SourceAccountId,
    GameRoleIdentity RoleIdentity,
    IReadOnlyList<ItemStateObservation> ItemObservations,
    IReadOnlyList<ManualAcquisition> ManualAcquisitions,
    IReadOnlyList<PortableManualAcquisitionRevision> ManualRevisions,
    IReadOnlyList<ChallengeObservation> ChallengeObservations);

public sealed record ArchiveDomainPortablePackage(
    ArchiveDomainDatasetKind DatasetKind,
    int FormatMajor,
    int FormatMinor,
    string PackageFingerprint,
    Guid SourceArchiveId,
    string SourceArchiveName,
    DateTimeOffset ExportedAt,
    IReadOnlyList<ArchiveDomainPortableAccount> Accounts)
{
    public bool SupportsVersion() => FormatMajor switch
    {
        1 => FormatMinor >= 0 && !ContainsDetailedChallenges(Accounts) && !ContainsCharacterDetails(Accounts),
        2 when DatasetKind == ArchiveDomainDatasetKind.CharacterWeaponHistory =>
            FormatMinor is 0 or 1 && ContainsCharacterDetails(Accounts) &&
            (FormatMinor == 1 || !ContainsCharacterEnhancements(Accounts)),
        2 => DatasetKind == ArchiveDomainDatasetKind.ChallengeHistory &&
            (FormatMinor == 3 || FormatMinor is 0 or 1 or 2 && !ContainsTheaterBuffDetails(Accounts) &&
                (FormatMinor == 2 || !ContainsStygianDetails(Accounts) &&
                    (FormatMinor == 1 || !ContainsTheaterDetails(Accounts)))),
        _ => false,
    };

    public static bool ContainsCharacterDetails(IEnumerable<ArchiveDomainPortableAccount> accounts) =>
        accounts.SelectMany(account => account.ItemObservations).Any(observation =>
            observation.CharacterMetadata is not null || observation.Entries.Any(entry => entry.CharacterDetail is not null));

    public static bool ContainsCharacterEnhancements(IEnumerable<ArchiveDomainPortableAccount> accounts) =>
        accounts.SelectMany(account => account.ItemObservations).SelectMany(observation => observation.Entries)
            .Any(entry => entry.CharacterDetail?.HasEnhancementData == true);

    public static bool ContainsTheaterBuffDetails(IEnumerable<ArchiveDomainPortableAccount> accounts) =>
        accounts.SelectMany(account => account.ChallengeObservations).Any(observation => observation.HasTheaterBuffDetails);

    public static bool ContainsStygianDetails(IEnumerable<ArchiveDomainPortableAccount> accounts) =>
        accounts.SelectMany(account => account.ChallengeObservations)
            .Any(observation => observation.HasStygianDetails || observation.HasConstellations);

    public static bool ContainsTheaterDetails(IEnumerable<ArchiveDomainPortableAccount> accounts) =>
        accounts.SelectMany(account => account.ChallengeObservations)
            .Any(observation => observation.HasTheaterDetails || observation.HasParticipantOrigins);

    public static bool ContainsDetailedChallenges(IEnumerable<ArchiveDomainPortableAccount> accounts) =>
        accounts.SelectMany(account => account.ChallengeObservations)
            .Any(observation => observation.RequiresDetailedPortableFormat);
}

public sealed record ArchiveDomainPortableAccountPlan(
    Guid SourceAccountId,
    Guid TargetAccountId,
    GameRoleIdentityId TargetRoleIdentityId,
    bool CreateAccount);

public sealed record ArchiveDomainPortablePlan(
    ArchiveDomainDatasetKind DatasetKind,
    string PackageFingerprint,
    Guid TargetArchiveId,
    bool CreateArchive,
    IReadOnlyList<ArchiveDomainPortableAccountPlan> Accounts,
    int AddCount,
    int SkipCount,
    int ConflictCount,
    IReadOnlyList<string> Conflicts);

public sealed record ArchiveDomainPortableApplyRequest(
    OperationId OperationId,
    ArchiveDomainPortablePackage Package,
    ArchiveDomainPortablePlan Plan,
    DateTimeOffset ReceivedAt,
    string Summary,
    bool CaptureUndo = true,
    Guid? CleanupConfirmationId = null);

public interface IArchiveDomainPortableApplier
{
    Task<ArchiveOperationResult> ApplyArchiveDomainPortableAsync(
        ArchiveDomainPortableApplyRequest request,
        CancellationToken cancellationToken = default);
}

public interface IArchiveDomainPortableFileService
{
    Task ExportAsync(
        Guid archiveId,
        ArchiveDomainDatasetKind datasetKind,
        Stream destination,
        DateTimeOffset exportedAt,
        CancellationToken cancellationToken = default);

    Task<ArchiveDomainPortablePackage> ReadAsync(
        Stream source,
        CancellationToken cancellationToken = default);

    Task<ArchiveDomainPortablePlan> PlanAsync(
        ArchiveDomainPortablePackage package,
        Guid? targetArchiveId = null,
        CancellationToken cancellationToken = default);

    Task ExportMarkdownAsync(
        Guid archiveId,
        ArchiveDomainDatasetKind datasetKind,
        TextWriter destination,
        CancellationToken cancellationToken = default);
}
