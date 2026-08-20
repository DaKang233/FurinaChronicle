using System;
using System.Collections.Generic;
using System.Text;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Services.Abstractions;

namespace FurinaChronicle.Services.Archives;

public sealed class RenamePlayerArchive(IPlayerArchiveRepository repository)
{
    public async Task<PlayerArchive> ExecuteAsync(Guid archiveId, string name, CancellationToken cancellationToken = default)
    {
        if (archiveId == Guid.Empty)
            throw new ArgumentException("档案 ID 不能为空。", nameof(archiveId));

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("档案名称不能为空。", nameof(name));

        string normalizedName = name.Trim();

        if (normalizedName.Length > 50)
            throw new ArgumentException("档案名称不能超过 50 个字符。", nameof(name));

        PlayerArchive? archive = await repository.GetByIdAsync(archiveId, cancellationToken) ?? throw new KeyNotFoundException("要重命名的玩家档案不存在。");

        PlayerArchive updated = archive with
        {
            Name = normalizedName,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await repository.UpdateAsync(updated, cancellationToken);

        return updated;
    }
}
