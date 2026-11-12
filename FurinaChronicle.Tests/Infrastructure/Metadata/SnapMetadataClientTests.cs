// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Net;
using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.Gacha.Metadata;
using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Infrastructure.Metadata;
using FurinaChronicle.Services.Gacha.Abstractions;

namespace FurinaChronicle.Tests.Infrastructure.Metadata;

public sealed class SnapMetadataClientTests : IDisposable
{
    private static readonly Uri BaseAddress =
        new("https://fixture.invalid/Genshin/CHS/");
    private readonly string cacheDirectory = Path.Combine(
        Path.GetTempPath(), "furina-snap-metadata-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ResolvesAvatarWeaponAndSchedulesAndCachesFiles()
    {
        var requests = new List<string>();
        using var client = new HttpClient(new RecordingHandler(request =>
        {
            requests.Add(request.RequestUri!.AbsolutePath);
            return request.RequestUri.AbsolutePath switch
            {
                "/Genshin/CHS/Meta.json" => Json("""{"Meta":"fixture"}"""),
                "/Genshin/CHS/Avatar/10000093.json" => Json(
                    """{"Id":10000093,"Name":"闲云","Icon":"UI_AvatarIcon_Xianyun","Quality":5}"""),
                "/Genshin/CHS/Weapon.json" => Json(
                    """[{"Id":11501,"Name":"天空之刃","Icon":"UI_EquipIcon_Sword_Sky","RankLevel":5}]"""),
                "/Genshin/CHS/RoleCombatSchedule.json" => Json(
                    """[{"Id":31,"Begin":"2026-10-01T10:00:00+08:00","End":"2026-11-01T04:00:00+08:00"}]"""),
                "/Genshin/CHS/HardChallengeSchedule.json" => Json(
                    """[{"Id":5269013,"Begin":"2026-10-21T10:00:00+08:00","End":"2026-12-02T04:00:00+08:00","Name":"迅悍之役"}]"""),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            };
        }));
        using var snap = new SnapMetadataClient(
            new SnapMetadataOptions(cacheDirectory, BaseAddress), client);

        var avatar = await snap.ResolveAvatarAsync("10000093");
        Assert.NotNull(avatar);
        Assert.Equal("闲云", avatar.Name);
        Assert.Equal(5, avatar.Rarity);
        Assert.Equal("Snap.Metadata", avatar.Source);
        Assert.Contains("UI_AvatarIcon_Xianyun", avatar.IconUrl);

        var weapon = await snap.ResolveWeaponAsync("11501");
        Assert.NotNull(weapon);
        Assert.Equal("天空之刃", weapon.Name);
        Assert.Equal(5, weapon.Rarity);

        var theater = await snap.ResolveScheduleAsync(
            ChallengeMode.ImaginariumTheater, "31");
        Assert.NotNull(theater);
        Assert.Null(theater.Name);
        Assert.Equal(
            DateTimeOffset.Parse("2026-10-01T10:00:00+08:00"),
            theater.Start);

        var stygian = await snap.ResolveScheduleAsync(
            ChallengeMode.StygianOnslaught, "5269013");
        Assert.NotNull(stygian);
        Assert.Equal("迅悍之役", stygian.Name);

        int requestCount = requests.Count;
        Assert.Equal("闲云", (await snap.ResolveAvatarAsync("10000093"))!.Name);
        Assert.Equal(requestCount, requests.Count);
    }

    [Fact]
    public async Task OfflineAfterWarmCacheStillResolves()
    {
        string avatarJson =
            """{"Id":10000093,"Name":"闲云","Icon":"UI_AvatarIcon_Xianyun","Quality":5}""";
        string manifest = """{"Meta":"fixture"}""";
        bool online = true;
        var handler = new RecordingHandler(request =>
        {
            if (!online)
            {
                throw new HttpRequestException("offline");
            }
            return request.RequestUri!.AbsolutePath switch
            {
                "/Genshin/CHS/Meta.json" => Json(manifest),
                "/Genshin/CHS/Avatar/10000093.json" => Json(avatarJson),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            };
        });
        using (var warmupClient = new HttpClient(handler))
        using (var warmup = new SnapMetadataClient(
                   new SnapMetadataOptions(
                       cacheDirectory,
                       BaseAddress,
                       manifestCheckInterval: TimeSpan.FromDays(1)),
                   warmupClient))
        {
            Assert.NotNull(await warmup.ResolveAvatarAsync("10000093"));
        }

        online = false;
        using var coldClient = new HttpClient(handler);
        using var cold = new SnapMetadataClient(
            new SnapMetadataOptions(
                cacheDirectory,
                BaseAddress,
                manifestCheckInterval: TimeSpan.FromDays(1)),
            coldClient);
        Assert.Equal("闲云", (await cold.ResolveAvatarAsync("10000093"))!.Name);
    }

    [Fact]
    public async Task CompositeFallsBackToCultivationMetadata()
    {
        using var client = new HttpClient(new RecordingHandler(
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)));
        using var snap = new SnapMetadataClient(
            new SnapMetadataOptions(cacheDirectory, BaseAddress), client);
        var service = new CompositeDomainMetadataService(
            snap,
            new StubGachaMetadata("芙宁娜", "UI_AvatarIcon_Furina", 5));

        var item = await service.ResolveItemAsync(
            InventoryItemKind.Character, "10000089");

        Assert.NotNull(item);
        Assert.Equal("芙宁娜", item.Name);
        Assert.Equal("GenshinCalculator", item.Source);
        Assert.Equal("UI_AvatarIcon_Furina", item.IconUrl);
    }

    [Fact]
    public async Task UnknownIdIsNullRatherThanAnError()
    {
        using var client = new HttpClient(new RecordingHandler(
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)));
        using var snap = new SnapMetadataClient(
            new SnapMetadataOptions(cacheDirectory, BaseAddress), client);

        Assert.Null(await snap.ResolveAvatarAsync("not-a-number"));
        Assert.Null(await snap.ResolveWeaponAsync("999999"));
        Assert.Null(await snap.ResolveScheduleAsync(
            ChallengeMode.SpiralAbyss, "1"));
    }

    private static HttpResponseMessage Json(string content) =>
        new(HttpStatusCode.OK) { Content = new StringContent(content) };

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, HttpResponseMessage> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(send(request));
    }

    private sealed class StubGachaMetadata(
        string name,
        string icon,
        int rank) : IGachaItemMetadataProvider
    {
        public ValueTask<GachaItemMetadata?> FindByIdAsync(
            GachaGame game,
            string itemId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<GachaItemMetadata?>(new GachaItemMetadata(
                game,
                itemId,
                name,
                "角色",
                rank,
                icon));
    }

    public void Dispose()
    {
        if (Directory.Exists(cacheDirectory))
        {
            Directory.Delete(cacheDirectory, recursive: true);
        }
    }
}
