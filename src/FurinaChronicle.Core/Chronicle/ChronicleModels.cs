// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Security.Cryptography;
using System.Text;
using FurinaChronicle.Core.Records;

namespace FurinaChronicle.Core.Chronicle;

public enum ChronicleDomain
{
    Gacha = 1,
    Acquisition = 2,
    Inventory = 3,
    Challenge = 4,
    Achievement = 5,
}

public enum ChronicleEntryKind
{
    GachaAcquisition = 1,
    ManualAcquisition = 2,
    StateObservation = 3,
    StateChange = 4,
    ChallengeObservation = 5,
    ChallengeCompleted = 6,
    AchievementUnlocked = 7,
}

[Flags]
public enum ChronicleTimeline
{
    None = 0,
    Main = 1,
    Character = 2,
    Weapon = 4,
    Challenge = 8,
}

public enum ChronicleGranularity
{
    Default = 1,
    Detailed = 2,
}

public enum ChronicleSortBasis
{
    Unknown = 0,
    Occurred = 1,
    Observed = 2,
    Fetched = 3,
}

public enum ChronicleImportance
{
    Minor = 1,
    Normal = 2,
    Major = 3,
    Critical = 4,
}

public enum ChronicleEvidenceStatus
{
    Unknown = 0,
    Ambiguous = 1,
    Known = 2,
}

public enum ChronicleEvidenceScope
{
    Unknown = 0,
    LocalArchiveRecords = 1,
    LocalGachaRecords = 2,
    DeclaredCompleteHistory = 3,
    AccountHistory = 4,
}

public readonly record struct ChronicleEntryId
{
    public ChronicleEntryId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length != 64 || !value.All(Uri.IsHexDigit))
        {
            throw new ArgumentException(
                "Chronicle entry IDs must be 64 hexadecimal characters.",
                nameof(value));
        }

        Value = value.ToLowerInvariant();
    }

    public string Value { get; }

    public override string ToString() => Value;

    public static ChronicleEntryId Create(
        Guid gameAccountId,
        ChronicleDomain domain,
        ChronicleEntryKind kind,
        string sourceEntityType,
        string sourceEntityId,
        int projectionVersion)
    {
        if (gameAccountId == Guid.Empty)
        {
            throw new ArgumentException(
                "Game account ID cannot be empty.",
                nameof(gameAccountId));
        }
        if (!Enum.IsDefined(domain) || !Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(domain));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceEntityType);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceEntityId);
        if (projectionVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(projectionVersion));
        }

        using var stream = new MemoryStream();
        WriteComponent(stream, gameAccountId.ToString("D"));
        WriteComponent(stream, ((int)domain).ToString());
        WriteComponent(stream, ((int)kind).ToString());
        WriteComponent(stream, sourceEntityType);
        WriteComponent(stream, sourceEntityId);
        WriteComponent(stream, projectionVersion.ToString());
        return new ChronicleEntryId(
            Convert.ToHexString(SHA256.HashData(stream.ToArray()))
                .ToLowerInvariant());
    }

    private static void WriteComponent(Stream destination, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        destination.Write(BitConverter.GetBytes(bytes.Length));
        destination.Write(bytes);
    }
}

public sealed record ChronicleTime
{
    public ChronicleTime(
        OccurrenceTime? occurred,
        DateTimeOffset? observedAt,
        DateTimeOffset? fetchedAt)
    {
        Occurred = occurred?.Kind == OccurrenceTimeKind.Unknown
            ? null
            : occurred;
        ObservedAt = observedAt;
        FetchedAt = fetchedAt;

        if (Occurred is not null)
        {
            SortAtUtc = Occurred.Kind == OccurrenceTimeKind.UncertainInterval
                ? Occurred.UtcEnd
                : Occurred.UtcStart;
            SortBasis = ChronicleSortBasis.Occurred;
        }
        else if (observedAt is not null)
        {
            SortAtUtc = observedAt.Value.ToUniversalTime();
            SortBasis = ChronicleSortBasis.Observed;
        }
        else if (fetchedAt is not null)
        {
            SortAtUtc = fetchedAt.Value.ToUniversalTime();
            SortBasis = ChronicleSortBasis.Fetched;
        }
        else
        {
            SortAtUtc = null;
            SortBasis = ChronicleSortBasis.Unknown;
        }
    }

    public OccurrenceTime? Occurred { get; }

    public DateTimeOffset? ObservedAt { get; }

    public DateTimeOffset? FetchedAt { get; }

    public DateTimeOffset? SortAtUtc { get; }

    public ChronicleSortBasis SortBasis { get; }

    public bool IsUndated => SortAtUtc is null;

