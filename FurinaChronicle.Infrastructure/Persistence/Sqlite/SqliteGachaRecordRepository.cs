// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Services.Abstractions;
using FurinaChronicle.Services.Gacha;
using System;
using System.Collections.Generic;
using System.Text;

namespace FurinaChronicle.Infrastructure.Persistence.Sqlite
{
    public sealed class SqliteGachaRecordRepository(FurinaDatabase database) : IGachaRecordRepository
    {
        public Task<IReadOnlyList<GachaRecord>> GetRecentAsync(
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

        public async Task<IReadOnlyList<GachaRecord>> GetPageAsync(
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

            List<GachaRecordRow> rows =
                await database.Connection.QueryAsync<GachaRecordRow>($"""
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
                        TimeOffsetMinutes,
                        Origin,
                        FetchedAtUtcTicks,
                        FetchedAtOffsetMinutes,
                        ImportedAtUtcTicks,
                        ImportedAtOffsetMinutes,
                        AcquisitionBatchId
                    FROM {GachaRecordRow.TableName}
                    WHERE GameAccountId = ?
                    ORDER BY
                        TimeUtcTicks DESC,
                        LENGTH(ExternalRecordId) DESC,
                        ExternalRecordId COLLATE BINARY DESC,
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
                $"SELECT COUNT(*) FROM {GachaRecordRow.TableName} WHERE GameAccountId = ?;",

                gameAccountId.ToString("D"));
        }

        public async Task<IReadOnlyList<GachaRecord>> QueryAsync(
            GachaRecordQuery query,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(query);
            query.Validate();
            await database.InitializeAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            (string where, object[] arguments) = BuildWhereClause(query);
            string direction = query.SortOrder == GachaRecordSortOrder.NewestFirst
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
                    TimeOffsetMinutes,
                    Origin,
                    FetchedAtUtcTicks,
                    FetchedAtOffsetMinutes,
                    ImportedAtUtcTicks,
                    ImportedAtOffsetMinutes,
                    AcquisitionBatchId
                FROM {GachaRecordRow.TableName}
                WHERE {where}
                ORDER BY
                    TimeUtcTicks {direction},
                    LENGTH(ExternalRecordId) {direction},
                    ExternalRecordId COLLATE BINARY {direction},
                    Id {direction}
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

            List<GachaRecordRow> rows =
                await database.Connection.QueryAsync<GachaRecordRow>(
                    sql.ToString(),
                    parameters.ToArray());
            cancellationToken.ThrowIfCancellationRequested();
            return rows.Select(row => row.ToDomain()).ToArray();
        }

        public async Task<int> CountAsync(
            GachaRecordQuery query,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(query);
            query.Validate();
            await database.InitializeAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            (string where, object[] arguments) = BuildWhereClause(query);
            return await database.Connection.ExecuteScalarAsync<int>(
                $"SELECT COUNT(*) FROM {GachaRecordRow.TableName} WHERE {where};",
                arguments);
        }

        public async Task<IReadOnlyList<GachaRecord>> GetByExternalRecordIdsAsync(
            Guid gameAccountId,
            IReadOnlyCollection<string> externalRecordIds,
            CancellationToken cancellationToken = default)
        {
            if (gameAccountId == Guid.Empty)
            {
                throw new ArgumentException(
                    "游戏账号 ID 不能为空。",
                    nameof(gameAccountId));
            }

            ArgumentNullException.ThrowIfNull(externalRecordIds);
            string[] ids = externalRecordIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (ids.Length != externalRecordIds.Count)
            {
                throw new ArgumentException(
                    "外部记录 ID 必须非空且不能重复。",
                    nameof(externalRecordIds));
            }

            if (ids.Length == 0)
            {
                return [];
            }

            await database.InitializeAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var result = new List<GachaRecord>(ids.Length);
            const int maximumIdsPerQuery = 500;
            for (int offset = 0; offset < ids.Length; offset += maximumIdsPerQuery)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string[] batch = ids
                    .Skip(offset)
                    .Take(maximumIdsPerQuery)
                    .ToArray();
                string placeholders = string.Join(", ", batch.Select(_ => "?"));
                object[] arguments =
                [
                    gameAccountId.ToString("D"),
                    .. batch.Cast<object>(),
                ];
                List<GachaRecordRow> rows =
                    await database.Connection.QueryAsync<GachaRecordRow>(
                        $"""
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
                            TimeOffsetMinutes,
                            Origin,
                            FetchedAtUtcTicks,
                            FetchedAtOffsetMinutes,
                            ImportedAtUtcTicks,
                            ImportedAtOffsetMinutes,
                            AcquisitionBatchId
                        FROM {GachaRecordRow.TableName}
                        WHERE GameAccountId = ?
                            AND ExternalRecordId IN ({placeholders});
                        """,
                        arguments);
                result.AddRange(rows.Select(row => row.ToDomain()));
            }

            return result;
        }

        public async Task<GachaSaveResult> SaveBatchAsync(
            IReadOnlyCollection<GachaRecord> records,
            CancellationToken cancellationToken = default,
            GachaRecordConflictPolicy conflictPolicy =
                GachaRecordConflictPolicy.PreserveExisting)
        {
            ArgumentNullException.ThrowIfNull(records);

            if (records.Count == 0)
            {
                return new GachaSaveResult(InsertedCount: 0, DuplicateCount: 0);
            }

            // 在进入事务前完成转换和校验。
            GachaRecordRow[] rows = records.Select(GachaRecordRow.FromDomain).ToArray();

            await database.InitializeAsync(cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            int insertedCount = 0;
            int updatedCount = 0;

            await database.Connection.RunInTransactionAsync(
                connection =>
                {
                    foreach (GachaRecordRow row in rows)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        int inserted = connection.Insert(row, "OR IGNORE");
                        insertedCount += inserted;
                        if (inserted == 0)
                        {
                            if (conflictPolicy ==
                                GachaRecordConflictPolicy.ReplaceExisting)
                            {
                                updatedCount += connection.Execute(
                                    $"""
                                    UPDATE {GachaRecordRow.TableName}
                                    SET
                                        ItemName = COALESCE(NULLIF(TRIM(?), ''), ItemName),
                                        ItemId = COALESCE(NULLIF(TRIM(?), ''), ItemId),
                                        ItemType = COALESCE(NULLIF(TRIM(?), ''), ItemType),
                                        GachaType = COALESCE(NULLIF(TRIM(?), ''), GachaType),
                                        UigfGachaType = COALESCE(NULLIF(TRIM(?), ''), UigfGachaType),
                                        RankType = COALESCE(?, RankType),
                                        Count = ?,
                                        TimeUtcTicks = ?,
                                        TimeOffsetMinutes = ?,
                                        Origin = ?,
                                        FetchedAtUtcTicks = ?,
                                        FetchedAtOffsetMinutes = ?,
                                        ImportedAtUtcTicks = ?,
                                        ImportedAtOffsetMinutes = ?,
                                        AcquisitionBatchId = ?
                                    WHERE GameAccountId = ? AND ExternalRecordId = ?;
                                    """,
                                    row.ItemName,
                                    row.ItemId,
                                    row.ItemType,
                                    row.GachaType,
                                    row.UigfGachaType,
                                    row.RankType,
                                    row.Count,
                                    row.TimeUtcTicks,
                                    row.TimeOffsetMinutes,
                                    row.Origin,
                                    row.FetchedAtUtcTicks,
                                    row.FetchedAtOffsetMinutes,
                                    row.ImportedAtUtcTicks,
                                    row.ImportedAtOffsetMinutes,
                                    row.AcquisitionBatchId,
                                    row.GameAccountId,
                                    row.ExternalRecordId);
                            }
                            else
                            {
                                connection.Execute(
                                $"""
                                UPDATE {GachaRecordRow.TableName}
                                SET
                                    ItemName = CASE
                                        WHEN ItemName IS NULL OR TRIM(ItemName) = ''
                                        THEN ?
                                        ELSE ItemName
                                    END,
                                    ItemId = CASE
                                        WHEN ItemId IS NULL OR TRIM(ItemId) = ''
                                        THEN ?
                                        ELSE ItemId
                                    END,
                                    ItemType = CASE
                                        WHEN ItemType IS NULL OR TRIM(ItemType) = ''
                                        THEN ?
                                        ELSE ItemType
                                    END,
                                    GachaType = CASE
                                        WHEN GachaType IS NULL OR TRIM(GachaType) = ''
                                        THEN ?
                                        ELSE GachaType
                                    END,
                                    UigfGachaType = CASE
                                        WHEN UigfGachaType IS NULL OR TRIM(UigfGachaType) = ''
                                        THEN ?
                                        ELSE UigfGachaType
                                    END,
                                    RankType = COALESCE(RankType, ?)
                                WHERE GameAccountId = ? AND ExternalRecordId = ?;
                                """,
                                row.ItemName,
                                row.ItemId,
                                row.ItemType,
                                row.GachaType,
                                row.UigfGachaType,
                                row.RankType,
                                row.GameAccountId,
                                row.ExternalRecordId);
                            }
                        }
                    }
                });

            return new GachaSaveResult(
                InsertedCount: insertedCount,
                DuplicateCount: rows.Length - insertedCount - updatedCount,
                UpdatedCount: updatedCount);
        }

        private static (string Where, object[] Arguments) BuildWhereClause(
            GachaRecordQuery query)
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
                foreach (GachaPoolGroup group in query.PoolGroups)
                {
                    string[] types = GetGachaTypes(group);
                    if (group == GachaPoolGroup.Unknown)
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

        private static string[] GetGachaTypes(GachaPoolGroup group)
        {
            return group switch
            {
                GachaPoolGroup.CharacterEvent => ["301", "400"],
                GachaPoolGroup.WeaponEvent => ["302"],
                GachaPoolGroup.Standard => ["200"],
                GachaPoolGroup.Novice => ["100"],
                GachaPoolGroup.Chronicled => ["500"],
                _ => []
            };
        }
    }
}
