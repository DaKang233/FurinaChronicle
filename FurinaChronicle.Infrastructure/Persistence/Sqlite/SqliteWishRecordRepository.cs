using FurinaChronicle.Core.Wishes;
using FurinaChronicle.Services.Abstractions;
using FurinaChronicle.Services.Wishes;
using System;
using System.Collections.Generic;
using System.Text;

namespace FurinaChronicle.Infrastructure.Persistence.Sqlite
{
    public sealed class SqliteWishRecordRepository(FurinaDatabase database) : IWishRecordRepository
    {
        public Task<IReadOnlyList<WishRecord>> GetRecentAsync(
            Guid gameAccountId,
            int count,
            CancellationToken cancellationToken = default)
        {
            return GetPageAsync(
                gameAccountId,
                offset: 0,
                count,
                cancellationToken);
        }

        public async Task<IReadOnlyList<WishRecord>> GetPageAsync(
            Guid gameAccountId,
            int offset,
            int count,
            CancellationToken cancellationToken = default)
        {
            if (gameAccountId == Guid.Empty)
            {
                throw new ArgumentException(
                    "游戏账号 ID 不能为空。",
                    nameof(gameAccountId));
            }
            if (offset < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(offset),
                    "查询偏移量不能小于零。");
            }
            if (count <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(count),
                    "查询数量必须大于零。");
            }

            await database.InitializeAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            List<WishRecordRow> rows =
                await database.Connection.QueryAsync<WishRecordRow>($"""
                    SELECT
                        Id,
                        GameAccountId,
                        ExternalRecordId,
                        ItemName,
                        ItemId,
                        ItemType,
                        GachaType,
                        UigfGachaType,
                        RankType,
                        Count,
                        TimeUtcTicks,
                        TimeOffsetMinutes
                    FROM {WishRecordRow.TableName}
                    WHERE GameAccountId = ?
                    ORDER BY
                        TimeUtcTicks DESC,
                        Id DESC
                    LIMIT ?
                    OFFSET ?
                    """,
                    gameAccountId.ToString("D"),
                    count,
                    offset);

            cancellationToken.ThrowIfCancellationRequested();
            return rows.Select(row => row.ToDomain()).ToArray();
        }

        public async Task<int> CountAsync(
            Guid gameAccountId,
            CancellationToken cancellationToken = default)
        {
            if (gameAccountId == Guid.Empty)
            {
                throw new ArgumentException(
                    "游戏账号 ID 不能为空。",
                    nameof(gameAccountId));
            }

            await database.InitializeAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            return await database.Connection.ExecuteScalarAsync<int>(
                $"SELECT COUNT(*) FROM {WishRecordRow.TableName} WHERE GameAccountId = ?;",

                gameAccountId.ToString("D"));
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
