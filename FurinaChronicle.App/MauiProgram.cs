// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.App.Composition;
using Microsoft.Extensions.Logging;

namespace FurinaChronicle.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        MauiAppBuilder builder = MauiApp.CreateBuilder();
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

        builder.Services
            .AddPlatformServices()
            .AddPersistence()
            .AddArchiveFeature()
            .AddPassportFeature()
            .AddGachaFeature()
            .AddPresentation();

        return builder.Build();
    }
}
