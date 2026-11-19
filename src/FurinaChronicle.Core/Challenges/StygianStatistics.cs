// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

namespace FurinaChronicle.Core.Challenges;

public sealed record StygianRunSummary
{
    public StygianRunSummary(bool hasData, int? difficulty = null, int? seconds = null, string? iconUrl = null)
    {
        if (difficulty is < 0 || seconds is < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        if (!hasData && (difficulty.HasValue || seconds.HasValue || !string.IsNullOrWhiteSpace(iconUrl)))
            throw new ArgumentException("An empty run cannot have best-result fields.");
        HasData = hasData;
        Difficulty = difficulty;
        Seconds = seconds;
        IconUrl = string.IsNullOrWhiteSpace(iconUrl) ? null : iconUrl.Trim();
    }
    public bool HasData { get; }
    public int? Difficulty { get; }
    public int? Seconds { get; }
    public string? IconUrl { get; }
}

public sealed record StygianAvatarPerformance
{
    public StygianAvatarPerformance(ChallengeParticipant participant, long? value, int? type)
    {
        ArgumentNullException.ThrowIfNull(participant);
        if (value is < 0 || type is < 0) throw new ArgumentOutOfRangeException(nameof(value));
        Participant = participant;
        Value = value;
        Type = type;
    }
    public ChallengeParticipant Participant { get; }
    public long? Value { get; }
    // Source code: 1 strongest hit, 2 total damage. Unknown nonnegative codes remain explicit.
    public int? Type { get; }
}

public sealed record StygianMonsterTag
{
    public StygianMonsterTag(int? type, string? description)
    {
        if (type is < 0) throw new ArgumentOutOfRangeException(nameof(type));
        if (!type.HasValue && string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("An enemy tag requires a type or description.");
        Type = type;
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
    }
    public int? Type { get; }
    public string? Description { get; }
}

public sealed record StygianStageDetails
{
    public StygianStageDetails(string? monsterId = null, IReadOnlyList<string>? monsterDescriptions = null,
        IReadOnlyList<StygianMonsterTag>? monsterTags = null,
        IReadOnlyList<StygianAvatarPerformance>? bestAvatars = null)
    {
        if (monsterDescriptions?.Any(item => item is null) == true || monsterTags?.Any(item => item is null) == true ||
            bestAvatars?.Any(item => item is null) == true) throw new ArgumentException("Stage details cannot contain null entries.");
        MonsterId = string.IsNullOrWhiteSpace(monsterId) ? null : monsterId.Trim();
        MonsterDescriptions = Array.AsReadOnly((monsterDescriptions ?? []).ToArray());
        MonsterTags = Array.AsReadOnly((monsterTags ?? []).ToArray());
        BestAvatars = Array.AsReadOnly((bestAvatars ?? []).ToArray());
    }
    public string? MonsterId { get; }
    public IReadOnlyList<string> MonsterDescriptions { get; }
    public IReadOnlyList<StygianMonsterTag> MonsterTags { get; }
    public IReadOnlyList<StygianAvatarPerformance> BestAvatars { get; }
}

public sealed record StygianBlingAvatar
{
    public StygianBlingAvatar(ChallengeParticipant participant, bool? isPlus, string? element = null)
    {
        ArgumentNullException.ThrowIfNull(participant);
        Participant = participant;
        IsPlus = isPlus;
        Element = string.IsNullOrWhiteSpace(element) ? null : element.Trim();
    }
    public ChallengeParticipant Participant { get; }
    public bool? IsPlus { get; }
    public string? Element { get; }
}

public sealed record StygianStatistics
{
    public StygianStatistics(StygianRunSummary? singlePlayer = null, StygianRunSummary? multiplayer = null,
        IReadOnlyList<StygianBlingAvatar>? blingAvatars = null)
    {
        if (blingAvatars?.Any(item => item is null) == true) throw new ArgumentException("Bling roster cannot contain null entries.");
        SinglePlayer = singlePlayer;
        Multiplayer = multiplayer;
        BlingAvatars = Array.AsReadOnly((blingAvatars ?? []).ToArray());
    }
    public StygianRunSummary? SinglePlayer { get; }
    public StygianRunSummary? Multiplayer { get; }
    public IReadOnlyList<StygianBlingAvatar> BlingAvatars { get; }
}
