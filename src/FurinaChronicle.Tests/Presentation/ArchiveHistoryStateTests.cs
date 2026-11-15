// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.History;
using FurinaChronicle.Presentation.Management;
using FurinaChronicle.Services.Gacha.History;
using FurinaChronicle.Services.History;
using FurinaChronicle.Services.HistoryDomains;

namespace FurinaChronicle.Tests.Presentation;

public sealed class ArchiveHistoryStateTests
{
    private static readonly Guid ArchiveId = Guid.NewGuid();
    private static readonly Guid AccountId = Guid.NewGuid();

    [Fact]
    public async Task EnterMapsEligibilityScopeAndLatestCandidate()
    {
        var source = new FakeSource();
        source.Archives.Add(Archive(ArchiveId, "01 合成档案"));
        source.History.Add(Item(
            Guid.NewGuid(), DataChangeOperationKind.Correction, DateTimeOffset.Now,
            "纠正最近记录", affected: 1, eligible: true));
        source.History.Add(Item(
            Guid.NewGuid(), DataChangeOperationKind.Import, DateTimeOffset.Now.AddMinutes(-5),
            "导入跨档案数据", affected: 2, eligible: false,
            reason: UndoIneligibilityReason.Conflict, archiveCount: 2));
        var state = new ArchiveHistoryState(source);

        await state.EnterAsync();

        Assert.Equal(ArchiveId, state.Snapshot.SelectedArchiveId);
        Assert.Equal(2, state.Snapshot.Entries.Count);
        ArchiveHistoryEntry latest = state.Snapshot.LatestUndoCandidate!;
        Assert.Equal("人工纠正", latest.Operation);
        Assert.Equal("可撤销", latest.UndoState);
        Assert.False(latest.IsCrossArchive);
        ArchiveHistoryEntry old = state.Snapshot.Entries[1];
        Assert.Equal("当前事实已变化", old.UndoState);
        Assert.True(old.IsCrossArchive);
        Assert.Contains("跨 2 个档案", old.Scope);
    }

    [Fact]
    public async Task UndoLatestUsesServiceResultAndReloads()
    {
        var source = new FakeSource();
        source.Archives.Add(Archive(ArchiveId, "01 合成档案"));
        source.History.Add(Item(
            Guid.NewGuid(), DataChangeOperationKind.Delete, DateTimeOffset.Now,
            "删除记录", affected: 1, eligible: true));
        source.UndoResult = new ArchiveOperationResult(
            ChangeExecutionStatus.Applied, Guid.NewGuid(), 1);
        var state = new ArchiveHistoryState(source);
        await state.EnterAsync();

        ArchiveHistoryActionResult result = await state.UndoLatestAsync();

        Assert.True(result.Succeeded);
        Assert.Contains("影响 1 条记录", result.Message);
        Assert.Equal(1, source.UndoCallCount);
        Assert.Equal(2, source.HistoryCallCount);
    }

    [Fact]
    public async Task UndoLatestWithoutCandidateDoesNotCallService()
    {
        var source = new FakeSource();
        source.Archives.Add(Archive(ArchiveId, "01 合成档案"));
        source.History.Add(Item(
            Guid.NewGuid(), DataChangeOperationKind.IrreversibleDelete, DateTimeOffset.Now,
            "永久删除", affected: 1, eligible: false,
            reason: UndoIneligibilityReason.IrreversibleDeletion));
        var state = new ArchiveHistoryState(source);
        await state.EnterAsync();

        ArchiveHistoryActionResult result = await state.UndoLatestAsync();

        Assert.False(result.Succeeded);
        Assert.Equal(0, source.UndoCallCount);
        Assert.True(state.Snapshot.Entries[0].IsIrreversible);
        Assert.Equal("永久删除，不可撤销", state.Snapshot.Entries[0].UndoState);
    }

