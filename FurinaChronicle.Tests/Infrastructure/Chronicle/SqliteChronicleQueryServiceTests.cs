// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Chronicle;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.History;
using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Infrastructure.Chronicle;
using FurinaChronicle.Services.Chronicle;
using FurinaChronicle.Services.Gacha.History;
using FurinaChronicle.Tests.Infrastructure.Persistence.Sqlite;

namespace FurinaChronicle.Tests.Infrastructure.Chronicle;

public sealed class SqliteChronicleQueryServiceTests
{
    [Fact]
    public async Task InsertKeepsOldCursorStableButNewQuerySeesNewGeneration()
    {
        await using SqliteRepositoryTestContext context =
            SqliteRepositoryTestContext.Create();
        (PlayerArchive archive, GameAccount account) = await Parents(context);
        await Commit(context, account.Id, "1", Time(10));
        await Commit(context, account.Id, "2", Time(20));
        var service = Service(context);
        ChronicleScope scope = new(ChronicleScopeKind.Account, account.Id);
        await service.RebuildAsync(scope, Catalog());
        ChronicleQuery query = Query(scope, pageSize: 1);

        ChroniclePage first = await service.QueryAsync(query);
        Assert.NotNull(first.NextCursor);
        await Commit(context, account.Id, "0", Time(5));

        ChroniclePage continued = await service.QueryAsync(
            query with { Cursor = first.NextCursor });
        Assert.Equal(ChronicleQueryError.None, continued.Error);
        Assert.Equal("1", Assert.Single(continued.Entries).SourceEntityId);

        ChroniclePage rebuilt = await service.QueryAsync(query with { PageSize = 10 });
        Assert.Equal(3, rebuilt.Entries.Count);
        Assert.NotEqual(first.GenerationId, rebuilt.GenerationId);
    }

    [Fact]
    public async Task CorrectionInvalidatesCursorAndClearsConcreteCacheRows()
    {
        await using SqliteRepositoryTestContext context =
            SqliteRepositoryTestContext.Create();
        (_, GameAccount account) = await Parents(context);
        await Commit(context, account.Id, "1", Time(10));
        await Commit(context, account.Id, "2", Time(20));
        var service = Service(context);
        ChronicleScope scope = new(ChronicleScopeKind.Account, account.Id);
        await service.RebuildAsync(scope, Catalog());
        ChronicleQuery query = Query(scope, 1);
        ChroniclePage first = await service.QueryAsync(query);

        await Commit(context, account.Id, "1", Time(11),
            DataChangeOperationKind.Correction, expectedVersion: 1);

        ChroniclePage continued = await service.QueryAsync(
            query with { Cursor = first.NextCursor });
        Assert.Equal(ChronicleQueryError.CursorInvalidated, continued.Error);
        using var raw = context.OpenRawConnection();
        Assert.Equal(0, raw.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM ChronicleEntries WHERE GenerationId = ?;",
            first.GenerationId));
    }

    [Fact]
    public async Task CursorCannotCrossFilterAndCacheClearExpiresIt()
    {
        await using SqliteRepositoryTestContext context =
            SqliteRepositoryTestContext.Create();
        (_, GameAccount account) = await Parents(context);
        await Commit(context, account.Id, "1", Time(10));
        await Commit(context, account.Id, "2", Time(20));
        var service = Service(context);
        ChronicleScope scope = new(ChronicleScopeKind.Account, account.Id);
        await service.RebuildAsync(scope, Catalog());
        ChronicleQuery query = Query(scope, 1);
        ChroniclePage first = await service.QueryAsync(query);

        ChroniclePage mismatch = await service.QueryAsync(query with
        {
            Cursor = first.NextCursor,
            Filter = new ChronicleFilter(Ranks: new HashSet<int> { 5 }),
        });
        Assert.Equal(ChronicleQueryError.InvalidCursor, mismatch.Error);

        await service.ClearCacheAsync(scope);
        ChroniclePage expired = await service.QueryAsync(
            query with { Cursor = first.NextCursor });
        Assert.Equal(ChronicleQueryError.CursorExpired, expired.Error);
    }

    private static SqliteChronicleQueryService Service(
        SqliteRepositoryTestContext context) => new(
            context.Database, context.Gacha, context.Accounts,
            context.AtomicGacha);

    private static ChronicleQuery Query(ChronicleScope scope, int pageSize) => new(
        scope,
        new ChronicleProfileRequest(ChronicleTimeline.Main),
        new ChronicleFilter(IncludeUndated: true),
        pageSize);

    private static async Task<(PlayerArchive, GameAccount)> Parents(
        SqliteRepositoryTestContext context)
    {
        PlayerArchive archive = SqliteRepositoryTestContext.CreateArchive();
        GameAccount account = SqliteRepositoryTestContext.CreateAccount(archive.Id);
        await context.Archives.AddAsync(archive);
        await context.Accounts.AddAsync(account);
        return (archive, account);
    }

    private static async Task Commit(
        SqliteRepositoryTestContext context,
        Guid accountId,
        string externalId,
        DateTimeOffset time,
        DataChangeOperationKind kind = DataChangeOperationKind.Import,
        long? expectedVersion = null)
    {
        var record = new GachaRecord(accountId, externalId, "Synthetic", 5, time)
        {
            ItemId = "synthetic-five",
            ItemType = "Character",
            UigfGachaType = "301",
            Provenance = new RecordProvenance(DataOrigin.StandardImport),
        };
        var mutation = new GachaFactMutation(
            new GachaFactReference(accountId, externalId),
            record,
            expectedVersion is null ? null : new FactVersion(expectedVersion.Value));
        var request = new GachaAtomicChangeRequest(
            OperationId.New(), kind, DataOrigin.StandardImport,
            "synthetic chronicle test", time, time, [mutation], captureUndo: true);
        GachaAtomicChangeResult result = await context.AtomicGacha.CommitAsync(request);
        Assert.Equal(ChangeExecutionStatus.Applied, result.Status);
    }

    private static IChronicleItemCatalogSnapshot Catalog() => new CatalogSnapshot();

    private sealed class CatalogSnapshot : IChronicleItemCatalogSnapshot
    {
        public string Version => "synthetic-v1";
        public ChronicleItemDescriptor? Find(string itemId) =>
            itemId == "synthetic-five"
                ? new(itemId, InventoryItemKind.Character, 5)
                : null;
    }

    private static DateTimeOffset Time(int second) =>
        new(2026, 1, 1, 0, 0, second, TimeSpan.FromHours(8));
}