    public bool Overlaps(DateTimeOffset fromInclusive, DateTimeOffset toExclusive)
    {
        if (toExclusive <= fromInclusive || Occurred is null)
        {
            return false;
        }

        DateTimeOffset start = Occurred.UtcStart!.Value;
        DateTimeOffset endExclusive = Occurred.Kind == OccurrenceTimeKind.Instant
            ? start.AddTicks(1)
            : Occurred.UtcEnd!.Value;
        return start < toExclusive.ToUniversalTime() &&
            endExclusive > fromInclusive.ToUniversalTime();
    }
}

public sealed record ChronicleEvidence(
    ChronicleEvidenceStatus Status,
    ChronicleEvidenceScope Scope,
    string ReasonKey,
    IReadOnlyList<string> InputReferences)
{
    public static ChronicleEvidence Unknown(
        string reasonKey,
        params string[] inputs) =>
        new(
            ChronicleEvidenceStatus.Unknown,
            ChronicleEvidenceScope.Unknown,
            reasonKey,
            inputs);
}

public sealed record ChronicleEntry
{
    public ChronicleEntry(
        ChronicleEntryId entryId,
        string? portableEntityKey,
        int projectionVersion,
        Guid gameAccountId,
        ChronicleDomain domain,
        ChronicleEntryKind kind,
        ChronicleTimeline timelines,
        ChronicleGranularity minimumGranularity,
        string? milestone,
        ChronicleTime time,
        DataOrigin baseOrigin,
        DataOrigin annotationOrigin,
        DataConfidence confidence,
        DataCompleteness completeness,
        string sourceEntityType,
        string sourceEntityId,
        string titleKey,
        IReadOnlyDictionary<string, string> summaryArguments,
        ChronicleImportance importance,
        ChronicleEvidence evidence,
        string? itemId = null,
        int? rankType = null,
        string? correlationId = null,
        int? localRecordedPulls = null,
        int? exactPity = null)
    {
        if (projectionVersion <= 0 || gameAccountId == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(nameof(projectionVersion));
        }
        if (!Enum.IsDefined(domain) || !Enum.IsDefined(kind) ||
            !Enum.IsDefined(minimumGranularity) ||
            !Enum.IsDefined(baseOrigin) ||
            !Enum.IsDefined(annotationOrigin) ||
            !Enum.IsDefined(confidence) ||
            !Enum.IsDefined(completeness) ||
            !Enum.IsDefined(importance))
        {
            throw new ArgumentOutOfRangeException(nameof(domain));
        }
        if (timelines == ChronicleTimeline.None)
        {
            throw new ArgumentOutOfRangeException(nameof(timelines));
        }
        ArgumentNullException.ThrowIfNull(time);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceEntityType);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceEntityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(titleKey);
        ArgumentNullException.ThrowIfNull(summaryArguments);
        ArgumentNullException.ThrowIfNull(evidence);

        EntryId = entryId;
        PortableEntityKey = portableEntityKey;
        ProjectionVersion = projectionVersion;
        GameAccountId = gameAccountId;
        Domain = domain;
        Kind = kind;
        Timelines = timelines;
        MinimumGranularity = minimumGranularity;
        Milestone = milestone;
        Time = time;
        BaseOrigin = baseOrigin;
        AnnotationOrigin = annotationOrigin;
        Confidence = confidence;
        Completeness = completeness;
        SourceEntityType = sourceEntityType;
        SourceEntityId = sourceEntityId;
        TitleKey = titleKey;
        SummaryArguments = summaryArguments;
        Importance = importance;
        Evidence = evidence;
        ItemId = itemId;
        RankType = rankType;
        CorrelationId = correlationId;
        LocalRecordedPulls = localRecordedPulls;
        ExactPity = exactPity;
    }

    public ChronicleEntryId EntryId { get; }
    public string? PortableEntityKey { get; }
    public int ProjectionVersion { get; }
    public Guid GameAccountId { get; }
    public ChronicleDomain Domain { get; }
    public ChronicleEntryKind Kind { get; }
    public ChronicleTimeline Timelines { get; }
    public ChronicleGranularity MinimumGranularity { get; }
    public string? Milestone { get; }
    public ChronicleTime Time { get; }
    public DataOrigin BaseOrigin { get; }
    public DataOrigin AnnotationOrigin { get; }
    public DataConfidence Confidence { get; }
    public DataCompleteness Completeness { get; }
    public string SourceEntityType { get; }
    public string SourceEntityId { get; }
    public string TitleKey { get; }
    public IReadOnlyDictionary<string, string> SummaryArguments { get; }
    public ChronicleImportance Importance { get; }
    public ChronicleEvidence Evidence { get; }
    public string? ItemId { get; }
    public int? RankType { get; }
    public string? CorrelationId { get; }
    public int? LocalRecordedPulls { get; }
    public int? ExactPity { get; }
}
