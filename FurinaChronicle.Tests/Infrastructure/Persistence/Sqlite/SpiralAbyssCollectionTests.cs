// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Net;
using System.Net.Http.Headers;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.History;
using FurinaChronicle.Core.Passport;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Infrastructure.HistoryDomains.Collecting;
using FurinaChronicle.Infrastructure.Persistence.Sqlite;
using FurinaChronicle.Services.History;
using FurinaChronicle.Services.HistoryDomains.Collecting;
using FurinaChronicle.Services.Passport;

namespace FurinaChronicle.Tests.Infrastructure.Persistence.Sqlite;

public sealed class SpiralAbyssCollectionTests
{
    [Fact]
    public async Task SyntheticHttp_StoresScopedHistoryAndPreservesReturnToEarlierContent()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        GameAccount target = await AddAccountAsync(context, "target");
        GameAccount other = await AddAccountAsync(context, "other");
        using var source = new TestSource();
        var collect = CreateCollector(context, source, target);
        PassportAccount passport = CreatePassport();

        Assert.Equal(ChangeExecutionStatus.Applied,
            (await collect.ExecuteAsync(target.PlayerArchiveId, target.Id, passport)).Commit?.Status);
        DateTimeOffset firstFetch = source.Time.Now;

        source.Time.Now = firstFetch.AddMinutes(1);
        Assert.Equal(ChangeExecutionStatus.NoOp,
            (await collect.ExecuteAsync(target.PlayerArchiveId, target.Id, passport)).Commit?.Status);

        source.Time.Now = firstFetch.AddMinutes(3);
        source.Stars = 3;
        Assert.Equal(ChangeExecutionStatus.Applied,
            (await collect.ExecuteAsync(target.PlayerArchiveId, target.Id, passport)).Commit?.Status);

        source.Stars = 2;
        Assert.Equal(ChangeExecutionStatus.Conflict,
            (await collect.ExecuteAsync(target.PlayerArchiveId, target.Id, passport)).Commit?.Status);

        source.Time.Now = firstFetch.AddMinutes(4);
        Assert.Equal(ChangeExecutionStatus.Applied,
            (await collect.ExecuteAsync(target.PlayerArchiveId, target.Id, passport)).Commit?.Status);

        await using var reopened = new FurinaDatabase(new SqliteDatabaseOptions(context.DatabasePath));
        var store = new SqliteGachaAtomicChangeStore(reopened);
        var observations = (await store.GetChallengeObservationsAsync(target.Id))
            .OrderBy(row => row.Provenance.Timestamps.ObservedAt).ToArray();

