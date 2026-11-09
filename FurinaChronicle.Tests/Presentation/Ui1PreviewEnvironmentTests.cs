// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Infrastructure.Diagnostics;
using FurinaChronicle.Infrastructure.Gacha.Metadata;
using FurinaChronicle.Presentation.Gacha;
using FurinaChronicle.Services.Archives;
using FurinaChronicle.Services.Gacha;
using FurinaChronicle.Services.Gacha.Analytics;

namespace FurinaChronicle.Tests.Presentation;

public sealed class Ui1PreviewEnvironmentTests
{
    [Fact]
    public async Task RealServicesSeedPersistAndPageIndependentCopiesOffline()
    {
        string directory = Path.Combine(Path.GetTempPath(), "FurinaChronicleTests", Guid.NewGuid().ToString("N"));
        try
        {
            await using (var environment = new Ui1PreviewEnvironment(directory, "WinUI"))
            {
                ServiceGachaReadOnlySource source = CreateSource(environment);
                var archives = await source.GetArchivesAsync(default);
                Assert.Equal(3, archives.Count);
                var accounts = await source.GetAccountsAsync(archives[0].Id, default);
                Assert.Equal(3, accounts.Count);
                var first = await source.GetPageAsync(accounts[0].Id, 1, 25, default);
                var last = await source.GetPageAsync(accounts[0].Id, 3, 25, default);
                Assert.Equal(61, first.TotalCount);
                Assert.Equal(25, first.Rows.Count);
                Assert.Equal(11, last.Rows.Count);
                Assert.Empty(first.Rows.Select(r => r.ExternalRecordId).Intersect(last.Rows.Select(r => r.ExternalRecordId)));
                Assert.Contains(first.Rows, r => r.Source == "来源未知");
                Assert.Contains(first.Rows, r => r.Source == "用户输入");
                Assert.All(first.Rows, r => { Assert.Null(r.Image.CachedPath); Assert.Contains("+08:00", r.Time); });
                Assert.Equal(7, (await source.GetPageAsync(accounts[1].Id, 1, 25, default)).TotalCount);
                Assert.Empty((await source.GetPageAsync(accounts[2].Id, 1, 25, default)).Rows);
                var copy = await source.GetAccountsAsync(archives[1].Id, default);
                Assert.NotEqual(accounts[0].Id, copy[0].Id);
                var copyPage = await source.GetPageAsync(copy[0].Id, 1, 25, default);
                Assert.Equal(3, copyPage.TotalCount);
                var originalRecord = last.Rows[^1];
                var copiedRecord = copyPage.Rows[^1];
                Assert.Equal(originalRecord.ExternalRecordId, copiedRecord.ExternalRecordId);
                Assert.Contains("变更后：合成物品 0-0-01", await source.GetDetailAsync(originalRecord, default));
                Assert.Contains("变更后：合成物品 1-0-01", await source.GetDetailAsync(copiedRecord, default));
                Assert.Empty(await source.GetAccountsAsync(archives[2].Id, default));
                Assert.Null(environment.GetCachedIconPath("../outside"));
                Assert.True(Directory.Exists(environment.TemporaryDirectory));
                Assert.False(File.Exists(Path.Combine(directory, "furinachronicle.db3")));
            }
            await using var reopened = new Ui1PreviewEnvironment(directory, "WinUI");
            var again = CreateSource(reopened);
            var savedArchives = await again.GetArchivesAsync(default);
            var savedAccounts = await again.GetAccountsAsync(savedArchives[0].Id, default);
            Assert.Equal(61, (await again.GetPageAsync(savedAccounts[0].Id, 1, 25, default)).TotalCount);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task ScopedAnalysisAndRecordFiltersUseTheSameIsolatedFacts()
    {
        string directory = Path.Combine(Path.GetTempPath(), "FurinaChronicleTests", Guid.NewGuid().ToString("N"));
        try
        {
            await using var environment = new Ui1PreviewEnvironment(directory, "WinUI");
            var source = CreateSource(environment);
            var state = new GachaReadOnlyState(source, new ImmediateDispatcher());
            await state.EnterAsync();
            GachaReadOnlyRow firstRecord = state.Snapshot.Rows[0];
            Assert.Contains("本地修订", await source.GetDetailAsync(firstRecord, default));
            var revisions = await environment.Changes.GetRecordRevisionsAsync(firstRecord.Reference!.Value);
            Assert.Single(revisions);
            Assert.Equal(firstRecord.Reference.Value, revisions[0].Reference);
            Guid[] accounts = state.Snapshot.Accounts.Select(account => account.Id).ToArray();
            await state.SelectArchiveAccountsAsync([accounts[0], accounts[1], Guid.NewGuid()]);
            Assert.Equal(68, state.Snapshot.TotalCount);
            Assert.Equal(2, state.Snapshot.ArchiveAccountIds!.Count);
            Assert.All(state.Snapshot.Rows, row => Assert.NotNull(row.AccountLabel));
            await state.ChangePageAsync(3);
            Assert.Equal(18, state.Snapshot.Rows.Count);

            await state.ApplyFilterAsync(new(Rank: 5, SortOrder: GachaRecordSortOrder.OldestFirst));
            Assert.Equal(1, state.Snapshot.PageNumber);
            Assert.Equal(6, state.Snapshot.TotalCount);
            Assert.Equal("100010", state.Snapshot.Rows[0].ExternalRecordId);

            await state.SelectViewAsync(GachaReadOnlyView.Overview);
            Assert.Contains("68 抽", Assert.Single(state.Snapshot.Rows).Detail);
            await state.SelectViewAsync(GachaReadOnlyView.History);
            Assert.NotEmpty(state.Snapshot.Rows);
            await state.SelectViewAsync(GachaReadOnlyView.Items);
            Assert.NotEmpty(state.Snapshot.Rows);

            await state.ApplyCalendarAsync(new(new DateOnly(2026, 11, 1), true));
            Assert.Empty(state.Snapshot.Rows);
            await state.ApplyCalendarAsync(new(new DateOnly(2026, 10, 1), false));
            var day = Assert.Single(state.Snapshot.Rows);
            Assert.Contains("68 抽", day.Detail);
            var month = GachaCalendarCell.Build(state.Snapshot.VisibleMonth!.Value, state.Snapshot.Rows).ToArray();
            Assert.Equal(31, month.Length);
            Assert.Equal(3, month[0].Column);
            Assert.Equal(68, month[0].Pulls);
            Assert.All(month.Skip(1), cell => Assert.Equal(0, cell.Pulls));
            state.Cancel();
            Assert.Null(state.Snapshot.VisibleMonth);
            await state.EnterAsync();
            await state.ShiftCalendarAsync(1);
            Assert.Equal(new DateOnly(2026, 11, 1), state.Snapshot.VisibleMonth);
            Assert.Empty(state.Snapshot.Rows);
            await state.ShiftCalendarAsync(-1);
            await state.OpenCalendarDayAsync(day.CalendarDay!.Value);
            Assert.Equal(GachaReadOnlyView.Records, state.Snapshot.View);
            Assert.Equal(68, state.Snapshot.TotalCount);

            await state.SelectArchiveAccountsAsync([]);
            Assert.Empty(state.Snapshot.Rows);
            Assert.Contains("选择账号", state.Snapshot.Status);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task PreviewLeaseRejectsConcurrentSameHostButKeepsHostPathsSeparate()
    {
        string directory = Path.Combine(Path.GetTempPath(), "FurinaChronicleTests", Guid.NewGuid().ToString("N"));
        try
        {
            await using var first = new Ui1PreviewEnvironment(directory, "WinUI");
            await first.InitializeAsync();
            await using var second = new Ui1PreviewEnvironment(directory, "WinUI");
            await Assert.ThrowsAsync<IOException>(() => second.InitializeAsync());
            await using var maui = new Ui1PreviewEnvironment(directory, "Maui");
            await maui.InitializeAsync();
            Assert.NotEqual(first.DatabasePath, maui.DatabasePath);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void PresentationHasNoPlatformOrInfrastructureReferences()
    {
        string[] references = typeof(GachaReadOnlyState).Assembly.GetReferencedAssemblies().Select(a => a.Name!).ToArray();
        Assert.DoesNotContain(references, n => n.Contains("Maui") || n.Contains("WinUI") ||
            n.Contains("Microsoft.UI") || n.Contains("Infrastructure") || n.Contains("SQLite"));
    }

    private static ServiceGachaReadOnlySource CreateSource(Ui1PreviewEnvironment environment) =>
        new(new GetPlayerArchives(environment.Archives), new GetGameAccounts(environment.Archives, environment.Accounts),
            new GetGachaRecordPage(environment.Records), environment.InitializeAsync, environment.GetCachedIconPath,
            new BuildGachaAnalytics(environment.Records, new EmptyGachaItemMetadataProvider(), new EmbeddedGachaEventCatalog()),
            environment.Changes.GetRecordRevisionsAsync);

    private sealed class ImmediateDispatcher : IUiDispatcher
    {
        public Task InvokeAsync(Action action)
        {
            action();
            return Task.CompletedTask;
        }
    }
}
