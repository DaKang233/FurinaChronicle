// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Services.Gacha.History;
using FurinaChronicle.Services.HistoryDomains;

namespace FurinaChronicle.Services.Archives.Portable;

public static class ArchiveRestoreSemanticDigest
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public static string ForGacha(GachaRecord value) => Compute(new
    {
        value.ExternalRecordId,
        value.ItemName,
        value.RankType,
        value.Time,
        value.ItemId,
        value.ItemType,
        value.GachaType,
        value.UigfGachaType,
        value.Count,
        value.Provenance,
    });

    public static string ForGachaRevision(GachaRevisionItem value) => Compute(new
    {
        value.RevisionId,
        value.Reference.ExternalRecordId,
        value.ChangeKind,
        value.BeforeVersion,
        value.AfterVersion,
        Before = value.Before is null ? null : ForGacha(value.Before),
        After = value.After is null ? null : ForGacha(value.After),
        value.CreatedAt,
    });

    public static string ForItemObservation(ItemStateObservation value) => Compute(new
    {
        value.ObservationId,
        value.Kind,
        value.CollectionCompleteness,
        Entries = value.Entries
            .OrderBy(item => item.StableItemKey, StringComparer.Ordinal)
            .ToArray(),
        value.Provenance,
    });

    public static string ForManualAcquisition(ManualAcquisitionState value) =>
        ForManualAcquisition(value.Acquisition);

    private static string ForManualAcquisition(ManualAcquisition value) => Compute(new
    {
        value.AcquisitionId,
        value.Kind,
        value.TypeId,
        value.OccurredAt,
        value.Reason,
        value.ReasonDetail,
        value.Provenance,
    });

    public static string ForManualRevision(
        ManualAcquisitionRevisionItem value) => Compute(new
    {
        value.RevisionId,
        value.ChangeKind,
        value.BeforeVersion,
        value.AfterVersion,
        Before = value.Before is null ? null : ForManualAcquisition(value.Before),
        After = value.After is null ? null : ForManualAcquisition(value.After),
        value.CreatedAt,
    });

    public static string ForChallenge(ChallengeObservation value) => Compute(new
    {
        value.ObservationId,
        value.Mode,
        value.SeasonId,
        value.PeriodStart,
        value.PeriodEnd,
        value.Completeness,
        value.SpiralAbyssFloors,
        value.Provenance,
    });

    private static string Compute<T>(T value)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
    }
}
