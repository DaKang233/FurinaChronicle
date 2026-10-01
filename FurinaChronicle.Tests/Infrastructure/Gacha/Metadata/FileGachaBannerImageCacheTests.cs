// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Infrastructure.Gacha.Metadata;
using System.Net;

namespace FurinaChronicle.Tests.Infrastructure.Gacha.Metadata;

public sealed class FileGachaBannerImageCacheTests
{
    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl2n7sAAAAASUVORK5CYII=");

    [Fact]
    public async Task GetOrRefreshAsync_UsesBackupUrlAndThenReusesCache()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "FurinaChronicleTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        int requests = 0;
        using var client = new HttpClient(new StubHandler(request =>
        {
            Interlocked.Increment(ref requests);
            if (request.RequestUri?.AbsolutePath == "/primary.png")
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(OnePixelPng)
            };
        }));
        using var cache = new FileGachaBannerImageCache(
            new GachaBannerImageCacheOptions(directory),
            TimeProvider.System,
            client);
        try
        {
            string? first = await cache.GetOrRefreshAsync(
                GachaGame.GenshinImpact,
                "event-banner",
                "https://example.test/primary.png",
                "https://example.test/backup.png");
            string? second = await cache.GetOrRefreshAsync(
                GachaGame.GenshinImpact,
                "event-banner",
                "https://example.test/primary.png",
                "https://example.test/backup.png");

            Assert.NotNull(first);
            Assert.Equal(first, second);
            Assert.True(File.Exists(first));
            Assert.Equal(2, requests);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, HttpResponseMessage> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(handler(request));
        }
    }
}
