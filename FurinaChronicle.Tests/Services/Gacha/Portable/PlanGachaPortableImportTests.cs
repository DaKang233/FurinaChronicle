// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Infrastructure.Persistence;
using FurinaChronicle.Services.Gacha.Portable;
using FurinaChronicle.Tests.TestDoubles;

namespace FurinaChronicle.Tests.Services.Gacha.Portable;

public sealed class PlanGachaPortableImportTests
{
    [Fact]
    public async Task ExecuteAsync_ExactArchivePlansAliasAddSkipAndConflictWithoutWriting()
    {
        var archives = new InMemoryPlayerArchiveRepository();
        var accounts = new InMemoryGameAccountRepository();
        var records = new InMemoryGachaRecordRepository([]);
        PlayerArchive archive = ArchiveTestData.Archive("Archive");
        await archives.AddAsync(archive);

        GameRoleNaturalIdentity naturalIdentity =
            GenshinGameRoleIdentity.Create(
                "100000001",
                GameServerRegion.ChinaOfficial);
        var targetIdentity = new GameRoleIdentity(
            new GameRoleIdentityId(
                Guid.Parse("10000000-0000-0000-0000-000000000001")),
            naturalIdentity);
        GameAccount targetAccount = ArchiveTestData.Account(
            archive.Id,
            "100000001",
            GameServerRegion.ChinaOfficial,
            roleIdentity: targetIdentity);
        await accounts.AddAsync(targetAccount);
        GachaRecord unchanged = CreateRecord(targetAccount.Id, "1", "1001");
        GachaRecord conflicting = CreateRecord(targetAccount.Id, "2", "old");
        await records.SaveBatchAsync([unchanged, conflicting]);

        GachaPortablePackage package = CreatePackage(
            "Archive",
            naturalIdentity,
            new GameRoleIdentityId(
                Guid.Parse("20000000-0000-0000-0000-000000000001")),
            [
                unchanged with { GameAccountId = SourceAccountId },
                conflicting with
                {
                    GameAccountId = SourceAccountId,
                    ItemId = "new",
                },
                CreateRecord(SourceAccountId, "3", "1003"),
            ]);
        var service = new PlanGachaPortableImport(
            archives,
            accounts,
            records,
            new FixedTimeProvider());

        GachaPortableImportPlan plan = await service.ExecuteAsync(
            new GachaPortableImportPlanRequest(package));

        Assert.Equal(
            GachaPortableArchivePlanKind.MapExisting,
            plan.Archive.Kind);
        GachaPortableAccountImportPlan accountPlan =
            Assert.Single(plan.Accounts);
        Assert.Equal(targetAccount.Id, accountPlan.TargetAccountId);
        Assert.Equal(
            GachaPortableIdentityPlanKind.AliasSourceToTarget,
            accountPlan.Identity.Kind);
        Assert.Equal(1, accountPlan.AddCount);
        Assert.Equal(1, accountPlan.SkipCount);
        Assert.Equal(1, accountPlan.ConflictCount);
        Assert.Contains(
            "item_id",
            Assert.Single(accountPlan.Conflicts).DifferentFields);
        Assert.Equal(DataOrigin.FurinaImport, plan.ImportContext.Origin);
        Assert.Equal(1, plan.Preview.AliasCount);
        Assert.False(plan.CanApply);

        Assert.Single(await archives.GetAllAsync());
        Assert.Single(await accounts.GetByArchiveIdAsync(archive.Id));
        Assert.Equal(2, await records.CountAsync(targetAccount.Id));
    }

    [Fact]
    public async Task ExecuteAsync_WhitespaceDifferenceCreatesArchiveAndOnlySuggestsSimilar()
    {
        var archives = new InMemoryPlayerArchiveRepository();
        var accounts = new InMemoryGameAccountRepository();
        var records = new InMemoryGachaRecordRepository([]);
        PlayerArchive existing = ArchiveTestData.Archive("Archive");
        await archives.AddAsync(existing);
        GameRoleNaturalIdentity naturalIdentity =
            GenshinGameRoleIdentity.Create(
                "100000001",
                GameServerRegion.ChinaOfficial);
        GachaPortablePackage package = CreatePackage(
            " Archive ",
            naturalIdentity,
            GameRoleIdentityId.FromNaturalIdentity(naturalIdentity),
            [CreateRecord(SourceAccountId, "1", "1001")]);

        GachaPortableImportPlan plan = await new PlanGachaPortableImport(
            archives,
            accounts,
            records,
            new FixedTimeProvider()).ExecuteAsync(
                new GachaPortableImportPlanRequest(package));

        Assert.Equal(
            GachaPortableArchivePlanKind.CreateNew,
            plan.Archive.Kind);
        Assert.Equal(" Archive ", plan.Archive.ProposedName);
        GachaPortableArchiveCandidate candidate =
            Assert.Single(plan.Archive.Candidates);
        Assert.False(candidate.IsExactMatch);
        Assert.Equal(existing.Id, candidate.ArchiveId);
        GachaPortableAccountImportPlan account = Assert.Single(plan.Accounts);
        Assert.Null(account.TargetAccountId);
        Assert.NotNull(account.ProposedAccountId);
        Assert.Equal(
            GachaPortableIdentityPlanKind.PreserveSource,
            account.Identity.Kind);
        Assert.Equal(1, account.AddCount);
        Assert.True(plan.CanApply);
    }

