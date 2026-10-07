// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Text;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Services.Abstractions;

namespace FurinaChronicle.Services.Archives
{
    public sealed class CreatePlayerArchive(IPlayerArchiveRepository repository)
    {
        public async Task<PlayerArchive> ExecuteAsync(string name, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(name)) { throw new ArgumentException("档案名称不能为空",nameof(name)); }
            if (name.Length > 50) { throw new ArgumentException("档案名称不能超过 50 个字符", nameof(name)); }
            IReadOnlyList<PlayerArchive> existing =
                await repository.GetAllAsync(cancellationToken);
            if (existing.Any(archive =>
                    string.Equals(archive.Name, name, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException("已存在完全同名的玩家档案。");
            }
            
            DateTimeOffset now = DateTimeOffset.UtcNow;

            var archive = new PlayerArchive(Guid.NewGuid(), name, now, now);
            await repository.AddAsync(archive, cancellationToken);
            return archive;
        }
    }
}
