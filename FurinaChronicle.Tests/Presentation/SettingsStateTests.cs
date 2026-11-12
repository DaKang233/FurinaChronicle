// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Presentation.Management;
using FurinaChronicle.Services.History;

namespace FurinaChronicle.Tests.Presentation;

public sealed class SettingsStateTests
{
    private static readonly Guid ArchiveId = Guid.NewGuid();

    [Fact]
    public async Task EnterLoadsAppearanceStorageHistoryAndApplicationInfo()
    {
        var fixture = new Fixture();
        fixture.Archives.Add(new PlayerArchive(
            ArchiveId, "01 合成档案", DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now));
        SettingsState state = fixture.CreateState();

        await state.EnterAsync();

        Assert.Equal(SettingsTheme.Dark, state.Snapshot.Theme);
        Assert.Equal("深色", state.Snapshot.ThemeLabel);
        Assert.Equal(ArchiveId, state.Snapshot.SelectedArchiveId);
        Assert.Equal(3, state.Snapshot.UndoLimit);
        Assert.Equal("E:\\isolated", state.Snapshot.DataRootPath);
        Assert.Equal("E:\\isolated\\ui1-preview.db3", state.Snapshot.DatabasePath);
        Assert.Equal("1.0 MB", state.Snapshot.DatabaseSizeText);
        Assert.Equal("2.0 KB", state.Snapshot.CacheSizeText);
        Assert.Contains("0.10.0", state.Snapshot.VersionText);
        Assert.Equal("UI-4", state.Snapshot.PhaseLabel);
        Assert.Equal("abc123", state.Snapshot.Commit);
    }

    [Fact]
    public async Task ThemeSelectionPersistsThroughPort()
    {
        var fixture = new Fixture();
        SettingsState state = fixture.CreateState();
        await state.EnterAsync();

        SettingsActionResult result = await state.SetThemeAsync(SettingsTheme.Light);

        Assert.True(result.Succeeded);
        Assert.Equal(SettingsTheme.Light, fixture.Appearance.SavedTheme);
        Assert.Equal(1, fixture.Appearance.SaveCallCount);
        Assert.Equal(SettingsTheme.Light, state.Snapshot.Theme);
    }

    [Fact]
    public async Task InvalidUndoLimitIsRejectedBeforeCallingHistory()
    {
        var fixture = new Fixture();
        fixture.Archives.Add(new PlayerArchive(
            ArchiveId, "01 合成档案", DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now));
        SettingsState state = fixture.CreateState();
        await state.EnterAsync();

        SettingsActionResult result = await state.SetUndoLimitAsync(1001);

        Assert.False(result.Succeeded);
        Assert.Equal(0, fixture.History.SetLimitCallCount);
        Assert.Contains("0～1000", state.Snapshot.Error);
    }

    [Fact]
    public async Task UndoLimitUsesSharedHistoryPort()
    {
        var fixture = new Fixture();
        fixture.Archives.Add(new PlayerArchive(
            ArchiveId, "01 合成档案", DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now));
        fixture.History.SetLimitResult = new UndoLimitChangeResult(3, 0, [Guid.NewGuid()]);
        SettingsState state = fixture.CreateState();
        await state.EnterAsync();

        SettingsActionResult result = await state.SetUndoLimitAsync(0);

        Assert.True(result.Succeeded);
        Assert.Equal(1, fixture.History.SetLimitCallCount);
        Assert.Equal(0, fixture.History.LastLimit);
        Assert.Contains("清理了 1 份", result.Message);
        Assert.Equal(0, state.Snapshot.UndoLimit);
    }

    [Fact]
    public async Task ClearIconCacheRefreshesSizesAndKeepsFacts()
    {
        var fixture = new Fixture();
        SettingsState state = fixture.CreateState();
        await state.EnterAsync();
        fixture.Storage.CacheBytes = 0;

        SettingsActionResult result = await state.ClearIconCacheAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(1, fixture.Storage.ClearCallCount);
        Assert.Equal("0 B", state.Snapshot.CacheSizeText);
        Assert.Contains("抽卡事实", result.Message);
    }

