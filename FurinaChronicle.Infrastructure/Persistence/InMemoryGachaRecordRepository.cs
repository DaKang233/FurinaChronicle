// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Services.Abstractions;
using FurinaChronicle.Services.Gacha;
using System;
using System.Collections.Generic;
using System.Text;

namespace FurinaChronicle.Infrastructure.Persistence
{
    public sealed class InMemoryGachaRecordRepository : IGachaRecordRepository
    {
        private readonly List<GachaRecord> records = [];
        private readonly HashSet<(Guid GameAccountId, string ExternalRecordId)> uniqueKeys = [];
        private readonly SemaphoreSlim gate = new(1, 1);

        public InMemoryGachaRecordRepository() : this(CreateSampleRecords()) { }

        public InMemoryGachaRecordRepository(IEnumerable<GachaRecord> initialRecords)
        {
            ArgumentNullException.ThrowIfNull(initialRecords, nameof(initialRecords));

            foreach (var record in initialRecords)
            {
                var key = (record.GameAccountId, record.ExternalRecordId);
                if (uniqueKeys.Add(key)) records.Add(record);
            }
        }

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
                throw new ArgumentException("游戏账号 ID 不能为空。", nameof(gameAccountId));
            if (offset < 0)
                throw new ArgumentOutOfRangeException(nameof(offset), "查询偏移量不能小于零。");
            if (count <= 0)
                throw new ArgumentOutOfRangeException(nameof(count), "查询数量必须大于0");

            await gate.WaitAsync(cancellationToken);
            try
            {
                return records
                    .Where(record => record.GameAccountId == gameAccountId)
                    .OrderByDescending(record => record.Time)
                    .ThenByDescending(record => record.ExternalRecordId)
                    .Skip(offset)
                    .Take(count)
                    .ToArray();
            }
            finally
            {
                gate.Release();
            }
        }

        public async Task<int> CountAsync(
            Guid gameAccountId,
            CancellationToken cancellationToken = default)
        {
            if (gameAccountId == Guid.Empty)
                throw new ArgumentException("游戏账号 ID 不能为空。", nameof(gameAccountId));

            await gate.WaitAsync(cancellationToken);
            try
            {
                return records.Count(record =>
                    record.GameAccountId == gameAccountId);
            }
            finally
            {
                gate.Release();
            }
        }

        public async Task<IReadOnlyList<GachaRecord>> QueryAsync(
            GachaRecordQuery query,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(query);
            query.Validate();
            await gate.WaitAsync(cancellationToken);
            try
            {
                IEnumerable<GachaRecord> filtered = ApplyQuery(records, query);
                filtered = query.SortOrder == GachaRecordSortOrder.NewestFirst
                    ? filtered
                        .OrderByDescending(record => record.Time)
                        .ThenByDescending(record => record.ExternalRecordId)
                    : filtered
                        .OrderBy(record => record.Time)
                        .ThenBy(record => record.ExternalRecordId);
                filtered = filtered.Skip(query.Offset);
                if (query.Limit is int limit)
                {
                    filtered = filtered.Take(limit);
                }

                return filtered.ToArray();
            }
            finally
            {
                gate.Release();
            }
        }

        public async Task<int> CountAsync(
            GachaRecordQuery query,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(query);
            query.Validate();
            await gate.WaitAsync(cancellationToken);
            try
            {
                return ApplyQuery(records, query).Count();
            }
            finally
            {
                gate.Release();
            }
        }

        public async Task<IReadOnlyList<GachaRecord>> GetByExternalRecordIdsAsync(
            Guid gameAccountId,
            IReadOnlyCollection<string> externalRecordIds,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(externalRecordIds);
            HashSet<string> ids = externalRecordIds.ToHashSet(
                StringComparer.Ordinal);
            await gate.WaitAsync(cancellationToken);
            try
            {
                return records.Where(record =>
                        record.GameAccountId == gameAccountId &&
                        ids.Contains(record.ExternalRecordId))
                    .ToArray();
            }
            finally
            {
                gate.Release();
            }
        }

