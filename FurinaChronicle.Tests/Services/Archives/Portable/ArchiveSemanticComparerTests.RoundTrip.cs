// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using FurinaChronicle.Core.History;
using FurinaChronicle.Infrastructure.Archives.Portable;
using FurinaChronicle.Infrastructure.Persistence.Sqlite;
using FurinaChronicle.Services.Archives.Portable;
using FurinaChronicle.Services.Gacha.History;
using FurinaChronicle.Tests.TestDoubles;

namespace FurinaChronicle.Tests.Services.Archives.Portable;

public sealed partial class ArchiveSemanticComparerTests
{
    [Theory]
    [InlineData("phase8c6_synthetic_unresolved")]
    [InlineData(null)]
    public async Task RepresentativeArchiveSurvivesTwoReopenedDatabaseRoundTrips(
        string? unresolvedReason)
    {
        await using MemoryArchiveRestoreDataSource source =
            Create(remapped: false, unresolvedReason: unresolvedReason);
        await VerifyTwoDatabaseRoundTripsAsync(source);
    }

    [Theory]
    [InlineData("windows")]
    [InlineData("android")]
    public async Task ActualValidationSeedsPreserveAllSemanticsAcrossTwoDatabases(
        string origin)
    {
        await using SyntheticArchiveSnapshot source =
            SyntheticArchiveSnapshot.Create(origin);
        await VerifyTwoDatabaseRoundTripsAsync(source);
    }