    [Fact]
    public async Task DiagnosticsExportPassesOnlyVersionMetadata()
    {
        var fixture = new Fixture();
        SettingsState state = fixture.CreateState();
        await state.EnterAsync();

        SettingsActionResult result = await state.ExportDiagnosticsAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(
            "FurinaChronicle · 0.10.0",
            fixture.Storage.DiagnosticsVersion);
        Assert.Equal("UI-4", fixture.Storage.DiagnosticsPhase);
        Assert.Equal("abc123", fixture.Storage.DiagnosticsCommit);
        Assert.Contains("ui4-diagnostics", result.Message);
        Assert.Contains("不含抽卡数据内容", result.Message);
    }

    [Fact]
    public async Task StorageFailureIsMappedWithoutLeakingPath()
    {
        var fixture = new Fixture();
        SettingsState state = fixture.CreateState();
        await state.EnterAsync();
        fixture.Storage.ClearError = new IOException("C:\\secret\\cache\\file is locked");

        SettingsActionResult result = await state.ClearIconCacheAsync();

        Assert.False(result.Succeeded);
        Assert.DoesNotContain("C:\\", result.Message);
        Assert.Contains("本地数据未被修改", state.Snapshot.Error);
    }

    private sealed class Fixture
    {
        public List<PlayerArchive> Archives => History.Archives;
        public FakeAppearanceStore Appearance { get; } = new();
        public FakeStorage Storage { get; } = new();
        public FakeHistory History { get; } = new();
        public FakeApplication Application { get; } = new();

        public SettingsState CreateState() =>
            new(Appearance, Storage, History, Application);
    }

    private sealed class FakeAppearanceStore : ISettingsAppearanceStore
    {
        public SettingsTheme Theme { get; set; } = SettingsTheme.Dark;
        public SettingsTheme? SavedTheme { get; private set; }
        public int SaveCallCount { get; private set; }

        public Task<SettingsTheme> LoadThemeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Theme);

        public Task SaveThemeAsync(
            SettingsTheme theme,
            CancellationToken cancellationToken = default)
        {
            SavedTheme = theme;
            Theme = theme;
            SaveCallCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeStorage : ISettingsStoragePort
    {
        public string DataRootPath => "E:\\isolated";
        public string DatabasePath => "E:\\isolated\\ui1-preview.db3";
        public string CacheDirectoryPath => "E:\\isolated\\images";
        public long? DatabaseSizeBytes { get; set; } = 1024 * 1024;
        public long? CacheBytes { get; set; } = 2048;
        public Exception? ClearError { get; set; }
        public int ClearCallCount { get; private set; }
        public string? DiagnosticsVersion { get; private set; }
        public string? DiagnosticsPhase { get; private set; }
        public string? DiagnosticsCommit { get; private set; }

        public long? GetDatabaseSizeBytes() => DatabaseSizeBytes;

        public long? GetCacheSizeBytes() => CacheBytes;

        public Task ClearIconCacheAsync(CancellationToken cancellationToken = default)
        {
            ClearCallCount++;
            if (ClearError is not null) return Task.FromException(ClearError);
            return Task.CompletedTask;
        }

        public Task<string> ExportDiagnosticsAsync(
            string versionText,
            string phaseLabel,
            string commit,
            CancellationToken cancellationToken = default)
        {
            DiagnosticsVersion = versionText;
            DiagnosticsPhase = phaseLabel;
            DiagnosticsCommit = commit;
            return Task.FromResult("ui4-diagnostics-20260101-000000.txt");
        }
    }

    private sealed class FakeHistory : ISettingsHistoryPort
    {
        public List<PlayerArchive> Archives { get; } = [];
        public int Limit { get; set; } = 3;
        public UndoLimitChangeResult SetLimitResult { get; set; } =
            new(3, 3, Array.Empty<Guid>());
        public int SetLimitCallCount { get; private set; }
        public int LastLimit { get; private set; }

        public Task<IReadOnlyList<PlayerArchive>> GetArchivesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PlayerArchive>>(Archives.ToArray());

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
            LastLimit = limit;
            Limit = limit;
            return Task.FromResult(SetLimitResult);
        }
    }

    private sealed class FakeApplication : ISettingsApplicationPort
    {
        public string VersionText => "FurinaChronicle · 0.10.0";
        public string PhaseLabel => "UI-4";
        public string Commit => "abc123";
        public string LicenseText => "MIT License";
        public string ThirdPartyNoticesText => "Third-party notices";
    }
}
