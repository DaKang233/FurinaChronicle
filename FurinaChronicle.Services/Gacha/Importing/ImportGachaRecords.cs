// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Services.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace FurinaChronicle.Services.Gacha.Importing
{
    public sealed class ImportGachaRecords(IGachaRecordReader reader, IGachaRecordRepository gachaRepository, IGameAccountRepository accountRepository)
    {
        public async Task<GachaRecordImportResult> ExecuteAsync(Stream source, Guid gameAccountId, CancellationToken cancellationToken = default)
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
            GachaSaveResult saveResult = await gachaRepository.SaveBatchAsync(
                distinctRecords,
                cancellationToken,
                GachaRecordConflictPolicy.PreserveExisting);
            return new GachaRecordImportResult(
                TotalCount: readResult.Records.Count + readResult.Errors.Count,
                ImportedCount: saveResult.InsertedCount,
                DuplicateCount: duplicatesInsideSource + saveResult.DuplicateCount,
                InvalidCount: readResult.Errors.Count);
        }
    }
}