    [Fact]
    public async Task UndoConflictIsReportedWithoutLeakingInternalText()
    {
        var source = new FakeSource();
        source.Archives.Add(Archive(ArchiveId, "01 合成档案"));
        source.History.Add(Item(
            Guid.NewGuid(), DataChangeOperationKind.Import, DateTimeOffset.Now,
            "导入", affected: 3, eligible: true));
        source.UndoResult = new ArchiveOperationResult(
            ChangeExecutionStatus.Conflict, null, 0,
            ConflictReason: "SQLite constraint at C:\\secret\\db");
        var state = new ArchiveHistoryState(source);
        await state.EnterAsync();

        ArchiveHistoryActionResult result = await state.UndoLatestAsync();

        Assert.False(result.Succeeded);
        Assert.DoesNotContain("SQLite", result.Message);
        Assert.DoesNotContain("C:\\", result.Message);
        Assert.Contains("安全失败", result.Message);
    }

    [Fact]
    public async Task UndoLimitRejectsOutOfRangeBeforeCallingService()
    {
        var source = new FakeSource();
        source.Archives.Add(Archive(ArchiveId, "01 合成档案"));
        var state = new ArchiveHistoryState(source);
        await state.EnterAsync();

        ArchiveHistoryActionResult tooHigh = await state.SetUndoLimitAsync(1001);
        ArchiveHistoryActionResult negative = await state.SetUndoLimitAsync(-1);

        Assert.False(tooHigh.Succeeded);
        Assert.False(negative.Succeeded);
        Assert.Equal(0, source.SetLimitCallCount);
        Assert.Contains("0～1000", state.Snapshot.Error);
    }

    [Fact]
    public async Task UndoLimitChangeRefreshesValueAndEvictionStatus()
    {
        var source = new FakeSource();
        source.Archives.Add(Archive(ArchiveId, "01 合成档案"));
        source.Limit = 3;
        source.SetLimitResult = new UndoLimitChangeResult(3, 1, [Guid.NewGuid()]);
        var state = new ArchiveHistoryState(source);
        await state.EnterAsync();

        ArchiveHistoryActionResult result = await state.SetUndoLimitAsync(1);

        Assert.True(result.Succeeded);
        Assert.Equal(1, source.SetLimitCallCount);
        Assert.Contains("清理了 1 份", result.Message);
        Assert.Equal(1, state.Snapshot.UndoLimit);
    }

    [Fact]
    public async Task RevisionDetailsExposeGachaBeforeAfterAndDomainLimitation()
    {
        var source = new FakeSource();
        source.Archives.Add(Archive(ArchiveId, "01 合成档案"));
        Guid changeSetId = Guid.NewGuid();
        source.History.Add(Item(
            changeSetId, DataChangeOperationKind.Correction, DateTimeOffset.Now,
            "纠正", affected: 1, eligible: true));
        GachaRecord before = new(
            AccountId, "external-1", "旧名称", 4, DateTimeOffset.Now.AddDays(-1));
        GachaRecord after = before with { ItemName = "新名称", RankType = 5 };
        source.GachaRevisions.Add(new GachaRevisionItem(
            Guid.NewGuid(), changeSetId, new GachaFactReference(AccountId, "external-1"),
            EntityChangeKind.Update, new FactVersion(1), new FactVersion(2),
            before, after, DateTimeOffset.Now));
        var state = new ArchiveHistoryState(source);
        await state.EnterAsync();

        await state.SelectOperationAsync(changeSetId);

        ArchiveHistoryRevision revision = Assert.Single(state.Snapshot.Revisions);
        Assert.Equal("抽卡记录", revision.Kind);
        Assert.Contains("旧名称", revision.Before);
        Assert.Contains("新名称", revision.After);
        Assert.False(revision.IsDomainSummary);
    }

    [Fact]
    public async Task DomainRevisionsAreMarkedAsSummaryNotFabricatedDiff()
    {
        var source = new FakeSource();
        source.Archives.Add(Archive(ArchiveId, "01 合成档案"));
        Guid changeSetId = Guid.NewGuid();
        source.History.Add(Item(
            changeSetId, DataChangeOperationKind.Correction, DateTimeOffset.Now,
            "领域纠正", affected: 1, eligible: true));
        source.DomainRevisions.Add(new DomainRevisionItem(
            Guid.NewGuid(), changeSetId, "Character", "100000001",
            EntityChangeKind.Update, new FactVersion(4), new FactVersion(5),
            "{\"level\":80}", "{\"level\":81}", DateTimeOffset.Now));
        var state = new ArchiveHistoryState(source);
        await state.EnterAsync();

        await state.SelectOperationAsync(changeSetId);

        ArchiveHistoryRevision revision = Assert.Single(state.Snapshot.Revisions);
        Assert.True(revision.IsDomainSummary);
        Assert.Contains("领域修订", revision.Kind);
        Assert.Contains("快照原文", revision.Before);
        Assert.Contains("不伪造", revision.Note);
    }

