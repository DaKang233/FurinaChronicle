// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Text.Json.Serialization;

namespace FurinaChronicle.Core.Challenges;

public enum ChallengeParticipantOrigin
{
    Owned = 1,
    Trial = 2,
    Support = 3,
}

public sealed record TheaterRoundDetails
{
    public TheaterRoundDetails(bool? isTarot, int? tarotSerialNumber,
        IReadOnlyList<TheaterBuff>? buffDetails = null, string? buffSummary = null)
    {
        if (tarotSerialNumber is < 0) throw new ArgumentOutOfRangeException(nameof(tarotSerialNumber));
        IsTarot = isTarot;
        TarotSerialNumber = tarotSerialNumber;
        if (buffDetails?.Any(buff => buff is null) == true)
            throw new ArgumentException("A Theater buff list cannot contain null entries.", nameof(buffDetails));
        BuffDetails = buffDetails is { Count: > 0 } ? Array.AsReadOnly(buffDetails.ToArray()) : null;
        BuffSummary = string.IsNullOrWhiteSpace(buffSummary) ? null : buffSummary.Trim();
    }
    public bool? IsTarot { get; }
    public int? TarotSerialNumber { get; }
    // Absent extensions must not change existing observation payloads or content hashes.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<TheaterBuff>? BuffDetails { get; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BuffSummary { get; }
}

/// <summary>Source-provided display value; missing values remain unknown.</summary>
public sealed record TheaterAvatarStatistic
{
    public TheaterAvatarStatistic(ChallengeParticipant participant, string? value)
    {
        ArgumentNullException.ThrowIfNull(participant);
        if (value?.Length > 256) throw new ArgumentException("A statistic value is too long.", nameof(value));
        Participant = participant;
        Value = value;
    }
    public ChallengeParticipant Participant { get; }
    public string? Value { get; }
}

/// <summary>Statistics and actual backup roster for one official Theater observation.</summary>
public sealed record TheaterStatistics
{
    public TheaterStatistics(
        int? maxRoundId = null, int? heraldry = null, int? coinCount = null,
        int? avatarBonusCount = null, int? rentCount = null, int? tarotFinishedCount = null,
        int? totalUseTime = null, bool? showBattleStats = null,
        IReadOnlyList<int>? medalRounds = null,
        IReadOnlyList<ChallengeParticipant>? backupAvatars = null,
        TheaterAvatarStatistic? maxDefeat = null, TheaterAvatarStatistic? maxDamage = null,
        TheaterAvatarStatistic? maxTakeDamage = null, string? totalCoinsConsumed = null,
        IReadOnlyList<TheaterAvatarStatistic>? shortestAvatars = null)
    {
        if (maxRoundId is < 0 || heraldry is < 0 || coinCount is < 0 || avatarBonusCount is < 0 ||
            rentCount is < 0 || tarotFinishedCount is < 0 || totalUseTime is < 0)
            throw new ArgumentOutOfRangeException(nameof(maxRoundId), "Theater counters cannot be negative.");
        if (medalRounds?.Any(round => round <= 0) == true)
            throw new ArgumentException("Medal round IDs must be positive.", nameof(medalRounds));
        if (backupAvatars?.Any(avatar => avatar is null) == true ||
            shortestAvatars?.Any(avatar => avatar is null) == true)
            throw new ArgumentException("A Theater roster cannot contain null entries.");
        if (totalCoinsConsumed?.Length > 256)
            throw new ArgumentException("A statistic value is too long.", nameof(totalCoinsConsumed));
        MaxRoundId = maxRoundId;
        Heraldry = heraldry;
        CoinCount = coinCount;
        AvatarBonusCount = avatarBonusCount;
        RentCount = rentCount;
        TarotFinishedCount = tarotFinishedCount;
        TotalUseTime = totalUseTime;
        ShowBattleStats = showBattleStats;
        MedalRounds = Array.AsReadOnly((medalRounds ?? []).Distinct().Order().ToArray());
        BackupAvatars = Array.AsReadOnly((backupAvatars ?? []).ToArray());
        MaxDefeat = maxDefeat;
        MaxDamage = maxDamage;
        MaxTakeDamage = maxTakeDamage;
        TotalCoinsConsumed = totalCoinsConsumed;
        ShortestAvatars = Array.AsReadOnly((shortestAvatars ?? []).ToArray());
    }
    public int? MaxRoundId { get; }
    public int? Heraldry { get; }
    public int? CoinCount { get; }
    public int? AvatarBonusCount { get; }
    public int? RentCount { get; }
    public int? TarotFinishedCount { get; }
    public int? TotalUseTime { get; }
    public bool? ShowBattleStats { get; }
    public IReadOnlyList<int> MedalRounds { get; }
    public IReadOnlyList<ChallengeParticipant> BackupAvatars { get; }
    public TheaterAvatarStatistic? MaxDefeat { get; }
    public TheaterAvatarStatistic? MaxDamage { get; }
    public TheaterAvatarStatistic? MaxTakeDamage { get; }
    public string? TotalCoinsConsumed { get; }
    public IReadOnlyList<TheaterAvatarStatistic> ShortestAvatars { get; }
}
