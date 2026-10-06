// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using FurinaChronicle.Infrastructure.Persistence.Sqlite;
using FurinaChronicle.Services.Abstractions;
using FurinaChronicle.Services.Gacha.History;
using FurinaChronicle.Services.Gacha.Portable;

namespace FurinaChronicle.App.Composition;

internal static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddPersistence(
        this IServiceCollection services)
    {
        string databasePath = Path.Combine(
            FileSystem.AppDataDirectory,
            "furinachronicle.db3");
        Debug.WriteLine($"FurinaChronicle database: {databasePath}");

        services.AddSingleton(new SqliteDatabaseOptions(databasePath));
        services.AddSingleton<FurinaDatabase>();
        services.AddSingleton<
            IHistoryStorageCapacityProvider,
            DatabaseStorageCapacityProvider>();
        services.AddSingleton<IGachaRecordRepository, SqliteGachaRecordRepository>();
        services.AddSingleton<SqliteGachaAtomicChangeStore>();
        services.AddSingleton<IGachaAtomicChangeStore>(
            provider => provider.GetRequiredService<
                SqliteGachaAtomicChangeStore>());
        services.AddSingleton<IGachaPortableImportApplier>(
            provider => provider.GetRequiredService<
                SqliteGachaAtomicChangeStore>());
        services.AddSingleton<IPlayerArchiveRepository, SqlitePlayerArchiveRepository>();
        services.AddSingleton<IGameAccountRepository, SqliteGameAccountRepository>();

        return services;
    }
}
