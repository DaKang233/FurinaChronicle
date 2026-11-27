// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.History;
using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Infrastructure.Archives.Portable;
using FurinaChronicle.Infrastructure.Persistence.Sqlite;
using FurinaChronicle.Services.Archives.Portable;
using FurinaChronicle.Services.History;
using FurinaChronicle.Tests.Infrastructure.Persistence.Sqlite;

namespace FurinaChronicle.Tests.Services.Archives.Portable;

public sealed partial class PlanArchiveRestoreTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CharacterDetailArchiveRetainsSnapshotAndDetectsMetadataOnlyConflicts(bool includeEnhancements)
    {
        await using var context = SqliteRepositoryTestContext.Create();
        string root = CreateRestoreSnapshotRoot();
        try
        {
            var archive = SqliteRepositoryTestContext.CreateArchive("Main", TargetArchiveId);
            await context.Archives.AddAsync(archive);
            await context.Accounts.AddAsync(SqliteRepositoryTestContext.CreateAccount(
                archive.Id, Role.NaturalIdentity.Uid, GameServerRegion.ChinaOfficial,
                "Traveler", TargetAccountId, roleIdentity: Role));
            var source = SourceData("Main", []);
            var retrievedAt = new DateTimeOffset(2026, 11, 27, 3, 0, 0, TimeSpan.FromHours(8));
            var property = new CharacterPropertyMetadata(20, "暴击率", "https://example.test/stat.png", null);
            var stamina = new CharacterPropertyMetadata(2000, "生命值上限", null, null);
            var metadata = new CharacterResponseMetadata(
                new Dictionary<string, CharacterPropertyMetadata> { ["20"] = property, ["2000"] = stamina },
                null, null, null, null);
            var detail = new CharacterDetail(
                Name: "合成角色", Element: "Hydro", Rarity: 5, Friendship: 10, Ascension: 6,
                Icon: null, SideIcon: null, Portrait: null, CardImage: null, IsFeatured: null, WeaponType: 1,
                Weapon: new(11501, "合成武器", null, 1, null, 5, 90, 3, 6, null,
                    new CharacterStat(4, null, null, "542"), null),
                Constellations: [new(1, 1, "合成命座", null, false, "{LINK#N1}电涌{/LINK}")],
                Talents: [new(2, 1, 10, "合成天赋", null, true, "<b>技能</b>", [new("伤害", "124.5%")],
                    includeEnhancements ? new(true, true, "强化") : null, null, includeEnhancements ? [0] : null)],
                SelectedStats: null, BaseStats: [new(2000, "10000", "2000", "12000")],
                AdvancedStats: [new(20, null, null, "35.5%")], ElementalStats: null,
                Artifacts: [new(15001, 1, "花", "合成圣遗物", null, 5, 20, null, null,
                    [new(20, "10.5%", 3), new(22, "7.8%", 0)])],
                Costumes: null, ArtifactRecommendation: null, IsTpsUnlocked: null, WeaponSkin: null);
            var expected = new ItemStateObservation(
                ItemObservationId.New(), SourceAccountId, InventoryItemKind.Character, DataCompleteness.Complete,
                [new(InventoryItemKind.Character, "10000089", null, 1, 90, 0,
                    InventoryFieldCoverage.Level | InventoryFieldCoverage.Progression,
                    DataCompleteness.Complete, detail)],
                new RecordProvenance(DataOrigin.OfficialApi,
                    new RecordTimestamps(ObservedAt: retrievedAt, FetchedAt: retrievedAt),
                    new DataSourceReference("miyoushe"), AcquisitionBatchId.New()),
                metadata);
            source.Items[SourceAccountId] = [expected];

            using var package = new MemoryStream();
            await new ArchivePackageWriter().WriteAsync(package, source,
                new ArchiveGeneratorInfo("FurinaChronicle", "0.10.0", "10.0", "fixture", "test"));
            package.Position = 0;
            await using var staged = await new SqliteArchiveRestoreInputStager(
                new ArchivePackageReader(), new ArchiveRestoreStorageOptions(root)).StageAsync(package);
            var descriptor = Assert.Single(staged.Summary.Manifest.Datasets,
                dataset => dataset.Kind == "character-weapon-history");
            Assert.Equal(includeEnhancements ? "1.6" : "1.5", descriptor.FormatVersion);
            Assert.True(descriptor.Required);
            var planner = new PlanArchiveRestore(CreateTargetFactory(context, root), TimeProvider.System);
            var plan = await planner.PlanAsync(new ArchiveRestorePlanRequest(staged));
            Assert.True(plan.CanApply);
            var applied = await context.AtomicGacha.ApplyAsync(
                new ArchiveRestoreApplyRequest(staged, plan, OperationId.New(), retrievedAt, retrievedAt));
            Assert.Equal(ChangeExecutionStatus.Applied, applied.Status);

            await using var reopened = new FurinaDatabase(new SqliteDatabaseOptions(context.DatabasePath));
            var store = new SqliteGachaAtomicChangeStore(reopened);
            var actual = Assert.Single(await store.GetItemObservationsAsync(TargetAccountId));
            Assert.Equal(TargetAccountId, actual.GameAccountId);
            Assert.Equal(ArchiveRestoreSemanticDigest.ForItemObservation(expected),
                ArchiveRestoreSemanticDigest.ForItemObservation(actual));
            Assert.Equal(property, actual.CharacterMetadata!.Properties!["20"]);
            var restored = Assert.Single(actual.Entries).CharacterDetail!;
            Assert.Equal("124.5%", Assert.Single(Assert.Single(restored.Talents!).Attributes!).Value);
            if (includeEnhancements)
                Assert.Equal("强化", Assert.Single(restored.Talents!).Enhancement?.Description);
            Assert.Equal(6, restored.Weapon!.Ascension);
            Assert.Equal(new int?[] { 3, 0 }, Assert.Single(restored.Artifacts!).SubStats!.Select(stat => stat.Hits));

            source.Items[SourceAccountId] = [ReplaceMetadata(metadata with
            {
                Properties = new Dictionary<string, CharacterPropertyMetadata> { ["2000"] = stamina, ["20"] = property },
            })];
            var repeated = await planner.PlanAsync(new ArchiveRestorePlanRequest(source));
            Assert.True(repeated.CanApply);
            Assert.Equal(ArchiveRestoreFactAction.Skip, Assert.Single(repeated.Facts).Action);

            source.Items[SourceAccountId] = [ReplaceMetadata(metadata with
            {
                Properties = new Dictionary<string, CharacterPropertyMetadata>
                {
                    ["20"] = property with { Name = "不同名称" },
                    ["2000"] = stamina,
                },
            })];
            var conflicting = await planner.PlanAsync(new ArchiveRestorePlanRequest(source));
            Assert.False(conflicting.CanApply);
            Assert.Equal(ArchiveRestoreFactAction.Conflict, Assert.Single(conflicting.Facts).Action);

            ItemStateObservation ReplaceMetadata(CharacterResponseMetadata value) => new(
                expected.ObservationId, expected.GameAccountId, expected.Kind, expected.CollectionCompleteness,
                expected.Entries, expected.Provenance, value);
        }
        finally
        {
            DeleteRestoreSnapshotRoot(root);
        }
    }
}
