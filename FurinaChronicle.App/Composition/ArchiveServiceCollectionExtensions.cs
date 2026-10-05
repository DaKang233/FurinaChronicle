// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.App.Persistence;
using FurinaChronicle.Services.Abstractions;
using FurinaChronicle.Services.Archives;

namespace FurinaChronicle.App.Composition;

internal static class ArchiveServiceCollectionExtensions
{
    public static IServiceCollection AddArchiveFeature(
        this IServiceCollection services)
    {
        services.AddSingleton<
            IArchiveSelectionStore,
            PreferencesArchiveSelectionStore>();

        services.AddTransient<CreatePlayerArchive>();
        services.AddTransient<GetPlayerArchives>();
        services.AddTransient<GetGameAccounts>();
        services.AddTransient<AddGameAccount>();
        services.AddTransient<UpdateGameAccount>();
        services.AddTransient<RenamePlayerArchive>();
        services.AddTransient<DeletePlayerArchive>();
        services.AddTransient<DeleteGameAccount>();
        services.AddTransient<ArchiveSelectionService>();

        return services;
    }
}
