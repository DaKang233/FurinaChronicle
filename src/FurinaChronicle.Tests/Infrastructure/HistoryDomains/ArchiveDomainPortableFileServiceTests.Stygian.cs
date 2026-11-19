// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.History;
using FurinaChronicle.Services.HistoryDomains.Portable;
using FurinaChronicle.Tests.TestDoubles;

namespace FurinaChronicle.Tests.Infrastructure.HistoryDomains;

public sealed partial class ArchiveDomainPortableFileServiceTests
{
    [Fact]
    public async Task StygianDetailsSurvivePortableApplyReexportAndReadableExport()
    {
        await using var source = await PortableContext.CreateAsync();
        var archive = await source.AddArchiveAndAccountAsync();
        var account = Assert.Single(await source.Accounts.GetByArchiveIdAsync(archive.Id));
        var observation = StygianDetailFixture.Observation(account.Id, (await StygianDetailFixture.ReadAsync()).Snapshot!);
        Assert.Equal(ChangeExecutionStatus.Applied,
            (await source.Store.CommitChallengeObservationsAsync(ChallengeRequest([observation]))).Status);
        using var output = new MemoryStream();
        await source.Files.ExportAsync(archive.Id, ArchiveDomainDatasetKind.ChallengeHistory, output, TestTime());
        output.Position = 0;
        var package = await source.Files.ReadAsync(output);
        Assert.Equal(2, package.FormatMajor);
        Assert.Equal(2, package.FormatMinor);
        Assert.False((package with { FormatMinor = 1 }).SupportsVersion());
        Assert.False((package with { FormatMinor = 0 }).SupportsVersion());
        Assert.False((package with { FormatMinor = 4 }).SupportsVersion());
        await using var target = await PortableContext.CreateAsync();
        var plan = await target.Files.PlanAsync(package);
        Assert.Equal(ChangeExecutionStatus.Applied,
            (await target.Store.ApplyArchiveDomainPortableAsync(ApplyRequest(package, plan))).Status);
        var targetAccount = Assert.Single(await target.Accounts.GetByArchiveIdAsync(plan.TargetArchiveId));
        var actual = Assert.Single(await target.Store.GetChallengeObservationsAsync(targetAccount.Id));
        Assert.Equal(6, actual.Stages[0].Participants[0].Constellation);
        Assert.Equal(0, actual.Stages[0].Participants[1].Constellation);
        Assert.Equal(240, actual.StygianStatistics!.Multiplayer!.Seconds);
        Assert.Equal(9_000_000_000, actual.MultiplayerStages[0].StygianDetails!.BestAvatars[0].Value);
        Assert.Equal("123456", actual.Stages[0].StygianDetails!.MonsterId);
        Assert.Null(actual.StygianStatistics.BlingAvatars[2].IsPlus);
        using var reexported = new MemoryStream();
        await target.Files.ExportAsync(plan.TargetArchiveId, ArchiveDomainDatasetKind.ChallengeHistory, reexported, TestTime());
        reexported.Position = 0;
        Assert.True(ArchiveDomainPortableSemanticComparer.Compare(package, await target.Files.ReadAsync(reexported)).Equivalent);
        Assert.Equal(0, (await target.Files.PlanAsync(package, plan.TargetArchiveId)).AddCount);
        using var markdown = new StringWriter();
        await target.Files.ExportMarkdownAsync(plan.TargetArchiveId, ArchiveDomainDatasetKind.ChallengeHistory, markdown);
        Assert.Contains("多人最佳难度：5，总成绩：240 秒", markdown.ToString());
        Assert.Contains("6 命", markdown.ToString());
        Assert.Contains("9000000000", markdown.ToString());
        Assert.Contains("敌人标签", markdown.ToString());
        Assert.Contains("增辉角色", markdown.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PortablePlanDoesNotSkipChangedMultiplayerContentWithSameObservationId(bool stageChange)
    {
        await using var initial = await PortableContext.CreateAsync();
        var initialArchive = await initial.AddArchiveAndAccountAsync();
        var initialAccount = Assert.Single(await initial.Accounts.GetByArchiveIdAsync(initialArchive.Id));
        var snapshot = (await StygianDetailFixture.ReadAsync()).Snapshot!;
        var original = StygianDetailFixture.Observation(initialAccount.Id, snapshot);
        await initial.Store.CommitChallengeObservationsAsync(ChallengeRequest([original]));
        using var originalFile = new MemoryStream();
        await initial.Files.ExportAsync(initialArchive.Id, ArchiveDomainDatasetKind.ChallengeHistory, originalFile, TestTime());
        originalFile.Position = 0;
        var originalPackage = await initial.Files.ReadAsync(originalFile);
        await using var target = await PortableContext.CreateAsync();
        var firstPlan = await target.Files.PlanAsync(originalPackage);
        await target.Store.ApplyArchiveDomainPortableAsync(ApplyRequest(originalPackage, firstPlan));

        await using var changed = await PortableContext.CreateAsync();
        var changedArchive = await changed.AddArchiveAndAccountAsync();
        var changedAccount = Assert.Single(await changed.Accounts.GetByArchiveIdAsync(changedArchive.Id));
        string response = stageChange
            ? StygianDetailFixture.Response.Replace("\"second\":200", "\"second\":201", StringComparison.Ordinal)
            : StygianDetailFixture.Response.Replace("\"second\":240", "\"second\":241", StringComparison.Ordinal);
        var changedSnapshot = (await StygianDetailFixture.ReadAsync(response)).Snapshot! with { FetchedAt = snapshot.FetchedAt };
        var conflicting = StygianDetailFixture.Observation(changedAccount.Id, changedSnapshot, original.ObservationId);
        await changed.Store.CommitChallengeObservationsAsync(ChallengeRequest([conflicting]));
        using var changedFile = new MemoryStream();
        await changed.Files.ExportAsync(changedArchive.Id, ArchiveDomainDatasetKind.ChallengeHistory, changedFile, TestTime());
        changedFile.Position = 0;
        var changedPackage = await changed.Files.ReadAsync(changedFile);
        var conflict = await target.Files.PlanAsync(changedPackage, firstPlan.TargetArchiveId);
        Assert.Equal(1, conflict.ConflictCount);
        Assert.Equal(0, conflict.SkipCount);
        var targetAccount = Assert.Single(await target.Accounts.GetByArchiveIdAsync(firstPlan.TargetArchiveId));
        var unchanged = Assert.Single(await target.Store.GetChallengeObservationsAsync(targetAccount.Id));
        Assert.Equal(240, unchanged.StygianStatistics!.Multiplayer!.Seconds);
        Assert.Equal(200, unchanged.MultiplayerStages[0].Score);
    }
}
