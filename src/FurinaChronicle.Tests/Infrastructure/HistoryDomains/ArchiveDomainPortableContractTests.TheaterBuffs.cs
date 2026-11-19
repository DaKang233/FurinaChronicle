// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Infrastructure.HistoryDomains.Portable;
using FurinaChronicle.Tests.Infrastructure.Persistence.Sqlite;

namespace FurinaChronicle.Tests.Infrastructure.HistoryDomains;

public sealed partial class ArchiveDomainPortableContractTests
{
    [Fact]
    public async Task StructuredTheaterExampleKeepsItsOwnMinorAndRequiredSourceDetails()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        var files = new ArchiveDomainPortableFileService(context.Archives, context.Accounts, context.AtomicGacha);
        var package = await ReadAsync(files, "challenge-history-v2-3-theater-buffs.json");
        Assert.Equal(2, package.FormatMajor);
        Assert.Equal(3, package.FormatMinor);
        var observation = Assert.Single(Assert.Single(package.Accounts).ChallengeObservations);
        Assert.True(observation.HasTheaterBuffDetails);
        var round = Assert.Single(observation.Stages).TheaterRound!;
        Assert.Equal(3, round.BuffDetails!.Count);
        Assert.All(round.BuffDetails, buff =>
        {
            Assert.False(string.IsNullOrWhiteSpace(buff.Name));
            Assert.NotNull(buff.IconUrl);
        });
        foreach (int minor in new[] { 0, 1, 2, 4 })
            Assert.False((package with { FormatMinor = minor }).SupportsVersion());
    }
}
