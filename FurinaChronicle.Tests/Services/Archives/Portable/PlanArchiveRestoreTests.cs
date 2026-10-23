// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Services.Archives.Portable;

namespace FurinaChronicle.Tests.Services.Archives.Portable;

public sealed partial class PlanArchiveRestoreTests
{
    private static readonly Guid SourceArchiveId =
        Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid SourceAccountId =
        Guid.Parse("20000000-0000-0000-0000-000000000002");
    private static readonly Guid TargetArchiveId =
        Guid.Parse("20000000-0000-0000-0000-000000000003");
    private static readonly Guid TargetAccountId =
        Guid.Parse("20000000-0000-0000-0000-000000000004");
    private static readonly GameRoleIdentity Role =
        GenshinGameRoleIdentity.CreateIdentity(
            "100000001",
            GameServerRegion.ChinaOfficial);

    [Fact]
    public async Task ExactParentsAndOnlyCurrentAddsAreUndoEligibleAndDeterministic()
    {
        GachaRecord sameSource = Record(SourceAccountId, "1", "Furina");
        GachaRecord addSource = Record(SourceAccountId, "2", "Neuvillette");
        MemoryArchiveData source = SourceData(
            "Main",
            [sameSource, addSource]);
        MemoryArchiveData target = TargetData(
            "Main",
            [sameSource with { GameAccountId = TargetAccountId }]);
        var planner = Planner(target);

        ArchiveRestorePlan first = await planner.PlanAsync(
            new ArchiveRestorePlanRequest(source));
        ArchiveRestorePlan second = await planner.PlanAsync(
            new ArchiveRestorePlanRequest(source));

        Assert.True(first.CanApply);
        Assert.True(first.Undo.IsEligibleCandidate);
        Assert.Equal(first.PlanFingerprint, second.PlanFingerprint);
        Assert.Equal(64, first.PlanFingerprint.Length);
        Assert.Single(first.Facts, item =>
            item.Action == ArchiveRestoreFactAction.Skip);
        Assert.Single(first.Facts, item =>
            item.Action == ArchiveRestoreFactAction.Add);
        Assert.Equal(TargetArchiveId, first.Archives.Single().TargetArchiveId);
        Assert.Equal(TargetAccountId, first.Accounts.Single().TargetAccountId);
    }

    [Fact]
    public async Task SameStableIdWithDifferentPortableMeaningIsConflict()
    {
        MemoryArchiveData source = SourceData(
            "Main",
            [Record(SourceAccountId, "1", "Furina")]);
        MemoryArchiveData target = TargetData(
            "Main",
            [Record(TargetAccountId, "1", "Diluc")]);

        ArchiveRestorePlan plan = await Planner(target).PlanAsync(
            new ArchiveRestorePlanRequest(source));

        Assert.False(plan.CanApply);
        Assert.Equal(
            ArchiveRestoreFactAction.Conflict,
            plan.Facts.Single().Action);
        Assert.Contains(plan.Conflicts, item =>
            item.Code == ArchiveRestoreConflictCode.CurrentFactDifference);
    }

    [Fact]
    public async Task SimilarArchiveNameRequiresExplicitDecisionAndConfirmation()
    {
        MemoryArchiveData source = SourceData(" Main ", []);
        MemoryArchiveData target = TargetData("main", []);
        var planner = Planner(target);

        ArchiveRestorePlan unresolved = await planner.PlanAsync(
            new ArchiveRestorePlanRequest(source));

        Assert.Equal(
            ArchiveRestoreArchiveMappingKind.RequiresSelection,
            unresolved.Archives.Single().Kind);
        Assert.False(unresolved.CanApply);

        string reference = SourceArchiveId.ToString("D");
        ArchiveRestorePlan confirmed = await planner.PlanAsync(
            new ArchiveRestorePlanRequest(
                source,
                [new ArchiveRestoreArchiveDecision(
                    SourceArchiveId,
                    ArchiveRestoreArchiveDecisionKind.MapExisting,
                    TargetArchiveId)],
                Confirmations:
                [new ArchiveRestoreConfirmation(
                    ArchiveRestoreConfirmationKind.SimilarArchiveName,
                    reference)]));

        Assert.Equal(
            ArchiveRestoreArchiveMappingKind.MapExisting,
            confirmed.Archives.Single().Kind);
        Assert.DoesNotContain(confirmed.Confirmations, item => !item.Satisfied);
        Assert.True(confirmed.CanApply);
    }

