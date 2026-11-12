// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.App.Diagnostics;
using FurinaChronicle.App.Startup;
using FurinaChronicle.App.UI.Gacha;
using FurinaChronicle.App.UI.Pages;
using FurinaChronicle.App.ViewModels;
using FurinaChronicle.Infrastructure.HistoryDomains.Collecting;
using FurinaChronicle.Infrastructure.Metadata;
using FurinaChronicle.Services.Abstractions;
using FurinaChronicle.Services.HistoryDomains.Collecting;

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
        services.AddTransient<ChroniclePageViewModel>();
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
        services.AddTransient<ChroniclePage>();
        services.AddTransient<ChallengeHistoryPage>();
        services.AddTransient<CharacterHistoryPage>();
        services.AddSingleton<ISpiralAbyssSource, HoYoGameRecordSpiralAbyssSource>();
        services.AddSingleton<ValidateSpiralAbyssCollection>();
        services.AddSingleton<
            IChallengeModeSource,
            HoYoGameRecordChallengeModeSource>();
        services.AddSingleton<ValidateChallengeModeCollection>();
        services.AddSingleton<
            ICharacterWeaponSource,
            HoYoGameRecordCharacterWeaponSource>();
        services.AddSingleton<ValidateCharacterWeaponCollection>();
        services.AddSingleton<CollectSpiralAbyss>();
        services.AddSingleton<CollectChallengeMode>();
        services.AddSingleton<CollectCharacterWeapon>();
        services.AddSingleton(new SnapMetadataOptions(
            Path.Combine(
                FileSystem.Current.AppDataDirectory,
                "Metadata",
                "SnapMetadata")));
        services.AddSingleton(serviceProvider => new SnapMetadataClient(
            serviceProvider.GetRequiredService<SnapMetadataOptions>()));
        services.AddSingleton<
            IDomainMetadataService,
            CompositeDomainMetadataService>();
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
