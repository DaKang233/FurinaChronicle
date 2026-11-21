// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Services.Ai;

namespace FurinaChronicle.Presentation.Ai;

public sealed class AiSettingsState(IAiConfigurationStore configuration, IAiChatClient client)
{
    private IAiProviderConfigurationStore Store => configuration as IAiProviderConfigurationStore
        ?? throw new AiAnalysisException("当前宿主不支持多供应商配置。");
    public AiProviderConfiguration Catalog { get; private set; } = new(null, []);
    public AiProviderProfile? EditingProvider { get; private set; }
    public Guid? EditingModelId { get; private set; }
    public AiServiceSettings Settings { get; private set; } = new();
    public async Task LoadAsync(CancellationToken token = default)
    {
        Catalog = await Store.LoadProvidersAsync(token);
        if ((Catalog.Find(Catalog.SelectedProviderId) ?? Catalog.Providers.FirstOrDefault()) is { } first) EditProvider(first.Id);
        else NewProvider();
    }
    public async Task SavePreferencesAsync(AiChatPreferences preferences, CancellationToken token = default)
    { preferences.Validate(); await Store.SavePreferencesAsync(preferences, token); Catalog = await Store.LoadProvidersAsync(token); }
    public void EditProvider(Guid id)
    {
        EditingProvider = Catalog.Find(id) ?? throw new AiAnalysisException("供应商已不存在。");
        EditingModelId = EditingProvider.SelectedModelId ?? EditingProvider.Models.FirstOrDefault()?.Id;
        LoadModel();
    }
    public void EditModel(Guid id)
    {
        if (EditingProvider?.Models.Any(m => m.Id == id) != true) throw new AiAnalysisException("模型已不存在。");
        EditingModelId = id; LoadModel();
    }
    private void LoadModel()
    {
        if (EditingProvider is not { } p) return;
        Settings = EditingModelId is not null ? p.Resolve(EditingModelId) : new(p.Protocol, p.BaseAddress, ProviderId: p.Id);
    }
    public void NewProvider()
    {
        Guid modelId = Guid.NewGuid();
        EditingProvider = new(Guid.NewGuid(), "新供应商", AiProtocol.OpenAiCompatible, "https://api.example.com/v1",
            [new AiModelProfile(modelId, "", "")], modelId);
        EditingModelId = modelId; LoadModel();
    }
    public void NewModel()
    {
        var p = EditingProvider ?? throw new AiAnalysisException("请先添加供应商。");
        if (p.Models.Count >= 100) throw new AiAnalysisException("每个供应商最多保存 100 个模型。");
        Guid id = Guid.NewGuid();
        // Model capabilities must be explicitly configured, never copied from another model.
        EditingProvider = p with { Models = p.Models.Append(new AiModelProfile(id, "", "")).ToArray() };
        EditingModelId = id; LoadModel();
    }
    public async Task SaveProfileAsync(string providerName, string modelName, AiServiceSettings settings, string? key = null,
        bool clear = false, CancellationToken token = default)
    {
        settings.Validate();
        var p = EditingProvider ?? throw new AiAnalysisException("请先添加供应商。");
        Guid modelId = EditingModelId ?? Guid.NewGuid();
        var model = AiModelProfile.FromSettings(modelId, modelName.Trim(), settings);
        var next = p with { Name = providerName.Trim(), Protocol = settings.Protocol, BaseAddress = settings.BaseAddress,
            Models = p.Models.Where(m => m.Id != modelId).Append(model).ToArray(), SelectedModelId = modelId };
        // Unsaved blank models are drafts, not silently submitted placeholders.
        if (next.Models.Any(m => m.Id != modelId && string.IsNullOrWhiteSpace(m.Model)))
            next = next with { Models = next.Models.Where(m => m.Id == modelId || !string.IsNullOrWhiteSpace(m.Model)).ToArray() };
        await Store.SaveProviderAsync(next, key, clear, token);
        await Store.SelectAsync(next.Id, modelId, token);
        Catalog = await Store.LoadProvidersAsync(token); EditProvider(next.Id);
    }
    public async Task DeleteProviderAsync(CancellationToken token = default)
    {
        if (EditingProvider is not { } p) return;
        if (Catalog.Find(p.Id) is not null) await Store.DeleteProviderAsync(p.Id, token);
        await LoadAsync(token);
    }
    public async Task DeleteModelAsync(CancellationToken token = default)
    {
        var p = EditingProvider ?? throw new AiAnalysisException("请先添加供应商。");
        var models = p.Models.Where(m => m.Id != EditingModelId).ToArray();
        var next = p with { Models = models, SelectedModelId = models.FirstOrDefault()?.Id };
        if (Catalog.Find(p.Id) is not null)
        {
            await Store.SaveProviderAsync(next, token: token);
            Catalog = await Store.LoadProvidersAsync(token); EditProvider(p.Id);
        }
        else { EditingProvider = next; EditingModelId = next.SelectedModelId; LoadModel(); }
    }
    public Task ClearKeyAsync(AiServiceSettings settings, CancellationToken token = default) => configuration.SetApiKeyAsync(settings, null, token);
    public async Task SaveAsync(AiServiceSettings settings, string? key = null, bool clear = false, CancellationToken token = default)
    {
        settings.Validate(); await configuration.SaveAsync(settings, token);
        if (key is not null || clear) await configuration.SetApiKeyAsync(settings, clear ? null : key, token);
        Settings = settings;
    }
    public async Task<IReadOnlyList<string>> ModelsAsync(AiServiceSettings settings, string? key = null, CancellationToken token = default)
    {
        settings.Validate(false);
        return await client.ListModelsAsync(settings, key ?? await configuration.GetApiKeyAsync(settings, token), token);
    }
}
