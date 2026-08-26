using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Infrastructure.Gacha.Metadata.Localization;

namespace FurinaChronicle.Tests.Infrastructure.Gacha.Metadata;

public sealed class EmbeddedGachaLocalizationSourceTests
{
    [Fact]
    public async Task LoadAsync_GenshinResourceContainsExpectedLocalizedNames()
    {
        var source = new EmbeddedGachaLocalizationSource();

        IReadOnlyDictionary<
            string,
            IReadOnlyDictionary<string, string>> result =
            await source.LoadAsync(GachaGame.GenshinImpact);

        IReadOnlyDictionary<string, string> furina = result["10000089"];
        Assert.Equal("\u8299\u5b81\u5a1c", furina["chs"]);
        Assert.Equal("Furina", furina["en"]);
        Assert.True(result.Count > 100);
    }
}
