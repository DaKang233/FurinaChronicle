// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Services.Abstractions;
using FurinaChronicle.Services.Archives;

namespace FurinaChronicle.Services.Gacha.Refreshing;

// Preparation never writes parents. The refresh's existing atomic commit creates them only
// together with accepted facts, and does not promise Undo for an operation creating parents.
public sealed record PreparedGachaRefreshTarget(
    GameAccount Account, PlayerArchive? ArchiveToCreate = null, GameAccount? AccountToCreate = null)
{
    public string? ArchiveName { get; init; }
}

public sealed class PrepareGachaRefreshTarget(
    IPlayerArchiveRepository archives, IGameAccountRepository accounts, TimeProvider clock)
{
    public async Task<PreparedGachaRefreshTarget> ExecuteAsync(
        GachaRefreshIdentity identity, Guid? archiveId, Guid? accountId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (archiveId == Guid.Empty || accountId == Guid.Empty ||
            (accountId.HasValue && !archiveId.HasValue))
            throw new ArgumentException("Invalid refresh selection.");
        GameRoleNaturalIdentity natural = GenshinGameRoleIdentity.Create(identity.Uid, identity.ServerRegion);
        bool createArchive = !archiveId.HasValue;
        PlayerArchive archive;
        if (archiveId is Guid selected)
            archive = await archives.GetByIdAsync(selected, cancellationToken)
                ?? throw new InvalidOperationException("目标档案已不存在，请重新选择。");
        else
        {
            IReadOnlyList<PlayerArchive> current = await archives.GetAllAsync(cancellationToken);
            var names = current.Select(item => item.Name).ToHashSet(StringComparer.Ordinal);
            int suffix = 1;
            while (names.Contains($"未命名档案{suffix}")) suffix++;
            DateTimeOffset now = clock.GetUtcNow();
            archive = new(Guid.NewGuid(), $"未命名档案{suffix}", now, now);
        }

        GameAccount? account;
        if (accountId is Guid selectedAccount)
        {
            account = await accounts.GetByIdAsync(selectedAccount, cancellationToken);
            if (account is null || account.PlayerArchiveId != archive.Id ||
                !GenshinGameRoleIdentity.TryCreate(account.Uid, account.ServerRegion, out var target) ||
                target != natural)
                throw new InvalidOperationException("来源角色与所选账号不一致，或目标已变化；未保存数据。");
            return new(account) { ArchiveName = archive.Name };
        }
        account = createArchive ? null :
            await accounts.GetByArchiveIdAndNaturalIdentityAsync(archive.Id, natural, cancellationToken);
        if (account is not null) return new(account) { ArchiveName = archive.Name };

        if (!createArchive)
        {
            IReadOnlyList<GameAccount> existing = await accounts.GetByArchiveIdAsync(archive.Id, cancellationToken);
            if (existing.Any(item => item.RoleIdentity is null && item.Uid == identity.Uid))
                throw new InvalidOperationException("该档案存在同 UID 的未解析账号，请先明确选择或修复身份；未自动映射。");
        }
        account = await new AddGameAccount(archives, accounts).PrepareAsync(
            archive, identity.Uid, identity.ServerRegion, identity.Uid, createArchive, cancellationToken);
        return new(account, createArchive ? archive : null, account) { ArchiveName = archive.Name };
    }
}
