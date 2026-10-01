// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.App.ViewModels;
using FurinaChronicle.App.Persistence;
using FurinaChronicle.App.Startup;
using FurinaChronicle.Infrastructure.Importing.Json;
using FurinaChronicle.Infrastructure.Persistence.Sqlite;
using FurinaChronicle.Services.Abstractions;
using FurinaChronicle.Services.Gacha.Importing;
using FurinaChronicle.Services.Gacha;
using FurinaChronicle.Services.Archives;
using Microsoft.Extensions.Logging;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Infrastructure.Gacha.Exporting;
using FurinaChronicle.Infrastructure.Gacha.Metadata;
using FurinaChronicle.Infrastructure.Gacha.Metadata.Abstractions;
using FurinaChronicle.Infrastructure.Gacha.Metadata.Localization;
using FurinaChronicle.Infrastructure.Gacha.Metadata.Persistence;
using FurinaChronicle.Infrastructure.Gacha.Metadata.Remote;
using FurinaChronicle.Infrastructure.Gacha.Uigf.V4_2;
using FurinaChronicle.Infrastructure.Gacha.Uigf.Compatibility;
using FurinaChronicle.Infrastructure.Gacha.Importing;
using FurinaChronicle.Services.Gacha.Abstractions;
using FurinaChronicle.Services.Gacha.Exporting;
using FurinaChronicle.Services.Gacha.Analytics;
using FurinaChronicle.Infrastructure.Gacha.Refreshing;
using FurinaChronicle.Infrastructure.Passport;
using FurinaChronicle.Services.Gacha.Refreshing;
using FurinaChronicle.Services.Passport;

using System.Diagnostics;

