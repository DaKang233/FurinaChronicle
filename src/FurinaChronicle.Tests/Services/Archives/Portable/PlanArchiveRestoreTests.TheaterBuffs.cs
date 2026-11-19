// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.History;
using FurinaChronicle.Infrastructure.Archives.Portable;
using FurinaChronicle.Services.Archives.Portable;
using FurinaChronicle.Services.History;
using FurinaChronicle.Tests.Infrastructure.Persistence.Sqlite;
using FurinaChronicle.Tests.TestDoubles;

namespace FurinaChronicle.Tests.Services.Archives.Portable;

public sealed partial class PlanArchiveRestoreTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TheaterBuffArchiveRetainsSourceDetailsAndRequiresVersionedDomainDescriptor(bool replace)
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
            var expected = TheaterBuffFixture.Observation(SourceAccountId, (await TheaterBuffFixture.ReadAsync()).Snapshot!);
            source.Challenges[SourceAccountId] = [expected];
            using var archiveFile = new MemoryStream();
            await new ArchivePackageWriter().WriteAsync(archiveFile, source,
                new ArchiveGeneratorInfo("FurinaChronicle", "0.10.0", "10.0", "fixture", "test"));
            archiveFile.Position = 0;
            await using var staged = await new SqliteArchiveRestoreInputStager(
                new ArchivePackageReader(), new ArchiveRestoreStorageOptions(root)).StageAsync(archiveFile);
            var descriptor = Assert.Single(staged.Summary.Manifest.Datasets, dataset => dataset.Kind == "challenge-history");
            Assert.Equal("1.4", descriptor.FormatVersion);
            Assert.True(descriptor.Required);
            var planner = new PlanArchiveRestore(CreateTargetFactory(context, root), TimeProvider.System);
            var request = new ArchiveRestorePlanRequest(staged,
                Mode: replace ? ArchiveRestoreMode.Replace : ArchiveRestoreMode.Merge,
                ReplaceSelections: replace
                    ? [new ArchiveReplaceSelection(SourceArchiveId, ArchiveReplaceScopeKind.Domain, SourceAccountId, ArchiveReplaceDomain.ChallengeObservation)]
                    : null);
            var warning = await planner.PlanAsync(request);
            var plan = await planner.PlanAsync(request with
            {
                Confirmations = warning.Confirmations.Where(item => !item.Satisfied)
                    .Select(item => new ArchiveRestoreConfirmation(item.Kind, item.Reference, item.MarkerKey, item.MarkerVersion)).ToArray(),
            });
            Assert.True(plan.CanApply);
            var now = DateTimeOffset.UtcNow;
            Assert.Equal(ChangeExecutionStatus.Applied,
                (await context.AtomicGacha.ApplyAsync(new ArchiveRestoreApplyRequest(staged, plan, OperationId.New(), now, now))).Status);
            var actual = Assert.Single(await context.AtomicGacha.GetChallengeObservationsAsync(TargetAccountId));
            Assert.Equal(ArchiveRestoreSemanticDigest.ForChallenge(expected), ArchiveRestoreSemanticDigest.ForChallenge(actual));
            Assert.Equal(new long?[] { 101, 102, null },
                actual.Stages.Single(stage => stage.StageId == "10").TheaterRound!.BuffDetails!.Select(buff => buff.SourceId));
            Assert.Equal("合成祝福总览", actual.Stages.Single(stage => stage.StageId == "10").TheaterRound!.BuffSummary);
            var repeated = await PlanForTargetAsync(context, root, source, confirmNonUndoable: true);
            Assert.True(repeated.CanApply);
            Assert.All(repeated.Facts, fact => Assert.Equal(ArchiveRestoreFactAction.Skip, fact.Action));
        }
        finally { DeleteRestoreSnapshotRoot(root); }
    }

    [Theory]
    [InlineData("1.0")]
    [InlineData("1.1")]
    [InlineData("1.2")]
    [InlineData("1.3")]
    public async Task ArchiveReaderRejectsTheaterBuffsDisguisedAsOlderDatasetWithValidChecksums(string older)
    {
        var source = SourceData("Main", []);
        source.Challenges[SourceAccountId] =
            [TheaterBuffFixture.Observation(SourceAccountId, (await TheaterBuffFixture.ReadAsync()).Snapshot!)];
        using var file = new MemoryStream();
        await new ArchivePackageWriter().WriteAsync(file, source,
            new ArchiveGeneratorInfo("FurinaChronicle", "0.10.0", "10.0", "fixture", "test"));
        file.Position = 0;
        var entries = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        using (var input = new System.IO.Compression.ZipArchive(file, System.IO.Compression.ZipArchiveMode.Read, true))
        {
            foreach (var entry in input.Entries)
            {
                using var bytes = new MemoryStream();
                await using var content = entry.Open();
                await content.CopyToAsync(bytes);
                entries.Add(entry.FullName, bytes.ToArray());
            }
        }
        var manifest = System.Text.Json.Nodes.JsonNode.Parse(entries["manifest.json"])!;
        var descriptor = manifest["datasets"]!.AsArray().Single(item => item!["kind"]!.GetValue<string>() == "challenge-history")!;
        string path = descriptor["paths"]![0]!.GetValue<string>();
        foreach (string changedPath in new[] { "manifest.json", path })
        {
            string json = System.Text.Encoding.UTF8.GetString(entries[changedPath]);
            string changed = System.Text.RegularExpressions.Regex.Replace(json,
                "(\"format_version\"\\s*:\\s*\")1\\.4(\")", match => match.Groups[1].Value + older + match.Groups[2].Value);
            Assert.NotEqual(json, changed);
            Assert.Equal(entries[changedPath].Length, System.Text.Encoding.UTF8.GetByteCount(changed));
            entries[changedPath] = System.Text.Encoding.UTF8.GetBytes(changed);
        }
        entries["checksums.sha256"] = System.Text.Encoding.UTF8.GetBytes(string.Concat(entries
            .Where(entry => entry.Key != "checksums.sha256").OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(entry.Value)).ToLowerInvariant() + "  " + entry.Key + "\n")));
        using var downgraded = new MemoryStream();
        using (var output = new System.IO.Compression.ZipArchive(downgraded, System.IO.Compression.ZipArchiveMode.Create, true))
            foreach (var entry in entries)
            {
                await using var content = output.CreateEntry(entry.Key).Open();
                await content.WriteAsync(entry.Value);
            }
        downgraded.Position = 0;
        var exception = await Assert.ThrowsAsync<ArchivePackageException>(
            () => new ArchivePackageReader().ReadAndValidateAsync(downgraded));
        Assert.Equal(ArchivePackageErrorCode.UnsupportedVersion, exception.Code);
    }
}
