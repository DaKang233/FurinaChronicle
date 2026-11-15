// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Services.Gacha.History;

namespace FurinaChronicle.Tests.Services.Gacha;

public sealed class GachaCorrectionTimeTests
{
    [Theory]
    [InlineData("2026-10-08T12:00:00Z", 0)]
    [InlineData("2026-10-08T12:00:00.1234567+08:00", 8)]
    public void ExplicitOffsetsArePreserved(string input, int offset)
    {
        Assert.True(GachaCorrectionTime.TryParse(input, out DateTimeOffset time));
        Assert.Equal(TimeSpan.FromHours(offset), time.Offset);
    }

    [Theory]
    [InlineData("2026-10-08T12:00:00")]
    [InlineData("2026-10-08T12:00")]
    public void MissingOffsetIsRejected(string input)
    {
        Assert.False(GachaCorrectionTime.TryParse(input, out _));
    }
}