    [Fact]
    public async Task SharedIdentityGuidWithDifferentNaturalIdentityBlocksPlan()
    {
        MemoryArchiveData source = SourceData("Main", []);
        GameRoleIdentity other = GenshinGameRoleIdentity.CreateIdentity(
            "100000002",
            GameServerRegion.ChinaOfficial);
        ArchiveCatalog targetCatalog = Catalog(
            TargetArchiveId,
            TargetAccountId,
            "Main",
            new ArchiveCatalogRoleIdentity(
                Role.Id.Value,
                Role.Id.Value,
                other.NaturalIdentity));
        MemoryArchiveData target = TargetData("Main", [], targetCatalog);

        ArchiveRestorePlan plan = await Planner(target).PlanAsync(
            new ArchiveRestorePlanRequest(source));

        Assert.False(plan.CanApply);
        Assert.Contains(plan.Conflicts, item =>
            item.Code == ArchiveRestoreConflictCode.IdentityCollision);
        Assert.Equal(
            ArchiveRestoreAccountMappingKind.Collision,
            plan.Accounts.Single().Kind);
    }

    [Fact]
    public async Task TombstoneRequiresExactMarkerAndNonUndoableConfirmations()
    {
        MemoryArchiveData source = SourceData(
            "Main",
            [Record(SourceAccountId, "deleted", "Furina")]);
        MemoryArchiveData target = TargetData("Main", []);
        target.Tombstones.Add(new ArchiveRestoreActiveTombstone(
            new ArchiveRestoreTombstoneProbe(
                ArchiveRestoreFactKind.GachaCurrent,
                TargetArchiveId,
                TargetAccountId,
                Role.Id.Value,
                "deleted"),
            new string('a', 64),
            7));
        var planner = Planner(target);

        ArchiveRestorePlan warning = await planner.PlanAsync(
            new ArchiveRestorePlanRequest(source));

        ArchiveRestoreTombstoneHit hit = Assert.Single(warning.Tombstones);
        Assert.Equal(
            ArchiveRestoreFactAction.Reintroduce,
            warning.Facts.Single().Action);
        Assert.False(warning.CanApply);
        string factReference = $"{SourceArchiveId:D}:{SourceAccountId:D}:" +
            $"{(int)ArchiveRestoreFactKind.GachaCurrent}:deleted";
        ArchiveRestorePlan confirmed = await planner.PlanAsync(
            new ArchiveRestorePlanRequest(
                source,
                Confirmations:
                [new ArchiveRestoreConfirmation(
                    ArchiveRestoreConfirmationKind.TombstoneReintroduction,
                    factReference,
                    hit.TombstoneKey,
                    hit.TombstoneVersion),
                 new ArchiveRestoreConfirmation(
                    ArchiveRestoreConfirmationKind.NonUndoableOperation,
                    source.PackageFingerprint)]));

        Assert.True(confirmed.Tombstones.Single().Confirmed);
        Assert.True(confirmed.CanApply);
    }

    [Fact]
    public async Task NewParentsAndAllDomainKindsProduceOneNonUndoablePlan()
    {
        MemoryArchiveData source = SourceData(
            "New",
            [Record(SourceAccountId, "1", "Furina")]);
        source.AddDomainFixture(SourceArchiveId, SourceAccountId);
        MemoryArchiveData emptyTarget = MemoryArchiveData.Target(
            new ArchiveCatalog([], []),
            new ArchivePortableSettings(true, []),
            "target-empty");
        var planner = Planner(emptyTarget);

        ArchiveRestorePlan warning = await planner.PlanAsync(
            new ArchiveRestorePlanRequest(source));

        Assert.Equal(1, warning.Preview.ArchiveCreateCount);
        Assert.Equal(1, warning.Preview.AccountCreateCount);
        Assert.Equal(6, warning.Facts.Select(item => item.Kind).Distinct().Count());
        Assert.False(warning.Undo.IsEligibleCandidate);
        Assert.False(warning.CanApply);

        ArchiveRestorePlan accepted = await planner.PlanAsync(
            new ArchiveRestorePlanRequest(
                source,
                Confirmations:
                [new ArchiveRestoreConfirmation(
                    ArchiveRestoreConfirmationKind.NonUndoableOperation,
                    source.PackageFingerprint)]));
        Assert.True(accepted.CanApply);
        Assert.Equal(
            warning.Archives.Single().ProposedArchiveId,
            accepted.Archives.Single().ProposedArchiveId);
        Assert.Equal(
            warning.Accounts.Single().ProposedAccountId,
            accepted.Accounts.Single().ProposedAccountId);
    }

    [Fact]
    public async Task UnresolvedAccountInExistingArchiveIsNeverGuessed()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-10-23T00:00:00Z");
        var unresolvedCatalog = new ArchiveCatalog(
            [],
            [new ArchiveCatalogArchive(
                SourceArchiveId,
                "Main",
                now,
                now,
                [new ArchiveCatalogAccount(
                    SourceAccountId,
                    null,
                    "Unknown",
                    "placeholder",
                    GameServerRegion.Unknown,
                    true,
                    "placeholder_account",
                    now,
                    now)])]);
        MemoryArchiveData source = MemoryArchiveData.Source(
            unresolvedCatalog,
            new ArchivePortableSettings(
                true,
                [new ArchivePortableArchiveSetting(SourceArchiveId, 3)]));
        MemoryArchiveData target = TargetData("Main", []);
        var planner = Planner(target);

