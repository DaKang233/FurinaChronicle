// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Infrastructure.Ai;
using FurinaChronicle.Services.Ai;

namespace FurinaChronicle.Tests.Ai;

public sealed class AiConfigurationTests
{
    [Fact]
    public async Task KeysAreSeparateFromPublicSettingsAndBoundToRecipientAndProtocol()
    {
        string folder = Path.Combine(Path.GetTempPath(), "FurinaAiConfigTests", Guid.NewGuid().ToString("N"));
        try
        {
            var secrets = new Secrets();
            var store = new JsonAiConfigurationStore(Path.Combine(folder, "settings.json"), secrets);
            var first = new AiServiceSettings(BaseAddress: "https://first.example/v1", Model: "model");
            await store.SaveAsync(first); await store.SetApiKeyAsync(first, "synthetic-only-secret");
            Assert.Equal(first, await store.LoadAsync());
            Assert.DoesNotContain("synthetic-only-secret", await File.ReadAllTextAsync(Path.Combine(folder, "settings.json")));
            Assert.Equal("synthetic-only-secret", await store.GetApiKeyAsync(first with { Model = "another" }));
            Assert.Null(await store.GetApiKeyAsync(first with { BaseAddress = "https://second.example/v1" }));
            Assert.Null(await store.GetApiKeyAsync(first with { Protocol = AiProtocol.Ollama }));
            await store.SetApiKeyAsync(first, null); Assert.Null(await store.GetApiKeyAsync(first));
            Assert.Empty(Directory.GetFiles(folder, "*.tmp"));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
    [Fact]
    public async Task CorruptSettingsFailClearlyAndDoNotFallBackToAnotherRecipient()
    {
        string folder = Path.Combine(Path.GetTempPath(), "FurinaAiConfigTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string file = Path.Combine(folder, "settings.json"); await File.WriteAllTextAsync(file, "{invalid}");
            var store = new JsonAiConfigurationStore(file, new Secrets());
            await Assert.ThrowsAsync<AiAnalysisException>(() => store.LoadAsync());
            Assert.Equal("{invalid}", await File.ReadAllTextAsync(file));
        }
        finally { Directory.Delete(folder, true); }
    }
    private sealed class Secrets : IAiSecretStore
    {
        private readonly Dictionary<string,string> values = [];
        public Task<string?> GetAsync(string slot, CancellationToken cancellationToken = default) => Task.FromResult(values.GetValueOrDefault(slot));
        public Task SetAsync(string slot, string? value, CancellationToken cancellationToken = default)
        { if (value is null) values.Remove(slot); else values[slot] = value; return Task.CompletedTask; }
    }
}
