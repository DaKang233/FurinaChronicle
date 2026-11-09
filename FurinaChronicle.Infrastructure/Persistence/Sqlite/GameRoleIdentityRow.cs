// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using SQLite;

namespace FurinaChronicle.Infrastructure.Persistence.Sqlite;

[Table(TableName)]
internal sealed class GameRoleIdentityRow
{
    internal const string TableName = "GameRoleIdentities";

    [PrimaryKey]
    public string Id { get; set; } = string.Empty;

    [NotNull]
    public string GameBiz { get; set; } = string.Empty;

    [NotNull]
    public string Server { get; set; } = string.Empty;

    [NotNull]
    public string Uid { get; set; } = string.Empty;

    public static GameRoleIdentityRow FromDomain(GameRoleIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        return new GameRoleIdentityRow
        {
            Id = identity.Id.ToString(),
            GameBiz = identity.NaturalIdentity.GameBiz,
            Server = identity.NaturalIdentity.Server,
            Uid = identity.NaturalIdentity.Uid,
        };
    }

    public GameRoleIdentity ToDomain() =>
        new(
            new GameRoleIdentityId(Guid.Parse(Id)),
            new GameRoleNaturalIdentity(GameBiz, Server, Uid));
}
