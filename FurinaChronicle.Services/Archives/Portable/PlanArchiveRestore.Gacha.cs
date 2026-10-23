// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Services.Gacha.History;

namespace FurinaChronicle.Services.Archives.Portable;

public sealed partial class PlanArchiveRestore
{
    private const int GachaLookupBatchSize = 500;

    private static async Task PlanGachaAsync(
        IArchiveRestoreStagedPackage source,
        IArchiveRestoreTargetSnapshot target,
        ArchiveRestoreAccountMapping account,
        Guid targetArchiveId,
        Guid targetAccountId,
        bool targetExists,
        ICollection<ArchiveRestoreFactPlan> facts,
        ICollection<ArchiveRestoreTombstoneProbe> probes,
        ICollection<ArchiveRestoreConflict> conflicts,
        CancellationToken cancellationToken)
    {
        var batch = new List<GachaRecord>(GachaLookupBatchSize);
        await foreach (GachaRecord record in source.ReadGachaRecordsAsync(
            account.SourceAccountReference,
            cancellationToken))
        {
            batch.Add(record);
            if (batch.Count == GachaLookupBatchSize)
            {
                await PlanBatchAsync(batch);
                batch.Clear();
            }
        }
        if (batch.Count > 0) await PlanBatchAsync(batch);

        await foreach (GachaRevisionItem revision in source.ReadGachaRevisionsAsync(
            account.SourceAccountReference,
            cancellationToken))
        {
            string sourceDigest = ArchiveRestoreSemanticDigest
                .ForGachaRevision(revision);
            ArchiveIndexedFact<GachaRevisionItem>? existing = targetExists
                ? await target.FindGachaRevisionAsync(
                    targetAccountId,
                    revision.RevisionId,
                    cancellationToken)
                : null;
            AddComparison(
                account,
                targetArchiveId,
                targetAccountId,
                ArchiveRestoreFactKind.GachaRevision,
                revision.RevisionId.ToString("D"),
                revision.Reference.ExternalRecordId,
                sourceDigest,
                existing?.SemanticDigest,
                facts,
                probes,
                conflicts);
        }
        return;

        async Task PlanBatchAsync(IReadOnlyCollection<GachaRecord> records)
        {
            IReadOnlyDictionary<string, ArchiveIndexedFact<GachaRecord>> existing =
                targetExists
                    ? await target.FindGachaRecordsAsync(
                        targetAccountId,
                        records.Select(item => item.ExternalRecordId).ToArray(),
                        cancellationToken)
                    : new Dictionary<string, ArchiveIndexedFact<GachaRecord>>();
            foreach (GachaRecord record in records)
            {
                string sourceDigest = ArchiveRestoreSemanticDigest.ForGacha(record);
                existing.TryGetValue(record.ExternalRecordId, out var targetFact);
                AddComparison(
                    account,
                    targetArchiveId,
                    targetAccountId,
                    ArchiveRestoreFactKind.GachaCurrent,
                    record.ExternalRecordId,
                    record.ExternalRecordId,
                    sourceDigest,
                    targetFact?.SemanticDigest,
                    facts,
                    probes,
                    conflicts);
            }
        }
    }
}
