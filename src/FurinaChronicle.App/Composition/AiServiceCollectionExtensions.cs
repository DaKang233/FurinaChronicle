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
        // The retained MAUI Windows host shares business data with WinUI, so its managed
        // chat copies must share the same directory and irreversible-delete protection.
        services.AddSingleton<IAiConversationStore>(sp => new JsonAiConversationStore(OperatingSystem.IsWindows()
            ? Path.Combine(FileSystem.Current.AppDataDirectory, "UI4", "WinUI", "settings", "ai", "conversations")
            : Path.Combine(FileSystem.Current.AppDataDirectory, "DeviceAi", "conversations")));
        services.AddSingleton<IAiConversationCopyCoordinator>(sp => (IAiConversationCopyCoordinator)sp.GetRequiredService<IAiConversationStore>());
        services.AddTransient<AiAttachmentReader>(); services.AddTransient<AiSettingsState>();
        services.AddSingleton<IAiChatClient>(_ => new HttpAiChatClient());
        services.AddTransient<BuildAiContext>(); services.AddTransient<ReadAiEvidence>(); services.AddTransient<IAiReadOnlyToolRunner, AiArchiveToolRunner>(); services.AddTransient<AiAnalysisSession>();
        services.AddTransient<AiWorkspaceState>(); services.AddSingleton<AiAnalysisPage>();
        return services;
    }
}
