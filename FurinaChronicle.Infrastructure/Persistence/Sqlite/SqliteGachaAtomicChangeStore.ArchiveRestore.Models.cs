// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Services.Archives.Portable;

namespace FurinaChronicle.Infrastructure.Persistence.Sqlite;

public sealed partial class SqliteGachaAtomicChangeStore
{
    private const int ArchiveRestoreSchemaVersion = FurinaDatabase.CurrentSchemaVersion;
    private static readonly SemaphoreSlim ArchiveRestoreWriterGate = new(1, 1);

    private sealed record MaterializedArchiveFact(
        ArchiveRestoreFactPlan Plan,
        object Value)
    {
        public Guid TargetAccountId => Plan.TargetAccountId;
    }

    private sealed record PreparedArchiveRestore(
        IReadOnlyDictionary<Guid, Guid> TargetArchives,
        IReadOnlyDictionary<Guid, Guid> TargetAccounts,
        IReadOnlyDictionary<Guid, Guid?> TargetRoleIds,
        IReadOnlyList<Guid> ArchiveIds,
        IReadOnlyList<PlayerArchiveRow> ArchivesToInsert,
        IReadOnlyList<GameRoleIdentityRow> IdentitiesToInsert,
        IReadOnlyList<GameAccountRow> AccountsToInsert,
        IReadOnlyList<(Guid Source, Guid Target)> AliasesToInsert,
        IReadOnlyList<(Guid ArchiveId, int UndoLimit)> NewArchiveUndoSettings,
        IReadOnlyList<PreparedMutation> GachaMutations,
        IReadOnlyList<PreparedDomainMutation> ItemMutations,
        IReadOnlyList<PreparedDomainMutation> ManualMutations,
        IReadOnlyList<PreparedDomainMutation> ChallengeMutations,
        IReadOnlyList<MaterializedArchiveFact> SourceRevisions,
        IReadOnlyList<PreparedArchiveRevisionRemoval> RevisionRemovals,
        IReadOnlyList<ArchiveRestoreAccountRemoval> AccountRemovals,
        IReadOnlySet<(Guid AccountId, string ExternalRecordId)>
            GachaFactsWithSourceRevision,
        IReadOnlySet<(Guid AccountId, string EntityId)>
            ManualFactsWithSourceRevision,
        IReadOnlyList<ArchiveRestoreTombstoneHit> Tombstones,
        bool UndoCandidate,
        bool UndoEligible,
        int AppliedFactCount,
        int CapacityMutationCount,
        long SnapshotBytes,
        bool IsNoOp);

    private sealed record PreparedArchiveRevisionRemoval(
        ArchiveRestoreFactKind Kind,
        Guid TargetAccountId,
        string StableReference);

    private sealed record PreparedArchiveParents(
        IReadOnlyDictionary<Guid, Guid> TargetArchives,
        IReadOnlyDictionary<Guid, Guid> TargetAccounts,
        IReadOnlyDictionary<Guid, Guid?> TargetRoleIds,
        IReadOnlyList<PlayerArchiveRow> ArchivesToInsert,
        IReadOnlyList<GameRoleIdentityRow> IdentitiesToInsert,
        IReadOnlyList<GameAccountRow> AccountsToInsert,
        IReadOnlyList<(Guid Source, Guid Target)> AliasesToInsert,
        IReadOnlyList<(Guid ArchiveId, int UndoLimit)> NewArchiveUndoSettings);

    private sealed class ArchiveRestoreReceiptRow
    {
        public string ReceiptId { get; set; } = string.Empty;
        public string OperationId { get; set; } = string.Empty;
        public string? ChangeSetId { get; set; }
        public string PackageFingerprint { get; set; } = string.Empty;
        public string PlanFingerprint { get; set; } = string.Empty;
        public int Status { get; set; }
        public long ReceivedAtUtcTicks { get; set; }
        public int ReceivedAtOffsetMinutes { get; set; }
        public int AffectedRecordCount { get; set; }
        public int ArchiveCount { get; set; }
        public int AccountCount { get; set; }
        public int FactCount { get; set; }
        public int Mode { get; set; }
        public string? RecoveryPointId { get; set; }
    }

    private sealed class ArchiveReplaceRecoveryPointRow
    {
        public string RecoveryPointId { get; set; } = string.Empty;
        public string OperationId { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string PackageFingerprint { get; set; } = string.Empty;
        public string PlanFingerprint { get; set; } = string.Empty;
        public int DatabaseApplicationId { get; set; }
        public int DatabaseSchemaVersion { get; set; }
        public long Length { get; set; }
        public string Sha256 { get; set; } = string.Empty;
        public long CreatedAtUtcTicks { get; set; }
        public int CreatedAtOffsetMinutes { get; set; }
        public long CommittedAtUtcTicks { get; set; }
        public int CommittedAtOffsetMinutes { get; set; }
        public int IsCurrent { get; set; }
    }

    private sealed class ArchiveRestoreReceiptArchiveRow
    {
        public string ReceiptId { get; set; } = string.Empty;
        public string SourceArchiveReference { get; set; } = string.Empty;
        public string TargetArchiveId { get; set; } = string.Empty;
    }

    private sealed class ArchiveRestoreReceiptAccountRow
    {
        public string ReceiptId { get; set; } = string.Empty;
        public string SourceArchiveReference { get; set; } = string.Empty;
        public string SourceAccountReference { get; set; } = string.Empty;
        public string TargetArchiveId { get; set; } = string.Empty;
        public string TargetAccountId { get; set; } = string.Empty;
        public string? TargetRoleIdentityId { get; set; }
    }

    private sealed class ArchiveRestoreApplyConflictException(string message)
        : Exception(message);
}

internal enum ArchiveRestoreFailurePoint
{
    AfterParents,
    AfterGacha,
    AfterItemObservations,
    AfterManualAcquisitions,
    AfterChallenges,
    AfterSourceRevisions,
    BeforeReceipt,
    AfterReceipt,
    BeforeCommit,
}

internal interface IArchiveRestoreFailureInjector
{
    void ThrowIfRequested(ArchiveRestoreFailurePoint point);
}

internal sealed class NoArchiveRestoreFailureInjector
    : IArchiveRestoreFailureInjector
{
    public static NoArchiveRestoreFailureInjector Instance { get; } = new();

    public void ThrowIfRequested(ArchiveRestoreFailurePoint point)
    {
    }
}
