using FurinaArchive.App.ViewModels;
using FurinaArchive.Services.Abstractions;
using FurinaArchive.Services.Wishes;
using FurinaArchive.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

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

            builder.Services.AddSingleton<
                IWishRecordRepository,
                InMemoryWishRecordRepository>();

            builder.Services.AddTransient<GetRecentWishRecords>();

            builder.Services.AddTransient<MainPageViewModel>();
            builder.Services.AddTransient<MainPage>();

            return builder.Build();
        }
    }
}
