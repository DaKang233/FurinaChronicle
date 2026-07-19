using System;
using System.Collections.Generic;
using System.Text;
using FurinaArchive.Core.Archives;
using FurinaArchive.Services.Abstractions;

namespace FurinaArchive.Services.Archives
{
    public sealed class CreatePlayerArchive(IPlayerArchiveRepository repository)
    {
        public async Task<PlayerArchive> ExecuteAsync(string name, CancellationToken cancellationToken = default)
        {
            string normalizedName = name.Trim();
            if (string.IsNullOrEmpty(normalizedName)) { throw new ArgumentException("档案名称不能为空",nameof(name)); }
            if (normalizedName.Length > 50) { throw new ArgumentException("档案名称不能超过 50 个字符", nameof(name)); }
            
            DateTimeOffset now = DateTimeOffset.UtcNow;

            var archive = new PlayerArchive(Guid.NewGuid(), normalizedName, now, now);
            await repository.AddAsync(archive, cancellationToken);
            return archive;
        }
    }
}
