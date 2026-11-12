// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Net;
using System.Net.Http.Headers;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.History;
using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Core.Passport;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Infrastructure.HistoryDomains.Collecting;
using FurinaChronicle.Infrastructure.Persistence.Sqlite;
using FurinaChronicle.Services.History;
using FurinaChronicle.Services.HistoryDomains.Collecting;
using FurinaChronicle.Services.Passport;

namespace FurinaChronicle.Tests.Infrastructure.Persistence.Sqlite;

public sealed class CharacterWeaponCollectionTests
{
    private const string RosterBody = """
        {
          "retcode": 0,
          "message": "OK",
          "data": {
            "list": [
              {
                "id": 10000093,
                "level": 90,
                "actived_constellation_num": 0,
                "weapon": { "id": 11501, "level": 90, "affix_level": 5 }
              },
              {
                "id": 10000103,
                "level": 80,
                "actived_constellation_num": 2,
                "weapon": { "id": 11501, "level": 80, "affix_level": 1 }
              },
              {
                "id": 10000106,
                "level": 90,
                "actived_constellation_num": 1,
                "weapon": { "id": 14501, "level": 90, "affix_level": 5 }
              }
            ]
          }
        }
        """;

    [Fact]
    public async Task SyntheticHttp_StoresCharacterAndAggregatedWeaponObservations()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        GameAccount target = await AddAccountAsync(context, "target");
        TestSource source = new TestSource(RosterBody);
        CollectCharacterWeapon collect = CreateCollector(context, source, target);
        PassportAccount passport = CreatePassport();

        Assert.Equal(
            ChangeExecutionStatus.Applied,
            (await collect.ExecuteAsync(target.PlayerArchiveId, target.Id, passport))
                .Commit?.Status);
        Assert.Equal(
            ChangeExecutionStatus.NoOp,
            (await collect.ExecuteAsync(target.PlayerArchiveId, target.Id, passport))
                .Commit?.Status);
        source.Body = RosterBody.Replace("\"level\": 80", "\"level\": 81", StringComparison.Ordinal);
        Assert.Equal(
            ChangeExecutionStatus.Applied,
            (await collect.ExecuteAsync(target.PlayerArchiveId, target.Id, passport))
                .Commit?.Status);

        await using var reopened = new FurinaDatabase(
            new SqliteDatabaseOptions(context.DatabasePath));
        var store = new SqliteGachaAtomicChangeStore(reopened);
        var observations = await store.GetItemObservationsAsync(target.Id);
        Assert.Equal(3, observations.Count);

        ItemStateObservation characters = observations
            .Where(observation => observation.Kind == InventoryItemKind.Character)
            .OrderByDescending(observation =>
                observation.Provenance.Timestamps.ObservedAt)
            .First();
        Assert.Equal(DataCompleteness.Complete, characters.CollectionCompleteness);
        Assert.Equal(3, characters.Entries.Count);
        Assert.Equal(
            81,
            Assert.Single(
                characters.Entries,
                entry => entry.TypeId == "10000103").Level);
        InventoryStateEntry banner = Assert.Single(
            characters.Entries,
            entry => entry.TypeId == "10000093");
        Assert.Equal(1, banner.Quantity);
        Assert.Equal(90, banner.Level);
        Assert.Equal(0, banner.Progression);
        Assert.Equal(
            InventoryFieldCoverage.Level | InventoryFieldCoverage.Progression,
            banner.FieldCoverage);

