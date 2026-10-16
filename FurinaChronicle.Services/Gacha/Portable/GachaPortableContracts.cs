// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

namespace FurinaChronicle.Services.Gacha.Portable;

public interface IGachaPortablePackageWriter
{
    Task<GachaPortableWriteResult> WriteAsync(
        Stream destination,
        GachaPortablePackage package,
        CancellationToken cancellationToken = default);

    Task<GachaPortableWriteResult> WriteAsync(
        Stream destination,
        IGachaPortableExportSnapshot snapshot,
        CancellationToken cancellationToken = default);
}

public interface IGachaPortablePackageReader
{
    Task<GachaPortableReadResult> ReadAsync(
        Stream source,
        CancellationToken cancellationToken = default);
}

public interface IGachaPortableInputStager
{
    Task<IGachaPortableStagedInput> StageAsync(
        Stream source,
        CancellationToken cancellationToken = default);
}

public interface IGachaPortableStagedInput : IAsyncDisposable
{
    long Length { get; }

    string Sha256 { get; }

    ValueTask<Stream> OpenReadAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>
/// A private, session-scoped disk index used by the product import path.
/// Implementations must not retain the complete record set in managed memory.
/// </summary>
public interface IGachaPortableIndexedPackage : IAsyncDisposable
{
    string FormatVersion { get; }

    DateTimeOffset GeneratedAt { get; }

    GachaPortableArchive SourceArchive { get; }

    IReadOnlyList<GachaPortableIndexedAccount> Accounts { get; }

    int RecordCount { get; }

    string PackageFingerprint { get; }

    IReadOnlyList<FurinaChronicle.Core.Gacha.GachaRecord> ReadRecordPage(
        Guid accountReference,
        int offset,
        int pageSize);

    void ResetPlanEntries();

    void StorePlanEntries(
        Guid accountReference,
        IReadOnlyList<GachaPortableIndexedPlanEntry> entries);

    IReadOnlyList<GachaPortableIndexedPlanEntry> ReadPlanPage(
        Guid accountReference,
        int offset,
        int pageSize);

    IReadOnlyList<GachaPortableRecordConflict> ReadConflictPage(
        Guid accountReference,
        int offset,
        int pageSize);
}

public interface IGachaPortableIndexedPackageFactory
{
    Task<IGachaPortableIndexedPackage> OpenAsync(
        IGachaPortableStagedInput stagedInput,
        CancellationToken cancellationToken = default);
}

public interface IGachaPortableExportSnapshot : IAsyncDisposable
{
    DateTimeOffset GeneratedAt { get; }

    GachaPortableArchive SourceArchive { get; }

    IReadOnlyList<GachaPortableExportAccount> Accounts { get; }

    IAsyncEnumerable<FurinaChronicle.Core.Gacha.GachaRecord> ReadRecordsAsync(
        Guid accountReference,
        CancellationToken cancellationToken = default);
}

public interface IGachaPortableExportSnapshotFactory
{
    Task<IGachaPortableExportSnapshot> OpenAsync(
        Guid playerArchiveId,
        IReadOnlyCollection<Guid> gameAccountIds,
        CancellationToken cancellationToken = default);
}

public interface IGachaPortableExportFileService
{
    Task<GachaPortableWriteResult> ExportAsync(
        string destinationPath,
        Guid playerArchiveId,
        IReadOnlyCollection<Guid> gameAccountIds,
        CancellationToken cancellationToken = default);
}

public sealed record GachaPortableLimits
{
    public const int DefaultMaximumManifestBytes = 1024 * 1024;
    public const int DefaultMaximumAccountCount = 128;
    public const int DefaultMaximumRecordCount = 2_000_000;
    public const long DefaultMaximumEntryBytes = 256L * 1024 * 1024;
    public const long DefaultMaximumUncompressedBytes = 512L * 1024 * 1024;
    public const long DefaultMaximumContainerBytes = 512L * 1024 * 1024;
    public const int DefaultMaximumNdjsonLineBytes = 64 * 1024;
    public const int DefaultMaximumJsonDepth = 32;

    public int MaximumManifestBytes { get; init; } =
        DefaultMaximumManifestBytes;

    public int MaximumAccountCount { get; init; } =
        DefaultMaximumAccountCount;

    public int MaximumRecordCount { get; init; } =
        DefaultMaximumRecordCount;

    public long MaximumEntryBytes { get; init; } =
        DefaultMaximumEntryBytes;

    public long MaximumUncompressedBytes { get; init; } =
        DefaultMaximumUncompressedBytes;

    public long MaximumContainerBytes { get; init; } =
        DefaultMaximumContainerBytes;

    public int MaximumNdjsonLineBytes { get; init; } =
        DefaultMaximumNdjsonLineBytes;

    public int MaximumJsonDepth { get; init; } =
        DefaultMaximumJsonDepth;
}

public enum GachaPortableErrorCode
{
    InvalidPackage = 1,
    UnsupportedVersion = 2,
    UnsupportedEnum = 3,
    UnsupportedAccount = 4,
    MissingRequiredField = 5,
    DuplicateReference = 6,
    InvalidReference = 7,
    InvalidPath = 8,
    UnexpectedEntry = 9,
    MissingEntry = 10,
    LengthMismatch = 11,
    HashMismatch = 12,
    RecordCountMismatch = 13,
    RecordMismatch = 14,
    ResourceLimitExceeded = 15,
    InputNotSeekable = 16,
}

public sealed class GachaPortableException : Exception
{
    public GachaPortableException(
        GachaPortableErrorCode code,
        string message,
        string? path = null,
        string? recordId = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
        Path = path;
        RecordId = recordId;
    }

    public GachaPortableErrorCode Code { get; }

    public string? Path { get; }

    public string? RecordId { get; }
}
