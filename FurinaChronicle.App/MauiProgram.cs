// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.App.Composition;
using FurinaChronicle.App.UI.TechnicalPreview;
using Microsoft.Extensions.Logging;

namespace FurinaChronicle.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        MauiAppBuilder builder = MauiApp.CreateBuilder();
        builder
#if UI1_PREVIEW
            .UseMauiApp<Ui1PreviewApp>()
#else
            .UseMauiApp<App>()
#endif
            .ConfigureMauiHandlers(handlers =>
            {
#if ANDROID
                handlers.AddHandler<
                    MaterialExtendedActionButton,
                    AndroidMaterialExtendedActionButtonHandler>();
#endif
            })
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

#if DEBUG
        builder.Logging.AddDebug();
#endif

#if UI1_PREVIEW
        // The standalone preview never resolves production startup, database or credential services.
        builder.Services.AddSingleton<UI.Pages.Ui1GachaPage>();
#else
        builder.Services
            .AddPlatformServices()
            .AddPersistence()
            .AddArchiveFeature()
            .AddPassportFeature()
            .AddGachaFeature()
            .AddPresentation();
#endif

        return builder.Build();
    }
}
