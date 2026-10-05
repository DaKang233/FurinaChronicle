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
    [InlineData("100000001", GameServerRegion.ChinaOfficial, "hk4e_cn", "cn_gf01")]
    [InlineData("500000001", GameServerRegion.ChinaBilibili, "hk4e_cn", "cn_qd01")]
    [InlineData("600000001", GameServerRegion.America, "hk4e_global", "os_usa")]
    [InlineData("700000001", GameServerRegion.Europe, "hk4e_global", "os_euro")]
    [InlineData("800000001", GameServerRegion.Asia, "hk4e_global", "os_asia")]
    [InlineData(
        "900000001",
        GameServerRegion.TaiwanHongKongMacao,
        "hk4e_global",
        "os_cht")]
    public void GenshinCreate_KnownRegion_UsesOfficialCodes(
        string uid,
        GameServerRegion region,
        string expectedGameBiz,
        string expectedServer)
    {
        GameRoleNaturalIdentity identity =
            GenshinGameRoleIdentity.Create(uid, region);

        Assert.Equal(expectedGameBiz, identity.GameBiz);
        Assert.Equal(expectedServer, identity.Server);
    }

    [Fact]
    public void GenshinTryCreate_UnknownRegion_ReturnsFalse()
    {
        bool created = GenshinGameRoleIdentity.TryCreate(
            "400000001",
            GameServerRegion.Unknown,
            out GameRoleNaturalIdentity? identity);

        Assert.False(created);
        Assert.Null(identity);
    }

    [Fact]
    public void GenshinCreate_DerivedRegionConflict_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(
            () => GenshinGameRoleIdentity.Create(
                "800000001",
                GameServerRegion.America));
    }

    [Fact]
    public void GenshinCreate_UnknownUidPrefix_UsesManualFallback()
    {
        GameRoleNaturalIdentity identity =
            GenshinGameRoleIdentity.Create(
                "400000001",
                GameServerRegion.Asia);

        Assert.Equal("hk4e_global", identity.GameBiz);
        Assert.Equal("os_asia", identity.Server);
    }

    [Fact]
    public void ResolveServerRegion_KnownUid_DoesNotRequireManualFallback()
    {
        Assert.Equal(
            GameServerRegion.Asia,
            GenshinGameRoleIdentity.ResolveServerRegion(
                "800000001",
                GameServerRegion.Unknown));
    }
}
