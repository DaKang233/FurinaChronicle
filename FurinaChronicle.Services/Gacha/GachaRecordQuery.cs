// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Gacha;

namespace FurinaChronicle.Services.Gacha;

public enum GachaRecordSortOrder
{
    NewestFirst,
    OldestFirst
}

public sealed record GachaRecordQuery(
    IReadOnlyList<Guid> GameAccountIds,
    IReadOnlySet<int>? RankTypes = null,
    IReadOnlySet<GachaPoolGroup>? PoolGroups = null,
    DateTimeOffset? StartTime = null,
    DateTimeOffset? EndTime = null,
    int Offset = 0,
    int? Limit = 50,
    GachaRecordSortOrder SortOrder = GachaRecordSortOrder.NewestFirst)
{
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(GameAccountIds);
        if (GameAccountIds.Count == 0 ||
            GameAccountIds.Any(id => id == Guid.Empty))
        {
            throw new ArgumentException(
                "至少需要提供一个有效的游戏账号 ID。",
                nameof(GameAccountIds));
        }
        if (RankTypes is not null &&
            RankTypes.Any(rank => rank is < 3 or > 5))
        {
            throw new ArgumentOutOfRangeException(
                nameof(RankTypes),
                "星级筛选只能包含 3、4 或 5。");
        }
        if (StartTime is not null &&
            EndTime is not null &&
            StartTime > EndTime)
        {
            throw new ArgumentException("开始时间不能晚于结束时间。");
        }
        if (Offset < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Offset),
                "查询偏移量不能小于零。");
        }
        if (Limit is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Limit),
                "查询数量必须大于零。");
        }
        if (!Enum.IsDefined(SortOrder))
        {
            throw new ArgumentOutOfRangeException(nameof(SortOrder));
        }
    }
}
