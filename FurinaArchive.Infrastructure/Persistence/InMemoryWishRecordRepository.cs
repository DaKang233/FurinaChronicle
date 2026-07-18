using FurinaArchive.Core.Wishes;
using FurinaArchive.Services.Abstractions;
using FurinaArchive.Services.Wishes;
using System;
using System.Collections.Generic;
using System.Text;

namespace FurinaArchive.Infrastructure.Persistence
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

        public async Task<IReadOnlyList<WishRecord>> GetRecentAsync(int count,  CancellationToken cancellationToken = default)
        {
            if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count), "查询数量必须大于0");
            await gate.WaitAsync(cancellationToken);
            try
            {
                return records.OrderByDescending(record => record.Time).Take(count).ToArray();
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
            Guid AccountId = Guid.Parse("90ca2f7f-327b-47bb-9562-0fdb3217dd26");
            return [new(AccountId, "100000000000000001", "芙宁娜", 5, new DateTimeOffset(2026, 7, 16, 18, 30, 0, TimeSpan.FromHours(8))),
                    new(AccountId, "100000000000000002", "夏洛蒂", 4, new DateTimeOffset(2026, 7, 16, 18, 29, 0, TimeSpan.FromHours(8))),
                    new(AccountId, "100000000000000003", "黎明神剑", 3, new DateTimeOffset(2026, 7, 16, 18, 28, 0, TimeSpan.FromHours(8)))];
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
