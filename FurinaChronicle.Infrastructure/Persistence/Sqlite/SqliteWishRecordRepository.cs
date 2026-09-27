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

        public async Task<IReadOnlyList<WishRecord>> QueryAsync(
            WishRecordQuery query,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(query);
            query.Validate();
            await database.InitializeAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            (string where, object[] arguments) = BuildWhereClause(query);
            string direction = query.SortOrder == WishRecordSortOrder.NewestFirst
                ? "DESC"
                : "ASC";
            var sql = new StringBuilder($"""
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
                WHERE {where}
                ORDER BY TimeUtcTicks {direction}, Id {direction}
                """);
            sql.AppendLine();
            var parameters = arguments.ToList();
            if (query.Limit is int limit)
            {
                sql.AppendLine("LIMIT ? OFFSET ?");
                parameters.Add(limit);
                parameters.Add(query.Offset);
            }
            else if (query.Offset > 0)
            {
                sql.AppendLine("LIMIT -1 OFFSET ?");
                parameters.Add(query.Offset);
            }

            List<WishRecordRow> rows =
                await database.Connection.QueryAsync<WishRecordRow>(
                    sql.ToString(),
                    parameters.ToArray());
            cancellationToken.ThrowIfCancellationRequested();
            return rows.Select(row => row.ToDomain()).ToArray();
        }

        public async Task<int> CountAsync(
            WishRecordQuery query,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(query);
            query.Validate();
            await database.InitializeAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            (string where, object[] arguments) = BuildWhereClause(query);
            return await database.Connection.ExecuteScalarAsync<int>(
                $"SELECT COUNT(*) FROM {WishRecordRow.TableName} WHERE {where};",
                arguments);
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

        private static (string Where, object[] Arguments) BuildWhereClause(
            WishRecordQuery query)
        {
            var conditions = new List<string>();
            var arguments = new List<object>();

            conditions.Add(
                $"GameAccountId IN ({string.Join(", ", query.GameAccountIds.Select(_ => "?"))})");
            arguments.AddRange(query.GameAccountIds.Select(id => (object)id.ToString("D")));

            if (query.RankTypes is { Count: > 0 })
            {
                conditions.Add(
                    $"RankType IN ({string.Join(", ", query.RankTypes.Select(_ => "?"))})");
                arguments.AddRange(query.RankTypes.Select(rank => (object)rank));
            }
            if (query.PoolGroups is { Count: > 0 })
            {
                string typeExpression =
                    "COALESCE(NULLIF(UigfGachaType, ''), GachaType)";
                var poolConditions = new List<string>();
                foreach (WishPoolGroup group in query.PoolGroups)
                {
                    string[] types = GetGachaTypes(group);
                    if (group == WishPoolGroup.Unknown)
                    {
                        poolConditions.Add(
                            $"({typeExpression} IS NULL OR {typeExpression} NOT IN ('100', '200', '301', '302', '400', '500'))");
                    }
                    else
                    {
                        poolConditions.Add(
                            $"{typeExpression} IN ({string.Join(", ", types.Select(_ => "?"))})");
                        arguments.AddRange(types.Cast<object>());
                    }
                }
                conditions.Add($"({string.Join(" OR ", poolConditions)})");
            }
            if (query.StartTime is DateTimeOffset start)
            {
                conditions.Add("TimeUtcTicks >= ?");
                arguments.Add(start.UtcDateTime.Ticks);
            }
            if (query.EndTime is DateTimeOffset end)
            {
                conditions.Add("TimeUtcTicks <= ?");
                arguments.Add(end.UtcDateTime.Ticks);
            }

            return (string.Join(" AND ", conditions), arguments.ToArray());
        }

        private static string[] GetGachaTypes(WishPoolGroup group)
        {
            return group switch
            {
                WishPoolGroup.CharacterEvent => ["301", "400"],
                WishPoolGroup.WeaponEvent => ["302"],
                WishPoolGroup.Standard => ["200"],
                WishPoolGroup.Novice => ["100"],
                WishPoolGroup.Chronicled => ["500"],
                _ => []
            };
        }
    }
}
