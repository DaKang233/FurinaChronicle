// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.History;
using FurinaChronicle.Services.Abstractions;
using FurinaChronicle.Services.Archives;
using FurinaChronicle.Services.Archives.Portable;
using FurinaChronicle.Services.Gacha.History;

namespace FurinaChronicle.Presentation.Management;

// Formally composed export/restore services. The Windows host builds this set from the isolated
// environment; the presentation state never constructs SQLite objects itself.
public sealed record ArchiveTransferServiceSet(
    IArchiveLogicalExporter Exporter,
    IArchiveRestoreInputStager Stager,
    IArchiveRestorePlanner Planner,
    IArchiveAtomicRestorer Restorer,
    IArchiveTransferWorkspace Workspace);

public sealed class ServiceArchiveTransferSource(
    GetPlayerArchives getArchives,
    IGameAccountRepository accounts,
    ArchiveTransferServiceSet services) : IArchiveTransferSource
{
    public Task<IReadOnlyList<PlayerArchive>> GetArchivesAsync(
        CancellationToken cancellationToken = default) =>
        getArchives.ExecuteAsync(cancellationToken);

    public Task<IReadOnlyList<GameAccount>> GetAccountsAsync(
        Guid archiveId,
        CancellationToken cancellationToken = default) =>
        accounts.GetByArchiveIdAsync(archiveId, cancellationToken);

    public Task<ArchiveWriteResult> ExportAsync(
        Stream destination,
        ArchiveExportScope scope,
        ArchiveGeneratorInfo generator,
        CancellationToken cancellationToken = default) =>
        services.Exporter.ExportAsync(destination, scope, generator, cancellationToken);

    public Task<IArchiveRestoreStagedPackage> StageAsync(
        Stream source,
        CancellationToken cancellationToken = default) =>
        services.Stager.StageAsync(source, cancellationToken);

    public Task<ArchiveRestorePlan> PlanAsync(
        ArchiveRestorePlanRequest request,
        CancellationToken cancellationToken = default) =>
        services.Planner.PlanAsync(request, cancellationToken);

    public Task<ArchiveRestorePlanFreshness> CheckFreshnessAsync(
        ArchiveRestorePlan plan,
        CancellationToken cancellationToken = default) =>
        services.Planner.CheckFreshnessAsync(plan, cancellationToken);

    public Task<ArchiveRestoreApplyResult> ApplyAsync(
        ArchiveRestoreApplyRequest request,
        CancellationToken cancellationToken = default) =>
        services.Restorer.ApplyAsync(request, cancellationToken);

    public Task<ArchiveRestoreReceipt?> FindReceiptAsync(
        OperationId operationId,
        CancellationToken cancellationToken = default) =>
        services.Restorer.FindReceiptAsync(operationId, cancellationToken);

    public Task<ArchiveReplaceRecoveryPoint?> FindCurrentRecoveryPointAsync(
        CancellationToken cancellationToken = default) =>
        services.Restorer.FindCurrentRecoveryPointAsync(cancellationToken);
}
