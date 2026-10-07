// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.History;
using FurinaChronicle.Services.Abstractions;
using FurinaChronicle.Services.Gacha;
using FurinaChronicle.Services.Gacha.Writing;

namespace FurinaChronicle.Tests.Services.Gacha;

internal sealed class RepositoryGachaCommitter(
    IGachaRecordRepository repository)
    : ICommitGachaRecords
{
    public async Task<CommitGachaRecordsResult> ExecuteAsync(
        CommitGachaRecordsRequest request,
        CancellationToken cancellationToken = default)
    {
        GachaSaveResult saved = await repository.SaveBatchAsync(
            request.Records,
            cancellationToken,
            request.ConflictPolicy);
        return new CommitGachaRecordsResult(
            ChangeExecutionStatus.Applied,
            ChangeSetId: null,
            saved.InsertedCount,
            saved.UpdatedCount,
            saved.DuplicateCount,
            ConflictCount: 0,
            SuppressedCount: 0);
    }
}
