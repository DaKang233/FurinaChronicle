// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.History;
using FurinaChronicle.Core.Records;

namespace FurinaChronicle.Services.Gacha.History;

public sealed record GachaCorrectionInput(
    string? ItemName,
    string? ItemId,
    int? RankType,
    DateTimeOffset OccurredAt);

public sealed class ManageGachaHistory(IGachaAtomicChangeStore store)
{
    public async Task<GachaAtomicChangeResult> CorrectAsync(
        OperationId operationId,
        GachaFactReference reference,
        GachaCorrectionInput input,
        Guid? cleanupConfirmationId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        GachaFactState current = await RequireCurrentAsync(
            reference,
            cancellationToken);
        GachaRecord corrected = current.Record with
        {
            ItemName = NormalizeOptional(input.ItemName),
            ItemId = NormalizeOptional(input.ItemId),
            RankType = input.RankType,
            Time = input.OccurredAt,
            Provenance = new RecordProvenance(DataOrigin.UserEntered)
        };
        DateTimeOffset now = DateTimeOffset.Now;
        return await store.CommitAsync(
            new GachaAtomicChangeRequest(
                operationId,
                DataChangeOperationKind.Correction,
                DataOrigin.UserEntered,
                $"人工纠正抽卡记录 {reference.ExternalRecordId}",
                now,
                now,
                [new GachaFactMutation(
                    reference,
                    corrected,
                    current.Version)],
                cleanupConfirmationId: cleanupConfirmationId),
            cancellationToken);
    }

    public async Task<GachaAtomicChangeResult> DeleteAsync(
        OperationId operationId,
        GachaFactReference reference,
        Guid? cleanupConfirmationId = null,
        CancellationToken cancellationToken = default)
    {
        GachaFactState current = await RequireCurrentAsync(
            reference,
            cancellationToken);
        DateTimeOffset now = DateTimeOffset.Now;
        return await store.CommitAsync(
            new GachaAtomicChangeRequest(
                operationId,
                DataChangeOperationKind.Delete,
                DataOrigin.UserEntered,
                $"删除抽卡记录 {reference.ExternalRecordId}（可撤销）",
                now,
                now,
                [new GachaFactMutation(
                    reference,
                    null,
                    current.Version)],
                cleanupConfirmationId: cleanupConfirmationId),
            cancellationToken);
    }

    public async Task<GachaAtomicChangeResult> IrreversiblyDeleteAsync(
        OperationId operationId,
        GachaFactReference reference,
        CancellationToken cancellationToken = default)
    {
        GachaFactState current = await RequireCurrentAsync(
            reference,
            cancellationToken);
        DateTimeOffset now = DateTimeOffset.Now;
        return await store.IrreversiblyDeleteAsync(
            new GachaIrreversibleDeleteRequest(
                operationId,
                reference,
                current.Version,
                now,
                now,
                $"永久删除抽卡记录 {reference.ExternalRecordId}"),
            cancellationToken);
    }

    public Task<GachaAtomicChangeResult> UndoLatestAsync(
        OperationId operationId,
        Guid archiveId,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = DateTimeOffset.Now;
        return store.UndoLatestAsync(
            new GachaUndoRequest(
                operationId,
                archiveId,
                now,
                now),
            cancellationToken);
    }

    private async Task<GachaFactState> RequireCurrentAsync(
        GachaFactReference reference,
        CancellationToken cancellationToken) =>
        await store.GetCurrentAsync(reference, cancellationToken) ??
        throw new InvalidOperationException(
            "The selected Gacha fact no longer exists. Reload before retrying.");

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
