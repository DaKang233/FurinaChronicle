// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;

namespace FurinaChronicle.Infrastructure.Persistence.Sqlite;

internal sealed class GameAccountReadRow
{
    public string Id { get; set; } = string.Empty;

    public string PlayerArchiveId { get; set; } = string.Empty;

    public string? GameRoleIdentityId { get; set; }

    public string Uid { get; set; } = string.Empty;

    public int ServerRegion { get; set; }

    public string? DisplayName { get; set; }

    public bool IsPlaceholder { get; set; }

    public long CreatedAtUtcTicks { get; set; }

    public long UpdatedAtUtcTicks { get; set; }

    public string? IdentityGameBiz { get; set; }

    public string? IdentityServer { get; set; }

    public string? IdentityUid { get; set; }

    public GameAccount ToDomain()
    {
        GameRoleIdentity? roleIdentity = CreateRoleIdentity();

        return new GameAccount(
            Guid.Parse(Id),
            Guid.Parse(PlayerArchiveId),
            Uid,
            (GameServerRegion)ServerRegion,
            DisplayName,
            IsPlaceholder,
            new DateTimeOffset(CreatedAtUtcTicks, TimeSpan.Zero),
            new DateTimeOffset(UpdatedAtUtcTicks, TimeSpan.Zero),
            roleIdentity);
    }

    private GameRoleIdentity? CreateRoleIdentity()
    {
        if (string.IsNullOrWhiteSpace(GameRoleIdentityId))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(IdentityGameBiz) ||
            string.IsNullOrWhiteSpace(IdentityServer) ||
            string.IsNullOrWhiteSpace(IdentityUid))
        {
            throw new InvalidDataException(
                $"Game account {Id} references incomplete role identity data.");
        }

        return new GameRoleIdentity(
            new GameRoleIdentityId(Guid.Parse(GameRoleIdentityId)),
            new GameRoleNaturalIdentity(
                IdentityGameBiz,
                IdentityServer,
                IdentityUid));
    }
}
