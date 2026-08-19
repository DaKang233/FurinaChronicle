using FurinaChronicle.Core.Archives;

namespace FurinaChronicle.Tests.Core.Archives;

public sealed class GameServerRegionResolverTests
{
    [Theory]
    [InlineData("123456789", GameServerRegion.ChinaOfficial)]
    [InlineData("187685434", GameServerRegion.ChinaOfficial)]
    [InlineData("200000001", GameServerRegion.ChinaOfficial)]
    [InlineData("300000001", GameServerRegion.ChinaOfficial)]
    [InlineData("500000001", GameServerRegion.ChinaBilibili)]
    [InlineData("600000001", GameServerRegion.America)]
    [InlineData("700000001", GameServerRegion.Europe)]
    [InlineData("800000001", GameServerRegion.Asia)]
    [InlineData("1800000001", GameServerRegion.Asia)]
    [InlineData("900000001", GameServerRegion.TaiwanHongKongMacao)]
    public void Resolve_KnownUidPrefix_ReturnsExpectedRegion(
        string uid,
        GameServerRegion expected)
    {
        Assert.Equal(expected, GameServerRegionResolver.Resolve(uid));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-uid")]
    [InlineData("400000001")]
    public void Resolve_InvalidOrUnknownUid_ReturnsUnknown(string uid)
    {
        Assert.Equal(
            GameServerRegion.Unknown,
            GameServerRegionResolver.Resolve(uid));
    }
}
