// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;

namespace FurinaChronicle.Tests.Core.Archives;

public sealed class GameRoleNaturalIdentityTests
{
    [Fact]
    public void Constructor_NormalizesCodesAndPreservesUidLeadingZeros()
    {
        var identity = new GameRoleNaturalIdentity(
            " HK4E_GLOBAL ",
            " OS_ASIA ",
            " 001234567 ");

        Assert.Equal("hk4e_global", identity.GameBiz);
        Assert.Equal("os_asia", identity.Server);
        Assert.Equal("001234567", identity.Uid);
        Assert.Equal(
            "11:hk4e_global7:os_asia9:001234567",
            identity.ToDeterministicName());
    }

    [Theory]
    [InlineData("hk4e-global", "os_asia", "123456789")]
    [InlineData("hk4e_global", "os asia", "123456789")]
    [InlineData("hk4e_global", "os_asia", "123x56789")]
    public void Constructor_InvalidCanonicalComponent_ThrowsArgumentException(
        string gameBiz,
        string server,
        string uid)
    {
        Assert.Throws<ArgumentException>(
            () => new GameRoleNaturalIdentity(gameBiz, server, uid));
    }

    [Theory]
    [InlineData(GameServerRegion.ChinaOfficial, "hk4e_cn", "cn_gf01")]
    [InlineData(GameServerRegion.ChinaBilibili, "hk4e_cn", "cn_qd01")]
    [InlineData(GameServerRegion.America, "hk4e_global", "os_usa")]
    [InlineData(GameServerRegion.Europe, "hk4e_global", "os_euro")]
    [InlineData(GameServerRegion.Asia, "hk4e_global", "os_asia")]
    [InlineData(
        GameServerRegion.TaiwanHongKongMacao,
        "hk4e_global",
        "os_cht")]
    public void GenshinCreate_KnownRegion_UsesOfficialCodes(
        GameServerRegion region,
        string expectedGameBiz,
        string expectedServer)
    {
        GameRoleNaturalIdentity identity =
            GenshinGameRoleIdentity.Create("123456789", region);

        Assert.Equal(expectedGameBiz, identity.GameBiz);
        Assert.Equal(expectedServer, identity.Server);
    }

    [Fact]
    public void GenshinTryCreate_UnknownRegion_ReturnsFalse()
    {
        bool created = GenshinGameRoleIdentity.TryCreate(
            "123456789",
            GameServerRegion.Unknown,
            out GameRoleNaturalIdentity? identity);

        Assert.False(created);
        Assert.Null(identity);
    }
}