        ItemStateObservation weapons = Assert.Single(
            observations,
            observation => observation.Kind == InventoryItemKind.Weapon);
        Assert.Equal(DataCompleteness.Partial, weapons.CollectionCompleteness);
        Assert.Equal(2, weapons.Entries.Count);
        InventoryStateEntry duplicated = Assert.Single(
            weapons.Entries,
            entry => entry.TypeId == "11501");
        Assert.Equal(2, duplicated.Quantity);
        Assert.Null(duplicated.Level);
        Assert.Null(duplicated.Progression);
        Assert.Equal(InventoryFieldCoverage.Quantity, duplicated.FieldCoverage);
        InventoryStateEntry single = Assert.Single(
            weapons.Entries,
            entry => entry.TypeId == "14501");
        Assert.Equal(1, single.Quantity);
        Assert.Equal(90, single.Level);
        Assert.Equal(DataCompleteness.Complete, single.FieldCompleteness);
        Assert.All(
            observations,
            observation =>
            {
                Assert.Equal(target.Id, observation.GameAccountId);
                Assert.Equal(DataOrigin.OfficialApi, observation.Provenance.Origin);
                Assert.Equal("miyoushe", observation.Provenance.Source?.Provider);
                Assert.Equal(
                    observation.Provenance.Timestamps.ObservedAt,
                    observation.Provenance.Timestamps.FetchedAt);
            });
    }

    [Fact]
    public async Task RoleMismatchStopsBeforeRequest()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        GameAccount target = await AddAccountAsync(context, "target");
        TestSource source = new TestSource(RosterBody);
        CollectCharacterWeapon collect = CreateCollector(
            context, source, target, region: "cn_qd01");

        Assert.Equal(
            CharacterWeaponCollectionStatus.RoleNotBound,
            (await collect.ExecuteAsync(
                target.PlayerArchiveId, target.Id, CreatePassport())).Status);
        Assert.Equal(0, source.RequestCount);
        Assert.Empty(await context.AtomicGacha.GetItemObservationsAsync(target.Id));
    }

    [Fact]
    public async Task RiskAndEmptyBodiesNeverWriteObservations()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        GameAccount target = await AddAccountAsync(context, "target");
        TestSource risk = new TestSource("""{"retcode":5003}""");
        CollectCharacterWeapon collect = CreateCollector(context, risk, target);

        Assert.Equal(
            CharacterWeaponCollectionStatus.AccessDenied,
            (await collect.ExecuteAsync(
                target.PlayerArchiveId, target.Id, CreatePassport())).Status);
        Assert.Empty(await context.AtomicGacha.GetItemObservationsAsync(target.Id));

        TestSource empty = new TestSource(
            """{"retcode":0,"data":{"list":[]}}""");
        collect = CreateCollector(context, empty, target);
        Assert.Equal(
            CharacterWeaponCollectionStatus.Empty,
            (await collect.ExecuteAsync(
                target.PlayerArchiveId, target.Id, CreatePassport())).Status);
        Assert.Empty(await context.AtomicGacha.GetItemObservationsAsync(target.Id));
    }

    [Fact]
    public async Task MalformedAndOversizedBodiesAreRejected()
    {
        var reader = new MiYouSheCharacterWeaponResponseReader(TimeProvider.System);
        using var malformed = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"retcode":0,"data":{"list":[{"id":0}]}}"""),
        };
        Assert.Equal(
            CharacterWeaponCollectionStatus.InvalidResponse,
            (await reader.ReadAsync(malformed)).Status);
        using var oversized = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(new string(' ', 2 * 1024 * 1024 + 1)),
        };
        Assert.Equal(
            CharacterWeaponCollectionStatus.InvalidResponse,
            (await reader.ReadAsync(oversized)).Status);
        using var rateLimited = new HttpResponseMessage(
            HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("{}"),
        };
        rateLimited.Headers.RetryAfter = new RetryConditionHeaderValue(
            TimeSpan.FromSeconds(15));
        CharacterWeaponSourceResult limited = await reader.ReadAsync(rateLimited);
        Assert.Equal(CharacterWeaponCollectionStatus.RateLimited, limited.Status);
        Assert.Equal(TimeSpan.FromSeconds(15), limited.RetryAfter);
    }

    private static CollectCharacterWeapon CreateCollector(
        SqliteRepositoryTestContext context,
        TestSource source,
        GameAccount target,
        string region = "cn_gf01") =>
        new(
            context.Accounts,
            new TestProfile(new PassportGameRole(
                "hk4e_cn", region, target.Uid, "fixture", 60, "fixture")),
            source,
            context.AtomicGacha,
            source.Time);

    private static async Task<GameAccount> AddAccountAsync(
        SqliteRepositoryTestContext context,
        string name)
    {
        PlayerArchive archive = SqliteRepositoryTestContext.CreateArchive(name);
        await context.Archives.AddAsync(archive);
        GameAccount account = SqliteRepositoryTestContext.CreateAccount(
            archive.Id, "100000001", GameServerRegion.ChinaOfficial);
        await context.Accounts.AddAsync(account);
        return account;
    }

    private static PassportAccount CreatePassport() =>
        new(
            Guid.NewGuid(), "fixture", null, null,
            PassportLoginMethod.ManualCookie,
            new PassportCredentials(
                null, "fixture", "fixture", null, null, null, null),
            PassportDeviceIdentity.Create(),
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch);

    private sealed class TestProfile(PassportGameRole role)
        : IMiHoYoAccountProfileClient
    {
        public Task<PassportAccountProfile> GetAsync(
            PassportAccount account,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PassportAccountProfile("fixture", null, [role]));
    }

    private sealed class TestSource(string body) : ICharacterWeaponSource
    {
        public string Body { get; set; } = body;
        public TestTime Time { get; } = new();
        public int RequestCount { get; private set; }

        public Task<CharacterWeaponSourceResult> FetchAsync(
            PassportAccount passport,
            GameRoleNaturalIdentity role,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestCount++;
            Time.Now = Time.Now.AddSeconds(1);
            using var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Body),
            };
            return new MiYouSheCharacterWeaponResponseReader(Time)
                .ReadAsync(response, cancellationToken);
        }
    }

    private sealed class TestTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } =
            DateTimeOffset.FromUnixTimeSeconds(1794100000);

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
