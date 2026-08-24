using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Services.Gacha.Abstractions;
using FurinaChronicle.Services.Gacha.Metadata;

namespace FurinaChronicle.App.Startup;

public sealed class ApplicationStartupService(IGachaMetadataRefreshService metadataRefreshService)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        GachaMetadataRefreshResult result =
            await metadataRefreshService.RefreshIfNeededAsync(
                GachaGame.GenshinImpact,
                force: false,
                cancellationToken);

        // 防御性检查，避免以后其他实现错误地返回“无数据但成功”。
        bool metadataAvailable = result.ContentUpdated || result.UsedExistingCache;

        if (!metadataAvailable)
        {
            throw new GachaMetadataUnavailableException("没有可用的原神角色和武器元数据。", new InvalidDataException("Metadata refresh completed without producing a usable cache."));
        }
    }
}