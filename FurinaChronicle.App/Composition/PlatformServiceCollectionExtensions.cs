// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

namespace FurinaChronicle.App.Composition;

internal static class PlatformServiceCollectionExtensions
{
    public static IServiceCollection AddPlatformServices(
        this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IPreferences>(Preferences.Default);
        services.AddSingleton<ISecureStorage>(SecureStorage.Default);

        return services;
    }
}
