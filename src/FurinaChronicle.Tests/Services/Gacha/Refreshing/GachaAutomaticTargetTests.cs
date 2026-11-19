// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Infrastructure.Gacha.Metadata;
using FurinaChronicle.Infrastructure.Persistence.Sqlite;
using FurinaChronicle.Services.Archives;
using FurinaChronicle.Services.Gacha;
using FurinaChronicle.Services.Gacha.Refreshing;
using FurinaChronicle.Services.Gacha.Writing;
using FurinaChronicle.Services.History;
using FurinaChronicle.Tests.Infrastructure.Persistence.Sqlite;

namespace FurinaChronicle.Tests.Services.Gacha.Refreshing;

public sealed class GachaAutomaticTargetTests
{
    private const string Url = "https://public-operation-hk4e.mihoyo.com/gacha_info/api/getGachaLog?authkey=synthetic-only";
    private static readonly GachaRefreshIdentity Identity = new("100000001", GameServerRegion.ChinaOfficial);

    [Theory]
    [InlineData(GachaRefreshSource.ManualUrl)]
    [InlineData(GachaRefreshSource.WindowsWebCache)]
    public async Task EmptySelectionCreatesParentsAndFactsTogether(GachaRefreshSource source)
    {
        await using var c = SqliteRepositoryTestContext.Create();
        var outcome = await Create(c, new Client()).ExecuteAsync(new(source, GachaImportMode.Full, null, Url, "synthetic"));
        Assert.True(outcome.ParentsCreated);
        var archive = Assert.Single(await c.Archives.GetAllAsync());
        var account = Assert.Single(await c.Accounts.GetByArchiveIdAsync(archive.Id));
        Assert.Equal("未命名档案1", archive.Name);
        Assert.Equal(outcome.Target.Account.Id, account.Id);
        Assert.Equal(2, await c.Gacha.CountAsync(account.Id));
        Assert.NotNull(account.RoleIdentity);
        using var raw = c.OpenRawConnection();
        Assert.Equal(0, raw.ExecuteScalar<int>("SELECT count(*) FROM pragma_foreign_key_check"));
        Assert.All(await c.AtomicGacha.GetHistoryAsync(archive.Id, 10), item => Assert.False(item.IsUndoEligible));
    }

    [Fact]
    public async Task EmptyArchiveIsReusedAndMatchingExistingAccountIsNotDuplicated()
    {
        await using var c = SqliteRepositoryTestContext.Create();
        var archive = await new CreatePlayerArchive(c.Archives).ExecuteAsync("已有空档案");
        var first = await Create(c, new Client()).ExecuteAsync(Request(archive.Id));
        var second = await Create(c, new Client()).ExecuteAsync(Request(archive.Id));
        Assert.True(first.ParentsCreated);
        Assert.False(second.ParentsCreated);
        Assert.Equal(first.Target.Account.Id, second.Target.Account.Id);
        Assert.Single(await c.Archives.GetAllAsync());
        Assert.Single(await c.Accounts.GetByArchiveIdAsync(archive.Id));
        Assert.Equal(2, await c.Gacha.CountAsync(first.Target.Account.Id));
        Assert.Equal(0, second.Refresh.InsertedCount);
    }

    [Fact]
    public async Task DifferentArchivesReuseExistingSharedGuidButKeepCopiesIndependent()
    {
        await using var c = SqliteRepositoryTestContext.Create();
        var a = await new CreatePlayerArchive(c.Archives).ExecuteAsync("甲");
        var b = await new CreatePlayerArchive(c.Archives).ExecuteAsync("乙");
        var natural = GenshinGameRoleIdentity.Create(Identity.Uid, Identity.ServerRegion);
        var legacyIdentity = new GameRoleIdentity(new GameRoleIdentityId(Guid.NewGuid()), natural);
        var original = SqliteRepositoryTestContext.CreateAccount(a.Id, Identity.Uid, Identity.ServerRegion,
            roleIdentity: legacyIdentity);
        await c.Accounts.AddAsync(original);
        var outcome = await Create(c, new Client()).ExecuteAsync(Request(b.Id));
        Assert.NotEqual(original.Id, outcome.Target.Account.Id);
        Assert.Equal(legacyIdentity.Id, outcome.Target.Account.GameRoleIdentityId);
        Assert.Equal(0, await c.Gacha.CountAsync(original.Id));
        Assert.Equal(2, await c.Gacha.CountAsync(outcome.Target.Account.Id));
    }

