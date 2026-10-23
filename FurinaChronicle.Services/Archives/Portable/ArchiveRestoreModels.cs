// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Services.Gacha.History;
using FurinaChronicle.Services.HistoryDomains;

namespace FurinaChronicle.Services.Archives.Portable;

public enum ArchiveRestoreMode
{
    Merge = 1,
}

public enum ArchiveRestoreArchiveDecisionKind
{
    MapExisting = 1,
    CreateNew = 2,
}

public sealed record ArchiveRestoreArchiveDecision(
    Guid SourceArchiveReference,
    ArchiveRestoreArchiveDecisionKind Kind,
    Guid? TargetArchiveId = null,
    string? NewArchiveName = null);

public enum ArchiveRestoreAccountDecisionKind
{
    MapExisting = 1,
    CreateNew = 2,
}

public sealed record ArchiveRestoreAccountDecision(
    Guid SourceAccountReference,
    ArchiveRestoreAccountDecisionKind Kind,
    Guid? TargetAccountId = null);

public enum ArchiveRestoreConfirmationKind
{
    PartialOptionalDatasets = 1,
    SimilarArchiveName = 2,
    UnresolvedAccountMapping = 3,
    TombstoneReintroduction = 4,
    ApplyPortableSettings = 5,
    NonUndoableOperation = 6,
}

public sealed record ArchiveRestoreConfirmation(
    ArchiveRestoreConfirmationKind Kind,
    string Reference,
    string? MarkerKey = null,
    long? MarkerVersion = null);

public sealed record ArchiveRestorePlanRequest(
    IArchiveRestoreStagedPackage Package,
    IReadOnlyList<ArchiveRestoreArchiveDecision>? ArchiveDecisions = null,
    IReadOnlyList<ArchiveRestoreAccountDecision>? AccountDecisions = null,
    IReadOnlyList<ArchiveRestoreConfirmation>? Confirmations = null,
    bool ApplyPortableSettings = false,
    ArchiveRestoreMode Mode = ArchiveRestoreMode.Merge);

public enum ArchiveRestoreArchiveMappingKind
{
    MapExisting = 1,
    CreateNew = 2,
    RequiresSelection = 3,
}

public sealed record ArchiveRestoreArchiveCandidate(
    Guid ArchiveId,
    string Name,
    bool IsExactMatch);

public sealed record ArchiveRestoreArchiveMapping(
    Guid SourceArchiveReference,
    string SourceName,
    ArchiveRestoreArchiveMappingKind Kind,
    Guid? TargetArchiveId,
    Guid? ProposedArchiveId,
    string ProposedName,
    IReadOnlyList<ArchiveRestoreArchiveCandidate> Candidates,
    DateTimeOffset? TargetUpdatedAt);

public enum ArchiveRestoreIdentityMappingKind
{
    Reuse = 1,
    PreserveSource = 2,
    AliasSourceToTarget = 3,
    Unresolved = 4,
    Collision = 5,
}

public enum ArchiveRestoreAccountMappingKind
{
    MapExisting = 1,
    CreateNew = 2,
    RequiresSelection = 3,
    Collision = 4,
}

public sealed record ArchiveRestoreAccountMapping(
    Guid SourceArchiveReference,
    Guid SourceAccountReference,
    ArchiveRestoreAccountMappingKind Kind,
    Guid? TargetArchiveId,
    Guid? TargetAccountId,
    Guid? ProposedAccountId,
    Guid? SourceRoleIdentityId,
    Guid? TargetRoleIdentityId,
    GameRoleNaturalIdentity? NaturalIdentity,
    ArchiveRestoreIdentityMappingKind IdentityKind,
    string? Diagnostic);

public enum ArchiveRestoreFactKind
{
    GachaCurrent = 1,
    GachaRevision = 2,
    ItemObservation = 3,
    ManualAcquisition = 4,
    ManualAcquisitionRevision = 5,
    ChallengeObservation = 6,
}

public enum ArchiveRestoreConflictCode
{
    ArchiveMapping = 1,
    AccountMapping = 2,
    IdentityCollision = 3,
    DuplicateSourceIdentity = 4,
    CurrentFactDifference = 5,
    RevisionDifference = 6,
    SettingsConflict = 7,
    UnsupportedPartialRestore = 8,
}

public sealed record ArchiveRestoreConflict(
    ArchiveRestoreConflictCode Code,
    string Message,
    Guid? SourceArchiveReference = null,
    Guid? SourceAccountReference = null,
    ArchiveRestoreFactKind? FactKind = null,
    string? StableReference = null);

public sealed record ArchiveRestoreDatasetPreview(
    ArchiveRestoreFactKind Kind,
    long AddCount,
    long SkipCount,
    long ConflictCount,
    long TombstoneCount);

public sealed record ArchiveRestoreTombstoneProbe(
    ArchiveRestoreFactKind Kind,
    Guid TargetArchiveId,
    Guid TargetAccountId,
    Guid? TargetRoleIdentityId,
    string StableReference);

public sealed record ArchiveRestoreActiveTombstone(
    ArchiveRestoreTombstoneProbe Probe,
    string TombstoneKey,
    long TombstoneVersion);

