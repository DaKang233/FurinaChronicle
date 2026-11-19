// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Services.Archives.Portable;

namespace FurinaChronicle.Tests.Services.Archives.Portable;

public sealed class ArchiveChallengeSemanticDigestTests
{
    [Theory]
    [InlineData("score")]
    [InlineData("completion")]
    [InlineData("participant")]
    [InlineData("buff")]
    [InlineData("enemy")]
    [InlineData("difficulty")]
    [InlineData("medals")]
    public void ChallengeDetailsChangingWithoutChangingIdentityInvalidatesDigest(string change)
    {
        Assert.NotEqual(
            ArchiveRestoreSemanticDigest.ForChallenge(Create()),
            ArchiveRestoreSemanticDigest.ForChallenge(Create(change)));
    }

    [Fact]
    public void RemappingAnAccountPreservesPortableContentDigest()
    {
        Assert.Equal(
            ArchiveRestoreSemanticDigest.ForChallenge(Create()),
            ArchiveRestoreSemanticDigest.ForChallenge(Create(accountId: Guid.NewGuid())));
    }

    private static ChallengeObservation Create(string? change = null, Guid? accountId = null)
    {
        var stage = new ChallengeStageResult(
            "1", change == "score" ? 0 : 1,
            OccurrenceTime.At(
                DateTimeOffset.Parse(change == "completion"
                    ? "2026-10-03T15:16:18+08:00" : "2026-10-03T15:16:17+08:00"),
                TimePrecision.Second),
            [new ChallengeParticipant("10000001", "合成角色", null,
                change == "participant" ? 89 : 90, 5)],
            [change == "buff" ? "合成增益二" : "合成增益一"],
            [new ChallengeEnemy(change == "enemy" ? "合成敌人二" : "合成敌人一", 90, null)]);
        return new ChallengeObservation(
            new ChallengeObservationId(Guid.Parse("18f9a058-6e2c-4010-978a-09b0bb045c21")),
            accountId ?? Guid.Parse("da600001-0000-4000-8000-000000000001"),
            ChallengeMode.ImaginariumTheater, "31", null, null,
            DataCompleteness.Partial, [], RecordProvenance.Unknown,
            [stage], change == "difficulty" ? 4 : 3, change == "medals" ? 7 : 8);
    }
}
