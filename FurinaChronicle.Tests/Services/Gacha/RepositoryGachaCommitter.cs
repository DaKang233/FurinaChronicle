// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.History;
using FurinaChronicle.Infrastructure.Persistence;
using FurinaChronicle.Services.Gacha;
using FurinaChronicle.Services.Gacha.Writing;
using FurinaChronicle.Tests.TestDoubles;

namespace FurinaChronicle.Tests.Services.Gacha;

internal sealed class RepositoryGachaCommitter(
    InMemoryGachaRecordRepository repository,
    InMemoryGameAccountRepository? accountRepository = null)
    : ICommitGachaRecords
{
    public async Task<CommitGachaRecordsResult> ExecuteAsync(
        CommitGachaRecordsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (accountRepository is not null &&
            request.AccountsToCreate is not null)
        {
            foreach (var account in request.AccountsToCreate)
            {
                await accountRepository.AddAsync(account, cancellationToken);
            }
        }
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
