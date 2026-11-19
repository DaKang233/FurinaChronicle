// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Text.Json;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.History;
using FurinaChronicle.Core.Passport;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Infrastructure.Gacha.Metadata;
using FurinaChronicle.Infrastructure.Gacha.Metadata.Localization;
using FurinaChronicle.Infrastructure.Gacha.Uigf.Compatibility;
using FurinaChronicle.Infrastructure.Gacha.Uigf.V4_2;
using FurinaChronicle.Presentation.Gacha;
using FurinaChronicle.Presentation.Management;
using FurinaChronicle.Services.Archives;
using FurinaChronicle.Services.Gacha;
using FurinaChronicle.Services.Gacha.Importing;
using FurinaChronicle.Services.Gacha.History;
using FurinaChronicle.Services.Gacha.Refreshing;
using FurinaChronicle.Services.Gacha.Writing;
using FurinaChronicle.Services.Passport;
using FurinaChronicle.Tests.Infrastructure.Persistence.Sqlite;

namespace FurinaChronicle.Tests.Presentation;

public sealed class GachaTeyvatAutomaticImportTests
{
    private const string Uid = "100000001";
    private const string DummyToken = "synthetic-not-a-real-stoken";
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task PreviewIsLocalFreezesTargetAndContainsNoCredentialOrGeneratedUrl()
    {
        await using var h = await Harness.CreateAsync();
        var plan = await h.PlanAsync(GachaImportMode.Full);
        Assert.Equal(h.Passports.Account!.Id, plan.PassportAccountId);
        Assert.Equal(h.Archive.Id, plan.ArchiveId);
        Assert.Equal(Uid, plan.Uid);
        Assert.Equal(GachaImportMode.Full, plan.Mode);
        Assert.Contains("lelaer.com", plan.PrivacyNotice);
        Assert.Contains("取消本确认不会发出请求", plan.PrivacyNotice);
        Assert.Contains("不能撤回已发送的数据", plan.PrivacyNotice);
        Assert.DoesNotContain(DummyToken, plan.ToString());
        Assert.DoesNotContain("authkey=", plan.ToString());
        Assert.Empty(h.Network.Urls);
        Assert.Equal(0, h.Network.Downloads);
        Assert.Empty(await h.Context.Accounts.GetByArchiveIdAsync(h.Archive.Id));
    }

    [Theory]
    [InlineData("removed")]
    [InlineData("unusable")]
    [InlineData("overseas")]
    [InlineData("cookie-only")]
    [InlineData("archive")]
    public async Task StaleRoleOrTargetIsRecheckedBeforeUrlGeneration(string change)
    {
        await using var h = await Harness.CreateAsync();
        var plan = await h.PlanAsync();
        var summary = Assert.Single(h.Credentials.Rows);
        h.Credentials.Rows = change switch
        {
            "removed" => [],
            "unusable" => [summary with { IsUsable = false }],
            "overseas" => [summary with { BoundRoleUid = "800000001" }],
            "cookie-only" => [summary with { CanRefreshGacha = false }],
            _ => h.Credentials.Rows,
        };
        if (change == "archive") h.Planning.Visible = false;
        var outcome = await h.Coordinator.DownloadAutomaticAsync(plan);
        Assert.False(outcome.Succeeded);
        Assert.Empty(h.Network.Urls);
        Assert.Equal(0, h.Network.Downloads);
        Assert.Empty(await h.Context.Accounts.GetByArchiveIdAsync(h.Archive.Id));
    }

