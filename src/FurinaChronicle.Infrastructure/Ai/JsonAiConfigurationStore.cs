// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FurinaChronicle.Services.Ai;

namespace FurinaChronicle.Infrastructure.Ai;

/// <summary>Device configuration only. Keys are stored separately and bound to provider and recipient.</summary>
public sealed class JsonAiConfigurationStore(string filePath, IAiSecretStore secrets) : IAiProviderConfigurationStore
{
    private static readonly Guid LegacyProvider = new("75400db6-e519-48b2-a99a-b5f634137eb7");
    private static readonly Guid LegacyModel = new("69c6b4da-ab91-4259-877f-a75ac7818e35");
    private readonly SemaphoreSlim gate = new(1, 1);
    private sealed record Envelope(int Version, AiProviderConfiguration Configuration);

    public async Task<AiServiceSettings> LoadAsync(CancellationToken cancellationToken = default) =>
        (await LoadProvidersAsync(cancellationToken)).Current;

    public async Task<AiProviderConfiguration> LoadProvidersAsync(CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try { return await ReadLockedAsync(token); }
        finally { gate.Release(); }
    }
    private async Task<AiProviderConfiguration> ReadLockedAsync(CancellationToken token)
    {
        if (!File.Exists(filePath)) return new(null, []);
        try
        {
            if (new FileInfo(filePath).Length > 2 * 1024 * 1024) throw new AiAnalysisException("AI 配置超过本机大小限制。");
            string text = await File.ReadAllTextAsync(filePath, token);
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.TryGetProperty("version", out var version))
            {
                if (version.GetInt32() != 2) throw new AiAnalysisException("此 AI 配置版本不受支持，已保留原文件。");
                var envelope = JsonSerializer.Deserialize<Envelope>(text, AiJson.Options)
                    ?? throw new AiAnalysisException("AI 配置无效。");
                Validate(envelope.Configuration);
                return envelope.Configuration;
            }
            if (!document.RootElement.TryGetProperty("base_address", out _))
                throw new AiAnalysisException("AI 配置无效，已保留原文件。");
            var old = JsonSerializer.Deserialize<AiServiceSettings>(text, AiJson.Options)
                ?? throw new AiAnalysisException("旧 AI 配置无效。");
            old.Validate(false);
            var provider = new AiProviderProfile(LegacyProvider, "原有供应商", old.Protocol, old.BaseAddress,
                [AiModelProfile.FromSettings(LegacyModel, old.Model, old)], LegacyModel);
            var catalog = new AiProviderConfiguration(LegacyProvider, [provider]);
            string? oldKey = await secrets.GetAsync(LegacySlot(old), token);
            string slot = Slot(provider.Resolve());
            string? previous = await secrets.GetAsync(slot, token);
            if (oldKey is not null) await secrets.SetAsync(slot, oldKey, token);
            try { await WriteLockedAsync(catalog, token); }
            catch { if (oldKey is not null) await secrets.SetAsync(slot, previous, CancellationToken.None); throw; }
            if (oldKey is not null) await RemoveOrphanKeyAsync(LegacySlot(old));
            return catalog;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException or InvalidOperationException or NullReferenceException or ArgumentException or FormatException)
        { throw new AiAnalysisException("无法读取 AI 配置，已保留原文件；请检查本地存储或配置版本。"); }
    }
    private static void Validate(AiProviderConfiguration c)
    {
        if (c is null || c.Providers is null || c.Providers.Count > 50 || c.Providers.Any(p => p is null) ||
            c.Providers.Select(p => p.Id).Distinct().Count() != c.Providers.Count ||
            (c.SelectedProviderId is not null && c.Find(c.SelectedProviderId) is null))
            throw new AiAnalysisException("AI 供应商列表无效。");
        foreach (var p in c.Providers) p.Validate();
    }
    private async Task WriteLockedAsync(AiProviderConfiguration configuration, CancellationToken token)
    {
        Validate(configuration);
        string temporary = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath))!);
            string json = JsonSerializer.Serialize(new Envelope(2, configuration), AiJson.Options);
            if (Encoding.UTF8.GetByteCount(json) > 2 * 1024 * 1024) throw new AiAnalysisException("AI 配置超过本机大小限制。");
            await File.WriteAllTextAsync(temporary, json, token);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, filePath, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { throw new AiAnalysisException("无法保存 AI 配置，请检查本地存储。"); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public async Task SaveProviderAsync(AiProviderProfile provider, string? key = null, bool clearKey = false, CancellationToken token = default)
    {
        provider.Validate(); ValidateKey(key);
        await gate.WaitAsync(token);
        try
        {
            var current = await ReadLockedAsync(token);
            var profiles = current.Find(provider.Id) is null ? current.Providers.Append(provider).ToArray()
                : current.Providers.Select(p => p.Id == provider.Id ? provider : p).ToArray();
            var next = new AiProviderConfiguration(current.SelectedProviderId ?? provider.Id, profiles);
            var target = new AiServiceSettings(provider.Protocol, provider.BaseAddress, ProviderId: provider.Id);
            string slot = Slot(target);
            string? oldKey = null;
            bool changingKey = key is not null || clearKey;
            if (changingKey)
            {
                oldKey = await secrets.GetAsync(slot, token);
                await secrets.SetAsync(slot, clearKey ? null : NormalizeKey(key), token);
            }
            try { await WriteLockedAsync(next, token); }
            catch { if (changingKey) await secrets.SetAsync(slot, oldKey, CancellationToken.None); throw; }
        }
        finally { gate.Release(); }
    }
    public async Task DeleteProviderAsync(Guid id, CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try
        {
            var current = await ReadLockedAsync(token);
            var profile = current.Find(id) ?? throw new AiAnalysisException("供应商已不存在。");
            await WriteLockedAsync(new(current.SelectedProviderId == id ? null : current.SelectedProviderId,
                current.Providers.Where(p => p.Id != id).ToArray()), token);
            await RemoveOrphanKeyAsync(Slot(new(profile.Protocol, profile.BaseAddress, ProviderId: id)));
        }
        finally { gate.Release(); }
    }
    private async Task RemoveOrphanKeyAsync(string slot)
    {
        // The public configuration is already committed. Cleanup failure must not pretend it was rolled back.
        try { await secrets.SetAsync(slot, null, CancellationToken.None); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or AiAnalysisException) { }
    }
    public async Task SelectAsync(Guid providerId, Guid modelId, CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try
        {
            var current = await ReadLockedAsync(token);
            var profile = current.Find(providerId) ?? throw new AiAnalysisException("供应商已不存在，请重新选择。");
            profile.Resolve(modelId).Validate();
            await WriteLockedAsync(new(providerId, current.Providers.Select(p => p.Id == providerId ? p with { SelectedModelId = modelId } : p).ToArray()), token);
        }
        finally { gate.Release(); }
    }
    public async Task SaveAsync(AiServiceSettings settings, CancellationToken cancellationToken = default)
    {
        settings.Validate();
        var catalog = await LoadProvidersAsync(cancellationToken);
        Guid id = settings.ProviderId ?? catalog.SelectedProviderId ?? LegacyProvider;
        var provider = catalog.Find(id);
        Guid modelId = settings.ModelProfileId ?? provider?.SelectedModelId ?? LegacyModel;
        var model = AiModelProfile.FromSettings(modelId, provider?.Models.FirstOrDefault(m => m.Id == modelId)?.Name ?? settings.Model, settings);
        await SaveProviderAsync(new(id, provider?.Name ?? "原有供应商", settings.Protocol, settings.BaseAddress,
            (provider?.Models.Where(m => m.Id != modelId) ?? []).Append(model).ToArray(), modelId), token: cancellationToken);
        await SelectAsync(id, modelId, cancellationToken);
    }
    public async Task<string?> GetApiKeyAsync(AiServiceSettings settings, CancellationToken cancellationToken = default) =>
        await secrets.GetAsync(Slot(await ResolveKeyIdentityAsync(settings, cancellationToken)), cancellationToken);
    private async Task<AiServiceSettings> ResolveKeyIdentityAsync(AiServiceSettings settings, CancellationToken token) =>
        settings.ProviderId is not null ? settings : settings with
        { ProviderId = (await LoadProvidersAsync(token)).SelectedProviderId ?? LegacyProvider };
    public async Task SetApiKeyAsync(AiServiceSettings settings, string? key, CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        await secrets.SetAsync(Slot(await ResolveKeyIdentityAsync(settings, cancellationToken)), NormalizeKey(key), cancellationToken);
    }
    private static void ValidateKey(string? key)
    { if (key?.Any(char.IsControl) == true || key?.Length > 4096) throw new AiAnalysisException("密钥格式无效。"); }
    private static string? NormalizeKey(string? key) => string.IsNullOrWhiteSpace(key) ? null : key.Trim();
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string LegacySlot(AiServiceSettings settings) => Hash($"{settings.Protocol}:{settings.Endpoint.AbsoluteUri}");
    private static string Slot(AiServiceSettings settings) => Hash($"provider:{settings.ProviderId:N}:{settings.Protocol}:{settings.Endpoint.AbsoluteUri}");
}
