// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;

namespace FurinaChronicle.Tests.Core.Archives;

public sealed class GameRoleIdentityIdTests
{
    [Fact]
    public void Constructor_EmptyGuid_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(
            () => new GameRoleIdentityId(Guid.Empty));
    }

    [Fact]
    public void ValueEquality_UsesGuidValue()
    {
        Guid value = Guid.NewGuid();

        Assert.Equal(
            new GameRoleIdentityId(value),
            new GameRoleIdentityId(value));
    }

    [Fact]
    public void FromNaturalIdentity_UsesFrozenUuidVersionFiveContract()
    {
        var naturalIdentity = new GameRoleNaturalIdentity(
            "hk4e_global",
            "os_asia",
            "123456789");

        GameRoleIdentityId identityId =
            GameRoleIdentityId.FromNaturalIdentity(naturalIdentity);

        Assert.Equal(
            Guid.Parse("ee9bba11-9ddd-5778-89d5-fc735ad4c716"),
            identityId.Value);
        Assert.Equal(5, identityId.Value.Version);
    }

    [Fact]
    public void FromNaturalIdentity_DifferentServer_ReturnsDifferentIdentity()
    {
        var asia = new GameRoleNaturalIdentity(
            "hk4e_global",
            "os_asia",
            "123456789");
        var america = new GameRoleNaturalIdentity(
            "hk4e_global",
            "os_usa",
            "123456789");

        Assert.NotEqual(
            GameRoleIdentityId.FromNaturalIdentity(asia),
            GameRoleIdentityId.FromNaturalIdentity(america));
    }

    [Fact]
    public void ToString_UsesCanonicalGuidFormat()
    {
        Guid value = Guid.Parse("7d9b05f0-4e21-4c55-9d39-7a2bd4e47f2b");

        Assert.Equal(
            "7d9b05f0-4e21-4c55-9d39-7a2bd4e47f2b",
            new GameRoleIdentityId(value).ToString());
    }
}
