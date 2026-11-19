// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Services.HistoryDomains;

namespace FurinaChronicle.Presentation.History;

/// <summary>Resolve only explicit source URLs or known game resource names; never infer from the score tier.</summary>
public static class StygianDifficultyArtwork
{
    public static bool IsBundledKey(string? key) =>
        key is { Length: 27 } && key.StartsWith("UI_LeyLineChallenge_Medal_", StringComparison.Ordinal) &&
        key[^1] is >= '0' and <= '7';

    public static ChallengePortrait? FromSource(string? source, string label)
    {
        if (ChallengeArtworkUri.Parse(source) is { } uri) return new(label, uri);
        string? key = source?.Split(',').Last().Trim();
        return IsBundledKey(key) ? new(label, null, ResourceKey: key) : null;
    }
}
