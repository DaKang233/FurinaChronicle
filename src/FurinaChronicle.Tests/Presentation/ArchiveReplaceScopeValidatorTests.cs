// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Presentation.Management;
using FurinaChronicle.Services.Archives.Portable;

namespace FurinaChronicle.Tests.Presentation;

public sealed class ArchiveReplaceScopeValidatorTests
{
    private static readonly Guid ArchiveA = Guid.NewGuid();
    private static readonly Guid ArchiveB = Guid.NewGuid();
    private static readonly Guid AccountA = Guid.NewGuid();
    private static readonly Guid AccountB = Guid.NewGuid();

    [Fact]
    public void ValidScopesPassAndAreDescribed()
    {
        ArchiveCatalog catalog = Catalog();
        var selections = new List<ArchiveReplaceSelection>
        {
            new(ArchiveA, ArchiveReplaceScopeKind.Account, AccountA),
            new(ArchiveA, ArchiveReplaceScopeKind.Domain, AccountB,
                ArchiveReplaceDomain.ChallengeObservation),
            new(ArchiveB, ArchiveReplaceScopeKind.Archive)
        };

        Assert.Null(ArchiveReplaceScopeValidator.Validate(catalog, selections));
        Assert.Contains(
            "账号",
            ArchiveReplaceScopeValidator.Describe(catalog, selections[0]));
        Assert.Contains(
            "挑战观察",
            ArchiveReplaceScopeValidator.Describe(catalog, selections[1]));
    }

    [Fact]
    public void ArchiveScopeOverlappingAccountScopeIsRejected()
    {
        ArchiveCatalog catalog = Catalog();
        var selections = new List<ArchiveReplaceSelection>
        {
            new(ArchiveA, ArchiveReplaceScopeKind.Archive),
            new(ArchiveA, ArchiveReplaceScopeKind.Account, AccountA)
        };

        string? error = ArchiveReplaceScopeValidator.Validate(catalog, selections);

        Assert.NotNull(error);
        Assert.Contains("重叠", error);
        Assert.Contains("未写入", error);
    }

    [Fact]
    public void DomainOverlapAndDuplicateScopesAreRejected()
    {
        ArchiveCatalog catalog = Catalog();
        var duplicate = new List<ArchiveReplaceSelection>
        {
            new(ArchiveA, ArchiveReplaceScopeKind.Domain, AccountA,
                ArchiveReplaceDomain.Gacha),
            new(ArchiveA, ArchiveReplaceScopeKind.Domain, AccountA,
                ArchiveReplaceDomain.Gacha)
        };
        var overlap = new List<ArchiveReplaceSelection>
        {
            new(ArchiveA, ArchiveReplaceScopeKind.Account, AccountA),
            new(ArchiveA, ArchiveReplaceScopeKind.Domain, AccountA,
                ArchiveReplaceDomain.ItemObservation)
        };

        Assert.Contains("重复", ArchiveReplaceScopeValidator.Validate(catalog, duplicate));
        Assert.Contains("重叠", ArchiveReplaceScopeValidator.Validate(catalog, overlap));
    }

    [Fact]
    public void EmptyUnknownAndMalformedScopesAreRejected()
    {
        ArchiveCatalog catalog = Catalog();
        Assert.Contains(
            "至少",
            ArchiveReplaceScopeValidator.Validate(catalog, []));
        Assert.Contains(
            "不存在",
            ArchiveReplaceScopeValidator.Validate(
                catalog,
                [new ArchiveReplaceSelection(
                    Guid.NewGuid(), ArchiveReplaceScopeKind.Archive)]));
        Assert.Contains(
            "形状无效",
            ArchiveReplaceScopeValidator.Validate(
                catalog,
                [new ArchiveReplaceSelection(
                    ArchiveA,
                    ArchiveReplaceScopeKind.Archive,
                    AccountA)]));
        Assert.Contains(
            "不存在",
            ArchiveReplaceScopeValidator.Validate(
                catalog,
                [new ArchiveReplaceSelection(
                    ArchiveA,
                    ArchiveReplaceScopeKind.Account,
                    Guid.NewGuid())]));
    }

    private static ArchiveCatalog Catalog() => new(
        [],
        [
            new ArchiveCatalogArchive(
                ArchiveA,
                "档案 A",
                DateTimeOffset.Now.AddDays(-1),
                DateTimeOffset.Now,
                [
                    new ArchiveCatalogAccount(
                        AccountA,
                        null,
                        "账号 A",
                        "100000001",
                        GameServerRegion.ChinaOfficial,
                        false,
                        null,
                        DateTimeOffset.Now,
                        DateTimeOffset.Now),
                    new ArchiveCatalogAccount(
                        AccountB,
                        null,
                        "账号 B",
                        "100000002",
                        GameServerRegion.ChinaOfficial,
                        false,
                        null,
                        DateTimeOffset.Now,
                        DateTimeOffset.Now)
                ]),
            new ArchiveCatalogArchive(
                ArchiveB,
                "档案 B",
                DateTimeOffset.Now.AddDays(-1),
                DateTimeOffset.Now,
                [])
        ]);
}
