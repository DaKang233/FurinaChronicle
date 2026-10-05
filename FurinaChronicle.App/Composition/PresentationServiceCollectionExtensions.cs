// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.App.Startup;
using FurinaChronicle.App.ViewModels;

namespace FurinaChronicle.App.Composition;

internal static class PresentationServiceCollectionExtensions
{
    public static IServiceCollection AddPresentation(
        this IServiceCollection services)
    {
        services.AddTransient<MainPageViewModel>();
        services.AddTransient<GachaAnalysisViewModel>();
        services.AddSingleton<UserPageViewModel>();
        services.AddSingleton<StartupPageViewModel>();

        services.AddTransient<MainPage>();
        services.AddSingleton<HomePage>();
        services.AddSingleton<UserPage>();
        services.AddTransient<MainlandLoginMethodPage>();
        services.AddTransient<HoYoLabPasswordLoginPage>();
        services.AddTransient<QrLoginPage>();
        services.AddTransient<MobileCaptchaLoginPage>();
        services.AddTransient<ManualCookieLoginPage>();
        services.AddSingleton<StartupPage>();

        services.AddSingleton<AppShell>();
        services.AddSingleton<ApplicationStartupService>();

        return services;
    }
}
