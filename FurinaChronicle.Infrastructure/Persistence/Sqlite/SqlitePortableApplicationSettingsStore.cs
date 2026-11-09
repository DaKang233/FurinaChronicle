// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Services.Abstractions;

namespace FurinaChronicle.Infrastructure.Persistence.Sqlite;

public sealed class SqlitePortableApplicationSettingsStore(
    FurinaDatabase database) : IPortableApplicationSettingsStore
{
    public async Task<bool> GetRequireUniqueArchiveNamesAsync(
        CancellationToken cancellationToken = default)
    {
        await database.InitializeAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return await database.Connection.ExecuteScalarAsync<int>(
            "SELECT RequireUniqueArchiveNames FROM PortableApplicationSettings WHERE Id = 1;") != 0;
    }

    public async Task SetRequireUniqueArchiveNamesAsync(
        bool value,
        CancellationToken cancellationToken = default)
    {
        await database.InitializeAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await database.Connection.RunInTransactionAsync(connection =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (value && connection.ExecuteScalar<int>(
                    """
                    SELECT COUNT(*)
                    FROM (
                        SELECT Name
                        FROM PlayerArchives
                        GROUP BY Name
                        HAVING COUNT(*) > 1
                    );
                    """) > 0)
            {
                throw new InvalidOperationException(
                    "存在完全同名的玩家档案，必须先完成重命名才能启用唯一名称约束。");
            }

            int affected = connection.Execute(
                "UPDATE PortableApplicationSettings SET RequireUniqueArchiveNames = ? WHERE Id = 1;",
                value ? 1 : 0);
            if (affected != 1)
            {
                throw new InvalidDataException(
                    "The portable application settings row is missing.");
            }
        });
    }
}
