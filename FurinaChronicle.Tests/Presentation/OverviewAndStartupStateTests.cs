// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Presentation.Management;

namespace FurinaChronicle.Tests.Presentation;

public sealed class OverviewAndStartupStateTests
{
    [Fact]
    public async Task OverviewStateExposesAggregateAndFailureStates()
    {
        var source = new FakeOverviewSource
        {
            Snapshot = new OverviewSnapshot
            {
                ArchiveCount = 3,
                AccountCount = 5,
                UnresolvedCount = 1,
                TotalGachaRecords = 71,
                Status = "已载入本地聚合",
                ContinueTargetLabel = "继续查看：01 合成档案 · A 账号"
            }
        };
        var state = new OverviewState(source);

        await state.EnterAsync();

        Assert.Equal(3, state.Snapshot.ArchiveCount);
        Assert.Equal(5, state.Snapshot.AccountCount);
        Assert.Equal(1, state.Snapshot.UnresolvedCount);
        Assert.Equal(71, state.Snapshot.TotalGachaRecords);
        Assert.Contains("继续查看", state.Snapshot.ContinueTargetLabel);

        source.Exception = new IOException("storage unavailable");
        await state.EnterAsync();

        Assert.False(state.Snapshot.IsBusy);
        Assert.NotNull(state.Snapshot.Error);
        Assert.DoesNotContain("storage unavailable", state.Snapshot.Error);
        Assert.Contains("未改变本地数据", state.Snapshot.Status);
    }

    [Fact]
    public async Task StartupClassifiesFailuresAndAllowsRetry()
    {
        var probe = new FakeProbe
        {
            Results =
            {
                new StartupProbeResult(
                    false,
                    StartupFailureKind.DirectoryNotWritable,
                    "数据目录不可写。",
                    "请检查 FURINA_UI1_DIRECTORY 的权限。"),
                new StartupProbeResult(true, StartupFailureKind.None, "隔离数据库已就绪。", null)
            }
        };
        var state = new StartupState(probe);

        await state.InitializeAsync();

        Assert.True(state.Snapshot.HasFailed);
        Assert.Equal(StartupFailureKind.DirectoryNotWritable, state.Snapshot.FailureKind);
        Assert.Equal("数据目录不可写", state.Snapshot.FailureKindLabel);
        Assert.True(state.Snapshot.CanRetry);
        Assert.Contains("FURINA_UI1_DIRECTORY", state.Snapshot.Detail);

        await state.InitializeAsync();

        Assert.True(state.Snapshot.IsReady);
        Assert.False(state.Snapshot.HasFailed);
        Assert.False(state.Snapshot.CanRetry);
        Assert.Equal(2, probe.Calls);
    }

    [Fact]
    public async Task StartupUnknownExceptionDoesNotLeakDetails()
    {
        var probe = new FakeProbe { Throw = new InvalidOperationException(@"C:\secret\db3") };
        var state = new StartupState(probe);

        await state.InitializeAsync();

        Assert.Equal(StartupFailureKind.Unknown, state.Snapshot.FailureKind);
        Assert.DoesNotContain("secret", state.Snapshot.Detail);
        Assert.True(state.Snapshot.CanRetry);
    }

    [Fact]
    public async Task StartupSchemaIncompatibilityIsSurfacedAsBlocking()
    {
        var probe = new FakeProbe
        {
            Results =
            {
                new StartupProbeResult(
                    false,
                    StartupFailureKind.SchemaIncompatible,
                    "数据库版本不兼容。",
                    "Schema 版本 99 高于支持的版本 12；原库未被修改。")
            }
        };
        var state = new StartupState(probe);

        await state.InitializeAsync();

        Assert.Equal(StartupFailureKind.SchemaIncompatible, state.Snapshot.FailureKind);
        Assert.Contains("版本不兼容", state.Snapshot.FailureKindLabel);
        Assert.Contains("未被修改", state.Snapshot.Detail);
    }

    private sealed class FakeOverviewSource : IOverviewSource
    {
        public OverviewSnapshot Snapshot { get; set; } = new();

        public Exception? Exception { get; set; }

        public Task<OverviewSnapshot> LoadAsync(CancellationToken cancellationToken = default) =>
            Exception is not null
                ? Task.FromException<OverviewSnapshot>(Exception)
                : Task.FromResult(Snapshot);
    }

    private sealed class FakeProbe : IStartupProbe
    {
        public List<StartupProbeResult> Results { get; } = [];

        public Exception? Throw { get; set; }

        public int Calls { get; private set; }

        public Task<StartupProbeResult> RunAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Throw is not null) return Task.FromException<StartupProbeResult>(Throw);
            if (Results.Count == 0)
            {
                return Task.FromResult(new StartupProbeResult(
                    true,
                    StartupFailureKind.None,
                    "已就绪。",
                    null));
            }

            StartupProbeResult result = Results[0];
            Results.RemoveAt(0);
            return Task.FromResult(result);
        }
    }
}
