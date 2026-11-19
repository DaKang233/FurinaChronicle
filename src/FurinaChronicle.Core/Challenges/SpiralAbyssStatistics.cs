// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

namespace FurinaChronicle.Core.Challenges;

/// <summary>Official per-avatar ranking value for one Abyss observation.</summary>
public sealed record SpiralAbyssRankEntry
{
    public SpiralAbyssRankEntry(string avatarId, long value, string? iconUrl, int? rarity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(avatarId);
        if (value < 0 || rarity is < 0) throw new ArgumentOutOfRangeException(nameof(value));
        AvatarId = avatarId.Trim();
        Value = value;
        IconUrl = string.IsNullOrWhiteSpace(iconUrl) ? null : iconUrl.Trim();
        Rarity = rarity;
    }

    public string AvatarId { get; }
    public long Value { get; }
    public string? IconUrl { get; }
    public int? Rarity { get; }
}

/// <summary>
/// Official season totals and ordered rankings. Missing counters remain unknown;
/// they are not reconstructed from the locally available chambers.
/// </summary>
public sealed record SpiralAbyssStatistics
{
    public SpiralAbyssStatistics(
        int? totalBattleTimes, int? totalWinTimes, string? maxFloor, int? totalStars,
        IReadOnlyList<SpiralAbyssRankEntry>? revealRank = null,
        IReadOnlyList<SpiralAbyssRankEntry>? defeatRank = null,
        IReadOnlyList<SpiralAbyssRankEntry>? damageRank = null,
        IReadOnlyList<SpiralAbyssRankEntry>? takeDamageRank = null,
        IReadOnlyList<SpiralAbyssRankEntry>? normalSkillRank = null,
        IReadOnlyList<SpiralAbyssRankEntry>? energySkillRank = null)
    {
        if (totalBattleTimes is < 0 || totalWinTimes is < 0 || totalStars is < 0)
            throw new ArgumentOutOfRangeException(nameof(totalBattleTimes));
        TotalBattleTimes = totalBattleTimes;
        TotalWinTimes = totalWinTimes;
        MaxFloor = string.IsNullOrWhiteSpace(maxFloor) ? null : maxFloor.Trim();
        TotalStars = totalStars;
        RevealRank = Copy(revealRank);
        DefeatRank = Copy(defeatRank);
        DamageRank = Copy(damageRank);
        TakeDamageRank = Copy(takeDamageRank);
        NormalSkillRank = Copy(normalSkillRank);
        EnergySkillRank = Copy(energySkillRank);
    }

    public int? TotalBattleTimes { get; }
    public int? TotalWinTimes { get; }
    public string? MaxFloor { get; }
    public int? TotalStars { get; }
    public IReadOnlyList<SpiralAbyssRankEntry> RevealRank { get; }
    public IReadOnlyList<SpiralAbyssRankEntry> DefeatRank { get; }
    public IReadOnlyList<SpiralAbyssRankEntry> DamageRank { get; }
    public IReadOnlyList<SpiralAbyssRankEntry> TakeDamageRank { get; }
    public IReadOnlyList<SpiralAbyssRankEntry> NormalSkillRank { get; }
    public IReadOnlyList<SpiralAbyssRankEntry> EnergySkillRank { get; }

    private static IReadOnlyList<SpiralAbyssRankEntry> Copy(IReadOnlyList<SpiralAbyssRankEntry>? values)
    {
        if (values?.Any(value => value is null) == true)
            throw new ArgumentException("A ranking cannot contain null entries.", nameof(values));
        return Array.AsReadOnly((values ?? []).ToArray());
    }
}