    [Fact]
    public async Task CancelledActionAndManualEntryCannotExecuteAnAutomaticPlan()
    {
        await using var h = await Harness.CreateAsync();
        var plan = await h.PlanAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            h.Coordinator.DownloadAutomaticAsync(plan, cancellation.Token));
        var manual = await h.Coordinator.DownloadAsync(plan,
            "https://public-operation-hk4e.mihoyo.com/gacha_info/api/getGachaLog?authkey=synthetic");
        Assert.False(manual.Succeeded);
        Assert.Empty(h.Network.Urls);
        Assert.Equal(0, h.Network.Downloads);
    }

    [Fact]
    public async Task ManualEntryDoesNotRequireReadableCredentialsOrAnyLoginRole()
    {
        await using var h = await Harness.CreateAsync();
        h.Credentials.ThrowOnRead = true;
        h.Passports.Account = null;
        var planned = await h.Coordinator.PlanAsync(Uid,
            "https://public-operation-hk4e.mihoyo.com/gacha_info/api/getGachaLog?authkey=synthetic",
            h.Archive.Id, GachaImportMode.Incremental);
        Assert.True(planned.Succeeded, planned.FailureMessage);
        Assert.Equal(0, h.Credentials.Reads);
        var downloaded = await h.Coordinator.DownloadAsync(planned.Plan!,
            "https://public-operation-hk4e.mihoyo.com/gacha_info/api/getGachaLog?authkey=synthetic");
        Assert.True(downloaded.Succeeded, downloaded.FailureMessage);
        Assert.Empty(h.Network.Urls); // No SToken URL generation on the manual path.
        Assert.Equal(1, h.Network.Downloads);
    }

    [Fact]
    public async Task NetworkFailureDoesNotExposeCredentialOrLinkOrCreateParentObjects()
    {
        await using var h = await Harness.CreateAsync();
        h.Network.Fail = true;
        var outcome = await h.Coordinator.DownloadAutomaticAsync(await h.PlanAsync());
        Assert.False(outcome.Succeeded);
        Assert.Contains("未写入任何数据", outcome.FailureMessage);
        Assert.DoesNotContain(DummyToken, outcome.FailureMessage);
        Assert.DoesNotContain("authkey=", outcome.FailureMessage);
        Assert.Equal(1, h.Network.Downloads);
        Assert.Empty(await h.Context.Accounts.GetByArchiveIdAsync(h.Archive.Id));
    }

    [Fact]
    public async Task FreshDownloadUsesRealLegacyUpgradeAndAtomicImportWithFullModeRevisions()
    {
        await using var h = await Harness.CreateAsync();
        var otherArchive = await new CreatePlayerArchive(h.Context.Archives).ExecuteAsync("同 UID 独立副本");
        var other = await new AddGameAccount(h.Context.Archives, h.Context.Accounts)
            .ExecuteAsync(otherArchive.Id, Uid, GameServerRegion.ChinaOfficial, null);
        var first = await h.DownloadReadAndPlanAsync(GachaImportMode.Incremental);
        Assert.Contains(first.Accounts, account => account.WillCreateAccount);
        Assert.Contains("撤销", first.ConfirmationText);
        var applied = await h.Import.ExecuteAsync(first);
        Assert.True(applied.IsSuccess, applied.Message);
        Assert.Equal(2, applied.Result!.ImportedCount);
        Assert.Equal(1, applied.Result.CreatedAccountCount);
        Assert.False(Assert.Single(await h.Context.AtomicGacha.GetHistoryAsync(h.Archive.Id, 10)).IsUndoEligible);
        var account = Assert.Single(await h.Context.Accounts.GetByArchiveIdAsync(h.Archive.Id));
        var records = await h.Context.Gacha.GetRecentAsync(account.Id, 100);
        Assert.Equal(2, records.Count);
        Assert.All(records, record =>
        {
            Assert.Equal(DataOrigin.StandardImport, record.Provenance.Origin);
            Assert.NotNull(record.Provenance.Timestamps.ImportedAt);
            Assert.Null(record.Provenance.Timestamps.FetchedAt);
            Assert.Equal(TimeSpan.FromHours(8), record.Time.Offset);
        });

        // A later fresh file changes the same facts. The frozen Full plan corrects both, never duplicates.
        var full = await h.DownloadReadAndPlanAsync(GachaImportMode.Full);
        Assert.DoesNotContain(full.Accounts, item => item.WillCreateAccount);
        var overwritten = await h.Import.ExecuteAsync(full);
        Assert.True(overwritten.IsSuccess, overwritten.Message);
        Assert.Equal(0, overwritten.Result!.ImportedCount);
        Assert.Equal(2, overwritten.Result.UpdatedCount);
        Assert.Single(await h.Context.Accounts.GetByArchiveIdAsync(h.Archive.Id));
        Assert.Equal(2, (await h.Context.Gacha.GetRecentAsync(account.Id, 100)).Count);
        var revisions = await h.Context.AtomicGacha.GetRecordRevisionsAsync(new(account.Id, "990020001"));
        Assert.Equal(EntityChangeKind.Update, revisions[^1].ChangeKind);
        Assert.Equal(1, revisions[^1].Before!.Time.Second);
        Assert.Equal(2, revisions[^1].After!.Time.Second);
        Assert.Equal(2, h.Network.Urls.Count);
        Assert.NotEqual(h.Network.Urls[0], h.Network.Urls[1]);
        Assert.Empty(await h.Context.Gacha.GetRecentAsync(other.Id, 10));
        Assert.True((await h.Context.AtomicGacha.GetHistoryAsync(h.Archive.Id, 10))[0].IsUndoEligible);
        var undone = await new ManageGachaHistory(h.Context.AtomicGacha)
            .UndoLatestAsync(OperationId.New(), h.Archive.Id);
        Assert.Equal(ChangeExecutionStatus.Applied, undone.Status);
        Assert.All(await h.Context.Gacha.GetRecentAsync(account.Id, 100), record => Assert.Equal(1, record.Time.Second));
    }

    [Theory]
    [InlineData("100000002", false)]
    [InlineData("100000001", true)]
    public async Task WrongOrMultipleDownloadedRolesAreRejectedBeforeEvenPlanningAnArchiveImport(string uid, bool multiple)
    {
        await using var h = await Harness.CreateAsync();
        h.Network.ResponseUid = uid;
        var outcome = await h.Coordinator.DownloadAutomaticAsync(await h.PlanAsync());
        var source = new GachaImportSource(outcome.FileName!,
            _ => Task.FromResult<Stream>(new MemoryStream(outcome.Content!, false)));
        var read = await h.Import.ReadSourceAsync(source);
        Assert.True(read.Succeeded, read.FailureMessage);
        var summary = read.Summary!;
        if (multiple) summary = summary with { Accounts = [.. summary.Accounts, new("100000002", 1, 0)] };
        Assert.False(GachaTeyvatHelperImportCoordinator.MatchesDownloadedRole(summary, Uid));
        Assert.Empty(await h.Context.Accounts.GetByArchiveIdAsync(h.Archive.Id));
    }

    private sealed class Harness : IAsyncDisposable
    {
        public SqliteRepositoryTestContext Context { get; }
        public required PlayerArchive Archive { get; set; }
        public PassportStore Passports { get; init; } = new();
        public SummaryStore Credentials { get; init; } = new();
        public RecordingNetwork Network { get; init; } = new();
        public required PlanningSource Planning { get; init; }
        public required GachaTeyvatHelperImportCoordinator Coordinator { get; init; }
        public required GachaImportCoordinator Import { get; init; }

        private Harness(SqliteRepositoryTestContext context) { Context = context; }

        public static async Task<Harness> CreateAsync()
        {
            var context = SqliteRepositoryTestContext.Create();
            var archive = await new CreatePlayerArchive(context.Archives).ExecuteAsync("合成小助手档案");
            var planning = new PlanningSource(new ServiceGachaImportPlanningSource(
                new GetPlayerArchives(context.Archives), new GetGameAccounts(context.Archives, context.Accounts)));
            var reader = new UigfCompatibleGachaReader(new UigfV42GachaReader(),
                new EmbeddedGachaLocalizationSource(), TimeProvider.System);
            var importer = new ImportUigfGachaRecords(reader, new EmptyGachaItemMetadataProvider(),
                context.Archives, context.Accounts, new CommitGachaRecords(context.AtomicGacha));
            var network = new RecordingNetwork();
            var passports = new PassportStore();
            var credentials = new SummaryStore
            {
                Rows = [new(passports.Account!.Id, "合成登录", "12****12", Uid, Now, true, null)],
            };
            return new(context)
            {
                Archive = archive, Planning = planning, Network = network, Passports = passports, Credentials = credentials,
                Coordinator = new(planning, new ServiceGachaTeyvatHelperImportGateway(
                    new TeyvatHelperUigfImportSource(passports, null, network, network)), credentials),
                Import = new(reader, new ServiceGachaUigfImporter(importer), planning),
            };
        }

        public async Task<GachaTeyvatHelperImportPlan> PlanAsync(GachaImportMode mode = GachaImportMode.Incremental)
        {
            var result = await Coordinator.PlanAutomaticAsync(Passports.Account!.Id, Uid, Archive.Id, mode);
            Assert.True(result.Succeeded, result.FailureMessage);
            return result.Plan!;
        }

        public async Task<GachaImportPlan> DownloadReadAndPlanAsync(GachaImportMode mode)
        {
            var outcome = await Coordinator.DownloadAutomaticAsync(await PlanAsync(mode));
            Assert.True(outcome.Succeeded, outcome.FailureMessage);
            var source = new GachaImportSource(outcome.FileName!,
                _ => Task.FromResult<Stream>(new MemoryStream(outcome.Content!, false)));
            var read = await Import.ReadSourceAsync(source);
            Assert.True(read.Succeeded, read.FailureMessage);
            Assert.Equal("v2.2", read.Summary!.FormatVersion);
            Assert.True(GachaTeyvatHelperImportCoordinator.MatchesDownloadedRole(read.Summary, Uid));
            var planned = await Import.PlanAsync(read.Summary,
                new(GachaImportTargetScope.ArchiveAccounts, Archive.Id), mode);
            Assert.True(planned.Succeeded, planned.FailureMessage);
            Assert.True(planned.Plan!.CanExecute, planned.Plan.BlockMessage);
            return planned.Plan!;
        }

        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }

    private sealed class PlanningSource(IGachaImportPlanningSource inner) : IGachaImportPlanningSource
    {
        public bool Visible { get; set; } = true;
        public Task<IReadOnlyList<PlayerArchive>> GetArchivesAsync(CancellationToken ct = default) =>
            Visible ? inner.GetArchivesAsync(ct) : Task.FromResult<IReadOnlyList<PlayerArchive>>([]);
        public Task<IReadOnlyList<GameAccount>> GetAccountsAsync(Guid archive, CancellationToken ct = default) =>
            inner.GetAccountsAsync(archive, ct);
    }

    private sealed class RecordingNetwork : ISTokenGachaUrlProvider, ITeyvatHelperUigfClient
    {
        public List<Uri> Urls { get; } = [];
        public int Downloads { get; private set; }
        public bool Fail { get; set; }
        public string? ResponseUid { get; set; }
        public Task<Uri> CreateAsync(PassportAccount passport, GameAccount account, CancellationToken ct = default)
        {
            var url = new Uri("https://public-operation-hk4e.mihoyo.com/gacha_info/api/getGachaLog?authkey=synthetic-" + Guid.NewGuid());
            Urls.Add(url);
            return Task.FromResult(url);
        }

        public Task<TeyvatHelperUigfDownload> DownloadAsync(string uid, Uri url, CancellationToken ct = default)
        {
            Downloads++;
            if (Fail) throw new HttpRequestException(DummyToken + " authkey=synthetic-private-value");
            var records = new[] { "990020001", "990020002" }.Select(id => new
            {
                id, item_id = "10000089", name = "合成角色", item_type = "角色", rank_type = "5",
                gacha_type = "301", uigf_gacha_type = "301", count = "1",
                time = $"2026-11-19 12:00:{Downloads:D2}",
            }).ToArray();
            return Task.FromResult(new TeyvatHelperUigfDownload("synthetic-legacy.json",
                JsonSerializer.SerializeToUtf8Bytes(new
                {
                    info = new { uid = ResponseUid ?? uid, uigf_version = "v2.2", lang = "zh-cn", export_timestamp = 1795000000 },
                    list = records,
                })));
        }
    }

    private sealed class SummaryStore : IIsolatedCredentialStore
    {
        public IReadOnlyList<IsolatedCredentialSummary> Rows { get; set; } = [];
        public bool ThrowOnRead { get; set; }
        public int Reads { get; private set; }
        public string? LoadWarning => null;
        public Task<IReadOnlyList<IsolatedCredentialSummary>> ListAsync(CancellationToken ct = default)
        {
            Reads++;
            return ThrowOnRead ? throw new IOException("Synthetic read failure") : Task.FromResult(Rows);
        }
        public Task<Guid> SaveAsync(string token, string mid, string uid, string? label, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task DeleteAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task ClearAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task MarkUnusableAsync(Guid id, string reason, CancellationToken ct = default) => throw new NotSupportedException();
        public Task MarkUsableAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class PassportStore : IPassportAccountStore
    {
        public PassportAccount? Account { get; set; } = new(Guid.NewGuid(), "12345", "123456789012",
            "合成登录", PassportLoginMethod.ManualCookie,
            new(DummyToken, null, null, Now, null, null, null), new("synthetic-device", null), Now, Now);
        public Task<IReadOnlyList<PassportAccount>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PassportAccount>>(Account is null ? [] : [Account]);
        public Task<PassportAccount?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(Account?.Id == id ? Account : null);
        public Task SaveAsync(PassportAccount account, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> TrySaveIfUnchangedAsync(PassportAccount original, PassportAccount updated, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task DeleteAsync(Guid id, CancellationToken ct = default) { Account = null; return Task.CompletedTask; }
    }
}