        ArchiveRestorePlan unresolved = await planner.PlanAsync(
            new ArchiveRestorePlanRequest(source));

        Assert.Equal(
            ArchiveRestoreAccountMappingKind.RequiresSelection,
            unresolved.Accounts.Single().Kind);

        ArchiveRestorePlan explicitCreate = await planner.PlanAsync(
            new ArchiveRestorePlanRequest(
                source,
                AccountDecisions:
                [new ArchiveRestoreAccountDecision(
                    SourceAccountId,
                    ArchiveRestoreAccountDecisionKind.CreateNew)],
                Confirmations:
                [new ArchiveRestoreConfirmation(
                    ArchiveRestoreConfirmationKind.UnresolvedAccountMapping,
                    SourceAccountId.ToString("D")),
                 new ArchiveRestoreConfirmation(
                    ArchiveRestoreConfirmationKind.NonUndoableOperation,
                    source.PackageFingerprint)]));

        Assert.Equal(
            ArchiveRestoreAccountMappingKind.CreateNew,
            explicitCreate.Accounts.Single().Kind);
        Assert.True(explicitCreate.CanApply);
    }

    [Fact]
    public async Task FreshnessDetectsSameCountContentChangeThroughStateToken()
    {
        MemoryArchiveData source = SourceData("Main", []);
        MemoryArchiveData firstTarget = TargetData("Main", []);
        MemoryArchiveData secondTarget = TargetData("Main", []);
        secondTarget.StateToken = "target-state-2";
        var factory = new QueueTargetFactory(firstTarget, secondTarget);
        var planner = new PlanArchiveRestore(factory, TimeProvider.System);
        ArchiveRestorePlan plan = await planner.PlanAsync(
            new ArchiveRestorePlanRequest(source));

        ArchiveRestorePlanFreshness freshness =
            await planner.CheckFreshnessAsync(plan);

        Assert.False(freshness.IsCurrent);
        Assert.NotEqual(
            freshness.ExpectedTargetStateToken,
            freshness.ActualTargetStateToken);
    }

    private static PlanArchiveRestore Planner(MemoryArchiveData target) =>
        new(new QueueTargetFactory(target), TimeProvider.System);

    private static MemoryArchiveData SourceData(
        string archiveName,
        IReadOnlyList<GachaRecord> records)
    {
        ArchiveCatalog catalog = Catalog(
            SourceArchiveId,
            SourceAccountId,
            archiveName,
            new ArchiveCatalogRoleIdentity(
                Role.Id.Value,
                Role.Id.Value,
                Role.NaturalIdentity));
        MemoryArchiveData result = MemoryArchiveData.Source(
            catalog,
            new ArchivePortableSettings(
                true,
                [new ArchivePortableArchiveSetting(SourceArchiveId, 3)]));
        result.Gacha[SourceAccountId] = records;
        return result;
    }

    private static MemoryArchiveData TargetData(
        string archiveName,
        IReadOnlyList<GachaRecord> records,
        ArchiveCatalog? catalog = null)
    {
        catalog ??= Catalog(
            TargetArchiveId,
            TargetAccountId,
            archiveName,
            new ArchiveCatalogRoleIdentity(
                Role.Id.Value,
                Role.Id.Value,
                Role.NaturalIdentity));
        MemoryArchiveData result = MemoryArchiveData.Target(
            catalog,
            new ArchivePortableSettings(
                true,
                [new ArchivePortableArchiveSetting(TargetArchiveId, 3)]),
            "target-state-1");
        result.Gacha[TargetAccountId] = records;
        return result;
    }

    private static ArchiveCatalog Catalog(
        Guid archiveId,
        Guid accountId,
        string archiveName,
        ArchiveCatalogRoleIdentity role) => new(
        [role],
        [new ArchiveCatalogArchive(
            archiveId,
            archiveName,
            DateTimeOffset.Parse("2026-10-23T00:00:00Z"),
            DateTimeOffset.Parse("2026-10-23T00:00:00Z"),
            [new ArchiveCatalogAccount(
                accountId,
                role.RoleIdentityReference,
                "Traveler",
                role.NaturalIdentity.Uid,
                GameServerRegion.ChinaOfficial,
                false,
                null,
                DateTimeOffset.Parse("2026-10-23T00:00:00Z"),
                DateTimeOffset.Parse("2026-10-23T00:00:00Z"))])]);

    private static GachaRecord Record(Guid accountId, string id, string name) =>
        new(
            accountId,
            id,
            name,
            5,
            DateTimeOffset.Parse("2026-10-23T12:00:00+08:00"))
        {
            ItemId = name == "Furina" ? "10000089" : "10000087",
            ItemType = "Character",
            GachaType = "301",
            UigfGachaType = "301",
            Count = 1,
            Provenance = new RecordProvenance(
                DataOrigin.OfficialApi,
                new RecordTimestamps(
                    FetchedAt: DateTimeOffset.Parse("2026-10-23T12:01:00+08:00"))),
        };
}
