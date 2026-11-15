// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Services.Archives;
using FurinaChronicle.Services.History;

namespace FurinaChronicle.Presentation.Management;

// Settings reuses the production archive and operation-history services so the undo retention
// value shown here is the same value shown (and edited) by the operation history page.
public sealed class ServiceSettingsHistoryPort(
    GetPlayerArchives getArchives,
    IArchiveOperationHistoryStore operationHistory) : ISettingsHistoryPort
{
    public Task<IReadOnlyList<PlayerArchive>> GetArchivesAsync(
        CancellationToken cancellationToken = default) =>
        getArchives.ExecuteAsync(cancellationToken);

    public Task<int> GetUndoLimitAsync(
        Guid archiveId,
        CancellationToken cancellationToken = default) =>
        operationHistory.GetUndoLimitAsync(archiveId, cancellationToken);

    public Task<UndoLimitChangeResult> SetUndoLimitAsync(
        Guid archiveId,
        int limit,
        CancellationToken cancellationToken = default) =>
        operationHistory.SetUndoLimitAsync(archiveId, limit, cancellationToken);
}
