// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Infrastructure.Diagnostics;
using FurinaChronicle.Presentation.Gacha;
using FurinaChronicle.Presentation.Management;

namespace FurinaChronicle.Windows.Composition;

// Everything the existing gacha page needs, resolved once from the shared composition root.
// The page keeps per-instance UI state; only stateless coordinators and the isolated environment
// are singletons.
public sealed record GachaPageServices(
    Ui1PreviewEnvironment Environment,
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
    GachaSTokenRefreshCoordinator STokenRefresh);
