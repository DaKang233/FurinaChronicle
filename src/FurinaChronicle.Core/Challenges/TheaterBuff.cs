// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

namespace FurinaChronicle.Core.Challenges;

public enum TheaterBuffKind { ChoiceCard = 1, Buff = 2, Splendour = 3 }

/// <summary>Source details within one round; neither name nor list position is a global fact identity.</summary>
public sealed record TheaterBuff
{
    public TheaterBuff(TheaterBuffKind kind, string name, long? sourceId = null,
        string? description = null, string? iconUrl = null, int? level = null,
        bool? isEnhanced = null, IReadOnlyList<string>? effects = null)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (sourceId is < 0 || level is < 0) throw new ArgumentOutOfRangeException(nameof(sourceId));
        if (effects?.Any(effect => effect is null) == true)
            throw new ArgumentException("A buff effect cannot be null.", nameof(effects));
        Kind = kind;
        Name = name.Trim();
        SourceId = sourceId;
        Description = Clean(description);
        IconUrl = string.IsNullOrWhiteSpace(iconUrl) ? null : iconUrl.Trim();
        Level = level;
        IsEnhanced = isEnhanced;
        Effects = Array.AsReadOnly((effects ?? []).Select(effect => Clean(effect) ?? string.Empty).ToArray());
    }
    public TheaterBuffKind Kind { get; }
    public string Name { get; }
    public long? SourceId { get; }
    public string? Description { get; }
    public string? IconUrl { get; }
    public int? Level { get; }
    public bool? IsEnhanced { get; }
    public IReadOnlyList<string> Effects { get; }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null :
        value.Replace("\\n", "\n", StringComparison.Ordinal).Trim();
}
