using FurinaArchive.App.ViewModels;
using FurinaArchive.Infrastructure.Importing.Json;
using FurinaArchive.Infrastructure.Persistence.Sqlite;
using FurinaArchive.Services.Abstractions;
using FurinaArchive.Services.Wishes.Importing;
using FurinaArchive.Services.Wishes;
using Microsoft.Extensions.Logging;
using FurinaArchive.Core.Wishes;
using System.Diagnostics;

namespace FurinaArchive.App
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
            string databasePath = Path.Combine(FileSystem.AppDataDirectory, "furinaarchive.db3");
            Debug.WriteLine($"FurinaArchive database: {databasePath}");
            builder.Services.AddSingleton(new SqliteDatabaseOptions(databasePath));
            builder.Services.AddSingleton<FurinaDatabase>();
            builder.Services.AddSingleton<IWishRecordRepository, SqliteWishRecordRepository>();

            builder.Services.AddSingleton<IWishRecordReader, JsonWishRecordReader>();
            builder.Services.AddTransient<GetRecentWishRecords>();
            builder.Services.AddTransient<ImportWishRecords>();

            builder.Services.AddTransient<MainPageViewModel>();
            builder.Services.AddTransient<MainPage>();

            return builder.Build();
        }
    }
}
