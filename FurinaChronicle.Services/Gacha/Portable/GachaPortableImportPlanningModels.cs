// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Core.History;
using FurinaChronicle.Services.Gacha.History;

namespace FurinaChronicle.Services.Gacha.Portable;

public sealed record GachaPortableImportPlanRequest(
    GachaPortablePackage Package,
    Guid? TargetArchiveId = null,
    bool CreateNewArchive = false,
    bool RequireUniqueArchiveNames = true);

public enum GachaPortableArchivePlanKind
{
    MapExisting = 1,
    CreateNew = 2,
    RequiresSelection = 3,
}

public sealed record GachaPortableArchiveCandidate(
    Guid ArchiveId,
    string Name,
    bool IsExactMatch);

public sealed record GachaPortableArchivePlan(
    GachaPortableArchivePlanKind Kind,
    Guid? TargetArchiveId,
    Guid? ProposedArchiveId,
    string ProposedName,
    IReadOnlyList<GachaPortableArchiveCandidate> Candidates,
    DateTimeOffset? TargetArchiveUpdatedAt);

public enum GachaPortableIdentityPlanKind
{
    Reuse = 1,
    PreserveSource = 2,
    AliasSourceToTarget = 3,
    Collision = 4,
}

public sealed record GachaPortableIdentityPlan(
    GachaPortableIdentityPlanKind Kind,
    GameRoleIdentityId SourceIdentityId,
    GameRoleIdentityId? TargetIdentityId,
    string? Diagnostic);

public sealed record GachaPortableRecordConflict(
    string ExternalRecordId,
    IReadOnlyList<string> DifferentFields);

public sealed record GachaPortableAccountImportPlan(
    Guid SourceAccountReference,
    Guid? TargetAccountId,
    Guid? ProposedAccountId,
    GameRoleNaturalIdentity NaturalIdentity,
    GachaPortableIdentityPlan Identity,
    int AddCount,
    int SkipCount,
    int ConflictCount,
    IReadOnlyList<GachaPortableRecordConflict> Conflicts,
    IReadOnlyDictionary<DataOrigin, int> SourceOrigins,
    DateTimeOffset? TargetAccountUpdatedAt,
    int? TargetRecordCountAtPlan);

public sealed record GachaPortableImportContext(
    DataOrigin Origin,
    DateTimeOffset PlannedAt,
    Guid AcquisitionBatchId);

public sealed record GachaPortableImportPreview(
    int AccountCreateCount,
    int AccountReuseCount,
    int AddCount,
    int SkipCount,
    int ConflictCount,
    int AliasCount);

public sealed record GachaPortableImportPlan(
    GachaPortableArchivePlan Archive,
    IReadOnlyList<GachaPortableAccountImportPlan> Accounts,
    GachaPortableImportContext ImportContext,
    GachaPortableImportPreview Preview,
    string PackageFingerprint,
    bool RequireUniqueArchiveNames)
{
    public bool CanApply =>
        Archive.Kind != GachaPortableArchivePlanKind.RequiresSelection &&
        Accounts.All(account =>
            account.Identity.Kind != GachaPortableIdentityPlanKind.Collision) &&
        Preview.ConflictCount == 0;
}

public sealed record GachaPortableApplyRequest(
    GachaPortablePackage Package,
    GachaPortableImportPlan Plan,
    OperationId OperationId,
    DateTimeOffset ReceivedAt,
    Guid ReceiptBatchId,
    IReadOnlyList<TombstoneReintroductionConfirmation>?
        ReintroductionConfirmations = null,
    Guid? CleanupConfirmationId = null);

public sealed record GachaPortableAccountApplyResult(
    Guid SourceAccountReference,
    Guid TargetAccountId,
    GameRoleIdentityId TargetRoleIdentityId,
    int InsertedRecordCount,
    int SkippedRecordCount);

public sealed record GachaPortableApplyResult(
    ChangeExecutionStatus Status,
    Guid? ChangeSetId,
    Guid? TargetArchiveId,
    IReadOnlyList<GachaPortableAccountApplyResult> Accounts,
    int InsertedRecordCount,
    int SkippedRecordCount,
    string? ConflictReason = null,
    TombstoneReintroductionWarning? ReintroductionWarning = null,
    HistoryCleanupPlan? CleanupPlan = null);

public interface IGachaPortableImportApplier
{
    Task<GachaPortableApplyResult> ApplyAsync(
        GachaPortableApplyRequest request,
        CancellationToken cancellationToken = default);
}
