// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.History;
using FurinaChronicle.Services.Archives;
using FurinaChronicle.Services.Gacha.History;
using FurinaChronicle.Services.History;
using FurinaChronicle.Services.HistoryDomains;

namespace FurinaChronicle.Presentation.Management;

// Adapter over the existing operation history services. Undo, retention limits and revisions keep
// their production semantics; the page only renders and confirms.
public sealed class ServiceArchiveHistorySource(
    GetPlayerArchives getArchives,
    IArchiveOperationHistoryStore operationHistory,
    IGachaAtomicChangeStore atomicChangeStore,
    IArchiveDomainHistoryStore domainHistory,
    TimeProvider timeProvider) : IArchiveHistorySource
{
    public Task<IReadOnlyList<PlayerArchive>> GetArchivesAsync(
        CancellationToken cancellationToken = default) =>
        getArchives.ExecuteAsync(cancellationToken);

    public Task<IReadOnlyList<OperationHistoryItem>> GetHistoryAsync(
        Guid archiveId,
        int count,
        CancellationToken cancellationToken = default) =>
        operationHistory.GetHistoryAsync(archiveId, count, cancellationToken);

    public Task<int> GetUndoLimitAsync(
        Guid archiveId,
        CancellationToken cancellationToken = default) =>
        operationHistory.GetUndoLimitAsync(archiveId, cancellationToken);

    public Task<UndoLimitChangeResult> SetUndoLimitAsync(
        Guid archiveId,
        int limit,
        CancellationToken cancellationToken = default) =>
        operationHistory.SetUndoLimitAsync(archiveId, limit, cancellationToken);

    public Task<ArchiveOperationResult> UndoLatestAsync(
        Guid archiveId,
        OperationId operationId,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = timeProvider.GetLocalNow();
        return operationHistory.UndoLatestAsync(
            new ArchiveUndoRequest(operationId, archiveId, now, now),
            cancellationToken);
    }

    public Task<IReadOnlyList<GachaRevisionItem>> GetGachaRevisionsAsync(
        Guid changeSetId,
        CancellationToken cancellationToken = default) =>
        atomicChangeStore.GetRevisionsAsync(changeSetId, cancellationToken);

    public Task<IReadOnlyList<DomainRevisionItem>> GetDomainRevisionsAsync(
        Guid changeSetId,
        CancellationToken cancellationToken = default) =>
        domainHistory.GetDomainRevisionsAsync(changeSetId, cancellationToken);
}
