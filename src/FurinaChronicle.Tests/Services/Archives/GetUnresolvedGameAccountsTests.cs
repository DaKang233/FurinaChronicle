// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Services.Archives;
using FurinaChronicle.Tests.TestDoubles;

namespace FurinaChronicle.Tests.Services.Archives;

public sealed class GetUnresolvedGameAccountsTests
{
    [Fact]
    public async Task ExecuteAsync_ReportsArchiveAndReliableReasons()
    {
        var archives = new InMemoryPlayerArchiveRepository();
        var accounts = new InMemoryGameAccountRepository();
        PlayerArchive archive = ArchiveTestData.Archive("诊断档案");
        await archives.AddAsync(archive);
        GameAccount placeholder = Account(
            archive.Id,
            "legacy-id",
            GameServerRegion.Unknown,
            isPlaceholder: true);
        GameAccount invalidUid = Account(
            archive.Id,
            "bad-id",
            GameServerRegion.Asia);
        GameAccount unknownRegion = Account(
            archive.Id,
            "400000001",
            GameServerRegion.Unknown);
        GameAccount mismatch = Account(
            archive.Id,
            "800000001",
            GameServerRegion.America);
        GameAccount missingIdentity = Account(
            archive.Id,
            "400000002",
            GameServerRegion.Asia);
        await accounts.AddAsync(placeholder);
        await accounts.AddAsync(invalidUid);
        await accounts.AddAsync(unknownRegion);
        await accounts.AddAsync(mismatch);
        await accounts.AddAsync(missingIdentity);
        await accounts.AddAsync(ArchiveTestData.Account(
            archive.Id,
            uid: "800000002"));

        IReadOnlyList<UnresolvedGameAccountDiagnostic> result =
            await new GetUnresolvedGameAccounts(archives, accounts)
                .ExecuteAsync();

        Assert.Equal(5, result.Count);
        Assert.All(
            result,
            item => Assert.Equal("诊断档案", item.PlayerArchiveName));
        Assert.Equal(
            UnresolvedGameAccountReason.PlaceholderAccount,
            Assert.Single(result, item => item.GameAccountId == placeholder.Id).Reason);
        Assert.Equal(
            UnresolvedGameAccountReason.InvalidUid,
            Assert.Single(result, item => item.GameAccountId == invalidUid.Id).Reason);
        Assert.Equal(
            UnresolvedGameAccountReason.UnknownServerRegion,
            Assert.Single(result, item => item.GameAccountId == unknownRegion.Id).Reason);
        Assert.Equal(
            UnresolvedGameAccountReason.UidRegionMismatch,
            Assert.Single(result, item => item.GameAccountId == mismatch.Id).Reason);
        Assert.Equal(
            UnresolvedGameAccountReason.MissingRoleIdentity,
            Assert.Single(result, item => item.GameAccountId == missingIdentity.Id).Reason);
    }

    private static GameAccount Account(
        Guid archiveId,
        string uid,
        GameServerRegion region,
        bool isPlaceholder = false)
    {
        DateTimeOffset now = DateTimeOffset.UnixEpoch;
        return new GameAccount(
            Guid.NewGuid(),
            archiveId,
            uid,
            region,
            null,
            isPlaceholder,
            now,
            now,
            RoleIdentity: null);
    }
}
