// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Services.Ai;

namespace FurinaChronicle.Presentation.Ai;

public sealed class AiSettingsState(IAiConfigurationStore configuration,IAiChatClient client)
{
    public AiServiceSettings Settings {get;private set;}=new();
    public async Task LoadAsync(CancellationToken token=default)=>Settings=await configuration.LoadAsync(token);
    public async Task SaveAsync(AiServiceSettings settings,string? key=null,bool clear=false,CancellationToken token=default)
    {
        settings.Validate();await configuration.SaveAsync(settings,token);
        if(key is not null||clear)await configuration.SetApiKeyAsync(settings,clear?null:key,token);
        Settings=settings;
    }
    public async Task<IReadOnlyList<string>> ModelsAsync(AiServiceSettings settings,string? key=null,CancellationToken token=default)
    {settings.Validate(false);return await client.ListModelsAsync(settings,key??await configuration.GetApiKeyAsync(settings,token),token);}
}
