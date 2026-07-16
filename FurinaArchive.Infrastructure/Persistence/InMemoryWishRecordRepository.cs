using FurinaArchive.Core.Wishes;
using FurinaArchive.Services.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace FurinaArchive.Infrastructure.Persistence
{
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
}
