// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.Gacha.Metadata;
using FurinaChronicle.Presentation.Gacha;
using FurinaChronicle.Services.Gacha.Abstractions;
using FurinaChronicle.Services.Gacha.Metadata;

namespace FurinaChronicle.Tests.Presentation;

public sealed class GachaArtworkResolverTests
{
    [Fact]
    public async Task MissingIdCanResolveArtworkWithoutChangingTheOriginalReference()
    {
        var image = new GachaImageReference(null, null);
        var metadata = new Metadata();
        var resolved = await GachaArtworkResolver.ResolveAsync(image, "合成角色", metadata);
        Assert.Equal("10000089", resolved.ResourceKey);
        Assert.Equal("https://example.invalid/icon.png", resolved.SourceUrl);
        Assert.Null(image.ResourceKey);
        Assert.Null(image.SourceUrl);
        Assert.Equal(1, metadata.NameCalls);
        Assert.Equal(0, metadata.IdCalls);
    }

    [Fact]
    public async Task ExistingIdentityIsNotReplacedByNameOrContradictingMetadata()
    {
        var metadata = new Metadata();
        var original = new GachaImageReference("different-id", "/existing.png");
        var resolved = await GachaArtworkResolver.ResolveAsync(original, "合成角色", metadata);
        Assert.Equal(original, resolved);
        Assert.Equal(0, metadata.NameCalls);
        Assert.Equal(1, metadata.IdCalls);
    }

    [Fact]
    public async Task MetadataOutageRetainsOptionalArtworkAndCancellationStillPropagates()
    {
        var original = new GachaImageReference(null, null);
        Assert.Equal(original, await GachaArtworkResolver.ResolveAsync(original, "合成角色", new Metadata { Unavailable = true }));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await GachaArtworkResolver.ResolveAsync(original, "合成角色", new Metadata(), cancellation.Token));
    }

    [Fact]
    public async Task AbsentNameDoesNotAskTheProviderToGuess()
    {
        var original = new GachaImageReference(null, null);
        var metadata = new Metadata();
        Assert.Equal(original, await GachaArtworkResolver.ResolveAsync(original, null, metadata));
        Assert.Equal(0, metadata.NameCalls + metadata.IdCalls);
    }

    private sealed class Metadata : IGachaItemMetadataProvider
    {
        public bool Unavailable { get; init; }
        public int NameCalls { get; private set; }
        public int IdCalls { get; private set; }
        private GachaItemMetadata Resolve() => Unavailable
            ? throw new GachaMetadataUnavailableException("Synthetic outage", new IOException())
            : new(GachaGame.GenshinImpact, "10000089", "合成角色", "角色", 5, "https://example.invalid/icon.png");
        public ValueTask<GachaItemMetadata?> FindByIdAsync(GachaGame game, string itemId, CancellationToken cancellationToken = default)
        { IdCalls++; return new(Resolve()); }
        public ValueTask<GachaItemMetadata?> FindByNameAsync(GachaGame game, string itemName, CancellationToken cancellationToken = default)
        { NameCalls++; return new(Resolve()); }
    }
}
