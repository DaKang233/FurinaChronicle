// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Services.Gacha;
using SQLite;

namespace FurinaChronicle.Tests.Infrastructure.Persistence.Sqlite;

public sealed class SqliteGachaRecordRepositoryBehaviorTests
{
    [Fact]
    public async Task SaveBatchAsync_EmptyBatch_ReturnsZeroCounts()
    {
        await using var context = SqliteRepositoryTestContext.Create();

        var result = await context.Gacha.SaveBatchAsync([]);

        Assert.Equal(0, result.InsertedCount);
        Assert.Equal(0, result.DuplicateCount);
    }

    [Fact]
    public async Task SaveBatchAsync_DuplicateWithinSameAccount_IsIgnored()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        GameAccount account = await AddAccountAsync(context);
        GachaRecord record = CreateGacha(account.Id, "gacha-1", minute: 30);

        var result = await context.Gacha.SaveBatchAsync([record, record]);

        Assert.Equal(1, result.InsertedCount);
        Assert.Equal(1, result.DuplicateCount);
        Assert.Single(await context.Gacha.GetRecentAsync(account.Id, 20));
    }

    [Fact]
    public async Task SaveBatchAsync_DuplicateCompletesPreviouslyMissingFields()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        GameAccount account = await AddAccountAsync(context);
        GachaRecord incomplete = CreateGacha(
            account.Id,
            "1756372800000232682",
            minute: 30) with
        {
            ItemId = null,
            ItemType = null,
            GachaType = "200",
            UigfGachaType = "200"
        };
        GachaRecord complete = incomplete with
        {
            ItemId = "14301",
            ItemType = "Weapon"
        };
        await context.Gacha.SaveBatchAsync([incomplete]);

        GachaSaveResult result = await context.Gacha.SaveBatchAsync([complete]);

        Assert.Equal(0, result.InsertedCount);
        Assert.Equal(1, result.DuplicateCount);
        GachaRecord stored = Assert.Single(
            await context.Gacha.GetRecentAsync(account.Id, 20));
        Assert.Equal("14301", stored.ItemId);
        Assert.Equal("Weapon", stored.ItemType);
    }

    [Fact]
    public async Task SaveBatchAsync_PreserveExisting_DoesNotOverwriteCompleteFields()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        GameAccount account = await AddAccountAsync(context);
        GachaRecord existing = CreateGacha(account.Id, "gacha-1", minute: 30) with
        {
            ItemId = "old-id",
            ItemType = "Avatar",
            GachaType = "301",
            UigfGachaType = "301",
            RankType = 5
        };
        GachaRecord incoming = existing with
        {
            ItemName = "纠正后的名称",
            ItemId = "new-id",
            ItemType = "Weapon",
            GachaType = "302",
            UigfGachaType = "302",
            RankType = 4,
            Time = existing.Time.AddMinutes(1)
        };
        await context.Gacha.SaveBatchAsync([existing]);

        GachaSaveResult result = await context.Gacha.SaveBatchAsync([incoming]);

        Assert.Equal(0, result.InsertedCount);
        Assert.Equal(0, result.UpdatedCount);
        Assert.Equal(1, result.DuplicateCount);
        GachaRecord stored = Assert.Single(
            await context.Gacha.GetRecentAsync(account.Id, 20));
        Assert.Equal(existing, stored);
    }

    [Fact]
    public async Task SaveBatchAsync_ReplaceExisting_OverwritesAuthoritativeFields()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        GameAccount account = await AddAccountAsync(context);
        GachaRecord existing = CreateGacha(account.Id, "gacha-1", minute: 30) with
        {
            ItemId = "old-id",
            ItemType = "Avatar",
            GachaType = "301",
            UigfGachaType = "301",
            RankType = 5
        };
        GachaRecord incoming = existing with
        {
            ItemName = "纠正后的名称",
            ItemId = "new-id",
            ItemType = "Weapon",
            GachaType = "302",
            UigfGachaType = "302",
            RankType = 4,
            Count = 2,
            Time = existing.Time.AddMinutes(1)
        };
        await context.Gacha.SaveBatchAsync([existing]);

        GachaSaveResult result = await context.Gacha.SaveBatchAsync(
            [incoming],
            conflictPolicy: GachaRecordConflictPolicy.ReplaceExisting);

        Assert.Equal(0, result.InsertedCount);
        Assert.Equal(1, result.UpdatedCount);
        Assert.Equal(0, result.DuplicateCount);
        GachaRecord stored = Assert.Single(
            await context.Gacha.GetRecentAsync(account.Id, 20));
        Assert.Equal(incoming, stored);
    }

    [Fact]
    public async Task GetRecentAsync_FiltersAccountSortsNewestFirstAndAppliesLimit()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        PlayerArchive archive = SqliteRepositoryTestContext.CreateArchive();
        await context.Archives.AddAsync(archive);
        GameAccount requestedAccount = SqliteRepositoryTestContext.CreateAccount(
            archive.Id,
            uid: "100000001");
        GameAccount otherAccount = SqliteRepositoryTestContext.CreateAccount(
            archive.Id,
            uid: "100000002");
        await context.Accounts.AddAsync(requestedAccount);
        await context.Accounts.AddAsync(otherAccount);
        await context.Gacha.SaveBatchAsync(
            [
                CreateGacha(requestedAccount.Id, "old", minute: 28),
                CreateGacha(requestedAccount.Id, "new", minute: 30),
                CreateGacha(requestedAccount.Id, "middle", minute: 29),
                CreateGacha(otherAccount.Id, "other-account", minute: 31)
            ]);

        IReadOnlyList<GachaRecord> loaded =
            await context.Gacha.GetRecentAsync(requestedAccount.Id, count: 2);

        Assert.Equal(["new", "middle"], loaded.Select(x => x.ExternalRecordId));
        Assert.All(loaded, x => Assert.Equal(requestedAccount.Id, x.GameAccountId));
    }

    [Fact]
    public async Task QueryAsync_SameSecondUsesRemoteRecordIdAsTieBreaker()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        GameAccount account = await AddAccountAsync(context);
        DateTimeOffset sameTime =
            new(2026, 9, 9, 13, 34, 0, TimeSpan.FromHours(8));
        await context.Gacha.SaveBatchAsync(
        [
            new GachaRecord(account.Id, "1788931200000208082", "鸦羽弓", 3, sameTime),
            new GachaRecord(account.Id, "1788931200000207182", "飞天御剑", 3, sameTime),
            new GachaRecord(account.Id, "1788931200000207982", "黑缨枪", 3, sameTime)
        ]);

        IReadOnlyList<GachaRecord> newest =
            await context.Gacha.GetRecentAsync(account.Id, 20);
        IReadOnlyList<GachaRecord> oldest = await context.Gacha.QueryAsync(
            new GachaRecordQuery(
                [account.Id],
                SortOrder: GachaRecordSortOrder.OldestFirst));

        Assert.Equal(
            [
                "1788931200000208082",
                "1788931200000207982",
                "1788931200000207182"
            ],
            newest.Select(record => record.ExternalRecordId));
        Assert.Equal(
            [
                "1788931200000207182",
                "1788931200000207982",
                "1788931200000208082"
            ],
            oldest.Select(record => record.ExternalRecordId));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task GetRecentAsync_NonPositiveCount_ThrowsArgumentOutOfRangeException(
        int count)
    {
        await using var context = SqliteRepositoryTestContext.Create();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => context.Gacha.GetRecentAsync(Guid.NewGuid(), count));
    }

    [Fact]
    public async Task SaveBatchAsync_WhenOneRecordViolatesForeignKey_RollsBackWholeBatch()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        GameAccount validAccount = await AddAccountAsync(context);
        GachaRecord valid = CreateGacha(validAccount.Id, "valid", minute: 30);
        GachaRecord invalid = CreateGacha(Guid.NewGuid(), "invalid", minute: 29);

        SQLiteException error = await Assert.ThrowsAsync<SQLiteException>(
            () => context.Gacha.SaveBatchAsync([valid, invalid]));

        Assert.Equal(SQLite3.Result.Constraint, error.Result);
        Assert.Empty(await context.Gacha.GetRecentAsync(validAccount.Id, 20));
    }

    [Fact]
    public async Task GetRecentAsync_CancelledToken_ThrowsOperationCancelledException()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => context.Gacha.GetRecentAsync(
                Guid.NewGuid(),
                20,
                cancellation.Token));
    }

    [Fact]
    public async Task QueryAsync_AppliesAccountPoolRankAndTimeFilters()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        PlayerArchive archive = SqliteRepositoryTestContext.CreateArchive();
        await context.Archives.AddAsync(archive);
        GameAccount first = SqliteRepositoryTestContext.CreateAccount(
            archive.Id,
            uid: "100000001");
        GameAccount second = SqliteRepositoryTestContext.CreateAccount(
            archive.Id,
            uid: "100000002");
        await context.Accounts.AddAsync(first);
        await context.Accounts.AddAsync(second);
        DateTimeOffset start =
            new(2026, 7, 16, 18, 0, 0, TimeSpan.FromHours(8));
        await context.Gacha.SaveBatchAsync(
        [
            CreateGacha(first.Id, "event-1", 1) with
            {
                RankType = 5,
                UigfGachaType = "301"
            },
            CreateGacha(second.Id, "event-2", 2) with
            {
                RankType = 5,
                GachaType = "400",
                UigfGachaType = null
            },
            CreateGacha(first.Id, "weapon", 3) with
            {
                RankType = 5,
                UigfGachaType = "302"
            },
            CreateGacha(second.Id, "four-star", 4) with
            {
                RankType = 4,
                UigfGachaType = "301"
            }
        ]);
        var query = new GachaRecordQuery(
            [first.Id, second.Id],
            RankTypes: new HashSet<int> { 5 },
            PoolGroups: new HashSet<GachaPoolGroup>
            {
                GachaPoolGroup.CharacterEvent
            },
            StartTime: start,
            EndTime: start.AddHours(1),
            SortOrder: GachaRecordSortOrder.OldestFirst);

        IReadOnlyList<GachaRecord> result =
            await context.Gacha.QueryAsync(query);
        int count = await context.Gacha.CountAsync(query);

        Assert.Equal(2, count);
        Assert.Equal(
            ["event-1", "event-2"],
            result.Select(record => record.ExternalRecordId));
    }

    private static async Task<GameAccount> AddAccountAsync(
        SqliteRepositoryTestContext context)
    {
        PlayerArchive archive = SqliteRepositoryTestContext.CreateArchive();
        await context.Archives.AddAsync(archive);
        GameAccount account = SqliteRepositoryTestContext.CreateAccount(archive.Id);
        await context.Accounts.AddAsync(account);
        return account;
    }

    private static GachaRecord CreateGacha(
        Guid accountId,
        string externalId,
        int minute)
    {
        return new GachaRecord(
            accountId,
            externalId,
            "芙宁娜",
            5,
            new DateTimeOffset(2026, 7, 16, 18, minute, 0, TimeSpan.FromHours(8)));
    }
}
