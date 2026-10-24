// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Text.Json;
using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Services.HistoryDomains.Importing;

namespace FurinaChronicle.Infrastructure.HistoryDomains.Importing;

public sealed class JsonArchiveDomainSourceFileReader
    : IArchiveDomainSourceFileReader
{
    public const long MaximumFileBytes = 2 * 1024 * 1024;
    public const int MaximumObservationCount = 10_000;
    private const string FormatName = "furina-collector-fixture";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = false,
        MaxDepth = 32,
    };

    public async Task<IReadOnlyList<ItemStateObservation>>
        ReadItemObservationsAsync(
            Stream source,
            Guid targetGameAccountId,
            CancellationToken cancellationToken = default)
    {
        ValidateAccount(targetGameAccountId);
        SourceDocument document = await ReadAsync(source, cancellationToken);
        if (document.Dataset != "character-weapon" ||
            document.ItemObservations is null ||
            document.ChallengeObservations is { Count: > 0 })
        {
            throw new InvalidDataException(
                "The collector fixture is not a character/weapon dataset.");
        }
        ValidateCount(document.ItemObservations.Count);
        return document.ItemObservations.Select(item =>
            new ItemStateObservation(
                new ItemObservationId(item.ObservationId),
                targetGameAccountId,
                item.Kind,
                item.Completeness,
                item.Entries.Select(entry => new InventoryStateEntry(
                    item.Kind,
                    entry.TypeId,
                    entry.InstanceId,
                    entry.Quantity,
                    entry.Level,
                    entry.Progression,
                    entry.FieldCoverage,
                    entry.FieldCompleteness)),
                CreateProvenance(item.Source)))
            .ToArray();
    }

    public async Task<IReadOnlyList<ChallengeObservation>>
        ReadChallengeObservationsAsync(
            Stream source,
            Guid targetGameAccountId,
            CancellationToken cancellationToken = default)
    {
        ValidateAccount(targetGameAccountId);
        SourceDocument document = await ReadAsync(source, cancellationToken);
        if (document.Dataset != "spiral-abyss" ||
            document.ChallengeObservations is null ||
            document.ItemObservations is { Count: > 0 })
        {
            throw new InvalidDataException(
                "The collector fixture is not a Spiral Abyss dataset.");
        }
        ValidateCount(document.ChallengeObservations.Count);
        return document.ChallengeObservations.Select(item =>
        {
            if (item.Mode != ChallengeMode.SpiralAbyss)
            {
                throw new InvalidDataException(
                    "Only Spiral Abyss collector fixtures are supported in Phase 8B.5.");
            }
            return new ChallengeObservation(
                new ChallengeObservationId(item.ObservationId),
                targetGameAccountId,
                item.Mode,
                item.SeasonId,
                item.PeriodStart,
                item.PeriodEnd,
                item.Completeness,
                item.Floors.Select(floor =>
                    new SpiralAbyssFloorResult(
                        floor.Floor,
                        floor.Completeness,
                        floor.Chambers.Select(chamber =>
                            new SpiralAbyssChamberResult(
                                chamber.Chamber,
                                chamber.Stars,
                                CreateOccurrence(chamber.CompletedAt))))),
                CreateProvenance(item.Source));
        }).ToArray();
    }

    private static async Task<SourceDocument> ReadAsync(
        Stream source,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[32 * 1024];
        while (true)
        {
            int read = await source.ReadAsync(chunk, cancellationToken);
            if (read == 0)
            {
                break;
            }
            if (buffer.Length + read > MaximumFileBytes)
            {
                throw new InvalidDataException(
                    $"The collector fixture exceeds the {MaximumFileBytes}-byte limit.");
            }
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }
        buffer.Position = 0;
        SourceDocument document =
            await JsonSerializer.DeserializeAsync<SourceDocument>(
                buffer,
                JsonOptions,
                cancellationToken) ?? throw new InvalidDataException(
                "The collector fixture is empty or invalid.");
        if (document.Format != FormatName || document.FormatMajor != 1)
        {
            throw new InvalidDataException(
                "The collector fixture format or major version is unsupported.");
        }
        return document;
    }

    private static RecordProvenance CreateProvenance(SourceWire source)
    {
        if (source.ObservedAt is null && source.SourceRecordId is null &&
            source.SourceSnapshotId is null)
        {
            throw new InvalidDataException(
                "A collector observation requires ordering or stable source evidence.");
        }
        DataSourceReference? reference =
            source.Provider is null && source.SourceRecordId is null &&
            source.SourceSnapshotId is null
                ? null
                : new DataSourceReference(
                    source.Provider,
                    source.SourceRecordId,
                    source.SourceSnapshotId);
        return new RecordProvenance(
            DataOrigin.OfficialApi,
            new RecordTimestamps(
                source.ObservedAt,
                source.FetchedAt),
            reference,
            source.AcquisitionBatchId is null
                ? null
                : new AcquisitionBatchId(source.AcquisitionBatchId.Value));
    }

    private static OccurrenceTime CreateOccurrence(OccurrenceWire? value)
    {
        if (value is null || value.Kind == OccurrenceTimeKind.Unknown)
        {
            return OccurrenceTime.Unknown;
        }
        if (value.Start is null || value.Precision is null)
        {
            throw new InvalidDataException(
                "A known occurrence requires a start and precision.");
        }
        return value.Kind switch
        {
            OccurrenceTimeKind.Instant =>
                OccurrenceTime.At(value.Start.Value, value.Precision.Value),
            OccurrenceTimeKind.Interval when value.End is not null =>
                OccurrenceTime.ForInterval(
                    value.Start.Value,
                    value.End.Value,
                    value.Precision.Value),
            OccurrenceTimeKind.UncertainInterval when value.End is not null =>
                OccurrenceTime.Within(
                    value.Start.Value,
                    value.End.Value,
                    value.Precision.Value),
            _ => throw new InvalidDataException(
                "The occurrence interval is incomplete or unsupported."),
        };
    }

    private static void ValidateAccount(Guid accountId)
    {
        if (accountId == Guid.Empty)
        {
            throw new ArgumentException(
                "The target game account ID cannot be empty.",
                nameof(accountId));
        }
    }

    private static void ValidateCount(int count)
    {
        if (count == 0 || count > MaximumObservationCount)
        {
            throw new InvalidDataException(
                $"A collector fixture must contain 1 to {MaximumObservationCount} observations.");
        }
    }

    private sealed record SourceDocument(
        string Format,
        int FormatMajor,
        string Dataset,
        IReadOnlyList<ItemObservationWire>? ItemObservations,
        IReadOnlyList<ChallengeObservationWire>? ChallengeObservations);

    private sealed record SourceWire(
        DateTimeOffset? ObservedAt,
        DateTimeOffset? FetchedAt,
        string? Provider,
        string? SourceRecordId,
        string? SourceSnapshotId,
        Guid? AcquisitionBatchId);

    private sealed record ItemObservationWire(
        Guid ObservationId,
        InventoryItemKind Kind,
        DataCompleteness Completeness,
        IReadOnlyList<InventoryEntryWire> Entries,
        SourceWire Source);

    private sealed record InventoryEntryWire(
        string TypeId,
        string? InstanceId,
        int? Quantity,
        int? Level,
        int? Progression,
        InventoryFieldCoverage FieldCoverage,
        DataCompleteness FieldCompleteness);

    private sealed record ChallengeObservationWire(
        Guid ObservationId,
        ChallengeMode Mode,
        string SeasonId,
        DateTimeOffset? PeriodStart,
        DateTimeOffset? PeriodEnd,
        DataCompleteness Completeness,
        IReadOnlyList<FloorWire> Floors,
        SourceWire Source);

    private sealed record FloorWire(
        int Floor,
        DataCompleteness Completeness,
        IReadOnlyList<ChamberWire> Chambers);

    private sealed record ChamberWire(
        int Chamber,
        int Stars,
        OccurrenceWire? CompletedAt);

    private sealed record OccurrenceWire(
        OccurrenceTimeKind Kind,
        DateTimeOffset? Start,
        DateTimeOffset? End,
        TimePrecision? Precision);
}
