// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.Gacha.Metadata;
using FurinaChronicle.Core.History;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Services.Abstractions;
using FurinaChronicle.Services.Gacha.Abstractions;
using FurinaChronicle.Services.Gacha;
using FurinaChronicle.Services.Gacha.Writing;

namespace FurinaChronicle.Services.Gacha.Importing;

public sealed class ImportUigfGachaRecords(
    IGachaImportReader reader,
    IGachaItemMetadataProvider metadataProvider,
    IPlayerArchiveRepository archiveRepository,
    IGameAccountRepository accountRepository,
    ICommitGachaRecords commitGachaRecords)
{
    public Task<GachaImportResult> ExecuteAsync(
        Stream source,
        Guid playerArchiveId,
        Guid? targetGameAccountId = null,
        CancellationToken cancellationToken = default) =>
        ExecuteCoreAsync(
            source,
            playerArchiveId,
            targetGameAccountId,
            archiveToCreate: null,
            cancellationToken);

    public Task<GachaImportResult> ExecuteAsync(
        Stream source,
        PlayerArchive archiveToCreate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(archiveToCreate);
        return ExecuteCoreAsync(
            source,
            archiveToCreate.Id,
            targetGameAccountId: null,
            archiveToCreate,
            cancellationToken);
    }

    private async Task<GachaImportResult> ExecuteCoreAsync(
        Stream source,
        Guid playerArchiveId,
        Guid? targetGameAccountId,
        PlayerArchive? archiveToCreate,
        CancellationToken cancellationToken)
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

        PlayerArchive? existingArchive =
            await archiveRepository.GetByIdAsync(
                playerArchiveId,
                cancellationToken);
        if (existingArchive is null &&
            archiveToCreate?.Id != playerArchiveId)
        {
            throw new KeyNotFoundException(
                "The target player archive does not exist.");
        }
        if (existingArchive is not null && archiveToCreate is not null)
        {
            throw new InvalidOperationException(
                "The proposed player archive already exists.");
        }

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
        DateTimeOffset importedAt = DateTimeOffset.UtcNow;
        var importProvenance = new RecordProvenance(
            DataOrigin.StandardImport,
            new RecordTimestamps(ImportedAt: importedAt),
            acquisitionBatchId: AcquisitionBatchId.New());
        Dictionary<Guid, List<GachaRecord>> recordsByAccount = [];
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
        var accountsToCreate = new List<GameAccount>();
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

            GameServerRegion region = GameServerRegionResolver.Resolve(uid);
            if (region == GameServerRegion.Unknown)
            {
                if (targetAccount is not null)
                {
                    throw AccountRejected(
                        uid,
                        "the server region cannot be inferred reliably from the UID");
                }

                invalidCount += sourceRecords.Length;
                continue;
            }

            GameRoleNaturalIdentity naturalIdentity =
                GenshinGameRoleIdentity.Create(uid, region);

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
                if (targetAccount.RoleIdentity?.NaturalIdentity != naturalIdentity)
                {
                    throw AccountRejected(
                        uid,
                        "the target account does not have the same resolved natural identity");
                }

                account = targetAccount;
            }
            else if (!accountsByUid.TryGetValue(uid, out account!))
            {
                GameAccount? existingAccount =
                    await accountRepository.GetByArchiveIdAndNaturalIdentityAsync(
                        playerArchiveId,
                        naturalIdentity,
                        cancellationToken);

                if (existingAccount is null)
                {
                    account = await PrepareAccountAsync(
                        playerArchiveId,
                        uid,
                        region,
                        naturalIdentity,
                        cancellationToken);
                    accountsToCreate.Add(account);
                    createdAccountCount++;
                }
                else
                {
                    account = existingAccount;
                }

                accountsByUid.Add(uid, account);
            }

            if (!recordsByAccount.TryGetValue(account.Id, out List<GachaRecord>? records))
            {
                records = [];
                recordsByAccount.Add(account.Id, records);
            }

            foreach (PreparedGachaRecord preparedRecord in preparedRecords)
            {
                GachaSourceRecord sourceRecord = preparedRecord.Source;
                records.Add(new GachaRecord(
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
                    Count = sourceRecord.Count,
                    Provenance = importProvenance
                });
            }
        }

        GachaRecord[] allRecords = recordsByAccount.Values
            .SelectMany(records => records)
            .ToArray();
        CommitGachaRecordsResult saveResult =
            await commitGachaRecords.ExecuteAsync(
                new CommitGachaRecordsRequest(
                    OperationId.New(),
                    DataChangeOperationKind.Import,
                    DataOrigin.StandardImport,
                    "Import UIGF Gacha records",
                    importedAt,
                    DateTimeOffset.UtcNow,
                    allRecords,
                    GachaRecordConflictPolicy.PreserveExisting,
                    SuppressTombstonesAndContinue: false,
                    CaptureUndo: createdAccountCount == 0,
                    ArchiveToCreate: archiveToCreate,
                    AccountsToCreate: accountsToCreate),
                cancellationToken);
        if (saveResult.Status == ChangeExecutionStatus.Conflict)
        {
            throw new GachaImportFormatException(
                saveResult.ConflictReason ??
                "The UIGF file conflicts with existing Gacha facts.");
        }
        if (saveResult.Status == ChangeExecutionStatus.Suppressed)
        {
            throw new GachaImportFormatException(
                $"The UIGF file contains {saveResult.SuppressedCount} record(s) " +
                "that were irreversibly deleted on this device. Explicit " +
                "reintroduction confirmation is required.");
        }
        if (saveResult.Status == ChangeExecutionStatus.NeedsConfirmation)
        {
            throw new IOException(
                "The UIGF import requires history cleanup confirmation before it can continue.");
        }

        return new GachaImportResult(
            readResult.TotalRecordCount,
            saveResult.InsertedCount,
            saveResult.DuplicateCount,
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

    private async Task<GameAccount> PrepareAccountAsync(
        Guid playerArchiveId,
        string uid,
        GameServerRegion region,
        GameRoleNaturalIdentity naturalIdentity,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        GameRoleIdentity roleIdentity =
            await accountRepository.GetRoleIdentityByNaturalIdentityAsync(
                naturalIdentity,
                cancellationToken) ??
            new GameRoleIdentity(
                GameRoleIdentityId.FromNaturalIdentity(naturalIdentity),
                naturalIdentity);
        var account = new GameAccount(
            Guid.NewGuid(),
            playerArchiveId,
            uid,
            region,
            uid,
            IsPlaceholder: false,
            now,
            now,
            roleIdentity);
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