        internal async Task<GachaSaveResult> SaveBatchAsync(
            IReadOnlyCollection<GachaRecord> newRecords,
            CancellationToken cancellationToken = default,
            GachaRecordConflictPolicy conflictPolicy =
                GachaRecordConflictPolicy.PreserveExisting)
        {
            ArgumentNullException.ThrowIfNull(newRecords, nameof(newRecords));
            await gate.WaitAsync(cancellationToken);
            try
            {
                int insertedCount = 0;
                int duplicateCount = 0;
                int updatedCount = 0;
                foreach (var record in newRecords)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var key = (record.GameAccountId, record.ExternalRecordId);

                    if (!uniqueKeys.Add(key))
                    {
                        if (conflictPolicy == GachaRecordConflictPolicy.ReplaceExisting)
                        {
                            int index = records.FindIndex(candidate =>
                                candidate.GameAccountId == record.GameAccountId &&
                                candidate.ExternalRecordId == record.ExternalRecordId);
                            if (index >= 0)
                            {
                                records[index] = MergeForReplacement(
                                    records[index],
                                    record);
                                updatedCount++;
                                continue;
                            }
                        }

                        duplicateCount++;
                        continue;
                    }
                    records.Add(record);
                    insertedCount++;
                }
                return new GachaSaveResult(
                    insertedCount,
                    duplicateCount,
                    updatedCount);
            }
            finally { gate?.Release(); }
        }

        private static GachaRecord MergeForReplacement(
            GachaRecord existing,
            GachaRecord incoming)
        {
            return incoming with
            {
                ItemName = UseIncomingOrExisting(
                    incoming.ItemName,
                    existing.ItemName),
                ItemId = UseIncomingOrExisting(
                    incoming.ItemId,
                    existing.ItemId),
                ItemType = UseIncomingOrExisting(
                    incoming.ItemType,
                    existing.ItemType),
                GachaType = UseIncomingOrExisting(
                    incoming.GachaType,
                    existing.GachaType),
                UigfGachaType = UseIncomingOrExisting(
                    incoming.UigfGachaType,
                    existing.UigfGachaType),
                RankType = incoming.RankType ?? existing.RankType
            };
        }

        private static string? UseIncomingOrExisting(
            string? incoming,
            string? existing)
        {
            return string.IsNullOrWhiteSpace(incoming) ? existing : incoming;
        }

        public static IEnumerable<GachaRecord> CreateSampleRecords()
        {
            Guid AccountId1 = Guid.Parse("90ca2f7f-327b-47bb-9562-0fdb3217dd26");
            Guid AccountId2 = Guid.Parse("f3e1c8a0-4b5d-4c9e-9f7a-1d2e3f4b5c6d");
            return [new(AccountId1, "100000000000000001", "芙宁娜", 5, new DateTimeOffset(2026, 7, 16, 18, 30, 0, TimeSpan.FromHours(8))),
                    new(AccountId1, "100000000000000002", "夏洛蒂", 4, new DateTimeOffset(2026, 7, 16, 18, 29, 0, TimeSpan.FromHours(8))),
                    new(AccountId1, "100000000000000003", "黎明神剑", 3, new DateTimeOffset(2026, 7, 16, 18, 28, 0, TimeSpan.FromHours(8))),
                    new(AccountId2, "100000000000000004", "奥黛塔", 5, new DateTimeOffset(2026, 8, 18, 18, 30, 0, TimeSpan.FromHours(8))),
                    new(AccountId2, "100000000000000005", "菲林斯", 5, new DateTimeOffset(2026, 6, 18, 9, 30, 0, TimeSpan.FromHours(8))),
                    new(AccountId2, "100000000000000006", "阿罗夏", 4, new DateTimeOffset(2026, 8, 18, 18, 30, 0, TimeSpan.FromHours(8))),
                    new(AccountId2, "100000000000000007", "阿蕾奇诺", 5, new DateTimeOffset(2026, 8, 18, 17, 30, 0, TimeSpan.FromHours(8)))];
        }

        private static IEnumerable<GachaRecord> ApplyQuery(
            IEnumerable<GachaRecord> source,
            GachaRecordQuery query)
        {
            HashSet<Guid> accountIds = query.GameAccountIds.ToHashSet();
            IEnumerable<GachaRecord> filtered = source.Where(
                record => accountIds.Contains(record.GameAccountId));

            if (query.RankTypes is not null)
            {
                filtered = filtered.Where(
                    record => record.RankType is int rank &&
                        query.RankTypes.Contains(rank));
            }
            if (query.PoolGroups is not null)
            {
                filtered = filtered.Where(
                    record => query.PoolGroups.Contains(
                        GachaPoolGroupResolver.Resolve(record)));
            }
            if (query.StartTime is DateTimeOffset start)
            {
                filtered = filtered.Where(record => record.Time >= start);
            }
            if (query.EndTime is DateTimeOffset end)
            {
                filtered = filtered.Where(record => record.Time <= end);
            }

            return filtered;
        }
    }
    /*
    public sealed class InMemoryGachaRecordRepository
        : IGachaRecordRepository
    {
        private static readonly Guid AccountId =
            Guid.Parse("90ca2f7f-327b-47bb-9562-0fdb3217dd26");

        private static readonly IReadOnlyList<GachaRecord> Records =
        [
            new(
            AccountId,
            "100000000000000001",
            "芙宁娜",
            5,
            new DateTimeOffset(2026, 7, 16, 18, 30, 0, TimeSpan.FromHours(8))),

        new(
            AccountId,
            "100000000000000002",
            "夏洛蒂",
            4,
            new DateTimeOffset(2026, 7, 16, 18, 29, 0, TimeSpan.FromHours(8))),

        new(
            AccountId,
            "100000000000000003",
            "黎明神剑",
            3,
            new DateTimeOffset(2026, 7, 16, 18, 28, 0, TimeSpan.FromHours(8)))
        ];

        public Task<IReadOnlyList<GachaRecord>> GetRecentAsync(
            int count,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IReadOnlyList<GachaRecord> result = Records
                .OrderByDescending(record => record.Time)
                .Take(count)
                .ToArray();

            return Task.FromResult(result);
        }
    }
    */
}
