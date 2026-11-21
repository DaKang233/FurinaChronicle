// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Infrastructure.Ai;
using FurinaChronicle.Presentation.Ai;
using FurinaChronicle.Presentation.History;
using FurinaChronicle.Services.Abstractions;
using FurinaChronicle.Services.Ai;
using FurinaChronicle.Services.Gacha.Abstractions;
using FurinaChronicle.Services.Gacha.Analytics;
using Microsoft.Extensions.DependencyInjection;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.Gacha.Metadata;

namespace FurinaChronicle.Windows.Composition;

internal static class WindowsAiComposition
{
    public static void AddAiAnalysis(this IServiceCollection services, WindowsPaths paths, JsonAiConversationStore conversations)
    {
        string directory = Path.Combine(paths.SettingsDirectory, "ai");
        services.AddSingleton<IAiSecretStore>(new DpapiAiSecretStore(Path.Combine(directory, "keys.bin")));
        services.AddSingleton<IAiConfigurationStore>(sp => new JsonAiConfigurationStore(Path.Combine(directory, "settings.json"), sp.GetRequiredService<IAiSecretStore>()));
        services.AddSingleton<IAiConversationStore>(conversations);
        services.AddSingleton<IAiConversationCopyCoordinator>(conversations);
        services.AddTransient<AiAttachmentReader>(); services.AddTransient<AiSettingsState>();
        services.AddSingleton<IAiChatClient>(_ => new HttpAiChatClient());
        services.AddTransient(sp => new BuildGachaAnalytics(sp.GetRequiredService<IGachaRecordRepository>(),
            sp.GetService<IGachaItemMetadataProvider>() ?? new EmptyMetadata(), sp.GetRequiredService<IGachaEventCatalog>()));
        services.AddTransient(sp => new BuildAiContext(sp.GetRequiredService<IPlayerArchiveRepository>(),
            sp.GetRequiredService<IGameAccountRepository>(), sp.GetRequiredService<IGachaRecordRepository>(),
            sp.GetRequiredService<BuildGachaAnalytics>(), sp.GetRequiredService<FurinaChronicle.Services.HistoryDomains.IArchiveDomainHistoryStore>(),
            sp.GetService<IDomainMetadataService>() ?? NullDomainMetadataService.Instance,
            sp.GetRequiredService<FurinaChronicle.Services.Chronicle.IChronicleQueryService>()));
        services.AddTransient<AiAnalysisSession>(); services.AddTransient<ReadAiEvidence>(); services.AddTransient<AiWorkspaceState>();
    }
    private sealed class EmptyMetadata : IGachaItemMetadataProvider
    {
        public ValueTask<GachaItemMetadata?> FindByIdAsync(GachaGame game, string itemId, CancellationToken cancellationToken = default) => ValueTask.FromResult<GachaItemMetadata?>(null);
    }
}
