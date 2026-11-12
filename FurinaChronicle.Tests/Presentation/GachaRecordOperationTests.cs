// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.History;
using FurinaChronicle.Presentation.Gacha;
using FurinaChronicle.Services.Gacha.History;
using FurinaChronicle.Services.History;

namespace FurinaChronicle.Tests.Presentation;

public sealed class GachaRecordOperationTests
{
    private static readonly Guid AccountId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid ArchiveId = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly GachaFactReference Reference = new(AccountId, "100001");
    private static readonly DateTimeOffset OriginalTime = new(2026, 10, 1, 12, 1, 0, TimeSpan.FromHours(8));

    [Fact]
    public async Task CorrectionPlanShowsChangeImpactAndUndoEligibility()
    {
        var harness = new Harness();
        harness.Store.Current = Fact();

        GachaRecordOperationPlanResult planned = await harness.Coordinator.PlanAsync(
            Reference,
            GachaRecordOperationKind.Correct,
            new GachaCorrectionInput(
                "纠正后的物品",
                "11401",
                4,
                OriginalTime.AddMinutes(1)));

        Assert.True(planned.Succeeded);
        GachaRecordOperationPlan plan = planned.Plan!;
        Assert.True(plan.CanExecute);
        Assert.False(plan.RequiresExplicitConfirmation);
        Assert.Equal(GachaRecordOperationKind.Correct, plan.Kind);
        Assert.Contains("名称", plan.Summary);
        Assert.Contains("星级 5→4", plan.Summary);
        Assert.Contains("影响范围：仅 1 条记录", plan.ImpactText);
        Assert.Contains("用户输入", plan.ImpactText);
        Assert.Contains("Revision", plan.ImpactText);
        Assert.Contains("撤销限额为 3", plan.ImpactText);
    }

    [Fact]
    public async Task CorrectionWithoutChangesIsBlocked()
    {
        var harness = new Harness();
        harness.Store.Current = Fact();

        GachaRecordOperationPlanResult planned = await harness.Coordinator.PlanAsync(
            Reference,
            GachaRecordOperationKind.Correct,
            new GachaCorrectionInput("原始物品", null, 5, OriginalTime));

        Assert.False(planned.Succeeded);
        Assert.Contains("没有需要保存的修改", planned.FailureMessage);
    }

    [Fact]
    public async Task DeletePlanIsReversibleAndPermanentDeleteNeedsExplicitConfirmation()
    {
        var harness = new Harness();
        harness.Store.Current = Fact();

        GachaRecordOperationPlan delete = (await harness.Coordinator.PlanAsync(
            Reference,
            GachaRecordOperationKind.Delete)).Plan!;
        Assert.False(delete.RequiresExplicitConfirmation);
        Assert.True(delete.IsUndoExpected);
        Assert.Contains("可撤销快照", delete.ImpactText);
        Assert.Contains("不是永久删除", delete.ImpactText);

        GachaRecordOperationPlan permanent = (await harness.Coordinator.PlanAsync(
            Reference,
            GachaRecordOperationKind.IrreversibleDelete)).Plan!;
        Assert.True(permanent.RequiresExplicitConfirmation);
        Assert.False(permanent.IsUndoExpected);
        Assert.Contains("Tombstone", permanent.ImpactText);
        Assert.Contains("不可撤销", permanent.ImpactText);
        Assert.Contains("逐次确认", permanent.ImpactText);
    }

    [Fact]
    public async Task DisabledUndoLimitIsStatedClearly()
    {
        var harness = new Harness();
        harness.Store.Current = Fact();
        harness.Store.UndoLimit = 0;

        GachaRecordOperationPlan plan = (await harness.Coordinator.PlanAsync(
            Reference,
            GachaRecordOperationKind.Delete)).Plan!;

        Assert.Contains("限额 0", plan.ImpactText);
        Assert.Contains("不能撤销", plan.ImpactText);
    }

    [Fact]
    public async Task ExecuteRejectsStaleVersionWithoutCommit()
    {
        var harness = new Harness();
        harness.Store.Current = Fact();
        GachaRecordOperationPlan plan = (await harness.Coordinator.PlanAsync(
            Reference,
            GachaRecordOperationKind.Delete)).Plan!;
        harness.Store.Current = Fact(version: 4);

        GachaRecordOperationOutcome outcome = await harness.Coordinator.ExecuteAsync(plan);

        Assert.Equal(GachaRecordOperationOutcomeKind.Conflict, outcome.Kind);
        Assert.Contains("预览已失效", outcome.Message);
        Assert.Equal(0, harness.Store.CommitCalls);
    }