    [Fact]
    public async Task ExecuteAsync_MultipleExactArchivesRequiresExplicitSelection()
    {
        var archives = new InMemoryPlayerArchiveRepository();
        await archives.AddAsync(ArchiveTestData.Archive("Duplicate"));
        await archives.AddAsync(ArchiveTestData.Archive("Duplicate"));
        GameRoleNaturalIdentity naturalIdentity =
            GenshinGameRoleIdentity.Create(
                "100000001",
                GameServerRegion.ChinaOfficial);
        GachaPortablePackage package = CreatePackage(
            "Duplicate",
            naturalIdentity,
            GameRoleIdentityId.FromNaturalIdentity(naturalIdentity),
            []);

        GachaPortableImportPlan plan = await new PlanGachaPortableImport(
            archives,
            new InMemoryGameAccountRepository(),
            new InMemoryGachaRecordRepository([]),
            new FixedTimeProvider()).ExecuteAsync(
                new GachaPortableImportPlanRequest(
                    package,
                    RequireUniqueArchiveNames: false));

        Assert.Equal(
            GachaPortableArchivePlanKind.RequiresSelection,
            plan.Archive.Kind);
        Assert.Equal(2, plan.Archive.Candidates.Count);
        Assert.Empty(plan.Accounts);
        Assert.False(plan.CanApply);
    }

    [Fact]
    public async Task ExecuteAsync_SourceGuidBoundToDifferentIdentityReportsCollision()
    {
        var archives = new InMemoryPlayerArchiveRepository();
        var accounts = new InMemoryGameAccountRepository();
        PlayerArchive archive = ArchiveTestData.Archive("Archive");
        await archives.AddAsync(archive);
        GameRoleIdentityId sharedId = new(
            Guid.Parse("30000000-0000-0000-0000-000000000001"));
        GameRoleNaturalIdentity existingNatural =
            GenshinGameRoleIdentity.Create(
                "100000002",
                GameServerRegion.ChinaOfficial);
        await accounts.AddAsync(ArchiveTestData.Account(
            archive.Id,
            "100000002",
            GameServerRegion.ChinaOfficial,
            roleIdentity: new GameRoleIdentity(sharedId, existingNatural)));
        GameRoleNaturalIdentity sourceNatural =
            GenshinGameRoleIdentity.Create(
                "100000001",
                GameServerRegion.ChinaOfficial);
        GachaPortablePackage package = CreatePackage(
            "Archive",
            sourceNatural,
            sharedId,
            [CreateRecord(SourceAccountId, "1", "1001")]);

        GachaPortableImportPlan plan = await new PlanGachaPortableImport(
            archives,
            accounts,
            new InMemoryGachaRecordRepository([]),
            new FixedTimeProvider()).ExecuteAsync(
                new GachaPortableImportPlanRequest(package));

        GachaPortableAccountImportPlan account = Assert.Single(plan.Accounts);
        Assert.Equal(
            GachaPortableIdentityPlanKind.Collision,
            account.Identity.Kind);
        Assert.False(plan.CanApply);
    }

    private static readonly Guid SourceAccountId = Guid.Parse(
        "90000000-0000-0000-0000-000000000001");

    private static GachaPortablePackage CreatePackage(
        string archiveName,
        GameRoleNaturalIdentity naturalIdentity,
        GameRoleIdentityId identityId,
        IReadOnlyList<GachaRecord> records)
    {
        return new GachaPortablePackage(
            new DateTimeOffset(
                2026,
                10,
                6,
                0,
                0,
                0,
                TimeSpan.Zero),
            new GachaPortableArchive(Guid.NewGuid(), archiveName),
            [
                new GachaPortableAccount(
                    SourceAccountId,
                    new GameRoleIdentity(identityId, naturalIdentity),
                    "Source Account",
                    records),
            ]);
    }

    private static GachaRecord CreateRecord(
        Guid accountId,
        string externalId,
        string itemId)
    {
        return new GachaRecord(
            accountId,
            externalId,
            "Item",
            5,
            new DateTimeOffset(
                2026,
                1,
                1,
                0,
                0,
                0,
                TimeSpan.FromHours(8)))
        {
            ItemId = itemId,
            ItemType = "Character",
            GachaType = "301",
            UigfGachaType = "301",
            Provenance = new RecordProvenance(DataOrigin.OfficialApi),
        };
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(
            2026,
            10,
            6,
            1,
            2,
            3,
            TimeSpan.Zero);
    }
}