    private static async Task VerifyTwoDatabaseRoundTripsAsync(
        IArchiveExportSnapshot source)
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "FurinaChronicleTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            byte[] original = await WritePackageAsync(
                source,
                "windows-synthetic-origin");
            long baselineMemory = Process.GetCurrentProcess().WorkingSet64;
            var timer = Stopwatch.StartNew();

            var androidLike = new RoundTripDatabase(
                Path.Combine(root, "android-like"));
            ArchiveRestoreApplyResult firstApply =
                await androidLike.ApplyMergeAsync(original);
            ArchiveRestoreApplyResult firstNoOp =
                await androidLike.ApplyMergeAsync(original);
            byte[] androidExport = await androidLike.ExportAfterReopenAsync(
                "android-like");

            var windowsLike = new RoundTripDatabase(
                Path.Combine(root, "windows-like"));
            ArchiveRestoreApplyResult secondApply =
                await windowsLike.ApplyMergeAsync(androidExport);
            ArchiveRestoreApplyResult secondNoOp =
                await windowsLike.ApplyMergeAsync(androidExport);
            byte[] returned = await windowsLike.ExportAfterReopenAsync(
                "windows-like");
            timer.Stop();
            long sampledPeakMemory = Process.GetCurrentProcess().WorkingSet64;

            ArchiveSemanticComparison comparison =
                await ComparePackagesAsync(root, original, returned);

            Assert.True(
                firstApply.Status == ChangeExecutionStatus.Applied,
                firstApply.ConflictReason);
            Assert.True(
                firstNoOp.Status == ChangeExecutionStatus.NoOp,
                firstNoOp.ConflictReason);
            Assert.True(
                secondApply.Status == ChangeExecutionStatus.Applied,
                secondApply.ConflictReason);
            Assert.True(
                secondNoOp.Status == ChangeExecutionStatus.NoOp,
                secondNoOp.ConflictReason);
            Assert.True(
                comparison.IsEquivalent,
                string.Join(
                    " | ",
                    comparison.Differences.Select(value =>
                        $"{value.Path}:{value.ExpectedCount}->" +
                        $"{value.ActualCount}:{value.ValueDigest}")));
            Assert.NotEqual(original, returned);
            Assert.True(original.Length > 0);
            Assert.True(androidExport.Length > 0);
            Assert.True(returned.Length > 0);
            Assert.True(timer.Elapsed > TimeSpan.Zero);
            Assert.True(baselineMemory > 0);
            Assert.True(sampledPeakMemory > 0);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static async Task<byte[]> WritePackageAsync(
        IArchiveExportSnapshot source,
        string platform)
    {
        using var stream = new MemoryStream();
        await new ArchivePackageWriter().WriteAsync(
            stream,
            source,
            new ArchiveGeneratorInfo(
                "FurinaChronicle",
                "0.8.15.1",
                "8C.6",
                "0000000",
                platform));
        return stream.ToArray();
    }

    private static async Task<ArchiveSemanticComparison> ComparePackagesAsync(
        string root,
        byte[] expected,
        byte[] actual)
    {
        var stager = new SqliteArchiveRestoreInputStager(
            new ArchivePackageReader(),
            new ArchiveRestoreStorageOptions(
                Path.Combine(root, "comparison-staging")));
        await using var expectedStream = new MemoryStream(expected);
        await using var actualStream = new MemoryStream(actual);
        await using IArchiveRestoreStagedPackage expectedPackage =
            await stager.StageAsync(expectedStream);
        await using IArchiveRestoreStagedPackage actualPackage =
            await stager.StageAsync(actualStream);

        Assert.True(expectedPackage.ContainerLength > 0);
        Assert.True(expectedPackage.StagingLength > 0);
        Assert.True(expectedPackage.Summary.UncompressedLength > 0);
        Assert.True(actualPackage.ContainerLength > 0);
        Assert.True(actualPackage.StagingLength > 0);
        Assert.True(actualPackage.Summary.UncompressedLength > 0);
        return await new ArchiveSemanticComparer().CompareAsync(
            expectedPackage,
            actualPackage);
    }

    private sealed class RoundTripDatabase
    {
        private readonly string directory;
        private readonly string databasePath;
        private int operationIndex;
        private ArchiveRestoreReceipt? previousReceipt;

        public RoundTripDatabase(string directory)
        {
            this.directory = directory;
            Directory.CreateDirectory(directory);
            databasePath = Path.Combine(directory, "roundtrip.db3");
        }

        public async Task<ArchiveRestoreApplyResult> ApplyMergeAsync(
            byte[] package)
        {
            operationIndex++;
            await using var database = new FurinaDatabase(
                new SqliteDatabaseOptions(databasePath));
            await database.InitializeAsync();
            var reader = new ArchivePackageReader();
            var stager = new SqliteArchiveRestoreInputStager(
                reader,
                new ArchiveRestoreStorageOptions(
                    Path.Combine(directory, $"staging-{operationIndex}")));
            await using var stream = new MemoryStream(package);
            await using IArchiveRestoreStagedPackage staged =
                await stager.StageAsync(stream);
            ArchiveCatalog catalog = staged.Catalog;
            foreach (ArchiveCatalogAccount account in catalog.Archives
                         .SelectMany(value => value.Accounts))
            {
                await foreach (GachaRevisionItem revision in staged
                                   .ReadGachaRevisionsAsync(
                                       account.AccountReference))
                {
                    Assert.False(string.IsNullOrWhiteSpace(
                        revision.Reference.ExternalRecordId));
                }
            }
            var snapshots = new SqliteArchiveExportSnapshotFactory(
                database,
                TimeProvider.System,
                new ArchiveSnapshotStorageOptions(
                    Path.Combine(directory, $"planning-{operationIndex}")));
            var planner = new PlanArchiveRestore(
                new SqliteArchiveRestoreTargetSnapshotFactory(snapshots),
                TimeProvider.System);
            ArchiveRestoreArchiveDecision[]? archiveDecisions =
                previousReceipt?.Archives
                    .Select(value => new ArchiveRestoreArchiveDecision(
                        value.SourceArchiveReference,
                        ArchiveRestoreArchiveDecisionKind.MapExisting,
                        value.TargetArchiveId))
                    .ToArray();
            ArchiveRestoreAccountDecision[]? accountDecisions =
                previousReceipt?.Accounts
                    .Select(value => new ArchiveRestoreAccountDecision(
                        value.SourceAccountReference,
                        ArchiveRestoreAccountDecisionKind.MapExisting,
                        value.TargetAccountId))
                    .ToArray();
            var request = new ArchiveRestorePlanRequest(
                staged,
                archiveDecisions,
                accountDecisions);
            ArchiveRestorePlan plan = await planner.PlanAsync(request);
            ArchiveRestoreConfirmation[] confirmations = plan.Confirmations
                .Where(value => !value.Satisfied)
                .Select(value => new ArchiveRestoreConfirmation(
                    value.Kind,
                    value.Reference,
                    value.MarkerKey,
                    value.MarkerVersion))
                .ToArray();
            if (confirmations.Length > 0)
            {
                request = request with { Confirmations = confirmations };
                plan = await planner.PlanAsync(request);
            }
            Assert.True(
                plan.CanApply,
                string.Join(
                    " | ",
                    plan.Conflicts.Select(value =>
                        $"{value.Code}:{value.SourceAccountReference}:" +
                        $"{value.StableReference}:{value.Message}")
                        .Concat(plan.Archives
                            .Where(value => value.Kind ==
                                ArchiveRestoreArchiveMappingKind
                                    .RequiresSelection)
                            .Select(value =>
                                $"archive-selection:{value.SourceName}"))
                        .Concat(plan.Accounts
                            .Where(value => value.Kind is
                                ArchiveRestoreAccountMappingKind
                                    .RequiresSelection or
                                ArchiveRestoreAccountMappingKind.Collision)
                            .Select(value =>
                                $"account:{value.SourceAccountReference}:" +
                                value.Kind))
                        .Concat(plan.Confirmations
                            .Where(value => !value.Satisfied)
                            .Select(value =>
                                $"confirmation:{value.Kind}:" +
                                value.Reference))));

            DateTimeOffset now =
                new(2026, 10, 25, 12, operationIndex, 0, TimeSpan.Zero);
            ArchiveRestoreApplyResult result =
                await new SqliteGachaAtomicChangeStore(database).ApplyAsync(
                new ArchiveRestoreApplyRequest(
                    staged,
                    plan,
                    OperationId.New(),
                    now,
                    now.AddSeconds(1),
                    "Phase 8C.6 synthetic round-trip"));
            previousReceipt = result.Receipt ?? previousReceipt;
            return result;
        }

        public async Task<byte[]> ExportAfterReopenAsync(string platform)
        {
            await using var database = new FurinaDatabase(
                new SqliteDatabaseOptions(databasePath));
            await database.InitializeAsync();
            var snapshots = new SqliteArchiveExportSnapshotFactory(
                database,
                TimeProvider.System,
                new ArchiveSnapshotStorageOptions(
                    Path.Combine(directory, $"export-{operationIndex}")));
            var exporter = new ArchiveLogicalExporter(
                snapshots,
                new ArchivePackageWriter(),
                new ArchivePackageReader());
            using var stream = new MemoryStream();
            await exporter.ExportAsync(
                stream,
                ArchiveExportScope.All(),
                new ArchiveGeneratorInfo(
                    "FurinaChronicle",
                    "0.8.15.1",
                    "8C.6",
                    "0000000",
                    platform));
            return stream.ToArray();
        }
    }
}
