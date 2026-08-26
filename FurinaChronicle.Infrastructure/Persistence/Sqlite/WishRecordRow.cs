using FurinaChronicle.Core.Wishes;
using SQLite;
using System;
using System.Collections.Generic;
using System.Text;

namespace FurinaChronicle.Infrastructure.Persistence.Sqlite
{
    [Table(TableName)]
    internal sealed class WishRecordRow
    {
        internal const string TableName = "WishRecords";

        [PrimaryKey]
        [AutoIncrement]
        public long Id { get; set; }

        [NotNull]
        public string GameAccountId { get; set; } = string.Empty;

        [NotNull]
        public string ExternalRecordId { get; set; } = string.Empty;

        public string? ItemName { get; set; }

        public string? ItemId { get; set; }

        public string? ItemType { get; set; }

        public string? GachaType { get; set; }

        public string? UigfGachaType { get; set; }

        public int? RankType { get; set; }

        public int Count { get; set; }

        public long TimeUtcTicks { get; set; }

        public int TimeOffsetMinutes { get; set; }

        public static WishRecordRow FromDomain(WishRecord record)
        {
            ArgumentNullException.ThrowIfNull(record);

            if (record.GameAccountId == Guid.Empty)
            {
                throw new ArgumentException("游戏账号 ID 不能为空。", nameof(record));
            }

            if (string.IsNullOrWhiteSpace(record.ExternalRecordId))
            {
                throw new ArgumentException("外部记录 ID 不能为空。", nameof(record));
            }

            if (string.IsNullOrWhiteSpace(record.ItemName) &&
                string.IsNullOrWhiteSpace(record.ItemId))
            {
                throw new ArgumentException("物品名称不能为空。", nameof(record));
            }

            if (record.RankType is < 3 or > 5)
            {
                throw new ArgumentOutOfRangeException(nameof(record), "星级必须处于 3 到 5 之间。");
            }

            if (record.Count <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(record), "Count must be greater than zero.");
            }

            return new WishRecordRow
            {
                GameAccountId = record.GameAccountId.ToString("D"),
                ExternalRecordId = record.ExternalRecordId,
                ItemName = record.ItemName,
                ItemId = record.ItemId,
                ItemType = record.ItemType,
                GachaType = record.GachaType,
                UigfGachaType = record.UigfGachaType,
                RankType = record.RankType,
                Count = record.Count,
                TimeUtcTicks = record.Time.UtcDateTime.Ticks,
                TimeOffsetMinutes = checked((int)record.Time.Offset.TotalMinutes)
            };
        }

        public WishRecord ToDomain()
        {
            if (!Guid.TryParse(GameAccountId, out Guid gameAccountId))
            {
                throw new InvalidDataException($"数据库中的账号 ID「{GameAccountId}」无效。");
            }
            if (TimeOffsetMinutes is < -840 or > 840)
            {
                throw new InvalidDataException($"数据库中的时间偏移「{TimeOffsetMinutes}」无效。");
            }
            TimeSpan offset = TimeSpan.FromMinutes(TimeOffsetMinutes);
            DateTimeOffset utcTime = new DateTimeOffset(TimeUtcTicks, TimeSpan.Zero);
            DateTimeOffset originalTime = utcTime.ToOffset(offset);

            return new WishRecord(gameAccountId, ExternalRecordId, ItemName, RankType, originalTime)
            {
                ItemId = ItemId,
                ItemType = ItemType,
                GachaType = GachaType,
                UigfGachaType = UigfGachaType,
                Count = Count
            };
        }
    }
}
