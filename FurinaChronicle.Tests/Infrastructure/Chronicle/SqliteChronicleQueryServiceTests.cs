// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.Chronicle;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.History;
using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Infrastructure.Chronicle;
using FurinaChronicle.Services.Chronicle;
using FurinaChronicle.Services.Gacha.History;
using FurinaChronicle.Services.HistoryDomains;
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

    [Fact]
    public async Task UndoInvalidatesPublishedGeneration()
    {
        await using SqliteRepositoryTestContext context =
            SqliteRepositoryTestContext.Create();
        (PlayerArchive archive, GameAccount account) = await Parents(context);
        await Commit(context, account.Id, "1", Time(10));
        await Commit(context, account.Id, "2", Time(20));
        var service = Service(context);
        ChronicleScope scope = new(ChronicleScopeKind.Account, account.Id);
        await service.RebuildAsync(scope, Catalog());
        ChronicleQuery query = Query(scope, 1);
        ChroniclePage first = await service.QueryAsync(query);

        GachaAtomicChangeResult undo = await context.AtomicGacha.UndoLatestAsync(
            new GachaUndoRequest(OperationId.New(), archive.Id, Time(21), Time(21)));

        Assert.Equal(ChangeExecutionStatus.Applied, undo.Status);
        ChroniclePage continued = await service.QueryAsync(
            query with { Cursor = first.NextCursor });
        Assert.Equal(ChronicleQueryError.CursorInvalidated, continued.Error);
    }

    [Fact]
    public async Task IrreversibleDeleteClearsConcreteProjectionWithoutAffectingOtherArchive()
    {
        await using SqliteRepositoryTestContext context =
            SqliteRepositoryTestContext.Create();
        (PlayerArchive firstArchive, GameAccount firstAccount) = await Parents(context);
        PlayerArchive secondArchive = SqliteRepositoryTestContext.CreateArchive("Second");
        GameAccount secondAccount = SqliteRepositoryTestContext.CreateAccount(
            secondArchive.Id, uid: "800000002");
        await context.Archives.AddAsync(secondArchive);
        await context.Accounts.AddAsync(secondAccount);
        await Commit(context, firstAccount.Id, "1", Time(10));
        await Commit(context, firstAccount.Id, "2", Time(20));
        await Commit(context, secondAccount.Id, "a", Time(10));
        await Commit(context, secondAccount.Id, "b", Time(20));
        var service = Service(context);
        ChronicleScope firstScope = new(ChronicleScopeKind.Account, firstAccount.Id);
        ChronicleScope secondScope = new(ChronicleScopeKind.Account, secondAccount.Id);
        await service.RebuildAsync(firstScope, Catalog());
        await service.RebuildAsync(secondScope, Catalog());
        ChronicleQuery firstQuery = Query(firstScope, 1);
        ChronicleQuery secondQuery = Query(secondScope, 1);
        ChroniclePage firstPage = await service.QueryAsync(firstQuery);
        ChroniclePage secondPage = await service.QueryAsync(secondQuery);
        GachaFactState current = Assert.IsType<GachaFactState>(
            await context.AtomicGacha.GetCurrentAsync(
                new GachaFactReference(firstAccount.Id, "1")));

        GachaAtomicChangeResult deleted = await context.AtomicGacha.IrreversiblyDeleteAsync(
            new GachaIrreversibleDeleteRequest(OperationId.New(),
                new GachaFactReference(firstAccount.Id, "1"),
                current.Version, Time(30), Time(30)));

        Assert.Equal(ChangeExecutionStatus.Applied, deleted.Status);
        Assert.Equal(ChronicleQueryError.CursorInvalidated,
            (await service.QueryAsync(firstQuery with { Cursor = firstPage.NextCursor })).Error);
        Assert.Equal(ChronicleQueryError.None,
            (await service.QueryAsync(secondQuery with { Cursor = secondPage.NextCursor })).Error);
        using var raw = context.OpenRawConnection();
        Assert.Equal(0, raw.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM ChronicleEntries WHERE GenerationId = ?;",
            firstPage.GenerationId));
        Assert.True(raw.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM ChronicleEntries WHERE GenerationId = ?;",
            secondPage.GenerationId) > 0);
    }

    [Fact]
    public async Task ActualDomainStoresProduceProfilesAndReadableReport()
    {
        await using SqliteRepositoryTestContext context =
            SqliteRepositoryTestContext.Create();
        (_, GameAccount account) = await Parents(context);
        await Commit(context, account.Id, "1", Time(10));
        RecordProvenance observed = new(DataOrigin.OfficialApi,
            new RecordTimestamps(ObservedAt: Time(20), FetchedAt: Time(21)));
        ItemStateObservation first = ItemObservation(account.Id, 1, 20, observed);
        ItemStateObservation second = ItemObservation(account.Id, 2, 30,
            new RecordProvenance(DataOrigin.OfficialApi,
                new RecordTimestamps(ObservedAt: Time(30), FetchedAt: Time(31))));
        Assert.Equal(ChangeExecutionStatus.Applied,
            (await context.AtomicGacha.CommitItemObservationsAsync(
                new ItemObservationCommitRequest(OperationId.New(),
                    DataChangeOperationKind.Refresh, DataOrigin.OfficialApi,
                    "synthetic item observations", Time(31), Time(31),
                    [first, second]))).Status);
        ChallengeObservation challenge = new(
            ChallengeObservationId.New(), account.Id, ChallengeMode.SpiralAbyss,
            "synthetic-season", null, null, DataCompleteness.Partial,
            [new SpiralAbyssFloorResult(12, DataCompleteness.Partial,
                [new SpiralAbyssChamberResult(3, 0,
                    OccurrenceTime.At(Time(40), TimePrecision.Second))])],
            new RecordProvenance(DataOrigin.OfficialApi,
                new RecordTimestamps(ObservedAt: Time(41))));
        Assert.Equal(ChangeExecutionStatus.Applied,
            (await context.AtomicGacha.CommitChallengeObservationsAsync(
                new ChallengeObservationCommitRequest(OperationId.New(),
                    DataChangeOperationKind.Refresh, DataOrigin.OfficialApi,
                    "synthetic abyss", Time(41), Time(41), [challenge]))).Status);

        var service = Service(context);
        ChronicleScope scope = new(ChronicleScopeKind.Account, account.Id);
        await service.RebuildAsync(scope, Catalog());
        ChroniclePage challengePage = await service.QueryAsync(new ChronicleQuery(
            scope, new ChronicleProfileRequest(ChronicleTimeline.Challenge),
            new ChronicleFilter(IncludeUndated: true), 50));
        Assert.Contains(challengePage.Entries, item =>
            item.Kind == ChronicleEntryKind.ChallengeCompleted);
        ChroniclePage statePage = await service.QueryAsync(new ChronicleQuery(
            scope,
            new ChronicleProfileRequest(ChronicleTimeline.Character,
                ChronicleGranularity.Detailed, IncludeStateChanges: true),
            new ChronicleFilter(IncludeUndated: true), 50));
        Assert.Contains(statePage.Entries, item =>
            item.Kind == ChronicleEntryKind.StateChange);

        using var writer = new StringWriter();
        await service.ExportMarkdownAsync(Query(scope, 50), writer);
        Assert.Contains("chronicle.gacha.acquisition", writer.ToString());
        Assert.Contains("chronicle.challenge.completed", writer.ToString());
        Assert.Contains("Projection rule: 1", writer.ToString());
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

    private static ItemStateObservation ItemObservation(
        Guid accountId,
        int suffix,
        int level,
        RecordProvenance provenance) => new(
            new ItemObservationId(Guid.Parse($"00000000-0000-4000-8000-{suffix:D12}")),
            accountId, InventoryItemKind.Character, DataCompleteness.Partial,
            [new InventoryStateEntry(InventoryItemKind.Character,
                "synthetic-five", null, 1, level, null,
                InventoryFieldCoverage.Quantity | InventoryFieldCoverage.Level,
                DataCompleteness.Partial)], provenance);

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
