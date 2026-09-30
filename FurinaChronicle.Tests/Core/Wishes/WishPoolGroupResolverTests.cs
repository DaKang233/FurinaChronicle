// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Wishes;

namespace FurinaChronicle.Tests.Core.Wishes;

public sealed class WishPoolGroupResolverTests
{
    [Theory]
    [InlineData("301", "301", WishPoolGroup.CharacterEvent)]
    [InlineData("301", "400", WishPoolGroup.CharacterEvent)]
    [InlineData(null, "400", WishPoolGroup.CharacterEvent)]
    [InlineData("302", "302", WishPoolGroup.WeaponEvent)]
    [InlineData("200", "200", WishPoolGroup.Standard)]
    [InlineData("100", "100", WishPoolGroup.Novice)]
    [InlineData("500", "500", WishPoolGroup.Chronicled)]
    [InlineData(null, "999", WishPoolGroup.Unknown)]
    public void Resolve_MapsRawTypesToDisplayGroups(
        string? uigfType,
        string? gachaType,
        WishPoolGroup expected)
    {
        Assert.Equal(
            expected,
            WishPoolGroupResolver.Resolve(uigfType, gachaType));
    }
}