    [Theory]
    [InlineData("network")]
    [InlineData("empty")]
    [InlineData("different-uid")]
    [InlineData("space")]
    public async Task FailedOrEmptyRefreshLeavesNoEmptyParentsOrOrphanIdentities(string scenario)
    {
        await using var c = SqliteRepositoryTestContext.Create();
        var client = new Client { Scenario = scenario };
        var store = scenario == "space" ? new SqliteGachaAtomicChangeStore(c.Database, new NoSpace()) : c.AtomicGacha;
        var service = Create(c, client, store);
        if (scenario == "empty")
        {
            var outcome = await service.ExecuteAsync(Request(null));
            Assert.False(outcome.ParentsCreated);
            Assert.Equal(0, outcome.Refresh.InsertedCount);
        }
        else if (scenario == "network")
            await Assert.ThrowsAsync<HttpRequestException>(() => service.ExecuteAsync(Request(null)));
        else if (scenario == "different-uid")
            await Assert.ThrowsAsync<InvalidDataException>(() => service.ExecuteAsync(Request(null)));
        else await Assert.ThrowsAsync<IOException>(() => service.ExecuteAsync(Request(null)));
        Assert.Empty(await c.Archives.GetAllAsync());
        using var raw = c.OpenRawConnection();
        Assert.Equal(0, raw.ExecuteScalar<int>("SELECT count(*) FROM GameAccounts"));
        Assert.Equal(0, raw.ExecuteScalar<int>("SELECT count(*) FROM GachaRecords"));
        Assert.Equal(0, raw.ExecuteScalar<int>("SELECT count(*) FROM GameRoleIdentities"));
        Assert.Equal(0, raw.ExecuteScalar<int>("SELECT count(*) FROM DataChangeSets"));
    }

