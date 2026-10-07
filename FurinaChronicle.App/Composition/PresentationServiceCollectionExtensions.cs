// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.App.Diagnostics;
using FurinaChronicle.App.Startup;
using FurinaChronicle.App.UI.Pages;
using FurinaChronicle.App.ViewModels;

namespace FurinaChronicle.App.Composition;

internal static class PresentationServiceCollectionExtensions
{
    public static IServiceCollection AddPresentation(
        this IServiceCollection services)
    {
        services.AddSingleton(_ =>
            ApplicationVersionInfo.FromAssembly(typeof(MauiProgram).Assembly));

        services.AddTransient<GachaPageViewModel>();
        services.AddTransient<GachaAnalysisViewModel>();
        services.AddSingleton<ArchivePageViewModel>();
        services.AddSingleton<UserPageViewModel>();
        services.AddSingleton<StartupPageViewModel>();

        services.AddTransient<GachaPage>();
        services.AddSingleton<HomePage>();
        services.AddSingleton<UserPage>();
        services.AddSingleton<ArchivePage>();
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
