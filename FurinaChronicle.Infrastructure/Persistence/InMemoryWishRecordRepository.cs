using FurinaChronicle.Core.Wishes;
using FurinaChronicle.Services.Abstractions;
using FurinaChronicle.Services.Wishes;
using System;
using System.Collections.Generic;
using System.Text;

namespace FurinaChronicle.Infrastructure.Persistence
{
    public sealed class InMemoryWishRecordRepository : IWishRecordRepository
    {
        private readonly List<WishRecord> records = [];
        private readonly HashSet<(Guid GameAccountId, string ExternalRecordId)> uniqueKeys = [];
        private readonly SemaphoreSlim gate = new(1, 1);

        public InMemoryWishRecordRepository() : this(CreateSampleRecords()) { }

        public InMemoryWishRecordRepository(IEnumerable<WishRecord> initialRecords)
        {
            ArgumentNullException.ThrowIfNull(initialRecords, nameof(initialRecords));

            foreach (var record in initialRecords)
            {
                var key = (record.GameAccountId, record.ExternalRecordId);
                if (uniqueKeys.Add(key)) records.Add(record);
            }
        }

        public async Task<IReadOnlyList<WishRecord>> GetRecentAsync(Guid gameAccountId, int count, CancellationToken cancellationToken = default)
        {
            if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count), "查询数量必须大于0");
            if (gameAccountId == Guid.Empty) throw new ArgumentException("游戏账号 ID 不能为空。", nameof(gameAccountId));
            await gate.WaitAsync(cancellationToken);
            try
            {
                return records.Where(record => record.GameAccountId.Equals(gameAccountId)).OrderByDescending(record => record.Time).Take(count).ToArray();
            }
            finally { gate?.Release(); }
        }

        public async Task<WishSaveResult> SaveBatchAsync(IReadOnlyCollection<WishRecord> newRecords, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(newRecords, nameof(newRecords));
            await gate.WaitAsync(cancellationToken);
            try
            {
                int insertedCount = 0;
                int duplicateCount = 0;
                foreach (var record in newRecords)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var key = (record.GameAccountId, record.ExternalRecordId);

                    if (!uniqueKeys.Add(key)) { duplicateCount++; continue; };
                    records.Add(record);
                    insertedCount++;
                }
                return new WishSaveResult(insertedCount, duplicateCount);
            }
            finally { gate?.Release(); }
        }

        public static IEnumerable<WishRecord> CreateSampleRecords()
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
    }
    /*
    public sealed class InMemoryWishRecordRepository
        : IWishRecordRepository
    {
        private static readonly Guid AccountId =
            Guid.Parse("90ca2f7f-327b-47bb-9562-0fdb3217dd26");

        private static readonly IReadOnlyList<WishRecord> Records =
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

        public Task<IReadOnlyList<WishRecord>> GetRecentAsync(
            int count,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IReadOnlyList<WishRecord> result = Records
                .OrderByDescending(record => record.Time)
                .Take(count)
                .ToArray();

            return Task.FromResult(result);
        }
    }
    */
}
