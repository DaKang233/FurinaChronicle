// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.Records;
using SQLite;
using System;
using System.Collections.Generic;
using System.Text;

namespace FurinaChronicle.Infrastructure.Persistence.Sqlite
{
    [Table(TableName)]
    internal class GachaRecordRow
    {
        internal const string TableName = "GachaRecords";

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

        public int Origin { get; set; }

        public long? FetchedAtUtcTicks { get; set; }

        public int? FetchedAtOffsetMinutes { get; set; }

        public long? ImportedAtUtcTicks { get; set; }

        public int? ImportedAtOffsetMinutes { get; set; }

        public string? AcquisitionBatchId { get; set; }

        public long Version { get; set; } = 1;

        public static GachaRecordRow FromDomain(GachaRecord record)
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

            ArgumentNullException.ThrowIfNull(record.Provenance);
            if (record.Provenance.Source is not null ||
                record.Provenance.Timestamps.ObservedAt is not null)
            {
                throw new NotSupportedException(
                    "Gacha persistence does not yet support source references or observation times.");
            }

            DateTimeOffset? fetchedAt =
                record.Provenance.Timestamps.FetchedAt;
            DateTimeOffset? importedAt =
                record.Provenance.Timestamps.ImportedAt;

            return new GachaRecordRow
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
                TimeOffsetMinutes = checked((int)record.Time.Offset.TotalMinutes),
                Origin = (int)record.Provenance.Origin,
                FetchedAtUtcTicks = fetchedAt?.UtcDateTime.Ticks,
                FetchedAtOffsetMinutes = fetchedAt is null
                    ? null
                    : checked((int)fetchedAt.Value.Offset.TotalMinutes),
                ImportedAtUtcTicks = importedAt?.UtcDateTime.Ticks,
                ImportedAtOffsetMinutes = importedAt is null
                    ? null
                    : checked((int)importedAt.Value.Offset.TotalMinutes),
                AcquisitionBatchId =
                    record.Provenance.AcquisitionBatchId?.ToString()
            };
        }

        public GachaRecord ToDomain()
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
            DateTimeOffset? fetchedAt = ReadOptionalTimestamp(
                FetchedAtUtcTicks,
                FetchedAtOffsetMinutes,
                nameof(FetchedAtUtcTicks));
            DateTimeOffset? importedAt = ReadOptionalTimestamp(
                ImportedAtUtcTicks,
                ImportedAtOffsetMinutes,
                nameof(ImportedAtUtcTicks));
            AcquisitionBatchId? batchId = null;
            if (!string.IsNullOrWhiteSpace(AcquisitionBatchId))
            {
                if (!Guid.TryParseExact(
                        AcquisitionBatchId,
                        "D",
                        out Guid parsedBatchId))
                {
                    throw new InvalidDataException(
                        $"数据库中的采集批次 ID「{AcquisitionBatchId}」无效。");
                }

                batchId = new AcquisitionBatchId(parsedBatchId);
            }

            var provenance = new RecordProvenance(
                (DataOrigin)Origin,
                new RecordTimestamps(
                    FetchedAt: fetchedAt,
                    ImportedAt: importedAt),
                acquisitionBatchId: batchId);

            return new GachaRecord(gameAccountId, ExternalRecordId, ItemName, RankType, originalTime)
            {
                ItemId = ItemId,
                ItemType = ItemType,
                GachaType = GachaType,
                UigfGachaType = UigfGachaType,
                Count = Count,
                Provenance = provenance
            };
        }

        private static DateTimeOffset? ReadOptionalTimestamp(
            long? utcTicks,
            int? offsetMinutes,
            string fieldName)
        {
            if (utcTicks is null && offsetMinutes is null)
            {
                return null;
            }

            if (utcTicks is null || offsetMinutes is null)
            {
                throw new InvalidDataException(
                    $"数据库中的时间字段「{fieldName}」不完整。");
            }

            if (offsetMinutes is < -840 or > 840)
            {
                throw new InvalidDataException(
                    $"数据库中的时间偏移「{offsetMinutes}」无效。");
            }

            var utc = new DateTimeOffset(utcTicks.Value, TimeSpan.Zero);
            return utc.ToOffset(TimeSpan.FromMinutes(offsetMinutes.Value));
        }
    }
}
