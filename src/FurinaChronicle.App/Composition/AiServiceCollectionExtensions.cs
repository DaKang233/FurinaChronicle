// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.App.Ai;
using FurinaChronicle.Infrastructure.Ai;
using FurinaChronicle.Presentation.Ai;
using FurinaChronicle.Services.Ai;

namespace FurinaChronicle.App.Composition;

internal static class AiServiceCollectionExtensions
{
    public static IServiceCollection AddAiAnalysis(this IServiceCollection services)
    {
        services.AddSingleton<IAiSecretStore, MauiAiSecretStore>();
        services.AddSingleton<IAiConfigurationStore>(sp => new JsonAiConfigurationStore(
            Path.Combine(FileSystem.Current.AppDataDirectory, "DeviceAi", "settings.json"), sp.GetRequiredService<IAiSecretStore>()));
        services.AddSingleton<IAiChatClient>(_ => new HttpAiChatClient());
        services.AddTransient<BuildAiContext>(); services.AddTransient<ReadAiEvidence>(); services.AddTransient<AiAnalysisSession>();
        services.AddTransient<AiWorkspaceState>(); services.AddSingleton<AiAnalysisPage>();
        return services;
    }
}
