using FurinaChronicle.App.ViewModels;
using FurinaChronicle.Infrastructure.Importing.Json;
using FurinaChronicle.Infrastructure.Persistence.Sqlite;
using FurinaChronicle.Services.Abstractions;
using FurinaChronicle.Services.Wishes.Importing;
using FurinaChronicle.Services.Wishes;
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

            return builder.Build();
        }
    }
}
