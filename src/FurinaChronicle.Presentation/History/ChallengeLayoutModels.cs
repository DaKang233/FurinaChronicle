// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

namespace FurinaChronicle.Presentation.History;

// Read-only layout hints. None of these values are persisted or exported as facts.
public enum ChallengeSectionKind
{
    Other, Summary, Metric, Blessing, AbyssFloor,
    TheaterSetup, TheaterRoster, TheaterSummary, TheaterRound,
    StygianSummary, StygianStage,
}

public sealed record ChallengeMetric(string Name, string Value, ChallengePortrait? Portrait = null);
public sealed record AbyssChamberDetail(string Title,
    IReadOnlyList<ChallengeCombatDetail> Upper, IReadOnlyList<ChallengeCombatDetail> Lower);
public sealed record ChallengeSeasonChoice(string Id, string Title, string Period, int SnapshotCount);

public sealed record StygianStageText(IReadOnlyList<string> Summary,
    IReadOnlyList<string> Descriptions, IReadOnlyList<string> Tags);
