// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Infrastructure.Gacha.Exporting;
using FurinaChronicle.Infrastructure.Gacha.Importing;
using FurinaChronicle.Infrastructure.Gacha.Metadata;
using FurinaChronicle.Infrastructure.Gacha.Metadata.Abstractions;
using FurinaChronicle.Infrastructure.Gacha.Metadata.Localization;
using FurinaChronicle.Infrastructure.Gacha.Metadata.Persistence;
using FurinaChronicle.Infrastructure.Gacha.Metadata.Remote;
using FurinaChronicle.Infrastructure.Gacha.Portable;
using FurinaChronicle.Infrastructure.Gacha.Refreshing;
using FurinaChronicle.Infrastructure.Gacha.Uigf.Compatibility;
using FurinaChronicle.Infrastructure.Gacha.Uigf.V4_2;
using FurinaChronicle.Infrastructure.Importing.Json;
using FurinaChronicle.Services.Abstractions;
using FurinaChronicle.Services.Gacha;
using FurinaChronicle.Services.Gacha.Abstractions;
using FurinaChronicle.Services.Gacha.Analytics;
using FurinaChronicle.Services.Gacha.Exporting;
using FurinaChronicle.Services.Gacha.Importing;
using FurinaChronicle.Services.Gacha.Portable;
using FurinaChronicle.Services.Gacha.Refreshing;

namespace FurinaChronicle.App.Composition;

internal static class GachaServiceCollectionExtensions
{
    public static IServiceCollection AddGachaFeature(
        this IServiceCollection services)
    {
        AddQueries(services);
        AddImportAndExport(services);
        AddPortable(services);
        AddMetadata(services);
        AddRefreshing(services);

        return services;
    }

    private static void AddQueries(IServiceCollection services)
    {
        services.AddTransient<GetGachaRecordPage>();
        services.AddTransient<GetRecentGachaRecords>();
        services.AddTransient<BuildGachaAnalytics>();
    }

    private static void AddImportAndExport(IServiceCollection services)
    {
        services.AddSingleton<IGachaRecordReader, JsonGachaRecordReader>();
        services.AddTransient<ImportGachaRecords>();
        services.AddSingleton<UigfV42GachaReader>();
        services.AddSingleton<IGachaImportReader, UigfCompatibleGachaReader>();
        services.AddTransient<ImportUigfGachaRecords>();
        services.AddSingleton<
            ITeyvatHelperUigfClient,
            TeyvatHelperUigfClient>();
        services.AddTransient<TeyvatHelperUigfImportSource>();

        services.AddSingleton<IUigfV42ExportWriter, UigfV42GachaWriter>();
        services.AddSingleton<IGachaTableExportWriter, GachaTableExportWriter>();
        services.AddTransient<LoadGachaExportData>();
        services.AddTransient<ExportUigfV42GachaRecords>();
        services.AddTransient<ExportGachaTable>();
    }

    private static void AddMetadata(IServiceCollection services)
    {
        string metadataDatabasePath = Path.Combine(
            FileSystem.AppDataDirectory,
            "genshin-metadata.db3");
        services.AddSingleton(new GachaMetadataOptions(metadataDatabasePath));
        services.AddSingleton<GachaMetadataDatabase>();
        services.AddSingleton<IGachaMetadataRemoteSource>(
            _ => new GenshinCalculatorMetadataSource());
        services.AddSingleton<IGachaEventCatalog, EmbeddedGachaEventCatalog>();
        services.AddSingleton<
            IGachaLocalizationSource,
            EmbeddedGachaLocalizationSource>();
        services.AddSingleton<SqliteGachaItemMetadataProvider>();
        services.AddSingleton<IGachaItemMetadataProvider>(
            provider => provider.GetRequiredService<SqliteGachaItemMetadataProvider>());
        services.AddSingleton<IGachaMetadataRefreshService>(
            provider => provider.GetRequiredService<SqliteGachaItemMetadataProvider>());

        services.AddSingleton(
            new GachaItemIconCacheOptions(
                Path.Combine(FileSystem.CacheDirectory, "gacha-item-icons")));
        services.AddSingleton<IGachaItemIconCache, FileGachaItemIconCache>();
        services.AddSingleton(
            new GachaBannerImageCacheOptions(
                Path.Combine(FileSystem.CacheDirectory, "gacha-banner-images")));
        services.AddSingleton<IGachaBannerImageCache, FileGachaBannerImageCache>();
        services.AddTransient<PreloadGachaBannerImages>();
    }

    private static void AddPortable(IServiceCollection services)
    {
        services.AddSingleton(new GachaPortableLimits());
        services.AddSingleton<
            IGachaPortablePackageWriter,
            GachaPortablePackageWriter>();
        services.AddSingleton<
            IGachaPortablePackageReader,
            GachaPortablePackageReader>();
    }

    private static void AddRefreshing(IServiceCollection services)
    {
        services.AddSingleton<
            ISTokenGachaUrlProvider,
            MiHoYoSTokenGachaUrlProvider>();
        services.AddSingleton<
            IWindowsGachaCacheUrlProvider,
            WindowsGachaCacheUrlProvider>();
        services.AddSingleton<IGachaLogClient, MiHoYoGachaLogClient>();
        services.AddTransient<RefreshGachaRecords>();
    }
}
