// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.History;
using FurinaChronicle.Services.Gacha;
using FurinaChronicle.Services.Gacha.History;
using FurinaChronicle.Services.Gacha.Portable;
using FurinaChronicle.Services.History;

namespace FurinaChronicle.Presentation.Gacha;

// Portable target selection mirrors the three real product paths: match by archive name,
// import into the page's current archive, or create a new archive from the source name.
public enum GachaPortableImportTargetMode
{
    Automatic = 0,
    CurrentArchive = 1,
    NewArchive = 2
}

public sealed record GachaPortableImportTarget(GachaPortableImportTargetMode Mode, Guid? ArchiveId = null)
{
    public static GachaPortableImportTarget Automatic { get; } = new(GachaPortableImportTargetMode.Automatic);

    public static GachaPortableImportTarget CurrentArchive(Guid archiveId) =>
        new(GachaPortableImportTargetMode.CurrentArchive, archiveId);

    public static GachaPortableImportTarget NewArchive { get; } = new(GachaPortableImportTargetMode.NewArchive);
}

public sealed record GachaPortableReplanResult(bool Succeeded, string? FailureMessage);

public sealed record GachaPortableImportPrepareResult(
    GachaPortableImportSession? Session,
    string? FailureMessage)
{
    public bool Succeeded => Session is not null;
}

// Owns the private staged input plus its disk index for exactly one flow. Disposing it removes
// the temporary package and index; records are never materialized as UI collections.
public sealed class GachaPortableImportSession : IAsyncDisposable
{
    private bool disposed;

    internal GachaPortableImportSession(
        IGachaPortableIndexedPackage package,
        GachaPortableImportPlan plan,
        GachaPortableImportTarget target,
        GachaImportMode mode,
        DateTimeOffset receivedAt,
        string sourceSha256,
        long sourceLength,
        string fileName)
    {
        Package = package;
        Plan = plan;
        Target = target;
        Mode = mode;
        ReceivedAt = receivedAt;
        SourceSha256 = sourceSha256;
        SourceLength = sourceLength;
        FileName = fileName;
        OperationId = OperationId.New();
        ReceiptBatchId = Guid.NewGuid();
    }

    public IGachaPortableIndexedPackage Package { get; }

    public GachaPortableImportPlan Plan { get; internal set; }

    public GachaPortableImportTarget Target { get; internal set; }

    public GachaImportMode Mode { get; }

    public OperationId OperationId { get; }

    public Guid ReceiptBatchId { get; }

    public DateTimeOffset ReceivedAt { get; }

    public string SourceSha256 { get; }

    public long SourceLength { get; }

    public string FileName { get; }

    public bool IsApplied { get; internal set; }

    public bool RequiresArchiveSelection =>
        Plan.Archive.Kind == GachaPortableArchivePlanKind.RequiresSelection;

    public string ConfirmationText => GachaPortableText.BuildConfirmationText(this);

    public string BlockingMessage => GachaPortableText.DescribeBlockers(Plan);

    public ValueTask DisposeAsync()
    {
        if (disposed) return ValueTask.CompletedTask;
        disposed = true;
        return Package.DisposeAsync();
    }
}

// The production planner is a concrete use case; this seam keeps the coordinator unit-testable
// without a real database while still writing through the same planning logic in production.
public interface IGachaPortableImportPlanner
{
    Task<GachaPortableImportPlan> ExecuteAsync(
        GachaPortableIndexedImportPlanRequest request,
        CancellationToken cancellationToken = default);
}

public enum GachaPortableApplyOutcomeKind
{
    Applied = 0,
    NoOp = 1,
    AlreadyCommitted = 2,
    Conflict = 3,
    RequiresReintroduction = 4,
    RequiresCleanup = 5,
    Failed = 6
}

public sealed record GachaPortableApplyOutcome(
    GachaPortableApplyOutcomeKind Kind,
    string Message,
    GachaPortableApplyResult? Result = null,
    IReadOnlyList<TombstoneReintroductionWarning>? ReintroductionWarnings = null,
    HistoryCleanupPlan? CleanupPlan = null)
{
    public bool IsSuccess =>
        Kind is GachaPortableApplyOutcomeKind.Applied or
            GachaPortableApplyOutcomeKind.NoOp or
            GachaPortableApplyOutcomeKind.AlreadyCommitted;

    public bool RequiresUserConfirmation =>
        Kind is GachaPortableApplyOutcomeKind.RequiresReintroduction or
            GachaPortableApplyOutcomeKind.RequiresCleanup;
}

public sealed record GachaPortableExportPlan(
    Guid ArchiveId,
    string ArchiveName,
    IReadOnlyList<GachaScopeOption> Accounts,
    string ScopeLabel,
    string ConfirmationText)
{
    public Guid[] AccountIds => Accounts.Select(account => account.Id).ToArray();

    public bool CanExecute => Accounts.Count > 0;
}

public sealed record GachaPortableExportPlanResult(GachaPortableExportPlan? Plan, string? FailureMessage)
{
    public bool Succeeded => Plan is not null;
}