public sealed record ArchiveRestoreTombstoneHit(
    Guid SourceArchiveReference,
    Guid SourceAccountReference,
    ArchiveRestoreFactKind FactKind,
    string StableReference,
    string TombstoneKey,
    long TombstoneVersion,
    bool Confirmed);

public sealed record ArchiveRestoreSettingDifference(
    string Setting,
    Guid? SourceArchiveReference,
    string? SourceValue,
    string? TargetValue,
    bool WillApply);

public sealed record ArchiveRestoreSettingsPlan(
    bool ApplySourceSettings,
    IReadOnlyList<ArchiveRestoreSettingDifference> Differences);

public sealed record ArchiveRestoreConfirmationRequirement(
    ArchiveRestoreConfirmationKind Kind,
    string Reference,
    string Message,
    bool Satisfied,
    string? MarkerKey = null,
    long? MarkerVersion = null);

public sealed record ArchiveRestoreUndoAssessment(
    bool IsEligibleCandidate,
    IReadOnlyList<string> IneligibleReasons);

public sealed record ArchiveRestoreResourceEstimate(
    long ContainerBytes,
    long StagingBytes,
    long EstimatedAdditionalDatabaseBytes,
    long EstimatedUndoBytes);

public sealed record ArchiveRestorePreview(
    int ArchiveCreateCount,
    int ArchiveReuseCount,
    int AccountCreateCount,
    int AccountReuseCount,
    IReadOnlyList<ArchiveRestoreDatasetPreview> Datasets,
    long ConflictCount,
    long TombstoneCount);

public sealed record ArchiveRestorePlan(
    string PackageFingerprint,
    string TargetStateToken,
    int TargetSchemaVersion,
    DateTimeOffset PlannedAt,
    ArchiveRestoreMode Mode,
    IReadOnlyList<ArchiveRestoreArchiveMapping> Archives,
    IReadOnlyList<ArchiveRestoreAccountMapping> Accounts,
    ArchiveRestorePreview Preview,
    IReadOnlyList<ArchiveRestoreConflict> Conflicts,
    IReadOnlyList<ArchiveRestoreTombstoneHit> Tombstones,
    ArchiveRestoreSettingsPlan Settings,
    IReadOnlyList<ArchiveRestoreConfirmationRequirement> Confirmations,
    ArchiveRestoreUndoAssessment Undo,
    ArchiveRestoreResourceEstimate Resources,
    string PlanFingerprint)
{
    public bool CanApply =>
        Mode == ArchiveRestoreMode.Merge &&
        Archives.All(item =>
            item.Kind != ArchiveRestoreArchiveMappingKind.RequiresSelection) &&
        Accounts.All(item =>
            item.Kind is not ArchiveRestoreAccountMappingKind.RequiresSelection and
                not ArchiveRestoreAccountMappingKind.Collision) &&
        Conflicts.Count == 0 &&
        Confirmations.All(item => item.Satisfied);
}

public sealed record ArchiveRestorePlanFreshness(
    bool IsCurrent,
    string ExpectedTargetStateToken,
    string ActualTargetStateToken,
    int ExpectedSchemaVersion,
    int ActualSchemaVersion);

public sealed record ArchiveIndexedFact<T>(
    T Value,
    string SemanticDigest);

public interface IArchiveRestoreDataSource : IArchiveExportSnapshot
{
    Task<IReadOnlyDictionary<string, ArchiveIndexedFact<GachaRecord>>>
        FindGachaRecordsAsync(
            Guid accountReference,
            IReadOnlyCollection<string> externalRecordIds,
            CancellationToken cancellationToken = default);

    Task<ArchiveIndexedFact<GachaRevisionItem>?> FindGachaRevisionAsync(
        Guid accountReference,
        Guid revisionId,
        CancellationToken cancellationToken = default);

    Task<ArchiveIndexedFact<ItemStateObservation>?> FindItemObservationAsync(
        Guid accountReference,
        ItemObservationId observationId,
        CancellationToken cancellationToken = default);

    Task<ArchiveIndexedFact<ManualAcquisitionState>?> FindManualAcquisitionAsync(
        Guid accountReference,
        ManualAcquisitionId acquisitionId,
        CancellationToken cancellationToken = default);

    Task<ArchiveIndexedFact<ManualAcquisitionRevisionItem>?>
        FindManualAcquisitionRevisionAsync(
            Guid accountReference,
            Guid revisionId,
            CancellationToken cancellationToken = default);

    Task<ArchiveIndexedFact<ChallengeObservation>?> FindChallengeObservationAsync(
        Guid accountReference,
        ChallengeObservationId observationId,
        CancellationToken cancellationToken = default);
}

public interface IArchiveRestoreStagedPackage : IArchiveRestoreDataSource
{
    string PackageFingerprint { get; }

    ArchivePackageSummary Summary { get; }

    long ContainerLength { get; }

    long StagingLength { get; }
}

public interface IArchiveRestoreTargetSnapshot : IArchiveRestoreDataSource
{
    int SchemaVersion { get; }

    string StateToken { get; }

    long SnapshotLength { get; }

    Task<IReadOnlyList<ArchiveRestoreActiveTombstone>>
        FindActiveTombstonesAsync(
            IReadOnlyCollection<ArchiveRestoreTombstoneProbe> probes,
            CancellationToken cancellationToken = default);
}
