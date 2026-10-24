// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Gacha;

namespace FurinaChronicle.Tests.Core.Gacha;

public sealed class GachaPoolGroupResolverTests
{
    [Theory]
    [InlineData("301", "301", GachaPoolGroup.CharacterEvent)]
    [InlineData("301", "400", GachaPoolGroup.CharacterEvent)]
    [InlineData(null, "400", GachaPoolGroup.CharacterEvent)]
    [InlineData("302", "302", GachaPoolGroup.WeaponEvent)]
    [InlineData("200", "200", GachaPoolGroup.Standard)]
    [InlineData("100", "100", GachaPoolGroup.Novice)]
    [InlineData("500", "500", GachaPoolGroup.Chronicled)]
    [InlineData(null, "999", GachaPoolGroup.Unknown)]
    public void Resolve_MapsRawTypesToDisplayGroups(
        string? uigfType,
        string? gachaType,
        GachaPoolGroup expected)
    {
        Assert.Equal(
            expected,
            GachaPoolGroupResolver.Resolve(uigfType, gachaType));
    }
}