    [Fact]
    public async Task PreparationIsReadOnlyNamesAreExactAndStaleSelectionsAreRejected()
    {
        await using var c = SqliteRepositoryTestContext.Create();
        await new CreatePlayerArchive(c.Archives).ExecuteAsync("未命名档案1");
        await new CreatePlayerArchive(c.Archives).ExecuteAsync("未命名档案2 ");
        var service = new PrepareGachaRefreshTarget(c.Archives, c.Accounts, TimeProvider.System);
        var target = await service.ExecuteAsync(Identity, null);
        Assert.Equal("未命名档案2", target.ArchiveToCreate!.Name);
        Assert.Equal(2, (await c.Archives.GetAllAsync()).Count);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExecuteAsync(Identity, Guid.NewGuid()));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ExecuteAsync(Identity, null, Guid.NewGuid()));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ExecuteAsync(
            new(Identity.Uid, GameServerRegion.Asia), null));
    }

    [Fact]
    public async Task SelectedAccountIsValidatedByArchiveAndNaturalIdentity()
    {
        await using var c = SqliteRepositoryTestContext.Create();
        var archive = await new CreatePlayerArchive(c.Archives).ExecuteAsync("合成");
        var account = await new AddGameAccount(c.Archives, c.Accounts)
            .ExecuteAsync(archive.Id, Identity.Uid, Identity.ServerRegion, null);
        var targets = new PrepareGachaRefreshTarget(c.Archives, c.Accounts, TimeProvider.System);
        var target = await targets.ExecuteAsync(Identity, archive.Id, account.Id);
        Assert.Equal(account.Id, target.Account.Id);
        Assert.Null(target.AccountToCreate);
        await Assert.ThrowsAsync<InvalidOperationException>(() => targets.ExecuteAsync(
            new("100000002", GameServerRegion.ChinaOfficial), archive.Id, account.Id));
    }

    [Fact]
    public async Task PurgedCopyIsNotSilentlyRestoredAndNewArchiveIsIndependent()
    {
        await using var c = SqliteRepositoryTestContext.Create();
        var service = Create(c, new Client());
        var first = await service.ExecuteAsync(Request(null));
        Guid archiveId = first.Target.Account.PlayerArchiveId;
        await new DeleteGameAccount(c.AtomicGacha).ExecuteAsync(first.Target.Account.Id);
        var suppressed = await service.ExecuteAsync(Request(archiveId));
        Assert.Equal(2, suppressed.Refresh.SuppressedCount);
        Assert.False(suppressed.ParentsCreated);
        Assert.Empty(await c.Accounts.GetByArchiveIdAsync(archiveId));
        var independent = await Create(c, new Client()).ExecuteAsync(Request(null));
        Assert.True(independent.ParentsCreated);
        Assert.Equal(2, independent.Refresh.InsertedCount);
        Assert.NotEqual(archiveId, independent.Target.Account.PlayerArchiveId);
    }

    [Fact]
    public async Task UnresolvedSameUidDoesNotParticipateInAutomaticMapping()
    {
        await using var c = SqliteRepositoryTestContext.Create();
        var archive = await new CreatePlayerArchive(c.Archives).ExecuteAsync("未解析");
        var unresolved = SqliteRepositoryTestContext.CreateAccount(
            archive.Id, Identity.Uid, Identity.ServerRegion, isPlaceholder: true);
        await c.Accounts.AddAsync(unresolved);
        var targets = new PrepareGachaRefreshTarget(c.Archives, c.Accounts, TimeProvider.System);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => targets.ExecuteAsync(Identity, archive.Id));
        Assert.Contains("未解析", error.Message);
        Assert.Single(await c.Accounts.GetByArchiveIdAsync(archive.Id));
        Assert.Null((await c.Accounts.GetByIdAsync(unresolved.Id))!.RoleIdentity);
    }

    [Fact]
    public async Task InvalidOrCancelledRequestCannotDiscoverOrCreateAnything()
    {
        await using var c = SqliteRepositoryTestContext.Create();
        var service = Create(c, new Client());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.ExecuteAsync(
            Request(null) with { Mode = (GachaImportMode)99 }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.ExecuteAsync(
            Request(null) with { Source = GachaRefreshSource.SToken }));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ExecuteAsync(Request(null), cancellationToken: cancellation.Token));
        Assert.Empty(await c.Archives.GetAllAsync());
    }

    private static GachaUnselectedRefreshRequest Request(Guid? archive) =>
        new(GachaRefreshSource.ManualUrl, GachaImportMode.Full, archive, Url);

    private static RefreshUnselectedGachaRecords Create(
        SqliteRepositoryTestContext c, Client client, SqliteGachaAtomicChangeStore? store = null) =>
        new(new(c.Archives, c.Accounts, TimeProvider.System),
            // These two sources cannot reach the passport/SToken ports.
            new(c.Gacha, new CommitGachaRecords(store ?? c.AtomicGacha), null!, null!,
                new WindowsSource(), client, new EmptyGachaItemMetadataProvider()));

    private sealed class NoSpace : IHistoryStorageCapacityProvider
    {
        public HistoryStorageCapacity GetCapacity() => new(0);
    }

    private sealed class WindowsSource : IWindowsGachaCacheUrlProvider
    {
        public bool IsSupported => true;
        public Task<Uri> FindAsync(string path, GameServerRegion region, CancellationToken cancellationToken = default) =>
            Task.FromResult(new Uri(Url));
    }

    private sealed class Client : IGachaLogClient
    {
        private int firstPoolRequests;
        public string? Scenario { get; init; }
        public Task<GachaRemotePage> GetPageAsync(
            Uri sourceUrl, string gachaType, string? endId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Scenario == "network" && gachaType == "302") throw new HttpRequestException("Synthetic failure");
            if (gachaType != "301" || endId is not null) return Task.FromResult(new GachaRemotePage([], null));
            firstPoolRequests++;
            if (Scenario == "empty" && firstPoolRequests > 1)
                return Task.FromResult(new GachaRemotePage([], null));
            string uid = Scenario == "different-uid" && firstPoolRequests > 1 ? "100000002" : Identity.Uid;
            GachaRemoteRecord[] records = [
                new(uid, "990000002", "合成五星", "10000089", "角色", 5, "301", DateTimeOffset.UnixEpoch),
                new(uid, "990000001", "合成四星", "10000016", "角色", 4, "301", DateTimeOffset.UnixEpoch),
            ];
            return Task.FromResult(new GachaRemotePage(records, null));
        }
    }
}
