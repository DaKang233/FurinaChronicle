// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.App.UI.TechnicalPreview;
using FurinaChronicle.App.Persistence;
using FurinaChronicle.Presentation.Gacha;
using FurinaChronicle.Presentation.Notifications;

namespace FurinaChronicle.App.Composition;

internal static class PlatformServiceCollectionExtensions
{
    public static IServiceCollection AddPlatformServices(
        this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IUiDispatcher>(new MauiUiDispatcher());
        services.AddSingleton<INotificationCenter, NotificationCenter>();
#if WINDOWS && (PHASE8C_VALIDATION || PHASE8B_VALIDATION)
        _ = PlatformStoragePaths.AppDataDirectory;
        services.AddSingleton<IPreferences, ValidationPreferences>();
        services.AddSingleton<ISecureStorage, DisabledValidationSecureStorage>();
#else
        services.AddSingleton<IPreferences>(Preferences.Default);
        services.AddSingleton<ISecureStorage>(SecureStorage.Default);
#endif
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
    private sealed class MauiUiDispatcher : IUiDispatcher
    {
        public Task InvokeAsync(Action action) => MainThread.InvokeOnMainThreadAsync(action);
    }
}
