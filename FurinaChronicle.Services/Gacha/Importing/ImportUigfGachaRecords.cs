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
        ILookup<string, GachaReadError> readErrorsByUid = readResult.Errors
            .Where(error => !string.IsNullOrWhiteSpace(error.Uid))
            .ToLookup(error => error.Uid!, StringComparer.Ordinal);

        if (targetAccount is not null &&
            readErrorsByUid[targetAccount.Uid].FirstOrDefault()
                is GachaReadError targetError)
        {
            throw AccountRejected(
                targetAccount.Uid,
                DescribeReadError(targetError));
        }

        int createdAccountCount = 0;
        int ignoredCount = 0;
        int invalidCount = readResult.Errors.Count;

        foreach (IGrouping<string, GachaSourceAccount> accountGroup in
            readResult.Accounts.GroupBy(account => account.Uid, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string uid = accountGroup.Key;
            GachaSourceRecord[] sourceRecords = accountGroup
                .SelectMany(account => account.Records)
                .ToArray();

            if (targetAccount is not null &&
                !string.Equals(uid, targetAccount.Uid, StringComparison.Ordinal))
            {
                ignoredCount += sourceRecords.Length;
                continue;
            }

            GachaReadError? readError = readErrorsByUid[uid].FirstOrDefault();
            if (readError is not null)
            {
                invalidCount += sourceRecords.Length;
                continue;
            }

            if (sourceRecords.Length == 0)
            {
                continue;
            }

            List<PreparedGachaRecord>? preparedRecords =
                await PrepareAccountRecordsAsync(
                    sourceRecords,
                    metadataByItemId,
                    cancellationToken);
            if (preparedRecords is null)
            {
                if (targetAccount is not null)
                {
                    throw AccountRejected(
                        uid,
                        "one or more records have metadata that cannot be completed");
                }

                invalidCount += sourceRecords.Length;
                continue;
            }

            GameAccount account;
            if (targetAccount is not null)
            {
                account = targetAccount;
            }
            else if (!accountsByUid.TryGetValue(uid, out account!))
            {
                GameAccount? existingAccount =
                    await accountRepository.GetByArchiveIdAndUidAsync(
                        playerArchiveId,
                        uid,
                        cancellationToken);

                if (existingAccount is null)
                {
                    account = await CreateAccountAsync(
                        playerArchiveId,
                        uid,
                        cancellationToken);
                    createdAccountCount++;
                }
                else
                {
                    account = existingAccount;
                }

                accountsByUid.Add(uid, account);
            }

            if (!recordsByAccount.TryGetValue(account.Id, out List<WishRecord>? records))
            {
                records = [];
                recordsByAccount.Add(account.Id, records);
            }

            foreach (PreparedGachaRecord preparedRecord in preparedRecords)
            {
                GachaSourceRecord sourceRecord = preparedRecord.Source;
                records.Add(new WishRecord(
                    account.Id,
                    sourceRecord.ExternalRecordId,
                    preparedRecord.ItemName,
                    preparedRecord.RankType,
                    sourceRecord.Time)
                {
                    ItemId = sourceRecord.ItemId,
                    ItemType = preparedRecord.ItemType,
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
            invalidCount,
            ignoredCount,
            createdAccountCount);
    }

    private async Task<List<PreparedGachaRecord>?> PrepareAccountRecordsAsync(
        IReadOnlyCollection<GachaSourceRecord> sourceRecords,
        IDictionary<string, GachaItemMetadata?> metadataByItemId,
        CancellationToken cancellationToken)
    {
        var preparedRecords = new List<PreparedGachaRecord>(sourceRecords.Count);
        foreach (GachaSourceRecord sourceRecord in sourceRecords)
        {
            cancellationToken.ThrowIfCancellationRequested();

            GachaItemMetadata? metadata = null;
            if (string.IsNullOrWhiteSpace(sourceRecord.ItemName) ||
                string.IsNullOrWhiteSpace(sourceRecord.ItemType) ||
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

            string? itemName = sourceRecord.ItemName ?? metadata?.Name;
            string? itemType = sourceRecord.ItemType ?? metadata?.ItemType;
            int? rankType = sourceRecord.RankType ?? metadata?.RankType;
            if (string.IsNullOrWhiteSpace(itemName) ||
                string.IsNullOrWhiteSpace(itemType) ||
                rankType is not (>= 3 and <= 5))
            {
                return null;
            }

            preparedRecords.Add(new PreparedGachaRecord(
                sourceRecord,
                itemName.Trim(),
                itemType.Trim(),
                rankType.Value));
        }

        return preparedRecords;
    }

    private static GachaImportFormatException AccountRejected(
        string uid,
        string reason)
    {
        return new GachaImportFormatException(
            $"Cannot import UID {uid}: {reason}. " +
            "No records for this account were saved.");
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

    private static string DescribeReadError(GachaReadError error)
    {
        string location = error.RecordIndex is int index
            ? $"record {index}"
            : "account";

        return $"{location} is invalid " +
            $"({error.Code}): {error.Message}";
    }

    private sealed record PreparedGachaRecord(
        GachaSourceRecord Source,
        string ItemName,
        string ItemType,
        int RankType);
}
