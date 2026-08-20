using System;
using System.Collections.Generic;
using System.Text;
using FurinaChronicle.Core.Archives;
using SQLite;

namespace FurinaChronicle.Infrastructure.Persistence.Sqlite;

[Table(TableName)]
internal sealed class GameAccountRow
{
    internal const string TableName = "GameAccounts";

    [PrimaryKey]
    public string Id { get; set; } = string.Empty;

    [NotNull]
    public string PlayerArchiveId { get; set; } = string.Empty;

    [NotNull]
    public string Uid { get; set; } = string.Empty;

    public int ServerRegion { get; set; }

    public string? DisplayName { get; set; }

    public bool IsPlaceholder { get; set; }

    public long CreatedAtUtcTicks { get; set; }

    public long UpdatedAtUtcTicks { get; set; }

    public static GameAccountRow FromDomain(GameAccount account)
    {
        return new GameAccountRow
        {
            Id = account.Id.ToString("D"),
            PlayerArchiveId = account.PlayerArchiveId.ToString("D"),
            Uid = account.Uid,
            ServerRegion = (int)account.ServerRegion,
            DisplayName = account.DisplayName,
            IsPlaceholder = account.IsPlaceholder,
            CreatedAtUtcTicks = account.CreatedAt.UtcDateTime.Ticks,
            UpdatedAtUtcTicks = account.UpdatedAt.UtcDateTime.Ticks
        };
    }

    public GameAccount ToDomain()
    {
        return new GameAccount(
            Guid.Parse(Id),
            Guid.Parse(PlayerArchiveId),
            Uid,
            (GameServerRegion)ServerRegion,
            DisplayName,
            IsPlaceholder,
            new DateTimeOffset(CreatedAtUtcTicks, TimeSpan.Zero),
            new DateTimeOffset(UpdatedAtUtcTicks, TimeSpan.Zero));
    }
}
