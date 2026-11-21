// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

namespace FurinaChronicle.Services.Ai;

public sealed record AiModelProfile(Guid Id, string Name, string Model,
    int ContextCharacters = 24000, int MaxOutputTokens = 2048, int OllamaContextTokens = 4096,
    bool CpuOnly = false, bool DisableCompatibleThinking = false, bool SupportsImages = false)
{
    public string Label => string.IsNullOrWhiteSpace(Name) ? Model : Name;
    public static AiModelProfile FromSettings(Guid id, string name, AiServiceSettings s) => new(id, name, s.Model,
        s.ContextCharacters, s.MaxOutputTokens, s.OllamaContextTokens, s.CpuOnly, s.DisableCompatibleThinking, s.SupportsImages);
}

public sealed record AiProviderProfile(Guid Id, string Name, AiProtocol Protocol, string BaseAddress,
    IReadOnlyList<AiModelProfile> Models, Guid? SelectedModelId = null)
{
    public string Label => Name;
    public AiServiceSettings Resolve(Guid? modelId = null)
    {
        var model = Models.FirstOrDefault(m => m.Id == (modelId ?? SelectedModelId))
            ?? throw new AiAnalysisException("请选择此供应商下的模型。");
        return new(Protocol, BaseAddress, model.Model, model.ContextCharacters, model.MaxOutputTokens,
            model.OllamaContextTokens, model.CpuOnly, model.DisableCompatibleThinking, model.SupportsImages, Id, model.Id);
    }
    public void Validate()
    {
        if (Id == Guid.Empty || string.IsNullOrWhiteSpace(Name) || Name.Length > 120 ||
            Name.Any(char.IsControl) || AiPrivacy.ContainsCredential(Name) || Models is null || Models.Count > 100 ||
            Models.Any(m => m is null) ||
            Models.Select(m => m.Id).Distinct().Count() != Models.Count ||
            (SelectedModelId is not null && !Models.Any(m => m.Id == SelectedModelId)))
            throw new AiAnalysisException("供应商配置无效。");
        new AiServiceSettings(Protocol, BaseAddress).Validate(false);
        foreach (var model in Models)
        {
            if (model.Id == Guid.Empty || model.Name is null || model.Name.Length > 120 || model.Name.Any(char.IsControl) || AiPrivacy.ContainsCredential(model.Name))
                throw new AiAnalysisException("模型配置无效。");
            Resolve(model.Id).Validate(false);
        }
    }
}

public sealed record AiProviderConfiguration(Guid? SelectedProviderId, IReadOnlyList<AiProviderProfile> Providers, AiChatPreferences? ChatPreferences = null)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public AiChatPreferences Preferences => ChatPreferences ?? new();
    [System.Text.Json.Serialization.JsonIgnore]
    public AiServiceSettings Current => Providers.FirstOrDefault(p => p.Id == SelectedProviderId) is { } provider && provider.SelectedModelId is not null
        ? provider.Resolve() : new();
    public AiProviderProfile? Find(Guid? id) => Providers.FirstOrDefault(p => p.Id == id);
}

public interface IAiProviderConfigurationStore : IAiConfigurationStore
{
    Task<AiProviderConfiguration> LoadProvidersAsync(CancellationToken token = default);
    Task SaveProviderAsync(AiProviderProfile provider, string? key = null, bool clearKey = false, CancellationToken token = default);
    Task DeleteProviderAsync(Guid id, CancellationToken token = default);
    Task SelectAsync(Guid providerId, Guid modelId, CancellationToken token = default);
    Task SavePreferencesAsync(AiChatPreferences preferences, CancellationToken token = default);
}