    [Fact]
    public async Task DeletedSelectedArchiveFallsBackWithMessage()
    {
        Guid fallbackId = Guid.NewGuid();
        var source = new FakeSource();
        source.Archives.Add(Archive(ArchiveId, "01 合成档案"));
        source.Archives.Add(Archive(fallbackId, "02 回退档案"));
        var state = new ArchiveHistoryState(source);
        await state.SelectArchiveAsync(ArchiveId);
        source.Archives.RemoveAll(archive => archive.Id == ArchiveId);

        await state.EnterAsync();

        Assert.Equal(fallbackId, state.Snapshot.SelectedArchiveId);
        Assert.Contains("已回退", state.Snapshot.Status);
    }

    private static PlayerArchive Archive(Guid id, string name) =>
        new(id, name, DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now);

    private static OperationHistoryItem Item(
        Guid changeSetId,
        DataChangeOperationKind kind,
        DateTimeOffset committedAt,
        string summary,
        int affected,
        bool eligible,
        UndoIneligibilityReason reason = UndoIneligibilityReason.None,
        int archiveCount = 1) =>
        new(
            changeSetId,
            kind,
            committedAt,
            summary,
            affected,
            eligible,
            reason,
            null,
            Enumerable.Repeat(ArchiveId, archiveCount).ToArray());

    private sealed class FakeSource : IArchiveHistorySource
    {
        public List<PlayerArchive> Archives { get; } = [];
        public List<OperationHistoryItem> History { get; } = [];
        public List<GachaRevisionItem> GachaRevisions { get; } = [];
        public List<DomainRevisionItem> DomainRevisions { get; } = [];
        public int Limit { get; set; } = 3;
        public UndoLimitChangeResult SetLimitResult { get; set; } =
            new(3, 3, Array.Empty<Guid>());
        public ArchiveOperationResult UndoResult { get; set; } =
            new(ChangeExecutionStatus.Applied, Guid.NewGuid(), 1);
        public int UndoCallCount { get; private set; }
        public int HistoryCallCount { get; private set; }
        public int SetLimitCallCount { get; private set; }

        public Task<IReadOnlyList<PlayerArchive>> GetArchivesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PlayerArchive>>(Archives.ToArray());

        public Task<IReadOnlyList<OperationHistoryItem>> GetHistoryAsync(
            Guid archiveId,
            int count,
            CancellationToken cancellationToken = default)
        {
            HistoryCallCount++;
            return Task.FromResult<IReadOnlyList<OperationHistoryItem>>(
                History.Take(count).ToArray());
        }

        public Task<int> GetUndoLimitAsync(
            Guid archiveId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Limit);

        public Task<UndoLimitChangeResult> SetUndoLimitAsync(
            Guid archiveId,
            int limit,
            CancellationToken cancellationToken = default)
        {
            SetLimitCallCount++;
            Limit = limit;
            return Task.FromResult(SetLimitResult);
        }

        public Task<ArchiveOperationResult> UndoLatestAsync(
            Guid archiveId,
            OperationId operationId,
            CancellationToken cancellationToken = default)
        {
            UndoCallCount++;
            return Task.FromResult(UndoResult);
        }

        public Task<IReadOnlyList<GachaRevisionItem>> GetGachaRevisionsAsync(
            Guid changeSetId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<GachaRevisionItem>>(
                GachaRevisions.Where(item => item.ChangeSetId == changeSetId).ToArray());

        public Task<IReadOnlyList<DomainRevisionItem>> GetDomainRevisionsAsync(
            Guid changeSetId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DomainRevisionItem>>(
                DomainRevisions.Where(item => item.ChangeSetId == changeSetId).ToArray());
    }
}
