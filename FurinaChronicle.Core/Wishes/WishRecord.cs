using System;
using System.Collections.Generic;
using System.Text;

namespace FurinaChronicle.Core.Wishes
{
    public sealed record WishRecord(
        Guid GameAccountId,
        string ExternalRecordId,
        string? ItemName,
        int? RankType,
        DateTimeOffset Time)
    {
        public string? ItemId { get; init; }

        public string? ItemType { get; init; }

        public string? GachaType { get; init; }

        public string? UigfGachaType { get; init; }

        public int Count { get; init; } = 1;
    }
}
