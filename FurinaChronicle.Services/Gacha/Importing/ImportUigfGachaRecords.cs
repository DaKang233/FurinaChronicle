using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.Gacha.Metadata;
using FurinaChronicle.Core.Wishes;
using FurinaChronicle.Services.Abstractions;
using FurinaChronicle.Services.Gacha.Abstractions;
using FurinaChronicle.Services.Wishes;

namespace FurinaChronicle.Services.Gacha.Importing;

public sealed class ImportUigfGachaRecords(
    IGachaImportReader reader,
    IGachaItemMetadataProvider metadataProvider,
    IPlayerArchiveRepository archiveRepository,
    IGameAccountRepository accountRepository,
    IWishRecordRepository recordRepository)
{
    public async Task<GachaImportResult> ExecuteAsync(
        Stream source,
        Guid playerArchiveId,
        Guid? targetGameAccountId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (playerArchiveId == Guid.Empty)
        {
            throw new ArgumentException("Player archive ID is required.", nameof(playerArchiveId));
        }

        if (targetGameAccountId == Guid.Empty)
        {
            throw new ArgumentException("Target game account ID is required.", nameof(targetGameAccountId));
        }

        _ = await archiveRepository.GetByIdAsync(playerArchiveId, cancellationToken)
            ?? throw new KeyNotFoundException("The target player archive does not exist.");

        GameAccount? targetAccount = null;
        if (targetGameAccountId is Guid targetId)
        {
            targetAccount = await accountRepository.GetByIdAsync(targetId, cancellationToken)
                ?? throw new KeyNotFoundException("The target game account does not exist.");

            if (targetAccount.PlayerArchiveId != playerArchiveId)
            {
                throw new InvalidOperationException("The target account does not belong to the target archive.");
            }
        }

        GachaReadResult readResult = await reader.ReadAsync(source, cancellationToken);
        Dictionary<Guid, List<WishRecord>> recordsByAccount = [];
        Dictionary<string, GameAccount> accountsByUid = new(StringComparer.Ordinal);
        Dictionary<string, GachaItemMetadata?> metadataByItemId = new(StringComparer.Ordinal);

        int createdAccountCount = 0;
        int ignoredCount = 0;

        foreach (GachaSourceAccount sourceAccount in readResult.Accounts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (targetAccount is not null &&
                !string.Equals(sourceAccount.Uid, targetAccount.Uid, StringComparison.Ordinal))
            {
                ignoredCount += sourceAccount.Records.Count;
                continue;
            }

            if (sourceAccount.Records.Count == 0)
            {
                continue;
            }

            GameAccount account;
            if (targetAccount is not null)
            {
                account = targetAccount;
            }
            else if (!accountsByUid.TryGetValue(sourceAccount.Uid, out account!))
            {
                GameAccount? existingAccount =
                    await accountRepository.GetByArchiveIdAndUidAsync(
                        playerArchiveId,
                        sourceAccount.Uid,
                        cancellationToken);

                if (existingAccount is null)
                {
                    account = await CreateAccountAsync(
                        playerArchiveId,
                        sourceAccount.Uid,
                        cancellationToken);
                    createdAccountCount++;
                }
                else
                {
                    account = existingAccount;
                }

                accountsByUid.Add(sourceAccount.Uid, account);
            }

            if (!recordsByAccount.TryGetValue(account.Id, out List<WishRecord>? records))
            {
                records = [];
                recordsByAccount.Add(account.Id, records);
            }

            foreach (GachaSourceRecord sourceRecord in sourceAccount.Records)
            {
                cancellationToken.ThrowIfCancellationRequested();

                GachaItemMetadata? metadata = null;
                if (sourceRecord.ItemName is null ||
                    sourceRecord.ItemType is null ||
                    sourceRecord.RankType is null)
                {
                    if (!metadataByItemId.TryGetValue(sourceRecord.ItemId, out metadata))
                    {
                        metadata = await metadataProvider.FindByIdAsync(
                            GachaGame.GenshinImpact,
                            sourceRecord.ItemId,
                            cancellationToken);
                        metadataByItemId.Add(sourceRecord.ItemId, metadata);
                    }
                }

                records.Add(new WishRecord(
                    account.Id,
                    sourceRecord.ExternalRecordId,
                    sourceRecord.ItemName ?? metadata?.Name,
                    sourceRecord.RankType ?? metadata?.RankType,
                    sourceRecord.Time)
                {
                    ItemId = sourceRecord.ItemId,
                    ItemType = sourceRecord.ItemType ?? metadata?.ItemType,
                    GachaType = sourceRecord.GachaType,
                    UigfGachaType = sourceRecord.UigfGachaType,
                    Count = sourceRecord.Count
                });
            }
        }

        int importedCount = 0;
        int duplicateCount = 0;

        foreach (List<WishRecord> records in recordsByAccount.Values)
        {
            WishRecord[] distinctRecords = records
                .DistinctBy(record => (record.GameAccountId, record.ExternalRecordId))
                .ToArray();

            duplicateCount += records.Count - distinctRecords.Length;
            WishSaveResult saveResult = await recordRepository.SaveBatchAsync(
                distinctRecords,
                cancellationToken);
            importedCount += saveResult.InsertedCount;
            duplicateCount += saveResult.DuplicateCount;
        }

        return new GachaImportResult(
            readResult.TotalRecordCount,
            importedCount,
            duplicateCount,
            readResult.Errors.Count,
            ignoredCount,
            createdAccountCount);
    }

    private async Task<GameAccount> CreateAccountAsync(
        Guid playerArchiveId,
        string uid,
        CancellationToken cancellationToken)
    {
        GameServerRegion region = GameServerRegionResolver.Resolve(uid);
        if (region == GameServerRegion.Unknown)
        {
            throw new GachaImportFormatException($"Cannot infer a server region from UID {uid}.");
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        var account = new GameAccount(
            Guid.NewGuid(),
            playerArchiveId,
            uid,
            region,
            uid,
            IsPlaceholder: false,
            now,
            now);
        await accountRepository.AddAsync(account, cancellationToken);
        return account;
    }
}
