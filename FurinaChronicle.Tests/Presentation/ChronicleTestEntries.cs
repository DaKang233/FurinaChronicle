// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Chronicle;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Presentation.Chronicle;
using FurinaChronicle.Services.Chronicle;

namespace FurinaChronicle.Tests.Presentation;

internal static class ChronicleTestEntries
{
    public static readonly Guid AccountId =
        Guid.Parse("11111111-1111-4111-8111-111111111111");

    public static readonly DateTimeOffset SampleTime =
        new(2026, 1, 1, 0, 0, 5, TimeSpan.FromHours(8));

    public static ChronicleEntry Create(
        string titleKey = ChronicleKeyCatalog.GachaAcquisitionTitle,
        ChronicleEntryKind kind = ChronicleEntryKind.GachaAcquisition,
        ChronicleDomain domain = ChronicleDomain.Gacha,
        ChronicleTimeline timelines = ChronicleTimeline.Main,
        ChronicleGranularity granularity = ChronicleGranularity.Default,
        string? milestone = null,
        ChronicleTime? time = null,
        IReadOnlyDictionary<string, string>? arguments = null,
        ChronicleEvidence? evidence = null,
        string? itemId = null,
        int? rankType = null,
        int? localRecordedPulls = null,
        int? exactPity = null,
        DataOrigin baseOrigin = DataOrigin.StandardImport,
        DataOrigin annotationOrigin = DataOrigin.Derived,
        DataCompleteness completeness = DataCompleteness.Unknown,
        DataConfidence confidence = DataConfidence.Unknown,
        ChronicleImportance importance = ChronicleImportance.Major,
        string seed = "1",
        Guid? accountId = null)
    {
        Guid account = accountId ?? AccountId;
        return new ChronicleEntry(
            ChronicleEntryId.Create(
                account, domain, kind, "test-entity", seed,
                ChronicleVersions.Projection),
            portableEntityKey: null,
            ChronicleVersions.Projection,
            account,
            domain,
            kind,
            timelines,
            granularity,
            milestone,
            time ?? Instant(SampleTime),
            baseOrigin,
            annotationOrigin,
            confidence,
            completeness,
            "test-entity",
            seed,
            titleKey,
            arguments ?? EmptyArguments(),
            importance,
            evidence ?? KnownEvidence(),
            itemId: itemId,
            rankType: rankType,
            correlationId: null,
            localRecordedPulls: localRecordedPulls,
            exactPity: exactPity);
    }

    public static ChronicleTime Instant(
        DateTimeOffset value,
        TimePrecision precision = TimePrecision.Second) =>
        new(OccurrenceTime.At(value, precision), null, null);

    public static ChronicleTime ObservedOnly(DateTimeOffset observed) =>
        new(null, observed, observed.AddMinutes(5));

    public static ChronicleTime FetchedOnly(DateTimeOffset fetched) =>
        new(null, null, fetched);

    public static ChronicleTime Undated() => new(null, null, null);

    public static ChronicleTime WithObservation(
        DateTimeOffset occurred,
        DateTimeOffset observed) =>
        new(OccurrenceTime.At(occurred, TimePrecision.Second), observed,
            observed.AddMinutes(1));

    public static ChronicleEvidence KnownEvidence(
        string reason = ChronicleKeyCatalog.SavedObservationReason) => new(
        ChronicleEvidenceStatus.Known,
        ChronicleEvidenceScope.LocalArchiveRecords,
        reason,
        ["test-entity:1"]);

    public static Dictionary<string, string> EmptyArguments() =>
        new(StringComparer.Ordinal);

    public static Dictionary<string, string> Arguments(
        params (string Key, string Value)[] values)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string key, string value) in values)
        {
            result[key] = value;
        }
        return result;
    }

    public static ChroniclePage Page(
        IReadOnlyList<ChronicleEntry> entries,
        string? nextCursor = null,
        string generationId = "generation-1",
        ChronicleQueryError error = ChronicleQueryError.None,
        string? errorMessage = null) =>
        new(entries, nextCursor, generationId, error, errorMessage);
}
