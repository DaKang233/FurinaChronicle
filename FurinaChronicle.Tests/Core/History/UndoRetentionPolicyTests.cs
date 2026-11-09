// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.History;

namespace FurinaChronicle.Tests.Core.History;

public sealed class UndoRetentionPolicyTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(1000)]
    public void ValidateLimit_AcceptsSupportedValues(int value)
    {
        Assert.Equal(value, UndoRetentionPolicy.ValidateLimit(value));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1001)]
    public void ValidateLimit_RejectsUnsupportedValues(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => UndoRetentionPolicy.ValidateLimit(value));
    }

    [Fact]
    public void ShouldCaptureNewMaterial_RequiresEveryArchiveToEnableUndo()
    {
        Assert.True(
            UndoRetentionPolicy.ShouldCaptureNewMaterial([3, 1]));
        Assert.False(
            UndoRetentionPolicy.ShouldCaptureNewMaterial([3, 0]));
    }

    [Fact]
    public void FindCapacityEvictions_UsesNewestPerArchiveAndReturnsSharedChangeSet()
    {
        Guid archiveA = Guid.NewGuid();
        Guid archiveB = Guid.NewGuid();
        Guid newest = Guid.NewGuid();
        Guid sharedOldest = Guid.NewGuid();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        UndoHistoryCandidate[] candidates =
        [
            new(newest, now, [archiveA], true),
            new(sharedOldest, now.AddMinutes(-1), [archiveA, archiveB], true),
        ];

        IReadOnlyList<Guid> evictions =
            UndoRetentionPolicy.FindCapacityEvictions(
                archiveA,
                1,
                candidates);

        Assert.Equal([sharedOldest], evictions);
    }

    [Fact]
    public void FindCapacityEvictions_ZeroRetainsExistingHistory()
    {
        Guid archiveId = Guid.NewGuid();
        var candidate = new UndoHistoryCandidate(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            [archiveId],
            true);

        Assert.Empty(
            UndoRetentionPolicy.FindCapacityEvictions(
                archiveId,
                0,
                [candidate]));
    }
}
