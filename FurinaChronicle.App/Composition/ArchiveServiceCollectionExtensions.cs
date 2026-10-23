// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.App.Persistence;
using FurinaChronicle.Infrastructure.Archives.Portable;
using FurinaChronicle.Services.Abstractions;
using FurinaChronicle.Services.Archives;
using FurinaChronicle.Services.Archives.Portable;

namespace FurinaChronicle.App.Composition;

internal static class ArchiveServiceCollectionExtensions
{
    public static IServiceCollection AddArchiveFeature(
        this IServiceCollection services)
    {
        services.AddSingleton<
            IArchiveSelectionStore,
            PreferencesArchiveSelectionStore>();

        var portableLimits = new ArchivePortableLimits();
        services.AddSingleton(portableLimits);
        services.AddSingleton(new ArchiveSnapshotStorageOptions(
            Path.Combine(
                FileSystem.CacheDirectory,
                "archive-export-snapshots")));
        services.AddSingleton(new ArchiveRestoreStorageOptions(
            Path.Combine(
                FileSystem.CacheDirectory,
                "archive-restore-sessions")));
        services.AddSingleton<IArchivePackageWriter, ArchivePackageWriter>();
        services.AddSingleton<ArchivePackageReader>();
        services.AddSingleton<IArchivePackageReader>(provider =>
            provider.GetRequiredService<ArchivePackageReader>());
        services.AddSingleton<SqliteArchiveExportSnapshotFactory>();
        services.AddSingleton<IArchiveExportSnapshotFactory>(provider =>
            provider.GetRequiredService<SqliteArchiveExportSnapshotFactory>());
        services.AddTransient<IArchiveLogicalExporter, ArchiveLogicalExporter>();
        services.AddTransient<
            IArchiveRestoreInputStager,
            SqliteArchiveRestoreInputStager>();
        services.AddTransient<
            IArchiveRestoreTargetSnapshotFactory,
            SqliteArchiveRestoreTargetSnapshotFactory>();
        services.AddTransient<IArchiveRestorePlanner, PlanArchiveRestore>();

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
