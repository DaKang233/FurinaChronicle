// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Services.Archives.Portable;

namespace FurinaChronicle.Infrastructure.Persistence.Sqlite;

public sealed partial class SqliteGachaAtomicChangeStore
{
    private const int ArchiveRestoreSchemaVersion = 8;

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
