using FurinaChronicle.Core.Wishes;
using FurinaChronicle.Services.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace FurinaChronicle.Services.Wishes.Importing
{
    public sealed class ImportWishRecords(IWishRecordReader reader, IWishRecordRepository repository)
    {
        public async Task<WishImportResult> ExecuteAsync(Stream source, Guid gameAccountId, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);

            if (gameAccountId == Guid.Empty)
            {
                throw new ArgumentException("游戏账号 ID 不能为空",nameof(gameAccountId));
            }

            WishReadResult readResult = await reader.ReadAsync(source, gameAccountId, cancellationToken);
            WishRecord[] distinctRecords = readResult.Records.DistinctBy(record => (record.GameAccountId, record.ExternalRecordId)).ToArray();

            int duplicatesInsideSource = readResult.Records.Count - distinctRecords.Length;
            WishSaveResult saveResult = await repository.SaveBatchAsync(distinctRecords, cancellationToken);
            return new WishImportResult(
                TotalCount: readResult.Records.Count + readResult.Errors.Count,
                ImportedCount: saveResult.InsertedCount,
                DuplicateCount: duplicatesInsideSource + saveResult.DuplicateCount,
                InvalidCount: readResult.Errors.Count);
        }
    }
}
