// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Infrastructure.Diagnostics;
using FurinaChronicle.Presentation.Diagnostics;
using FurinaChronicle.Presentation.Gacha;
using FurinaChronicle.Presentation.Management;
using FurinaChronicle.Presentation.Notifications;
using FurinaChronicle.Services.Gacha.Abstractions;
using FurinaChronicle.Services.Gacha;
using FurinaChronicle.Services.Gacha.Refreshing;

namespace FurinaChronicle.Windows.Composition;

// Everything the existing gacha page needs, resolved once from the shared composition root.
// The page keeps per-instance UI state; only stateless coordinators and the isolated environment
// are singletons.
public sealed record GachaPageServices(
    WindowsHostEnvironment Environment,
    ServiceGachaReadOnlySource Source,
    IUiDispatcher Dispatcher,
    IFilePickerService FilePicker,
    GachaImportCoordinator Import,
    GachaPortableImportCoordinator PortableImport,
    GachaPortableExportPlanner PortableExport,
    GachaRecordOperationCoordinator RecordOperations,
    GachaManualRefreshCoordinator ManualRefresh,
    GachaWindowsCacheRefreshCoordinator WindowsCacheRefresh,
    GachaTeyvatHelperImportCoordinator TeyvatImport,
    GachaSTokenRefreshCoordinator STokenRefresh,
    RefreshUnselectedGachaRecords UnselectedRefresh,
    IGachaItemMetadataProvider? ItemMetadata,
    PreloadGachaBannerImages BannerPreloader,
    INotificationCenter Notifications,
    IDiagnosticService Diagnostics,
    IGachaItemMetadataProvider? ExportMetadata = null,
    GachaSelectionPreferences? SelectionPreferences = null);
