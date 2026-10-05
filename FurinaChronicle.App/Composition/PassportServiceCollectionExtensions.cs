// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.App.Persistence;
using FurinaChronicle.Infrastructure.Passport;
using FurinaChronicle.Services.Passport;

namespace FurinaChronicle.App.Composition;

internal static class PassportServiceCollectionExtensions
{
    public static IServiceCollection AddPassportFeature(
        this IServiceCollection services)
    {
        // Passport credentials are secrets and must not be stored in Preferences
        // or in the main SQLite database.
        services.AddSingleton<
            IPassportAccountStore,
            SecureStoragePassportAccountStore>();
        services.AddSingleton<MiHoYoPassportClient>();
        services.AddSingleton<
            IMainlandPassportClient,
            MainlandPassportClient>();
        services.AddSingleton<
            IHoYoLabPassportClient,
            HoYoLabPassportClient>();
        services.AddSingleton<
            IMiHoYoAccountProfileClient,
            MiHoYoAccountProfileClient>();
        services.AddSingleton<
            IPassportSelectionStore,
            PreferencesPassportSelectionStore>();
        services.AddSingleton(new PassportCredentialMaintenanceOptions());

        services.AddTransient<PassportAccountWriter>();
        services.AddTransient<MainlandPassportLoginService>();
        services.AddTransient<HoYoLabPassportLoginService>();
        services.AddTransient<PassportCredentialMaintenanceService>();
        services.AddSingleton<MobileCaptchaCooldown>();

        return services;
    }
}
