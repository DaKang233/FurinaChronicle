// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Text;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Services.Abstractions;

namespace FurinaChronicle.Services.Archives;

public sealed class RenamePlayerArchive
{
    private readonly IPlayerArchiveRepository repository;
    private readonly IPortableApplicationSettingsStore settings;

    public RenamePlayerArchive(IPlayerArchiveRepository repository)
        : this(repository, AlwaysUniqueSettingsStore.Instance)
    {
    }

    public RenamePlayerArchive(
        IPlayerArchiveRepository repository,
        IPortableApplicationSettingsStore settings)
    {
        this.repository = repository;
        this.settings = settings;
    }

    public async Task<PlayerArchive> ExecuteAsync(Guid archiveId, string name, CancellationToken cancellationToken = default)
    {
        if (archiveId == Guid.Empty)
            throw new ArgumentException("档案 ID 不能为空。", nameof(archiveId));

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("档案名称不能为空。", nameof(name));

        if (name.Length > 50)
            throw new ArgumentException("档案名称不能超过 50 个字符。", nameof(name));

        PlayerArchive? archive = await repository.GetByIdAsync(archiveId, cancellationToken) ?? throw new KeyNotFoundException("要重命名的玩家档案不存在。");
        IReadOnlyList<PlayerArchive> existing =
            await repository.GetAllAsync(cancellationToken);
        if (await settings.GetRequireUniqueArchiveNamesAsync(cancellationToken) &&
            existing.Any(candidate =>
                candidate.Id != archiveId &&
                string.Equals(candidate.Name, name, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("已存在完全同名的玩家档案。");
        }

        PlayerArchive updated = archive with
        {
            Name = name,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await repository.UpdateAsync(updated, cancellationToken);

        return updated;
    }

    private sealed class AlwaysUniqueSettingsStore
        : IPortableApplicationSettingsStore
    {
        public static AlwaysUniqueSettingsStore Instance { get; } = new();

        public Task<bool> GetRequireUniqueArchiveNamesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task SetRequireUniqueArchiveNamesAsync(
            bool value,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
