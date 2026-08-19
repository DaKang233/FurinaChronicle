using FurinaChronicle.App.ViewModels;
using FurinaChronicle.App.Persistence;
using FurinaChronicle.Infrastructure.Importing.Json;
using FurinaChronicle.Infrastructure.Persistence.Sqlite;
using FurinaChronicle.Services.Abstractions;
using FurinaChronicle.Services.Wishes.Importing;
using FurinaChronicle.Services.Wishes;
using FurinaChronicle.Services.Archives;
using Microsoft.Extensions.Logging;
using FurinaChronicle.Core.Wishes;
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
            // builder.Services.AddSingleton<IWishRecordRepository>(_ => new InMemoryWishRecordRepository(Array.Empty<WishRecord>()));

            // Phase 4
            string databasePath = Path.Combine(FileSystem.AppDataDirectory, "furinachronicle.db3");
            Debug.WriteLine($"FurinaChronicle database: {databasePath}");
            builder.Services.AddSingleton(new SqliteDatabaseOptions(databasePath));
            builder.Services.AddSingleton<FurinaDatabase>();
            builder.Services.AddSingleton<IWishRecordRepository, SqliteWishRecordRepository>();

            builder.Services.AddSingleton<IWishRecordReader, JsonWishRecordReader>();
            builder.Services.AddTransient<GetRecentWishRecords>();
            builder.Services.AddTransient<ImportWishRecords>();

            builder.Services.AddTransient<MainPageViewModel>();
            builder.Services.AddTransient<MainPage>();

            // Phase 5
            builder.Services.AddSingleton<IPlayerArchiveRepository, SqlitePlayerArchiveRepository>();
            builder.Services.AddSingleton<IGameAccountRepository, SqliteGameAccountRepository>();
            builder.Services.AddSingleton<IPreferences>(Preferences.Default);
            builder.Services.AddSingleton<
                IArchiveSelectionStore,
                PreferencesArchiveSelectionStore>();

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

            return builder.Build();
        }
    }
}