    [Fact]
    public async Task CleanupConfirmationIsRequiredAndForwarded()
    {
        var harness = new Harness();
        harness.Store.Current = Fact();
        GachaRecordOperationPlan plan = (await harness.Coordinator.PlanAsync(
            Reference,
            GachaRecordOperationKind.Correct,
            new GachaCorrectionInput("纠正后的物品", null, null, OriginalTime))).Plan!;
        Guid cleanupId = Guid.NewGuid();
        harness.Store.CommitResult = new GachaAtomicChangeResult(
            ChangeExecutionStatus.NeedsConfirmation,
            null,
            0,
            CleanupPlan: new HistoryCleanupPlan(cleanupId, 10, 1, 100, []));

        GachaRecordOperationOutcome first = await harness.Coordinator.ExecuteAsync(plan);
        Assert.Equal(GachaRecordOperationOutcomeKind.RequiresCleanup, first.Kind);
        Assert.True(first.RequiresUserConfirmation);

        harness.Store.CommitResult = new GachaAtomicChangeResult(
            ChangeExecutionStatus.Applied,
            Guid.NewGuid(),
            1);
        GachaRecordOperationOutcome second = await harness.Coordinator.ExecuteAsync(plan, cleanupId);

        Assert.True(second.IsSuccess);
        Assert.Equal(cleanupId, harness.Store.LastCommit!.CleanupConfirmationId);
    }

    [Fact]
    public async Task IrreversibleDeleteUsesDedicatedAtomicCommand()
    {
        var harness = new Harness();
        harness.Store.Current = Fact();
        GachaRecordOperationPlan plan = (await harness.Coordinator.PlanAsync(
            Reference,
            GachaRecordOperationKind.IrreversibleDelete)).Plan!;

        GachaRecordOperationOutcome outcome = await harness.Coordinator.ExecuteAsync(plan);

        Assert.True(outcome.IsSuccess);
        Assert.NotNull(harness.Store.LastIrreversible);
        Assert.Equal(Reference, harness.Store.LastIrreversible!.Reference);
        Assert.Contains("已永久删除", outcome.Message);
    }

    private static GachaFactState Fact(long version = 3) => new(
        new GachaRecord(AccountId, "100001", "原始物品", 5, OriginalTime),
        new FactVersion(version),
        ArchiveId);

    private sealed class Harness
    {
        public FakeStore Store { get; } = new();

        public GachaRecordOperationCoordinator Coordinator { get; }

        public Harness()
        {
            Coordinator = new(new ManageGachaHistory(Store), Store);
        }
    }

    private sealed class FakeStore : IGachaAtomicChangeStore
    {
        public GachaFactState? Current { get; set; }

        public int UndoLimit { get; set; } = 3;

        public GachaAtomicChangeResult CommitResult { get; set; } =
            new(ChangeExecutionStatus.Applied, Guid.NewGuid(), 1);

        public GachaAtomicChangeResult IrreversibleResult { get; set; } =
            new(ChangeExecutionStatus.Applied, Guid.NewGuid(), 1);

        public GachaAtomicChangeRequest? LastCommit { get; private set; }

        public GachaIrreversibleDeleteRequest? LastIrreversible { get; private set; }

        public int CommitCalls { get; private set; }

        public Task<GachaFactState?> GetCurrentAsync(
            GachaFactReference reference,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Current);

        public Task<IReadOnlyList<TombstoneReintroductionWarning>> FindActiveTombstonesAsync(
            IReadOnlyCollection<GachaFactReference> references,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TombstoneReintroductionWarning>>([]);

        public Task<GachaAtomicChangeResult> CommitAsync(
            GachaAtomicChangeRequest request,
            CancellationToken cancellationToken = default)
        {
            CommitCalls++;
            LastCommit = request;
            return Task.FromResult(CommitResult);
        }

        public Task<GachaAtomicChangeResult> IrreversiblyDeleteAsync(
            GachaIrreversibleDeleteRequest request,
            CancellationToken cancellationToken = default)
        {
            CommitCalls++;
            LastIrreversible = request;
            return Task.FromResult(IrreversibleResult);
        }

        public Task<int> GetUndoLimitAsync(
            Guid archiveId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(UndoLimit);

        public Task<GachaAtomicChangeResult> UndoLatestAsync(
            GachaUndoRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GachaAtomicChangeResult> PurgeAccountAsync(
            GachaScopePurgeRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GachaAtomicChangeResult> PurgeArchiveAsync(
            GachaScopePurgeRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<UndoLimitChangeResult> SetUndoLimitAsync(
            Guid archiveId,
            int undoLimit,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<OperationHistoryItem>> GetHistoryAsync(
            Guid archiveId,
            int count,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<OperationHistoryItem>>([]);

        public Task<IReadOnlyList<GachaRevisionItem>> GetRevisionsAsync(
            Guid changeSetId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<GachaRevisionItem>>([]);

        public Task<IReadOnlyList<GachaRevisionItem>> GetRevisionsByAccountAsync(
            Guid gameAccountId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<GachaRevisionItem>>([]);
    }
}
