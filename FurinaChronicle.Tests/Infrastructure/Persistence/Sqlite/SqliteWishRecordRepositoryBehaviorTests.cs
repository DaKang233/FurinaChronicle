using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Wishes;
using FurinaChronicle.Services.Wishes;
using SQLite;

namespace FurinaChronicle.Tests.Infrastructure.Persistence.Sqlite;

public sealed class SqliteWishRecordRepositoryBehaviorTests
{
    [Fact]
    public async Task SaveBatchAsync_EmptyBatch_ReturnsZeroCounts()
    {
        await using var context = SqliteRepositoryTestContext.Create();

        var result = await context.Wishes.SaveBatchAsync([]);

        Assert.Equal(0, result.InsertedCount);
        Assert.Equal(0, result.DuplicateCount);
    }

    [Fact]
    public async Task SaveBatchAsync_DuplicateWithinSameAccount_IsIgnored()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        GameAccount account = await AddAccountAsync(context);
        WishRecord record = CreateWish(account.Id, "wish-1", minute: 30);

        var result = await context.Wishes.SaveBatchAsync([record, record]);

        Assert.Equal(1, result.InsertedCount);
        Assert.Equal(1, result.DuplicateCount);
        Assert.Single(await context.Wishes.GetRecentAsync(account.Id, 20));
    }

    [Fact]
    public async Task SaveBatchAsync_DuplicateCompletesPreviouslyMissingFields()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        GameAccount account = await AddAccountAsync(context);
        WishRecord incomplete = CreateWish(
            account.Id,
            "1756372800000232682",
            minute: 30) with
        {
            ItemId = null,
            ItemType = null,
            GachaType = "200",
            UigfGachaType = "200"
        };
        WishRecord complete = incomplete with
        {
            ItemId = "14301",
            ItemType = "Weapon"
        };
        await context.Wishes.SaveBatchAsync([incomplete]);

        WishSaveResult result = await context.Wishes.SaveBatchAsync([complete]);

        Assert.Equal(0, result.InsertedCount);
        Assert.Equal(1, result.DuplicateCount);
        WishRecord stored = Assert.Single(
            await context.Wishes.GetRecentAsync(account.Id, 20));
        Assert.Equal("14301", stored.ItemId);
        Assert.Equal("Weapon", stored.ItemType);
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
        await context.Wishes.SaveBatchAsync(
            [
                CreateWish(requestedAccount.Id, "old", minute: 28),
                CreateWish(requestedAccount.Id, "new", minute: 30),
                CreateWish(requestedAccount.Id, "middle", minute: 29),
                CreateWish(otherAccount.Id, "other-account", minute: 31)
            ]);

        IReadOnlyList<WishRecord> loaded =
            await context.Wishes.GetRecentAsync(requestedAccount.Id, count: 2);

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
        await context.Wishes.SaveBatchAsync(
        [
            new WishRecord(account.Id, "1788931200000208082", "鸦羽弓", 3, sameTime),
            new WishRecord(account.Id, "1788931200000207182", "飞天御剑", 3, sameTime),
            new WishRecord(account.Id, "1788931200000207982", "黑缨枪", 3, sameTime)
        ]);

        IReadOnlyList<WishRecord> newest =
            await context.Wishes.GetRecentAsync(account.Id, 20);
        IReadOnlyList<WishRecord> oldest = await context.Wishes.QueryAsync(
            new WishRecordQuery(
                [account.Id],
                SortOrder: WishRecordSortOrder.OldestFirst));

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
            () => context.Wishes.GetRecentAsync(Guid.NewGuid(), count));
    }

    [Fact]
    public async Task SaveBatchAsync_WhenOneRecordViolatesForeignKey_RollsBackWholeBatch()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        GameAccount validAccount = await AddAccountAsync(context);
        WishRecord valid = CreateWish(validAccount.Id, "valid", minute: 30);
        WishRecord invalid = CreateWish(Guid.NewGuid(), "invalid", minute: 29);

        SQLiteException error = await Assert.ThrowsAsync<SQLiteException>(
            () => context.Wishes.SaveBatchAsync([valid, invalid]));

        Assert.Equal(SQLite3.Result.Constraint, error.Result);
        Assert.Empty(await context.Wishes.GetRecentAsync(validAccount.Id, 20));
    }

    [Fact]
    public async Task GetRecentAsync_CancelledToken_ThrowsOperationCancelledException()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => context.Wishes.GetRecentAsync(
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
        await context.Wishes.SaveBatchAsync(
        [
            CreateWish(first.Id, "event-1", 1) with
            {
                RankType = 5,
                UigfGachaType = "301"
            },
            CreateWish(second.Id, "event-2", 2) with
            {
                RankType = 5,
                GachaType = "400",
                UigfGachaType = null
            },
            CreateWish(first.Id, "weapon", 3) with
            {
                RankType = 5,
                UigfGachaType = "302"
            },
            CreateWish(second.Id, "four-star", 4) with
            {
                RankType = 4,
                UigfGachaType = "301"
            }
        ]);
        var query = new WishRecordQuery(
            [first.Id, second.Id],
            RankTypes: new HashSet<int> { 5 },
            PoolGroups: new HashSet<WishPoolGroup>
            {
                WishPoolGroup.CharacterEvent
            },
            StartTime: start,
            EndTime: start.AddHours(1),
            SortOrder: WishRecordSortOrder.OldestFirst);

        IReadOnlyList<WishRecord> result =
            await context.Wishes.QueryAsync(query);
        int count = await context.Wishes.CountAsync(query);

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

    private static WishRecord CreateWish(
        Guid accountId,
        string externalId,
        int minute)
    {
        return new WishRecord(
            accountId,
            externalId,
            "芙宁娜",
            5,
            new DateTimeOffset(2026, 7, 16, 18, minute, 0, TimeSpan.FromHours(8)));
    }
}
