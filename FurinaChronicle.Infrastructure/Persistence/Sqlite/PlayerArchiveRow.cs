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

    public long UpdatedAtUtcTicks { get; set; }

    public static PlayerArchiveRow FromDomain(PlayerArchive archive)
    {
        return new PlayerArchiveRow
        {
            Id = archive.Id.ToString("D"),
            Name = archive.Name,
            CreatedAtUtcTicks = archive.CreatedAt.UtcDateTime.Ticks,
            UpdatedAtUtcTicks = archive.UpdatedAt.UtcDateTime.Ticks
        };
    }

    public PlayerArchive ToDomain()
    {
        return new PlayerArchive(
            Guid.Parse(Id),
            Name,
            new DateTimeOffset(CreatedAtUtcTicks, TimeSpan.Zero),
            new DateTimeOffset(UpdatedAtUtcTicks, TimeSpan.Zero));
    }
}