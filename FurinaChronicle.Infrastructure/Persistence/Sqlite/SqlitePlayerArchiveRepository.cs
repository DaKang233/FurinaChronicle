using FurinaChronicle.Core.Archives;
using FurinaChronicle.Services.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace FurinaChronicle.Infrastructure.Persistence.Sqlite
{
    public sealed class SqlitePlayerArchiveRepository(FurinaDatabase database) : IPlayerArchiveRepository
    {
        public async Task<PlayerArchive?> GetByIdAsync(Guid archiveId, CancellationToken cancellationToken = default)
        {
            if (archiveId == Guid.Empty)
            {
                throw new ArgumentException("存档 ID 不能为空。", nameof(archiveId));
            }
            await database.InitializeAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            
            var rows = await database.Connection.QueryAsync<PlayerArchiveRow>($"""
                SELECT
                    Id,
                    Name,
                    CreatedAtUtcTicks,
                    UpdatedAtUtcTicks
                FROM {PlayerArchiveRow.TableName}
                WHERE Id = ?
                LIMIT 1
                """, archiveId.ToString("D"));

            cancellationToken.ThrowIfCancellationRequested();

            var row = rows.FirstOrDefault();
            if (row == null) { return null; }
            var createdAtUtc = new DateTimeOffset(row.CreatedAtUtcTicks, TimeSpan.Zero);
            var updatedAtUtc = new DateTimeOffset(row.UpdatedAtUtcTicks, TimeSpan.Zero);

            return new PlayerArchive(archiveId, row.Name, createdAtUtc, updatedAtUtc);
        }

        public async Task UpdateAsync(PlayerArchive playerArchive, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(playerArchive);

            await database.InitializeAsync(cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            int affected = await database.Connection.UpdateAsync(PlayerArchiveRow.FromDomain(playerArchive));

            if (affected == 0)
            {
                throw new KeyNotFoundException("要更新的玩家档案不存在。");
            }
        }

        public async Task AddAsync(PlayerArchive playerArchive, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(playerArchive);
            await database.InitializeAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            await database.Connection.RunInTransactionAsync((connection) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                connection.Insert(PlayerArchiveRow.FromDomain(playerArchive));
            });
        }

        public async Task DeleteAsync(Guid archiveId, CancellationToken cancellationToken = default)
        {
            if (archiveId == Guid.Empty)
            {
                throw new ArgumentException("存档 ID 不能为空。", nameof(archiveId));
            }
            await database.InitializeAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await database.Connection.RunInTransactionAsync((connection) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                int affected = connection.Execute($"""
                    DELETE FROM {PlayerArchiveRow.TableName}
                    WHERE Id = ?
                    """, archiveId.ToString("D"));
                if (affected == 0)
                {
                    throw new KeyNotFoundException("要删除的玩家档案不存在。");
                }
                else Console.WriteLine($"Deleted {affected} rows from PlayerArchiveRow.");
            });
        }

        public async Task<IReadOnlyList<PlayerArchive>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            await database.InitializeAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            PlayerArchive[] result = [];

            var rows = await database.Connection.QueryAsync<PlayerArchiveRow>($"""
                SELECT
                    Id,
                    Name,
                    CreatedAtUtcTicks,
                    UpdatedAtUtcTicks
                FROM {PlayerArchiveRow.TableName}
                """);

            return rows.Select(row => row.ToDomain()).ToArray();
        }
    }
}
