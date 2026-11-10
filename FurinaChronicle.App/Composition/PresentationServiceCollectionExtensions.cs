// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.App.Diagnostics;
using FurinaChronicle.App.Startup;
using FurinaChronicle.App.UI.Gacha;
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
        services.AddTransient<ArchiveHistoryPageViewModel>();
        services.AddSingleton<ArchivePageViewModel>();
        services.AddSingleton<ArchiveTransferPageViewModel>();
        services.AddSingleton<UserPageViewModel>();
        services.AddSingleton<StartupPageViewModel>();
#if PHASE8B_VALIDATION
        services.AddSingleton<Phase8BValidationPageViewModel>();
#endif

        services.AddTransient<GachaPage>();
        services.AddTransient<ArchiveHistoryPage>();
        services.AddSingleton<HomePage>();
        services.AddSingleton<Ui1GachaPage>();
        services.AddSingleton<GachaAppearanceSettings>();
        services.AddTransient<SettingsPage>();
        services.AddSingleton<UserPage>();
        services.AddSingleton<ArchivePage>();
        services.AddSingleton<ArchiveTransferPage>();
        services.AddTransient<MainlandLoginMethodPage>();
        services.AddTransient<HoYoLabPasswordLoginPage>();
        services.AddTransient<QrLoginPage>();
        services.AddTransient<MobileCaptchaLoginPage>();
        services.AddTransient<ManualCookieLoginPage>();
        services.AddSingleton<StartupPage>();
#if PHASE8B_VALIDATION
        services.AddSingleton<Phase8BValidationPage>();
#endif

        services.AddSingleton<AppShell>();
        services.AddSingleton<ApplicationStartupService>();

        return services;
    }
}
