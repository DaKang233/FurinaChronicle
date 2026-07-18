using FurinaArchive.App.ViewModels;
using FurinaArchive.Infrastructure.Importing.Json;
using FurinaArchive.Infrastructure.Persistence;
using FurinaArchive.Services.Abstractions;
using FurinaArchive.Services.Wishes.Importing;
using FurinaArchive.Services.Wishes;
using Microsoft.Extensions.Logging;
using FurinaArchive.Core.Wishes;

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

            builder.Services.AddSingleton<IWishRecordRepository>(_ => new InMemoryWishRecordRepository(Array.Empty<WishRecord>()));

            builder.Services.AddTransient<GetRecentWishRecords>();
            builder.Services.AddSingleton<IWishRecordReader, JsonWishRecordReader>();
            builder.Services.AddTransient<ImportWishRecords>();

            builder.Services.AddTransient<MainPageViewModel>();
            builder.Services.AddTransient<MainPage>();

            return builder.Build();
        }
    }
}