namespace FurinaChronicle.App
{
	public static class MauiProgram
	{
		public static MauiApp CreateMauiApp()
		{
			var builder = MauiApp.CreateBuilder();
			builder
				.UseMauiApp<App>()
				.ConfigureFonts(fonts =>
				{
					fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
					fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
				});

#if DEBUG
			builder.Logging.AddDebug();
#endif
			// Phase 3
			// builder.Services.AddSingleton<IGachaRecordRepository>(_ => new InMemoryGachaRecordRepository(Array.Empty<GachaRecord>()));

			// Phase 4
			string databasePath = Path.Combine(FileSystem.AppDataDirectory, "furinachronicle.db3");
			Debug.WriteLine($"FurinaChronicle database: {databasePath}");
			builder.Services.AddSingleton(new SqliteDatabaseOptions(databasePath));
			builder.Services.AddSingleton<FurinaDatabase>();
			builder.Services.AddSingleton<IGachaRecordRepository, SqliteGachaRecordRepository>();
			builder.Services.AddTransient<GetGachaRecordPage>();
			builder.Services.AddTransient<BuildGachaAnalytics>();

			builder.Services.AddSingleton<IGachaRecordReader, JsonGachaRecordReader>();
			builder.Services.AddTransient<GetRecentGachaRecords>();
			builder.Services.AddTransient<ImportGachaRecords>();
			builder.Services.AddSingleton<UigfV42GachaReader>();
			builder.Services.AddSingleton<IGachaImportReader, UigfCompatibleGachaReader>();

			string metadataDatabasePath = Path.Combine(
				FileSystem.AppDataDirectory,
				"genshin-metadata.db3");
			builder.Services.AddSingleton(new GachaMetadataOptions(metadataDatabasePath));
			builder.Services.AddSingleton(TimeProvider.System);
			builder.Services.AddSingleton<GachaMetadataDatabase>();
			builder.Services.AddSingleton<IGachaMetadataRemoteSource>(
				_ => new GenshinCalculatorMetadataSource());
			builder.Services.AddSingleton<
				IGachaEventCatalog,
				EmbeddedGachaEventCatalog>();
			builder.Services.AddSingleton<
				IGachaLocalizationSource,
				EmbeddedGachaLocalizationSource>();
			builder.Services.AddSingleton<SqliteGachaItemMetadataProvider>();
			builder.Services.AddSingleton<IGachaItemMetadataProvider>(
				services => services.GetRequiredService<SqliteGachaItemMetadataProvider>());
			builder.Services.AddSingleton<IGachaMetadataRefreshService>(
				services => services.GetRequiredService<SqliteGachaItemMetadataProvider>());
			builder.Services.AddSingleton(
				new GachaItemIconCacheOptions(
					Path.Combine(
						FileSystem.CacheDirectory,
						"gacha-item-icons")));
			builder.Services.AddSingleton<
				IGachaItemIconCache,
				FileGachaItemIconCache>();
			builder.Services.AddSingleton(
				new GachaBannerImageCacheOptions(
					Path.Combine(
						FileSystem.CacheDirectory,
						"gacha-banner-images")));
			builder.Services.AddSingleton<
				IGachaBannerImageCache,
				FileGachaBannerImageCache>();
			builder.Services.AddTransient<ImportUigfGachaRecords>();
			builder.Services.AddSingleton<
				ITeyvatHelperUigfClient,
				TeyvatHelperUigfClient>();
			builder.Services.AddTransient<TeyvatHelperUigfImportSource>();
			builder.Services.AddSingleton<
				IUigfV42ExportWriter,
				UigfV42GachaWriter>();
			builder.Services.AddSingleton<
				IGachaTableExportWriter,
				GachaTableExportWriter>();
			builder.Services.AddTransient<LoadGachaExportData>();
			builder.Services.AddTransient<ExportUigfV42GachaRecords>();
			builder.Services.AddTransient<ExportGachaTable>();


			builder.Services.AddTransient<MainPageViewModel>();
			builder.Services.AddTransient<GachaAnalysisViewModel>();
			builder.Services.AddTransient<MainPage>();

			// Phase 5
			builder.Services.AddSingleton<IPlayerArchiveRepository, SqlitePlayerArchiveRepository>();
			builder.Services.AddSingleton<IGameAccountRepository, SqliteGameAccountRepository>();
			builder.Services.AddSingleton<IPreferences>(Preferences.Default);
			builder.Services.AddSingleton<
				IArchiveSelectionStore,
				PreferencesArchiveSelectionStore>();

			// Passport credentials are secrets and must not be stored in Preferences
			// or in the main SQLite database.
			builder.Services.AddSingleton<ISecureStorage>(SecureStorage.Default);
			builder.Services.AddSingleton<
				IPassportAccountStore,
				SecureStoragePassportAccountStore>();
			builder.Services.AddSingleton<MiHoYoPassportClient>();
			builder.Services.AddSingleton<
				IMainlandPassportClient,
				MainlandPassportClient>();
			builder.Services.AddSingleton<
				IHoYoLabPassportClient,
				HoYoLabPassportClient>();
			builder.Services.AddSingleton<
				IMiHoYoAccountProfileClient,
				MiHoYoAccountProfileClient>();
			builder.Services.AddSingleton<
				IPassportSelectionStore,
				PreferencesPassportSelectionStore>();
			builder.Services.AddSingleton(new PassportCredentialMaintenanceOptions());
			builder.Services.AddTransient<PassportAccountWriter>();
			builder.Services.AddTransient<MainlandPassportLoginService>();
			builder.Services.AddTransient<HoYoLabPassportLoginService>();
			builder.Services.AddTransient<PassportCredentialMaintenanceService>();
			builder.Services.AddSingleton<MobileCaptchaCooldown>();
			builder.Services.AddSingleton<ISTokenGachaUrlProvider, MiHoYoSTokenGachaUrlProvider>();
			builder.Services.AddSingleton<IWindowsGachaCacheUrlProvider, WindowsGachaCacheUrlProvider>();
			builder.Services.AddSingleton<IGachaLogClient, MiHoYoGachaLogClient>();
			builder.Services.AddTransient<RefreshGachaRecords>();
			builder.Services.AddSingleton<UserPageViewModel>();
			builder.Services.AddSingleton<HomePage>();
			builder.Services.AddSingleton<UserPage>();
			builder.Services.AddTransient<MainlandLoginMethodPage>();
			builder.Services.AddTransient<HoYoLabPasswordLoginPage>();
			builder.Services.AddTransient<QrLoginPage>();
			builder.Services.AddTransient<MobileCaptchaLoginPage>();
			builder.Services.AddTransient<ManualCookieLoginPage>();

			builder.Services.AddTransient<CreatePlayerArchive>();
			builder.Services.AddTransient<GetPlayerArchives>();
			builder.Services.AddTransient<GetGameAccounts>();
			builder.Services.AddTransient<AddGameAccount>();
			builder.Services.AddTransient<UpdateGameAccount>();
			builder.Services.AddTransient<RenamePlayerArchive>();
			builder.Services.AddTransient<DeletePlayerArchive>();
			builder.Services.AddTransient<DeleteGameAccount>();
			builder.Services.AddSingleton<AppShell>();
			builder.Services.AddTransient<ArchiveSelectionService>();
			builder.Services.AddSingleton<ApplicationStartupService>();
			builder.Services.AddSingleton<StartupPageViewModel>();
			builder.Services.AddSingleton<StartupPage>();

			return builder.Build();
		}
	}
}
