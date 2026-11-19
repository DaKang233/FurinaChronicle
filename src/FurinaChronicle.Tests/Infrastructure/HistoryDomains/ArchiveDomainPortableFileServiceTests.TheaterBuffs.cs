// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.History;
using FurinaChronicle.Services.HistoryDomains.Portable;
using FurinaChronicle.Tests.TestDoubles;

namespace FurinaChronicle.Tests.Infrastructure.HistoryDomains;

public sealed partial class ArchiveDomainPortableFileServiceTests
{
    [Fact]
    public async Task TheaterBuffsSurvivePortableApplyReexportAndMarkdownWithoutDowngrading()
    {
        await using var source = await PortableContext.CreateAsync();
        var archive = await source.AddArchiveAndAccountAsync();
        var account = Assert.Single(await source.Accounts.GetByArchiveIdAsync(archive.Id));
        var expected = TheaterBuffFixture.Observation(account.Id, (await TheaterBuffFixture.ReadAsync()).Snapshot!);
        Assert.Equal(ChangeExecutionStatus.Applied,
            (await source.Store.CommitChallengeObservationsAsync(ChallengeRequest([expected]))).Status);
        using var output = new MemoryStream();
        await source.Files.ExportAsync(archive.Id, ArchiveDomainDatasetKind.ChallengeHistory, output, TestTime());
        output.Position = 0;
        var package = await source.Files.ReadAsync(output);
        Assert.Equal(2, package.FormatMajor);
        Assert.Equal(3, package.FormatMinor);
        foreach (int minor in new[] { 0, 1, 2, 4 })
            Assert.False((package with { FormatMinor = minor }).SupportsVersion());
        await using var target = await PortableContext.CreateAsync();
        var plan = await target.Files.PlanAsync(package);
        Assert.Equal(ChangeExecutionStatus.Applied,
            (await target.Store.ApplyArchiveDomainPortableAsync(ApplyRequest(package, plan))).Status);
        var targetAccount = Assert.Single(await target.Accounts.GetByArchiveIdAsync(plan.TargetArchiveId));
        var actual = Assert.Single(await target.Store.GetChallengeObservationsAsync(targetAccount.Id));
        Assert.Equal(new long?[] { 101, 102, null }, actual.Stages.Single(stage => stage.StageId == "10").TheaterRound!.BuffDetails!.Select(buff => buff.SourceId));
        Assert.Equal("合成祝福总览", actual.Stages.Single(stage => stage.StageId == "10").TheaterRound!.BuffSummary);
        using var reexport = new MemoryStream();
        await target.Files.ExportAsync(plan.TargetArchiveId, ArchiveDomainDatasetKind.ChallengeHistory, reexport, TestTime());
        reexport.Position = 0;
        Assert.True(ArchiveDomainPortableSemanticComparer.Compare(package, await target.Files.ReadAsync(reexport)).Equivalent);
        Assert.Equal(0, (await target.Files.PlanAsync(package, plan.TargetArchiveId)).AddCount);
        using var markdown = new StringWriter();
        await target.Files.ExportMarkdownAsync(plan.TargetArchiveId, ArchiveDomainDatasetKind.ChallengeHistory, markdown);
        Assert.Contains("来源 ID 101", markdown.ToString());
        Assert.Contains("已强化", markdown.ToString());
        Assert.Contains("未强化", markdown.ToString());
        Assert.Contains("Lv.3", markdown.ToString());
        Assert.Contains("效果乙", markdown.ToString());
    }
}
