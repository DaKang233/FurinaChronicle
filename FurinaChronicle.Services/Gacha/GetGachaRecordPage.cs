// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Services.Abstractions;

namespace FurinaChronicle.Services.Gacha;

public sealed class GetGachaRecordPage(IGachaRecordRepository repository)
{
    public async Task<GachaRecordPage> ExecuteAsync(
        Guid gameAccountId,
        int pageNumber,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        if (gameAccountId == Guid.Empty)
        {
            throw new ArgumentException(
                "游戏账号 ID 不能为空。",
                nameof(gameAccountId));
        }

        return await ExecuteAsync(
            new GachaRecordQuery([gameAccountId]),
            pageNumber,
            pageSize,
            cancellationToken);
    }

    public async Task<GachaRecordPage> ExecuteAsync(
        GachaRecordQuery query,
        int pageNumber,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        query.Validate();
        if (pageNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pageNumber),
                "页码必须大于零。");
        }

        if (pageSize is <= 0 or > 200)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pageSize),
                "每页数量必须处于 1 到 200 之间。");
        }

        GachaRecordQuery unpaged = query with { Offset = 0, Limit = null };
        int totalCount = await repository.CountAsync(unpaged, cancellationToken);
        int totalPages = Math.Max(
            1,
            (int)Math.Ceiling((double)totalCount / pageSize));
        int normalizedPage = Math.Min(pageNumber, totalPages);
        int offset = checked((normalizedPage - 1) * pageSize);

        IReadOnlyList<GachaRecord> records = await repository.QueryAsync(
            query with { Offset = offset, Limit = pageSize },
            cancellationToken);

        return new GachaRecordPage(
            records,
            totalCount,
            normalizedPage,
            pageSize);
    }
}