        Assert.Equal([2, 3, 2], observations.Select(row => row.SpiralAbyssFloors.Single().Stars));
        Assert.Empty(await store.GetChallengeObservationsAsync(other.Id));
        Assert.All(observations, row =>
        {
            Assert.Equal("901", row.SeasonId);
            Assert.Equal(DataOrigin.OfficialApi, row.Provenance.Origin);
            Assert.Equal("miyoushe", row.Provenance.Source?.Provider);
            Assert.Null(row.Provenance.Source?.SourceRecordId);
            Assert.NotNull(row.Provenance.AcquisitionBatchId);
            Assert.Equal(row.Provenance.Timestamps.ObservedAt, row.Provenance.Timestamps.FetchedAt);
            Assert.Null(row.Provenance.Timestamps.ImportedAt);
            Assert.Equal(DataCompleteness.Partial, row.Completeness);
            Assert.Equal(OccurrenceTime.Unknown, row.SpiralAbyssFloors.Single().Chambers.Single().CompletedAt);
        });
        Assert.Equal(firstFetch, observations[0].Provenance.Timestamps.FetchedAt);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1794000000), observations[0].PeriodStart);
        Assert.Equal(3, observations.Select(row => row.Provenance.AcquisitionBatchId).Distinct().Count());
    }

    [Fact]
    public async Task RoleAndArchiveMismatchStopBeforeRequest()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        GameAccount target = await AddAccountAsync(context, "target");
        using var source = new TestSource();
        var collect = CreateCollector(context, source, target, "cn_qd01");

        Assert.Equal(SpiralAbyssCollectionStatus.TargetUnavailable,
            (await collect.ExecuteAsync(Guid.NewGuid(), target.Id, CreatePassport())).Status);
        Assert.Equal(SpiralAbyssCollectionStatus.RoleNotBound,
            (await collect.ExecuteAsync(target.PlayerArchiveId, target.Id, CreatePassport())).Status);
        Assert.Equal(0, source.RequestCount);
        Assert.Empty(await context.AtomicGacha.GetChallengeObservationsAsync(target.Id));
    }

    [Fact]
    public async Task AccountDeletedDuringResponseIsRejectedInsideCommit()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        GameAccount target = await AddAccountAsync(context, "target");
        using var source = new TestSource();
        source.BeforeResponse = async () =>
        {
            IArchiveOperationHistoryStore history = context.AtomicGacha;
            var purged = await history.PurgeAccountAsync(new ArchiveScopePurgeRequest(
                OperationId.New(), target.Id, source.Time.Now, source.Time.Now, "test purge"));
            Assert.Equal(ChangeExecutionStatus.Applied, purged.Status);
        };

        var collect = CreateCollector(context, source, target);
        var result = await collect.ExecuteAsync(target.PlayerArchiveId, target.Id, CreatePassport());

        Assert.Equal(ChangeExecutionStatus.Conflict, result.Commit?.Status);
        Assert.Null(await context.Accounts.GetByIdAsync(target.Id));
        Assert.Empty(await context.AtomicGacha.GetChallengeObservationsAsync(target.Id));
    }

    [Fact]
    public async Task NewObservationIdDoesNotBypassPurgeInSameArchive()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        GameAccount target = await AddAccountAsync(context, "target");
        GameAccount other = await AddAccountAsync(context, "other");
        using var source = new TestSource();
        var collect = CreateCollector(context, source, target);
        PassportAccount passport = CreatePassport();
        Assert.Equal(ChangeExecutionStatus.Applied,
            (await collect.ExecuteAsync(target.PlayerArchiveId, target.Id, passport)).Commit?.Status);
        IArchiveOperationHistoryStore history = context.AtomicGacha;
        Assert.Equal(ChangeExecutionStatus.Applied, (await history.PurgeAccountAsync(
            new ArchiveScopePurgeRequest(
                OperationId.New(), target.Id, source.Time.Now, source.Time.Now, "test purge"))).Status);

        GameAccount replacement = target with { Id = Guid.NewGuid() };
        await context.Accounts.AddAsync(replacement);
        source.Time.Now = source.Time.Now.AddMinutes(1);

        Assert.Equal(ChangeExecutionStatus.Conflict,
            (await collect.ExecuteAsync(replacement.PlayerArchiveId, replacement.Id, passport)).Commit?.Status);
        Assert.Empty(await context.AtomicGacha.GetChallengeObservationsAsync(replacement.Id));
        Assert.Equal(ChangeExecutionStatus.Applied,
            (await collect.ExecuteAsync(other.PlayerArchiveId, other.Id, passport)).Commit?.Status);
    }

    [Fact]
    public async Task ZeroStarSlotDoesNotBecomeACompletedChamberWithoutEvidence()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        GameAccount target = await AddAccountAsync(context, "target");
        using var source = new TestSource { Stars = 0 };
        var collect = CreateCollector(context, source, target);

        var result = await collect.ExecuteAsync(target.PlayerArchiveId, target.Id, CreatePassport());

        Assert.Equal(ChangeExecutionStatus.Applied, result.Commit?.Status);
        ChallengeObservation observation = Assert.Single(
            await context.AtomicGacha.GetChallengeObservationsAsync(target.Id));
        Assert.Empty(Assert.Single(observation.SpiralAbyssFloors).Chambers);
        Assert.Equal(DataCompleteness.Partial, observation.Completeness);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, "{\"retcode\":0,\"data\":{\"schedule_id\":901,\"floors\":[]}}",
        SpiralAbyssCollectionStatus.Empty)]
    [InlineData(HttpStatusCode.OK, "{\"retcode\":0,\"data\":{\"floors\":[]}}",
        SpiralAbyssCollectionStatus.InvalidResponse)]
    [InlineData(HttpStatusCode.OK, "{\"retcode\":123,\"message\":\"ignored\"}",
        SpiralAbyssCollectionStatus.SourceRejected)]
    [InlineData(HttpStatusCode.OK, "{", SpiralAbyssCollectionStatus.InvalidResponse)]
    [InlineData(HttpStatusCode.OK, "{\"retcode\":0,\"data\":{\"uid\":100000002,\"schedule_id\":901,\"floors\":[]}}",
        SpiralAbyssCollectionStatus.ResponseRoleMismatch)]
    [InlineData(HttpStatusCode.OK, """{"retcode":5003,"data":null}""",
        SpiralAbyssCollectionStatus.AccessDenied)]
    [InlineData(HttpStatusCode.OK, """{"retcode":0,"data":{"is_unlock":false}}""",
        SpiralAbyssCollectionStatus.NotUnlocked)]
    [InlineData(HttpStatusCode.Unauthorized, "", SpiralAbyssCollectionStatus.AuthenticationRequired)]
    [InlineData(HttpStatusCode.Forbidden, "", SpiralAbyssCollectionStatus.AccessDenied)]
    [InlineData(HttpStatusCode.TooManyRequests, "", SpiralAbyssCollectionStatus.RateLimited)]
    [InlineData(HttpStatusCode.ServiceUnavailable, "", SpiralAbyssCollectionStatus.Unavailable)]
    public async Task EmptyAndFailedResponsesDoNotWrite(
        HttpStatusCode httpStatus, string content, SpiralAbyssCollectionStatus expected)
    {
        await using var context = SqliteRepositoryTestContext.Create();
        GameAccount target = await AddAccountAsync(context, "target");
        using var source = new TestSource { Status = httpStatus, Body = content };
        var collect = CreateCollector(context, source, target);

        var result = await collect.ExecuteAsync(target.PlayerArchiveId, target.Id, CreatePassport());

        Assert.Equal(expected, result.Status);
        Assert.Null(result.Commit);
        if (httpStatus == HttpStatusCode.TooManyRequests)
        {
            Assert.Equal(TimeSpan.FromSeconds(25), result.RetryAfter);
        }
        Assert.Empty(await context.AtomicGacha.GetChallengeObservationsAsync(target.Id));
    }

    [Fact]
    public async Task ReaderRejectsOversizeAndHonorsCancellation()
    {
        var expectedRole = new GameRoleNaturalIdentity("hk4e_cn", "cn_gf01", "100000001");
        var reader = new MiYouSheSpiralAbyssResponseReader(TimeProvider.System);
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(new string(' ', 2 * 1024 * 1024 + 1)),
        };
        Assert.Equal(SpiralAbyssCollectionStatus.InvalidResponse,
            (await reader.ReadAsync(response, expectedRole)).Status);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => reader.ReadAsync(response, expectedRole, new CancellationToken(canceled: true)));
    }

    private static CollectSpiralAbyss CreateCollector(
        SqliteRepositoryTestContext context, TestSource source, GameAccount target,
        string region = "cn_gf01") =>
        new(context.Accounts,
            new TestProfile(new PassportGameRole("hk4e_cn", region, target.Uid, "fixture", 60, "fixture")),
            source, context.AtomicGacha, source.Time);

    private static async Task<GameAccount> AddAccountAsync(SqliteRepositoryTestContext context, string name)
    {
        PlayerArchive archive = SqliteRepositoryTestContext.CreateArchive(name);
        await context.Archives.AddAsync(archive);
        GameAccount account = SqliteRepositoryTestContext.CreateAccount(
            archive.Id, "100000001", GameServerRegion.ChinaOfficial);
        await context.Accounts.AddAsync(account);
        return account;
    }

    private static PassportAccount CreatePassport() =>
        new(Guid.NewGuid(), "fixture", null, null, PassportLoginMethod.ManualCookie,
            new PassportCredentials(null, null, "fixture", null, null, null, null),
            PassportDeviceIdentity.Create(), DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    private sealed class TestProfile(PassportGameRole role) : IMiHoYoAccountProfileClient
    {
        public Task<PassportAccountProfile> GetAsync(
            PassportAccount account, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PassportAccountProfile("fixture", null, [role]));
    }

    private sealed class TestTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.FromUnixTimeSeconds(1794100000);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class TestSource : HttpMessageHandler, ISpiralAbyssSource
    {
        private readonly HttpClient client;

        public TestSource() => client = new HttpClient(this, disposeHandler: false);

        public TestTime Time { get; } = new();
        public int Stars { get; set; } = 2;
        public int RequestCount { get; private set; }
        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
        public string? Body { get; init; }
        public Func<Task>? BeforeResponse { get; set; }

        public async Task<SpiralAbyssSourceResult> FetchCurrentAsync(
            PassportAccount passport, GameRoleNaturalIdentity role, CancellationToken cancellationToken)
        {
            using HttpResponseMessage response = await client.GetAsync(
                "https://fixture.invalid/current-season", HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            return await new MiYouSheSpiralAbyssResponseReader(Time).ReadAsync(response, role, cancellationToken);
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestCount++;
            if (BeforeResponse is not null)
            {
                await BeforeResponse();
            }
            var response = new HttpResponseMessage(Status)
            {
                Content = new StringContent(Body ?? $$"""
                    {
                        "retcode": 0,
                        "data": {
                            "schedule_id": 901,
                            "start_time": "1794000000",
                            "end_time": 1795000000,
                            "floors": [
                                { "index": 12, "levels": [ { "index": 3, "star": {{Stars}} } ] }
                            ]
                        }
                    }
                    """),
            };
            if (Status == HttpStatusCode.TooManyRequests)
            {
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(25));
            }
            return response;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                client.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
