// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Presentation.Gacha;
using FurinaChronicle.Services.Abstractions;
using FurinaChronicle.Services.Archives;
using FurinaChronicle.Windows.Composition;

namespace FurinaChronicle.Tests.Presentation;

public sealed class GachaSelectionPersistenceTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "furina-selection-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(directory, "gacha-selection.json");
    private GachaReadOnlyState State(Source source) => new(source, new Dispatcher(),
        GachaReadOnlyView.Overview, selectionPreferences: new(new JsonFileArchiveSelectionStore(FilePath)));

    [Fact]
    public async Task NewInstancesRestoreArchiveAndPerArchiveAccountWithoutSharingOtherPages()
    {
        var source = new Source();
        var other = new JsonFileArchiveSelectionStore(Path.Combine(directory, "other-selection.json"));
        await other.SaveAsync(new(source.First, source.A));
        var state = State(source);
        await state.EnterAsync();
        await state.SelectAccountAsync(source.B);
        await state.SelectArchiveAsync(source.Second);
        await state.SelectAccountAsync(source.D);
        state.Leave();
        await state.FlushSelectionAsync();
        var restarted = State(source);
        await restarted.EnterAsync();
        Assert.Equal(source.Second, restarted.Snapshot.ArchiveId);
        Assert.Equal(source.D, restarted.Snapshot.AccountId);
        Assert.Equal(GachaReadOnlyView.Overview, restarted.Snapshot.View);
        Assert.Null(restarted.Snapshot.ArchiveAccountIds);
        await restarted.SelectArchiveAsync(source.First);
        Assert.Equal(source.B, restarted.Snapshot.AccountId);
        await restarted.SelectArchiveAsync(source.Second);
        Assert.Equal(source.D, restarted.Snapshot.AccountId);
        Assert.Equal(new ArchiveSelection(source.First, source.A), await other.LoadAsync());
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    [Theory]
    [InlineData("deleted-account")]
    [InlineData("wrong-archive-account")]
    [InlineData("deleted-archive")]
    [InlineData("empty-archive")]
    [InlineData("empty-database")]
    [InlineData("corrupt-file")]
    public async Task InvalidSavedSelectionNeverQueriesAnAccountOutsideItsArchive(string scenario)
    {
        var source = new Source();
        var store = new JsonFileArchiveSelectionStore(FilePath);
        await store.SaveAsync(new(source.Second, source.D));
        if (scenario == "deleted-account") source.Accounts[source.Second].Remove(source.D);
        if (scenario == "wrong-archive-account") await store.SaveAsync(new(source.Second, source.B));
        if (scenario == "deleted-archive") source.Accounts.Remove(source.Second);
        if (scenario == "empty-archive") source.Accounts[source.Second].Clear();
        if (scenario == "empty-database") source.Accounts.Clear();
        if (scenario == "corrupt-file") await File.WriteAllTextAsync(FilePath, "{invalid");
        var state = State(source);
        await state.EnterAsync();
        Assert.False(state.Snapshot.IsBusy);
        if (scenario == "empty-database")
        {
            Assert.Null(state.Snapshot.ArchiveId);
            Assert.Null(state.Snapshot.AccountId);
            Assert.Empty(state.Snapshot.Rows);
            // A later successful import may create parents without any preference-driven creation.
            source.Accounts[source.First] = [source.A, source.B];
            await state.EnterAsync();
            Assert.Equal(source.First, state.Snapshot.ArchiveId);
            Assert.Equal(source.A, state.Snapshot.AccountId);
        }
        else
        {
            Guid archive = scenario is "deleted-archive" or "corrupt-file" ? source.First : source.Second;
            Assert.Equal(archive, state.Snapshot.ArchiveId);
            Assert.Equal(source.Accounts[archive].Cast<Guid?>().FirstOrDefault(), state.Snapshot.AccountId);
        }
    }

    [Fact]
    public async Task QueuedWritesFinishInSelectionOrderAndNormalCloseCanDrain()
    {
        var store = new DelayedStore();
        var memory = new GachaSelectionPreferences(store);
        Guid archive = Guid.NewGuid(), a = Guid.NewGuid(), b = Guid.NewGuid();
        Task first = memory.RememberAsync(archive, a);
        await store.Started.Task;
        Task second = memory.RememberAsync(archive, b);
        Assert.False(second.IsCompleted);
        store.Release.SetResult();
        await memory.FlushAsync();
        await Task.WhenAll(first, second);
        Assert.Equal(new ArchiveSelection(archive, b), store.Last);
        Assert.Equal(2, store.Writes);
    }

    [Fact]
    public async Task FailedPreferenceReadAndWriteDoNotFailPageAndDoNotExposePaths()
    {
        var source = new Source();
        var state = new GachaReadOnlyState(source, new Dispatcher(),
            selectionPreferences: new(new FailingStore()));
        await state.EnterAsync();
        Assert.Equal(source.A, state.Snapshot.AccountId);
        Assert.Single(state.Snapshot.Rows);
        Assert.False(state.Snapshot.IsBusy);
        Assert.Contains("无法保存", state.SelectionWarning);
        Assert.DoesNotContain("private-path", state.SelectionWarning);
        await state.SelectAccountAsync(source.B);
        Assert.Equal(source.B, state.Snapshot.AccountId);
        await state.FlushSelectionAsync();
    }

    [Fact]
    public async Task SuccessfulRetryClearsPreferenceWarningAndNotifiesTheVisiblePage()
    {
        var source = new Source();
        var store = new FailingStore();
        var state = new GachaReadOnlyState(source, new Dispatcher(), selectionPreferences: new(store));
        await state.EnterAsync();
        Assert.NotNull(state.SelectionWarning);
        store.Fail = false;
        store.Release.SetResult();
        bool notified = false;
        state.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(state.SelectionWarning)) notified = true; };
        await state.SelectAccountAsync(source.B);
        Assert.Null(state.SelectionWarning);
        Assert.True(notified);
        Assert.Equal(source.B, store.Last?.GameAccountId);
    }

    [Fact]
    public async Task FailedDataLoadDoesNotReplacePreviouslySavedSelection()
    {
        var source = new Source();
        var store = new JsonFileArchiveSelectionStore(FilePath);
        await store.SaveAsync(new(source.Second, source.D));
        source.Fail = true;
        await State(source).EnterAsync();
        Assert.Equal(new ArchiveSelection(source.Second, source.D), await store.LoadAsync());
    }

    private sealed class Dispatcher : IUiDispatcher
    {
        public Task InvokeAsync(Action action) { action(); return Task.CompletedTask; }
    }

    private sealed class Source : IGachaReadOnlySource
    {
        public Guid First { get; } = Guid.NewGuid();
        public Guid Second { get; } = Guid.NewGuid();
        public Guid A { get; } = Guid.NewGuid();
        public Guid B { get; } = Guid.NewGuid();
        public Guid C { get; } = Guid.NewGuid();
        public Guid D { get; } = Guid.NewGuid();
        public Dictionary<Guid, List<Guid>> Accounts { get; } = [];
        public bool Fail { get; set; }
        public Source() { Accounts[First] = [A, B]; Accounts[Second] = [C, D]; }
        public Task<IReadOnlyList<GachaScopeOption>> GetArchivesAsync(CancellationToken token) =>
            Task.FromResult<IReadOnlyList<GachaScopeOption>>(Accounts.Keys.Select(id => new GachaScopeOption(id, id.ToString())).ToArray());
        public Task<IReadOnlyList<GachaScopeOption>> GetAccountsAsync(Guid archive, CancellationToken token) =>
            Task.FromResult<IReadOnlyList<GachaScopeOption>>(Accounts[archive].Select(id => new GachaScopeOption(id, id.ToString())).ToArray());
        public Task<GachaReadOnlyPage> GetPageAsync(Guid account, int page, int size, CancellationToken token)
        {
            if (Fail) throw new IOException("data unavailable");
            Assert.Contains(Accounts, pair => pair.Value.Contains(account));
            return Task.FromResult(new GachaReadOnlyPage(
                [new("1", account.ToString(), "5", "", "", "", new(null, null))], 1, 1, 1));
        }
        public Task<string> GetDetailAsync(GachaReadOnlyRow row, CancellationToken token) => Task.FromResult("");
    }

    private class DelayedStore : IArchiveSelectionStore
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ArchiveSelection? Last { get; private set; }
        public int Writes { get; private set; }
        public virtual Task<Guid?> LoadCurrentArchiveIdAsync(CancellationToken token = default) => Task.FromResult(Last?.PlayerArchiveId);
        public Task<ArchiveSelection?> LoadAsync(CancellationToken token = default) => Task.FromResult(Last);
        public Task<ArchiveSelection?> LoadForArchiveAsync(Guid id, CancellationToken token = default) => Task.FromResult(Last?.PlayerArchiveId == id ? Last : null);
        public Task SaveCurrentArchiveIdAsync(Guid id, CancellationToken token = default) => Task.CompletedTask;
        public virtual async Task SaveAsync(ArchiveSelection value, CancellationToken token = default)
        {
            if (++Writes == 1) { Started.SetResult(); await Release.Task; }
            Last = value;
        }
        public Task ClearAsync(CancellationToken token = default) => Task.CompletedTask;
    }
    private sealed class FailingStore : DelayedStore
    {
        public bool Fail { get; set; } = true;
        public override Task<Guid?> LoadCurrentArchiveIdAsync(CancellationToken token = default) =>
            Fail ? throw new IOException("private-path") : base.LoadCurrentArchiveIdAsync(token);
        public override async Task SaveAsync(ArchiveSelection value, CancellationToken token = default)
        {
            if (Fail) throw new IOException("private-path");
            await Task.Yield();
            await base.SaveAsync(value, token);
        }
    }
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
