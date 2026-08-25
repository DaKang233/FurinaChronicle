using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Wishes;
using FurinaChronicle.Services.Abstractions;

namespace FurinaChronicle.Services.Gacha.Exporting;

public sealed class LoadGachaExportData(
    IPlayerArchiveRepository archiveRepository,
    IGameAccountRepository accountRepository,
    IWishRecordRepository recordRepository,
    TimeProvider timeProvider)
{
    private const int BatchSize = 500;

    public async Task<GachaExportDocument> ExecuteAsync(
        Guid playerArchiveId,
        IReadOnlyCollection<Guid> gameAccountIds,
        CancellationToken cancellationToken = default)
    {
        if (playerArchiveId == Guid.Empty)
        {
            throw new ArgumentException(
                "玩家档案 ID 不能为空。",
                nameof(playerArchiveId));
        }

        ArgumentNullException.ThrowIfNull(gameAccountIds);
        if (gameAccountIds.Count == 0)
        {
            throw new ArgumentException(
                "至少需要选择一个游戏账号。",
                nameof(gameAccountIds));
        }

        _ = await archiveRepository.GetByIdAsync(
            playerArchiveId,
            cancellationToken)
            ?? throw new KeyNotFoundException("要导出的玩家档案不存在。");

        IReadOnlyList<GameAccount> archiveAccounts =
            await accountRepository.GetByArchiveIdAsync(
                playerArchiveId,
                cancellationToken);
        Dictionary<Guid, GameAccount> accountsById =
            archiveAccounts.ToDictionary(account => account.Id);

        List<GachaExportAccount> exportAccounts = [];
        HashSet<Guid> seenAccountIds = [];
        foreach (Guid gameAccountId in gameAccountIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (gameAccountId == Guid.Empty)
            {
                throw new ArgumentException(
                    "游戏账号 ID 不能为空。",
                    nameof(gameAccountIds));
            }

            if (!seenAccountIds.Add(gameAccountId))
            {
                continue;
            }

            if (!accountsById.TryGetValue(
                gameAccountId,
                out GameAccount? account))
            {
                throw new InvalidOperationException(
                    "所选游戏账号不属于要导出的玩家档案。");
            }

            IReadOnlyList<WishRecord> records =
                await LoadAllRecordsAsync(
                    gameAccountId,
                    cancellationToken);
            exportAccounts.Add(new GachaExportAccount(
                account,
                ResolveTimezone(account, records),
                records));
        }

        return new GachaExportDocument(
            timeProvider.GetUtcNow(),
            exportAccounts);
    }

    private async Task<IReadOnlyList<WishRecord>> LoadAllRecordsAsync(
        Guid gameAccountId,
        CancellationToken cancellationToken)
    {
        int totalCount = await recordRepository.CountAsync(
            gameAccountId,
            cancellationToken);
        List<WishRecord> records = new(totalCount);

        for (int offset = 0; offset < totalCount; offset += BatchSize)
        {
            IReadOnlyList<WishRecord> batch =
                await recordRepository.GetPageAsync(
                    gameAccountId,
                    offset,
                    Math.Min(BatchSize, totalCount - offset),
                    cancellationToken);
            records.AddRange(batch);
        }

        return records
            .OrderBy(record => record.Time)
            .ThenBy(record => record.ExternalRecordId, StringComparer.Ordinal)
            .ToArray();
    }

    private static int ResolveTimezone(
        GameAccount account,
        IReadOnlyList<WishRecord> records)
    {
        int? recordTimezone = records
            .Select(record => record.Time.Offset.TotalHours)
            .Where(hours =>
                hours >= -14 &&
                hours <= 14 &&
                hours == Math.Truncate(hours))
            .GroupBy(hours => (int)hours)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key)
            .Select(group => (int?)group.Key)
            .FirstOrDefault();

        return recordTimezone ?? account.ServerRegion switch
        {
            GameServerRegion.America => -5,
            GameServerRegion.Europe => 1,
            GameServerRegion.ChinaOfficial or
            GameServerRegion.ChinaBilibili or
            GameServerRegion.Asia or
            GameServerRegion.TaiwanHongKongMacao => 8,
            _ => 0
        };
    }
}
