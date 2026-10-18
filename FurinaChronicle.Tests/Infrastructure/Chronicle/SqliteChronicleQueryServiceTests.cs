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
using FurinaChronicle.Infrastructure.Gacha.Metadata;
using FurinaChronicle.Infrastructure.Gacha.Metadata.Persistence;
using FurinaChronicle.Services.Chronicle;
using FurinaChronicle.Services.Gacha.History;
using FurinaChronicle.Services.HistoryDomains;
using FurinaChronicle.Tests.Infrastructure.Persistence.Sqlite;

namespace FurinaChronicle.Tests.Infrastructure.Chronicle;

public sealed class SqliteChronicleQueryServiceTests
{
    [Fact]
    [Trait("Category", "Stress")]
    public async Task SyntheticHundredThousandGacha_RebuildsAndPages()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("FURINA_RUN_CHRONICLE_STRESS"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        await using SqliteRepositoryTestContext context =
            SqliteRepositoryTestContext.Create();
        (_, GameAccount account) = await Parents(context);
        await context.Database.InitializeAsync();
        using (var raw = context.OpenRawConnection())
        {
            raw.RunInTransaction(() =>
            {
                for (int index = 0; index < 100_000; index++)
                {
                    bool five = index % 100 == 0;
                    DateTimeOffset time = Time(0).AddSeconds(index);
                    raw.Execute(
                        """
                        INSERT INTO GachaRecords
                            (GameAccountId, ExternalRecordId, ItemName, ItemId,
                             ItemType, GachaType, UigfGachaType, RankType, Count,
                             TimeUtcTicks, TimeOffsetMinutes, Origin, Version)
                        VALUES (?, ?, ?, ?, ?, ?, ?, ?, 1, ?, ?, ?, 1);
                        """,
                        account.Id.ToString("D"), index.ToString("D12"),
                        five ? "Synthetic Five" : "Synthetic Three",
                        five ? "synthetic-five" : "synthetic-three",
                        five ? "Character" : "Weapon", "301", "301",
                        five ? 5 : 3, time.UtcTicks,
                        (int)time.Offset.TotalMinutes, (int)DataOrigin.StandardImport);
                }
            });
        }

        var service = Service(context);
        ChronicleScope scope = new(ChronicleScopeKind.Account, account.Id);
        await service.RebuildAsync(scope, Catalog());
        ChroniclePage first = await service.QueryAsync(Query(scope, 500));

        Assert.Equal(500, first.Entries.Count);
        Assert.NotNull(first.NextCursor);
        using var verification = context.OpenRawConnection();
        Assert.Equal(1_001, verification.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM ChronicleEntries WHERE GenerationId = ?;",
            first.GenerationId));
    }

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

    [Fact]
    public async Task CancelledRebuildPublishesNoPartialGeneration()
    {
        await using SqliteRepositoryTestContext context =
            SqliteRepositoryTestContext.Create();
        (_, GameAccount account) = await Parents(context);
        await Commit(context, account.Id, "1", Time(10));
        var service = Service(context);
        ChronicleScope scope = new(ChronicleScopeKind.Account, account.Id);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.RebuildAsync(scope, Catalog(), cancellation.Token));

        using var raw = context.OpenRawConnection();
        Assert.Equal(0, raw.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM ChronicleGenerations;"));
        Assert.Equal(0, raw.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM ChronicleEntries;"));
    }

    [Fact]
    public async Task ArchiveAccountSelectionUsesDistinctGenerationIdentity()
    {
        await using SqliteRepositoryTestContext context =
            SqliteRepositoryTestContext.Create();
        (PlayerArchive archive, GameAccount first) = await Parents(context);
        GameAccount second = SqliteRepositoryTestContext.CreateAccount(
            archive.Id, uid: "800000002");
        await context.Accounts.AddAsync(second);
        await Commit(context, first.Id, "first", Time(10));
        await Commit(context, second.Id, "second", Time(20));
        var service = Service(context);
        ChronicleScope selected = new(ChronicleScopeKind.Archive, archive.Id,
            new HashSet<Guid> { first.Id });
        ChronicleScope all = new(ChronicleScopeKind.Archive, archive.Id);
        await service.RebuildAsync(selected, Catalog());

        ChroniclePage selectedPage = await service.QueryAsync(Query(selected, 50));
        ChroniclePage allPage = await service.QueryAsync(Query(all, 50));

        Assert.Single(selectedPage.Entries);
        Assert.Equal(2, allPage.Entries.Count);
        Assert.NotEqual(selectedPage.GenerationId, allPage.GenerationId);
    }

    [Fact]
    public async Task AutomaticRebuildUsesLocalMetadataWithoutRemoteRefresh()
    {
        await using SqliteRepositoryTestContext context =
            SqliteRepositoryTestContext.Create();
        (_, GameAccount account) = await Parents(context);
        await Commit(context, account.Id, "1", Time(10),
            itemType: "角色");
        string directory = Path.Combine(Path.GetTempPath(),
            "FurinaChronicleChronicleMetadataTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await using var metadata = new GachaMetadataDatabase(
                new GachaMetadataOptions(Path.Combine(directory, "metadata.db3")));
            await metadata.InitializeAsync();
            await metadata.Connection.ExecuteAsync(
                """
                INSERT INTO GachaMetadataItems
                    (GameId, ItemId, ItemType, RankType, IconUrl)
                VALUES (?, ?, ?, ?, NULL);
                """,
                (int)GachaGame.GenshinImpact, "synthetic-five", "Character", 5);
            var service = new SqliteChronicleQueryService(
                context.Database, context.Gacha, context.Accounts,
                context.AtomicGacha, metadata);
            ChronicleScope scope = new(ChronicleScopeKind.Account, account.Id);

            ChroniclePage page = await service.QueryAsync(new ChronicleQuery(
                scope, new ChronicleProfileRequest(ChronicleTimeline.Character),
                new ChronicleFilter(IncludeUndated: true), 50));

            ChronicleEntry entry = Assert.Single(page.Entries);
            Assert.Equal("synthetic-five", entry.ItemId);
            Assert.True(entry.Timelines.HasFlag(ChronicleTimeline.Character));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
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
        long? expectedVersion = null,
        string itemType = "Character")
    {
        var record = new GachaRecord(accountId, externalId, "Synthetic", 5, time)
        {
            ItemId = "synthetic-five",
            ItemType = itemType,
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
