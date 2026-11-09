// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Text;
using FurinaChronicle.Core.Records;

namespace FurinaChronicle.Core.Gacha
{
    public sealed record GachaRecord(
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

        public RecordProvenance Provenance { get; init; } =
            RecordProvenance.Unknown;
    }
}
