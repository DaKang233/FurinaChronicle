// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Services.Abstractions;
using FurinaChronicle.Core.History;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Services.Gacha.Writing;
using System;
using System.Collections.Generic;
using System.Text;

namespace FurinaChronicle.Services.Gacha.Importing
{
    public sealed class ImportGachaRecords(IGachaRecordReader reader, ICommitGachaRecords commitGachaRecords, IGameAccountRepository accountRepository)
    {
        public Task<GachaRecordImportResult> ExecuteAsync(
            Stream source,
            Guid gameAccountId,
            CancellationToken cancellationToken = default) =>
            ExecuteAsync(
                source,
                gameAccountId,
                GachaImportMode.Incremental,
                cancellationToken);

        public async Task<GachaRecordImportResult> ExecuteAsync(
            Stream source,
            Guid gameAccountId,
            GachaImportMode mode,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);

            if (gameAccountId == Guid.Empty)
            {
                throw new ArgumentException("游戏账号 ID 不能为空",nameof(gameAccountId));
            }
            var gameAccount = await accountRepository.GetByIdAsync(gameAccountId, cancellationToken);
            if (gameAccount == null) { throw new KeyNotFoundException("指定的游戏账号不存在。"); }

            GachaRecordReadResult readResult = await reader.ReadAsync(source, gameAccountId, cancellationToken);
            GachaRecord[] distinctRecords = readResult.Records.DistinctBy(record => (record.GameAccountId, record.ExternalRecordId)).ToArray();

            int duplicatesInsideSource = readResult.Records.Count - distinctRecords.Length;
            DateTimeOffset now = DateTimeOffset.UtcNow;
            CommitGachaRecordsResult saveResult =
                await commitGachaRecords.ExecuteAsync(
                    new CommitGachaRecordsRequest(
                        OperationId.New(),
                        DataChangeOperationKind.Import,
                        DataOrigin.StandardImport,
                        mode == GachaImportMode.Full
                            ? "Full import compatible Gacha JSON"
                            : "Import compatible Gacha JSON",
                        now,
                        now,
                        distinctRecords,
                        mode == GachaImportMode.Full
                            ? GachaRecordConflictPolicy.ReplaceExisting
                            : GachaRecordConflictPolicy.PreserveExisting,
                        SuppressTombstonesAndContinue: false),
                    cancellationToken);
            if (saveResult.Status is ChangeExecutionStatus.Conflict or
                ChangeExecutionStatus.Suppressed or
                ChangeExecutionStatus.NeedsConfirmation)
            {
                throw new GachaRecordImportFormatException(
                    saveResult.ConflictReason ??
                    "The compatible JSON import could not be committed safely.");
            }
            return new GachaRecordImportResult(
                TotalCount: readResult.Records.Count + readResult.Errors.Count,
                ImportedCount: saveResult.InsertedCount,
                DuplicateCount: duplicatesInsideSource + saveResult.DuplicateCount,
                InvalidCount: readResult.Errors.Count,
                UpdatedCount: saveResult.UpdatedCount);
        }
    }
}
