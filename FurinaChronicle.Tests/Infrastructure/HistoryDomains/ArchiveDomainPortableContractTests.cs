// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Text.Json;
using FurinaChronicle.Infrastructure.HistoryDomains.Portable;
using FurinaChronicle.Services.HistoryDomains.Portable;
using FurinaChronicle.Tests.Infrastructure.Persistence.Sqlite;

namespace FurinaChronicle.Tests.Infrastructure.HistoryDomains;

public sealed class ArchiveDomainPortableContractTests
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
