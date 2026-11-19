// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Text.Json;
using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Infrastructure.HistoryDomains.Portable;
using FurinaChronicle.Services.HistoryDomains.Portable;
using FurinaChronicle.Tests.Infrastructure.Persistence.Sqlite;

namespace FurinaChronicle.Tests.Infrastructure.HistoryDomains;

public sealed partial class ArchiveDomainPortableContractTests
{
    [Theory]
    [InlineData("character-weapon-history-v1.schema.json", 1)]
    [InlineData("challenge-history-v1.schema.json", 2)]
    public async Task Schemas_AreDraft202012AndFixDatasetKind(
        string fileName,
        int datasetKind)
    {
        await using FileStream input = File.OpenRead(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Schemas",
            fileName));
        using JsonDocument document = await JsonDocument.ParseAsync(input);
        JsonElement root = document.RootElement;

        Assert.Equal(
            "https://json-schema.org/draft/2020-12/schema",
            root.GetProperty("$schema").GetString());
        Assert.False(string.IsNullOrWhiteSpace(
            root.GetProperty("$id").GetString()));
        Assert.Equal(
            datasetKind,
            root.GetProperty("properties")
                .GetProperty("package")
                .GetProperty("properties")
                .GetProperty("dataset_kind")
                .GetProperty("const")
                .GetInt32());
    }

    [Fact]
    public async Task Examples_CoverMinimumCompleteBadAndFutureMajor()
    {
        await using SqliteRepositoryTestContext context =
            SqliteRepositoryTestContext.Create();
        await context.Database.InitializeAsync();
        var service = new ArchiveDomainPortableFileService(
            context.Archives,
            context.Accounts,
            context.AtomicGacha);

        ArchiveDomainPortablePackage characterMinimum = await ReadAsync(
            service,
            "character-weapon-history-v1-minimum.json");
        Assert.Equal(
            ArchiveDomainDatasetKind.CharacterWeaponHistory,
            characterMinimum.DatasetKind);
        ArchiveDomainPortablePackage challengeMinimum = await ReadAsync(
            service,
            "challenge-history-v1-minimum.json");
        Assert.Equal(
            ArchiveDomainDatasetKind.ChallengeHistory,
            challengeMinimum.DatasetKind);
        ArchiveDomainPortablePackage complete = await ReadAsync(
            service,
            "character-weapon-history-v1-complete.json");
        ArchiveDomainPortableAccount account = Assert.Single(
            complete.Accounts);
        Assert.Single(account.ItemObservations);
        Assert.Single(account.ManualAcquisitions);
        Assert.Single(account.ManualRevisions);

        await Assert.ThrowsAsync<InvalidDataException>(() => ReadAsync(
            service,
            "domain-history-v1-bad-fingerprint.json"));
        await Assert.ThrowsAsync<InvalidDataException>(() => ReadAsync(
            service,
            "domain-history-v2-future-major.json"));
    }

    [Fact]
    public async Task AbyssStatisticsExampleHasValidFingerprintAndLosslessV2Details()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        var service = new ArchiveDomainPortableFileService(
            context.Archives, context.Accounts, context.AtomicGacha);
        var package = await ReadAsync(service, "challenge-history-v2-abyss-statistics.json");
        Assert.Equal(2, package.FormatMajor);
        Assert.Equal(0, package.FormatMinor);
        var detailed = Assert.Single(
            package.Accounts.SelectMany(account => account.ChallengeObservations),
            observation => observation.SpiralAbyssStatistics is not null);
        Assert.Equal(34, detailed.SpiralAbyssStatistics!.TotalStars);
        Assert.Equal(3_000_000_000, Assert.Single(detailed.SpiralAbyssStatistics.DamageRank).Value);
        Assert.True(detailed.HasSpiralAbyssBattleDetails);
        Assert.False((package with { FormatMajor = 1, FormatMinor = 99 }).SupportsVersion());
    }

    [Fact]
    public async Task TheaterExampleKeepsOriginsAndStatsWithItsOwnMinorVersion()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        var service = new ArchiveDomainPortableFileService(context.Archives, context.Accounts, context.AtomicGacha);
        var package = await ReadAsync(service, "challenge-history-v2-1-theater.json");
        Assert.Equal(2, package.FormatMajor);
        Assert.Equal(1, package.FormatMinor);
        var report = Assert.Single(package.Accounts.SelectMany(account => account.ChallengeObservations),
            observation => observation.TheaterStatistics is not null);
        Assert.Equal("3000000000", report.TheaterStatistics!.MaxDamage!.Value);
        Assert.Equal(90761, report.TheaterStatistics.TotalUseTime);
        Assert.Equal(ChallengeParticipantOrigin.Support, report.TheaterStatistics.BackupAvatars[2].Origin);
        Assert.Contains(report.Stages, stage => stage.TheaterRound?.IsTarot == true);
        Assert.False((package with { FormatMinor = 0 }).SupportsVersion());
        Assert.True((package with { FormatMinor = 2 }).SupportsVersion());
        Assert.True((package with { FormatMinor = 3 }).SupportsVersion());
        Assert.False((package with { FormatMinor = 4 }).SupportsVersion());
    }

    [Fact]
    public async Task StygianExampleKeepsBothRunsAndRejectsDowngradedVersion()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        var service = new ArchiveDomainPortableFileService(context.Archives, context.Accounts, context.AtomicGacha);
        var package = await ReadAsync(service, "challenge-history-v2-2-stygian.json");
        Assert.Equal(2, package.FormatMinor);
        var observation = Assert.Single(package.Accounts.SelectMany(account => account.ChallengeObservations),
            observation => observation.StygianStatistics is not null);
        Assert.Equal(240, observation.StygianStatistics!.Multiplayer!.Seconds);
        Assert.Equal(6, observation.Stages[0].Participants[0].Constellation);
        Assert.Equal(9_000_000_000, observation.MultiplayerStages[0].StygianDetails!.BestAvatars[0].Value);
        Assert.False((package with { FormatMinor = 1 }).SupportsVersion());
        Assert.True((package with { FormatMinor = 3 }).SupportsVersion());
        Assert.False((package with { FormatMinor = 4 }).SupportsVersion());
    }

    private static async Task<ArchiveDomainPortablePackage> ReadAsync(
        ArchiveDomainPortableFileService service,
        string fileName)
    {
        await using FileStream input = File.OpenRead(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "ArchiveDomainExamples",
            fileName));
        return await service.ReadAsync(input);
    }
}
