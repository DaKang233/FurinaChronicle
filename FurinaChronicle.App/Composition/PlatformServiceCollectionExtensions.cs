// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.App.UI.TechnicalPreview;

namespace FurinaChronicle.App.Composition;

internal static class PlatformServiceCollectionExtensions
{
    public static IServiceCollection AddPlatformServices(
        this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IPreferences>(Preferences.Default);
        services.AddSingleton<ISecureStorage>(SecureStorage.Default);
#if ANDROID
        services.AddSingleton<
            IMaterialBottomSheetPreview,
            AndroidMaterialBottomSheetPreview>();
#else
        services.AddSingleton<
            IMaterialBottomSheetPreview,
            UnsupportedMaterialBottomSheetPreview>();
#endif

        return services;
    }
}
