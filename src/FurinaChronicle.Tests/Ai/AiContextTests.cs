// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Text.Json;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Services.HistoryDomains;
using FurinaChronicle.Core.Chronicle;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.Gacha.Metadata;
using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Core.Metadata;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Core.History;
using FurinaChronicle.Services.Abstractions;
using FurinaChronicle.Services.Ai;
using FurinaChronicle.Services.Chronicle;
using FurinaChronicle.Services.Gacha.Abstractions;
using FurinaChronicle.Services.Gacha.Analytics;
using FurinaChronicle.Services.Gacha.History;
using FurinaChronicle.Tests.Infrastructure.Persistence.Sqlite;

namespace FurinaChronicle.Tests.Ai;

public sealed class AiContextTests
{
    [Fact]
    public async Task SingleAccountOmitsUidNamesRawReferencesAndOtherArchiveDataAndMatchesExistingStatistics()
    {
        await using var db = SqliteRepositoryTestContext.Create();
        var (archive, first, _, outside) = await Seed(db);
        var context = await Builder(db).ExecuteAsync(new(archive.Id, first.Id));
        string json = context.Json;
        Assert.False(context.IncludesUids);
        foreach (string value in new[] { first.Uid, outside.Id.ToString(), archive.Id.ToString(), first.Id.ToString(), "PRIVATE_ARCHIVE", "PRIVATE_NICKNAME", "PRIVATE_EXTERNAL", "PRIVATE_PROVIDER", "PRIVATE_ITEM_NAME", "10000089" })
            Assert.DoesNotContain(value, json);
        var pool = Assert.Single(context.Packet.GachaStatistics);
        Assert.Equal(5, pool.Pulls); Assert.Equal(2, pool.FiveStars); Assert.Equal(1, pool.FourStars);
        Assert.Equal(2, pool.MeanFiveStarInterval);
        Assert.NotEmpty(context.Evidence);
        var detail = await new ReadAiEvidence(db.Gacha, db.AtomicGacha).ExecuteAsync(context.Evidence[0]);
        Assert.Contains("仍在本地", detail.Availability);
        using var raw = db.OpenRawConnection();
        Assert.Equal(7, raw.ExecuteScalar<int>("SELECT COUNT(*) FROM GachaRecords"));
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ArchiveUidPolicySortsNumericallyAndAnonymousQuestionsUseTheSameAliases(bool allowUids)
    {
        await using var db = SqliteRepositoryTestContext.Create();
        var (archive, first, second, _) = await Seed(db);
        var context = await Builder(db).ExecuteAsync(new(archive.Id, AllowArchiveUids: allowUids));
        Assert.Equal(allowUids, context.IncludesUids);
        Assert.StartsWith("账号 01", context.Packet.Accounts[0]); Assert.StartsWith("账号 02", context.Packet.Accounts[1]);
        Assert.Equal(second.Id, context.Evidence.First(item => item.Fact.Account.StartsWith("账号 01", StringComparison.Ordinal)).AccountId);
        if (allowUids) { Assert.Contains(first.Uid, context.Json); Assert.Contains(second.Uid, context.Json); }
        else { Assert.DoesNotContain(first.Uid, context.Json); Assert.DoesNotContain(second.Uid, context.Json); }
        Assert.Equal("对比 账号 01 和 账号 02", context.SanitizeText($"对比 {second.Uid} 和 {first.Uid}"));
        Assert.Equal(6, context.Packet.GachaStatistics.Sum(item => item.Pulls));
    }
    [Fact]
    public async Task ForeignAccountCannotBeSelectedThroughAnotherArchive()
    {
        await using var db = SqliteRepositoryTestContext.Create();
        var (archive, _, _, outside) = await Seed(db);
        await Assert.ThrowsAsync<AiAnalysisException>(() => Builder(db).ExecuteAsync(new(archive.Id, outside.Id)));
    }
    [Fact]
    public async Task TimeRangeRestrictsFactsAndStatisticsWithoutSubstitutingObservationForOccurrence()
    {
        await using var db = SqliteRepositoryTestContext.Create();
        var (archive, first, _, _) = await Seed(db);
        var context = await Builder(db).ExecuteAsync(new(archive.Id, first.Id, new(2026, 11, 12), new(2026, 11, 12)));
        Assert.Empty(context.Packet.GachaStatistics); Assert.Empty(context.Evidence);
    }
    [Fact]
    public async Task ObservationsUseDeclaredCoverageUnknownOccurrenceAndOnlyTrustedPublicTeamNames()
    {
        await using var db = SqliteRepositoryTestContext.Create();
        var (archive, first, _, _) = await Seed(db);
        var time = new DateTimeOffset(2026, 11, 15, 1, 0, 0, TimeSpan.Zero);
        var provenance = new RecordProvenance(DataOrigin.OfficialApi, new RecordTimestamps(ObservedAt: time, FetchedAt: time.AddSeconds(1)));
        var snapshots = new[] { 40, 90 }.Select((level, index) => new ItemStateObservation(ItemObservationId.New(), first.Id,
            InventoryItemKind.Character, DataCompleteness.Partial,
            [new(InventoryItemKind.Character, "10000089", null, 1, level, 6, InventoryFieldCoverage.Level, DataCompleteness.Partial)],
            new RecordProvenance(DataOrigin.OfficialApi, new RecordTimestamps(ObservedAt: time.AddDays(-index), FetchedAt: time)))).ToArray();
        await db.AtomicGacha.CommitItemObservationsAsync(new(OperationId.New(), DataChangeOperationKind.Refresh, DataOrigin.OfficialApi,
            "synthetic", time, time, snapshots, captureUndo: false));
        var challenge = new ChallengeObservation(ChallengeObservationId.New(), first.Id, ChallengeMode.ImaginariumTheater,
            "PRIVATE_SEASON", null, null, DataCompleteness.Partial, [], provenance,
            [new("PRIVATE_STAGE", 1, OccurrenceTime.Unknown,
                [new("10000089", "PRIVATE_PARTICIPANT", "https://private.example/icon", 90, 5, ChallengeParticipantOrigin.Trial, 2)],
                ["PRIVATE_BUFF"])], 3, 7, theaterStatistics: new(maxRoundId: 10, totalUseTime: 900));
        await db.AtomicGacha.CommitChallengeObservationsAsync(new(OperationId.New(), DataChangeOperationKind.Refresh, DataOrigin.OfficialApi,
            "synthetic", time, time, [challenge], captureUndo: false));
        var result = await Builder(db).ExecuteAsync(new(archive.Id, first.Id, Datasets: AiDatasets.Items | AiDatasets.Challenges));
        var items = result.Evidence.Where(e => e.Fact.Dataset == "inventory").ToArray();
        Assert.Equal(2, items.Length);
        Assert.Contains(items, item => item.Fact.Description.Contains("等级 40", StringComparison.Ordinal));
        Assert.Contains(items, item => item.Fact.Description.Contains("等级 90", StringComparison.Ordinal));
        Assert.All(items, item => { Assert.Null(item.Fact.OccurredDate); Assert.Contains("命座 未覆盖", item.Fact.Description); Assert.Contains("Partial", item.Fact.Coverage); });
        var fact = Assert.Single(result.Evidence, e => e.Fact.Dataset == "challenge").Fact;
        Assert.Null(fact.OccurredDate); Assert.Equal("2026-11-15", fact.ObservedDate);
        Assert.Contains("合成公共物品", fact.Description); Assert.Contains("Trial", fact.Description); Assert.Contains("900", fact.Description);
        foreach (string secret in new[] { "PRIVATE_SEASON", "PRIVATE_STAGE", "PRIVATE_PARTICIPANT", "PRIVATE_BUFF", "private.example", first.Uid, first.Id.ToString() })
            Assert.DoesNotContain(secret, result.Json);
        var reference = Assert.Single(result.Evidence, e => e.Fact.Dataset == "challenge");
        var detail = await new ReadAiEvidence(db.Gacha, db.AtomicGacha).ExecuteAsync(reference);
        Assert.Contains("已定位本地", detail.Availability);
    }
    [Fact]
    public async Task EvidenceBudgetDoesNotPretendItContainsAllRecords()
    {
        await using var db = SqliteRepositoryTestContext.Create();
        var (archive, first, _, _) = await Seed(db);
        var context = await Builder(db).ExecuteAsync(new(archive.Id, first.Id, MaxEvidence: 12), 4000);
        Assert.True(context.Json.Length <= 4000);
        Assert.Contains(context.Packet.Coverage, note => note.Contains("示例", StringComparison.Ordinal));
        Assert.Equal(context.Evidence.Select(e => e.Fact), context.Packet.Evidence);
    }
    private static BuildAiContext Builder(SqliteRepositoryTestContext db)
    {
        var metadata = new Names();
        return new(db.Archives, db.Accounts, db.Gacha, new BuildGachaAnalytics(db.Gacha, metadata), db.AtomicGacha, metadata, new Chronicle());
    }
    private static async Task<(PlayerArchive, GameAccount, GameAccount, GameAccount)> Seed(SqliteRepositoryTestContext db)
    {
        var archive = SqliteRepositoryTestContext.CreateArchive("PRIVATE_ARCHIVE");
        var other = SqliteRepositoryTestContext.CreateArchive("OTHER_PRIVATE_ARCHIVE");
        await db.Archives.AddAsync(archive); await db.Archives.AddAsync(other);
        var first = SqliteRepositoryTestContext.CreateAccount(archive.Id, "200000001", GameServerRegion.ChinaOfficial, "PRIVATE_NICKNAME");
        var second = SqliteRepositoryTestContext.CreateAccount(archive.Id, "100000001", GameServerRegion.ChinaOfficial);
        var outside = SqliteRepositoryTestContext.CreateAccount(other.Id, "100000001", GameServerRegion.ChinaOfficial);
        await db.Accounts.AddAsync(first); await db.Accounts.AddAsync(second); await db.Accounts.AddAsync(outside);
        var time = new DateTimeOffset(2026, 11, 10, 0, 0, 0, TimeSpan.Zero);
        var records = new List<GachaRecord>();
        int[] ranks = [5,3,5,4,3];
        for (int i = 0; i < ranks.Length; i++) records.Add(new(first.Id, "PRIVATE_EXTERNAL_" + i, "PRIVATE_ITEM_NAME", ranks[i], time.AddMinutes(i))
        { ItemId = ranks[i] == 5 ? "10000089" : ranks[i] == 4 ? "10000087" : "11301", GachaType = "301", UigfGachaType = "301",
            Provenance = new(DataOrigin.StandardImport, source: new DataSourceReference("PRIVATE_PROVIDER")) });
        records.Add(new(second.Id, "PRIVATE_EXTERNAL_SECOND", "private", 5, time) { ItemId = "10000089", GachaType = "301" });
        records.Add(new(outside.Id, "PRIVATE_EXTERNAL_OUTSIDE", "private", 5, time) { ItemId = "10000089", GachaType = "301" });
        var result = await db.AtomicGacha.CommitAsync(new(OperationId.New(), DataChangeOperationKind.Import, DataOrigin.StandardImport,
            "synthetic AI input", time, time, records.Select(item => new GachaFactMutation(new(item.GameAccountId, item.ExternalRecordId), item)), captureUndo: false));
        Assert.Equal(ChangeExecutionStatus.Applied, result.Status);
        return (archive, first, second, outside);
    }
    private sealed class Names : IDomainMetadataService, IGachaItemMetadataProvider
    {
        public ValueTask<DomainItemMetadata?> ResolveItemAsync(InventoryItemKind kind, string typeId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<DomainItemMetadata?>(new(kind, typeId, "合成公共物品", 5, null, "Snap.Metadata"));
        public ValueTask<ChallengeScheduleMetadata?> ResolveScheduleAsync(FurinaChronicle.Core.Challenges.ChallengeMode mode, string seasonId, CancellationToken cancellationToken = default) => ValueTask.FromResult<ChallengeScheduleMetadata?>(null);
        public ValueTask<GachaItemMetadata?> FindByIdAsync(GachaGame game, string itemId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<GachaItemMetadata?>(new(game, itemId, "合成公共物品", itemId.StartsWith("100", StringComparison.Ordinal) ? "角色" : "武器", itemId == "11301" ? 3 : itemId == "10000087" ? 4 : 5));
    }
    private sealed class Chronicle : IChronicleQueryService
    {
        public Task<ChroniclePage> QueryAsync(ChronicleQuery query, CancellationToken cancellationToken = default) => Task.FromResult(new ChroniclePage([], null, "synthetic"));
        public Task RebuildAsync(ChronicleScope scope, IChronicleItemCatalogSnapshot itemCatalog, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ClearCacheAsync(ChronicleScope scope, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ExportMarkdownAsync(ChronicleQuery query, TextWriter destination, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
