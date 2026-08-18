using FurinaChronicle.Core.Archives;
using FurinaChronicle.Services.Abstractions;
using System;
using System.Collections.Generic;
using System.Data;
using System.Text;

namespace FurinaChronicle.Infrastructure.Persistence.Sqlite
{
    public sealed class SqliteGameAccountRepository(FurinaDatabase database) : IGameAccountRepository
    {
        public async Task<GameAccount?> GetByIdAsync(Guid gameAccountId, CancellationToken cancellationToken = default)
        {
            if ( gameAccountId == Guid.Empty)
            {
                throw new ArgumentException("游戏账号 ID 不能为空。", nameof(gameAccountId));
            }
            await database.InitializeAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            
            var rows = await database.Connection.QueryAsync<GameAccountRow>($"""
                    SELECT
                        Id,
                        PlayerArchiveId,
                        Uid,
                        ServerRegion,
                        DisplayName,
                        IsPlaceholder,
                        CreatedAtUtcTicks,
                        UpdatedAtUtcTicks
                    FROM {GameAccountRow.TableName}
                    WHERE Id = ?
                    LIMIT 1
                    """, gameAccountId.ToString("D"));
            cancellationToken.ThrowIfCancellationRequested();

            return rows.FirstOrDefault()?.ToDomain();
        }

        public async Task<GameAccount?> FindByUidAsync(GameServerRegion serverRegion, string uid, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(uid);
            await database.InitializeAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            var rows = await database.Connection.QueryAsync<GameAccountRow>($"""
                    SELECT
                        Id,
                        PlayerArchiveId,
                        Uid,
                        ServerRegion,
                        DisplayName,
                        IsPlaceholder,
                        CreatedAtUtcTicks,
                        UpdatedAtUtcTicks
                    FROM {GameAccountRow.TableName}
                    WHERE ServerRegion = ? AND Uid = ?
                    LIMIT 1
                    """, (int)serverRegion, uid);

            return rows.FirstOrDefault()?.ToDomain();
        }

        public async Task UpdateAsync(GameAccount gameAccount, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(gameAccount);
            await database.InitializeAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            await database.Connection.RunInTransactionAsync((connection) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                int affected = connection.Update(GameAccountRow.FromDomain(gameAccount));
                if (affected == 0)
                {
                    throw new KeyNotFoundException("要更新的游戏账号不存在。");
                }
            });
        }

        public async Task DeleteAsync(Guid gameAccountId, CancellationToken cancellationToken = default)
        {
            if (gameAccountId == Guid.Empty)
            {
                throw new ArgumentException("游戏账号 ID 不能为空。", nameof(gameAccountId));
            }
            await database.InitializeAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await database.Connection.RunInTransactionAsync((connection) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                int affected = connection.Execute($"""
                    DELETE FROM {GameAccountRow.TableName}
                    WHERE Id = ?
                    """, gameAccountId.ToString("D"));
                if (affected == 0)
                {
                    throw new KeyNotFoundException("要删除的游戏账号不存在。");
                }
            });
        }

        public async Task AddAsync(GameAccount gameAccount, CancellationToken cancellationToken = default)
        {
            if (gameAccount == null) { throw new ArgumentNullException(nameof(gameAccount), "游戏账号不能为空。"); }
            await database.InitializeAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await database.Connection.RunInTransactionAsync((connection) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                connection.Insert(GameAccountRow.FromDomain(gameAccount));
            });
        }
        
        public async Task<IReadOnlyList<GameAccount>> GetByArchiveIdAsync(Guid archiveId, CancellationToken cancellationToken = default)
        {
            if (archiveId == Guid.Empty)
            {
                throw new ArgumentException("存档 ID 不能为空。", nameof(archiveId));
            }
            await database.InitializeAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            var rows = await database.Connection.QueryAsync<GameAccountRow>($"""
                    SELECT
                        Id,
                        PlayerArchiveId,
                        Uid,
                        ServerRegion,
                        DisplayName,
                        IsPlaceholder,
                        CreatedAtUtcTicks,
                        UpdatedAtUtcTicks
                    FROM {GameAccountRow.TableName}
                    WHERE PlayerArchiveId = ?
                    """, archiveId.ToString("D"));

            return rows.Select(row => row.ToDomain()).ToArray();
        }
    }
}
