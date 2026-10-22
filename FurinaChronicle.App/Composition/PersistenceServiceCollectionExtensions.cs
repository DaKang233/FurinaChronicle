// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using FurinaChronicle.App.Persistence;
using FurinaChronicle.Infrastructure.Persistence.Sqlite;
using FurinaChronicle.Services.Abstractions;
using FurinaChronicle.Services.Gacha.History;
using FurinaChronicle.Services.Gacha.Portable;
using FurinaChronicle.Services.History;
using FurinaChronicle.Services.HistoryDomains;
using FurinaChronicle.Services.HistoryDomains.Portable;
using FurinaChronicle.Infrastructure.HistoryDomains.Portable;
using FurinaChronicle.Infrastructure.HistoryDomains.Importing;
using FurinaChronicle.Infrastructure.Chronicle;
using FurinaChronicle.Services.Chronicle;
using FurinaChronicle.Services.HistoryDomains.Importing;

namespace FurinaChronicle.App.Composition;

internal static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddPersistence(
        this IServiceCollection services)
    {
        string databasePath;
#if PHASE8B_VALIDATION
        string? validationDirectory = Environment.GetEnvironmentVariable(
            "FURINA_PHASE8B7_DIRECTORY");
        if (!string.IsNullOrWhiteSpace(validationDirectory) &&
            !Path.IsPathFullyQualified(validationDirectory))
        {
            throw new InvalidOperationException(
                "FURINA_PHASE8B7_DIRECTORY must be an absolute path.");
        }
        databasePath = Path.Combine(
            string.IsNullOrWhiteSpace(validationDirectory)
                ? Path.Combine(
                    FileSystem.AppDataDirectory,
                    "phase8b7-validation")
                : Path.GetFullPath(validationDirectory),
            "furinachronicle-validation.db3");
#else
        databasePath = Path.Combine(
            FileSystem.AppDataDirectory,
            "furinachronicle.db3");
#endif
        Debug.WriteLine($"FurinaChronicle database: {databasePath}");

        services.AddSingleton(new SqliteDatabaseOptions(databasePath));
        services.AddSingleton<FurinaDatabase>();
        services.AddSingleton<
            IHistoryStorageCapacityProvider,
            PlatformDatabaseStorageCapacityProvider>();
        services.AddSingleton<IGachaRecordRepository, SqliteGachaRecordRepository>();
        services.AddSingleton<SqliteGachaAtomicChangeStore>();
        services.AddSingleton<IGachaAtomicChangeStore>(
            provider => provider.GetRequiredService<
                SqliteGachaAtomicChangeStore>());
        services.AddSingleton<IArchiveOperationHistoryStore>(
            provider => provider.GetRequiredService<
                SqliteGachaAtomicChangeStore>());
        services.AddSingleton<IArchiveDomainHistoryStore>(
            provider => provider.GetRequiredService<
                SqliteGachaAtomicChangeStore>());
        services.AddSingleton<IArchiveDomainPortableApplier>(
            provider => provider.GetRequiredService<
                SqliteGachaAtomicChangeStore>());
        services.AddSingleton<
            IArchiveDomainPortableFileService,
            ArchiveDomainPortableFileService>();
        services.AddSingleton<
            IArchiveDomainSourceFileReader,
            JsonArchiveDomainSourceFileReader>();
        services.AddSingleton<IGachaPortableImportApplier>(
            provider => provider.GetRequiredService<
                SqliteGachaAtomicChangeStore>());
        services.AddSingleton<IPlayerArchiveRepository, SqlitePlayerArchiveRepository>();
        services.AddSingleton<IGameAccountRepository, SqliteGameAccountRepository>();
        services.AddSingleton<IChronicleQueryService, SqliteChronicleQueryService>();

        return services;
    }
}
