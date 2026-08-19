using System;
using System.Collections.Generic;
using System.Text;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Services.Abstractions;

namespace FurinaChronicle.Services.Archives;

public sealed class ArchiveSelectionService(
    IArchiveSelectionStore selectionStore,
    IPlayerArchiveRepository archiveRepository,
    IGameAccountRepository accountRepository)
{
    public async Task<ArchiveSelection?> GetForArchiveAsync(
        Guid playerArchiveId,
        CancellationToken cancellationToken = default)
    {
        if (playerArchiveId == Guid.Empty)
        {
            throw new ArgumentException(
                "玩家档案 ID 不能为空。",
                nameof(playerArchiveId));
        }

        PlayerArchive? archive = await archiveRepository.GetByIdAsync(
            playerArchiveId,
            cancellationToken);
        if (archive is null)
        {
            throw new KeyNotFoundException(
                "要选择的玩家档案不存在。");
        }

        ArchiveSelection? savedSelection =
            await selectionStore.LoadForArchiveAsync(
                playerArchiveId,
                cancellationToken);
        if (savedSelection is not null &&
            savedSelection.PlayerArchiveId == playerArchiveId)
        {
            GameAccount? savedAccount = await accountRepository.GetByIdAsync(
                savedSelection.GameAccountId,
                cancellationToken);
            if (savedAccount?.PlayerArchiveId == playerArchiveId)
            {
                await selectionStore.SaveAsync(savedSelection, cancellationToken);
                return savedSelection;
            }
        }

        IReadOnlyList<GameAccount> accounts =
            await accountRepository.GetByArchiveIdAsync(
                playerArchiveId,
                cancellationToken);
        GameAccount? firstAccount = accounts.FirstOrDefault();
        if (firstAccount is null)
        {
            return null;
        }

        var repairedSelection =
            new ArchiveSelection(playerArchiveId, firstAccount.Id);
        await selectionStore.SaveAsync(repairedSelection, cancellationToken);
        return repairedSelection;
    }
    public async Task<ArchiveSelection?> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        ArchiveSelection? savedSelection = await selectionStore.LoadAsync(cancellationToken);

        if (savedSelection is not null)
        {
            PlayerArchive? savedArchive = await archiveRepository.GetByIdAsync(savedSelection.PlayerArchiveId, cancellationToken);

            GameAccount? savedAccount = await accountRepository.GetByIdAsync(savedSelection.GameAccountId, cancellationToken);

            bool selectionIsValid = savedArchive is not null && savedAccount is not null && savedAccount.PlayerArchiveId == savedArchive.Id;

            if (selectionIsValid)
            {
                return savedSelection;
            }
        }

        // 原选择不存在或已经失效，自动选择第一个可用账号。
        IReadOnlyList<PlayerArchive> archives = await archiveRepository.GetAllAsync(cancellationToken);

        foreach (PlayerArchive archive in archives)
        {
            IReadOnlyList<GameAccount> accounts = await accountRepository.GetByArchiveIdAsync(archive.Id, cancellationToken);

            GameAccount? firstAccount = accounts.FirstOrDefault();

            if (firstAccount is null)
            {
                continue;
            }

            var repairedSelection = new ArchiveSelection(archive.Id, firstAccount.Id);

            await selectionStore.SaveAsync(repairedSelection, cancellationToken);

            return repairedSelection;
        }

        await selectionStore.ClearAsync(cancellationToken);

        return null;
    }

    public async Task SelectAsync(Guid gameAccountId, CancellationToken cancellationToken = default)
    {
        if (gameAccountId == Guid.Empty)
        {
            throw new ArgumentException("游戏账号 ID 不能为空。", nameof(gameAccountId));
        }

        GameAccount? account = await accountRepository.GetByIdAsync(gameAccountId, cancellationToken);

        if (account is null)
        {
            throw new KeyNotFoundException("要选择的游戏账号不存在。");
        }

        PlayerArchive? archive = await archiveRepository.GetByIdAsync(account.PlayerArchiveId, cancellationToken) ?? throw new InvalidOperationException("游戏账号所属的玩家档案不存在。");
        
        var selection = new ArchiveSelection(archive.Id, account.Id);

        await selectionStore.SaveAsync(selection, cancellationToken);
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        return selectionStore.ClearAsync(cancellationToken);
    }
}
