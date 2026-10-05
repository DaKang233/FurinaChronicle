// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Services.Abstractions;
using SQLite;

namespace FurinaChronicle.Infrastructure.Persistence.Sqlite;

public sealed class SqliteGameAccountRepository(FurinaDatabase database)
    : IGameAccountRepository
{
    private const string AccountSelect =
        """
        SELECT
            ga.Id,
            ga.PlayerArchiveId,
            ga.GameRoleIdentityId,
            ga.Uid,
            ga.ServerRegion,
            ga.DisplayName,
            ga.IsPlaceholder,
            ga.CreatedAtUtcTicks,
            ga.UpdatedAtUtcTicks,
            gri.GameBiz AS IdentityGameBiz,
            gri.Server AS IdentityServer,
            gri.Uid AS IdentityUid
        FROM GameAccounts AS ga
        LEFT JOIN GameRoleIdentities AS gri
            ON gri.Id = ga.GameRoleIdentityId
        """;

    public async Task<GameAccount?> GetByIdAsync(
        Guid gameAccountId,
        CancellationToken cancellationToken = default)
    {
        if (gameAccountId == Guid.Empty)
        {
            throw new ArgumentException(
                "游戏账号 ID 不能为空。",
                nameof(gameAccountId));
        }

        await database.InitializeAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        List<GameAccountReadRow> rows =
            await database.Connection.QueryAsync<GameAccountReadRow>(
                AccountSelect + "\n" +
                """
                WHERE ga.Id = ?
                LIMIT 1;
                """,
                gameAccountId.ToString("D"));

        cancellationToken.ThrowIfCancellationRequested();
        return rows.FirstOrDefault()?.ToDomain();
    }

    public async Task<GameAccount?> GetByArchiveIdAndUidAsync(
        Guid archiveId,
        string uid,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uid);
        if (archiveId == Guid.Empty)
        {
            throw new ArgumentException(
                "存档 ID 不能为空。",
                nameof(archiveId));
        }

        await database.InitializeAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        List<GameAccountReadRow> rows =
            await database.Connection.QueryAsync<GameAccountReadRow>(
                AccountSelect + "\n" +
                """
                WHERE ga.PlayerArchiveId = ? AND ga.Uid = ?
                ORDER BY ga.CreatedAtUtcTicks
                LIMIT 1;
                """,
                archiveId.ToString("D"),
                uid.Trim());

        return rows.FirstOrDefault()?.ToDomain();
    }

    public async Task UpdateAsync(
        GameAccount gameAccount,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(gameAccount);
        ValidateRoleIdentity(gameAccount);
        await database.InitializeAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        await database.Connection.RunInTransactionAsync(connection =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureRoleIdentity(connection, gameAccount.RoleIdentity);

            int affected = connection.Update(
                GameAccountRow.FromDomain(gameAccount));
            if (affected == 0)
            {
                throw new KeyNotFoundException("要更新的游戏账号不存在。");
            }
        });
    }

    public async Task DeleteAsync(
        Guid gameAccountId,
        CancellationToken cancellationToken = default)
    {
        if (gameAccountId == Guid.Empty)
        {
            throw new ArgumentException(
                "游戏账号 ID 不能为空。",
                nameof(gameAccountId));
        }

        await database.InitializeAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await database.Connection.RunInTransactionAsync(connection =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            int affected = connection.Execute(
                """
                DELETE FROM GameAccounts
                WHERE Id = ?;
                """,
                gameAccountId.ToString("D"));
            if (affected == 0)
            {
                throw new KeyNotFoundException("要删除的游戏账号不存在。");
            }
        });
    }

    public async Task AddAsync(
        GameAccount gameAccount,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(gameAccount);
        ValidateRoleIdentity(gameAccount);
        await database.InitializeAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        await database.Connection.RunInTransactionAsync(connection =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureRoleIdentity(connection, gameAccount.RoleIdentity);
            connection.Insert(GameAccountRow.FromDomain(gameAccount));
        });
    }

    public async Task<IReadOnlyList<GameAccount>> GetByArchiveIdAsync(
        Guid archiveId,
        CancellationToken cancellationToken = default)
    {
        if (archiveId == Guid.Empty)
        {
            throw new ArgumentException(
                "存档 ID 不能为空。",
                nameof(archiveId));
        }

        await database.InitializeAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        List<GameAccountReadRow> rows =
            await database.Connection.QueryAsync<GameAccountReadRow>(
                AccountSelect + "\n" +
                """
                WHERE ga.PlayerArchiveId = ?
                ORDER BY ga.CreatedAtUtcTicks;
                """,
                archiveId.ToString("D"));

        return rows.Select(row => row.ToDomain()).ToArray();
    }

    private static void EnsureRoleIdentity(
        SQLiteConnection connection,
        GameRoleIdentity? roleIdentity)
    {
        if (roleIdentity is null)
        {
            return;
        }

        GameRoleIdentityRow requested =
            GameRoleIdentityRow.FromDomain(roleIdentity);
        GameRoleIdentityRow? existingById = connection
            .Query<GameRoleIdentityRow>(
                """
                SELECT Id, GameBiz, Server, Uid
                FROM GameRoleIdentities
                WHERE Id = ?
                LIMIT 1;
                """,
                requested.Id)
            .FirstOrDefault();

        if (existingById is not null)
        {
            if (existingById.ToDomain() != roleIdentity)
            {
                throw new InvalidDataException(
                    $"Role identity {requested.Id} refers to different natural identities.");
            }

            return;
        }

        GameRoleIdentityRow? existingByNaturalIdentity = connection
            .Query<GameRoleIdentityRow>(
                """
                SELECT Id, GameBiz, Server, Uid
                FROM GameRoleIdentities
                WHERE GameBiz = ? AND Server = ? AND Uid = ?
                LIMIT 1;
                """,
                requested.GameBiz,
                requested.Server,
                requested.Uid)
            .FirstOrDefault();

        if (existingByNaturalIdentity is not null)
        {
            throw new InvalidOperationException(
                "The natural identity already exists with another role identity ID. " +
                "Resolve the import alias before saving the account.");
        }

        connection.Insert(requested);
    }

    private static void ValidateRoleIdentity(GameAccount gameAccount)
    {
        if (gameAccount.RoleIdentity is null)
        {
            return;
        }

        if (!GenshinGameRoleIdentity.TryCreate(
                gameAccount.Uid,
                gameAccount.ServerRegion,
                out GameRoleNaturalIdentity? expectedNaturalIdentity) ||
            expectedNaturalIdentity !=
                gameAccount.RoleIdentity.NaturalIdentity)
        {
            throw new InvalidDataException(
                "The game account fields do not match its resolved natural identity.");
        }
    }
}
