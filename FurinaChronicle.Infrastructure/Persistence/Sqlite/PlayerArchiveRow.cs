// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Text;
using FurinaChronicle.Core.Archives;
using SQLite;

namespace FurinaChronicle.Infrastructure.Persistence.Sqlite;

[Table(TableName)]
internal sealed class PlayerArchiveRow
{
    internal const string TableName = "PlayerArchives";

    [PrimaryKey]
    public string Id { get; set; } = string.Empty;

    [NotNull]
    public string Name { get; set; } = string.Empty;

    public long CreatedAtUtcTicks { get; set; }

    public int? CreatedAtOffsetMinutes { get; set; }

    public long UpdatedAtUtcTicks { get; set; }

    public int? UpdatedAtOffsetMinutes { get; set; }

    public static PlayerArchiveRow FromDomain(PlayerArchive archive)
    {
        return new PlayerArchiveRow
        {
            Id = archive.Id.ToString("D"),
            Name = archive.Name,
            CreatedAtUtcTicks = archive.CreatedAt.UtcDateTime.Ticks,
            CreatedAtOffsetMinutes =
                checked((int)archive.CreatedAt.Offset.TotalMinutes),
            UpdatedAtUtcTicks = archive.UpdatedAt.UtcDateTime.Ticks,
            UpdatedAtOffsetMinutes =
                checked((int)archive.UpdatedAt.Offset.TotalMinutes),
        };
    }

    public PlayerArchive ToDomain()
    {
        return new PlayerArchive(
            Guid.Parse(Id),
            Name,
            ReadTimestamp(CreatedAtUtcTicks, CreatedAtOffsetMinutes),
            ReadTimestamp(UpdatedAtUtcTicks, UpdatedAtOffsetMinutes));
    }

    private static DateTimeOffset ReadTimestamp(long utcTicks, int? offsetMinutes) =>
        new DateTimeOffset(new DateTime(utcTicks, DateTimeKind.Utc))
            .ToOffset(TimeSpan.FromMinutes(offsetMinutes ?? 0));
}
