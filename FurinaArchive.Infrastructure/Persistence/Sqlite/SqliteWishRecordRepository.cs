using FurinaArchive.Core.Wishes;
using FurinaArchive.Services.Abstractions;
using FurinaArchive.Services.Wishes;
using System;
using System.Collections.Generic;
using System.Text;

namespace FurinaArchive.Infrastructure.Persistence.Sqlite
{
    public sealed class SqliteWishRecordRepository(FurinaDatabase database) : IWishRecordRepository
    {
        public async Task<IReadOnlyList<WishRecord>> GetRecentAsync(int count, CancellationToken cancellationToken = default)
        {
            if (count <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count), "查询数量必须大于零。");
            }

            await database.InitializeAsync(cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            List<WishRecordRow> rows = await database.Connection.QueryAsync<WishRecordRow>($"""
                    SELECT
                        Id,
                        GameAccountId,
                        ExternalRecordId,
                        ItemName,
                        RankType,
                        TimeUtcTicks,
                        TimeOffsetMinutes
                    FROM {WishRecordRow.TableName}
                    ORDER BY
                        TimeUtcTicks DESC,
                        Id DESC
                    LIMIT ?
                    """,count);

            cancellationToken.ThrowIfCancellationRequested();
            return rows.Select(row => row.ToDomain()).ToArray();
        }

        public async Task<WishSaveResult> SaveBatchAsync(IReadOnlyCollection<WishRecord> records, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(records);

            if (records.Count == 0)
            {
                return new WishSaveResult(InsertedCount: 0, DuplicateCount: 0);
            }

            // 在进入事务前完成转换和校验。
            WishRecordRow[] rows = records.Select(WishRecordRow.FromDomain).ToArray();

            await database.InitializeAsync(cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            int insertedCount = 0;

            await database.Connection.RunInTransactionAsync(
                connection =>
                {
                    foreach (WishRecordRow row in rows)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        insertedCount += connection.Insert(row,"OR IGNORE");
                    }
                });

            return new WishSaveResult(InsertedCount: insertedCount, DuplicateCount: rows.Length - insertedCount);
        }
    }
}
