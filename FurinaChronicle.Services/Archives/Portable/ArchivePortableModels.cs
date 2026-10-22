// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;

namespace FurinaChronicle.Services.Archives.Portable;

public readonly record struct ArchiveFormatVersion(int Major, int Minor)
{
    public static ArchiveFormatVersion Current { get; } = new(1, 0);

    public static ArchiveFormatVersion Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        string[] parts = value.Split('.', StringSplitOptions.None);
        if (parts.Length != 2 ||
            !int.TryParse(parts[0], out int major) ||
            !int.TryParse(parts[1], out int minor) ||
            major < 0 || minor < 0)
        {
            throw new FormatException($"Invalid Archive version: {value}.");
        }

        return new ArchiveFormatVersion(major, minor);
    }

    public override string ToString() => $"{Major}.{Minor}";
}

public sealed record ArchiveGeneratorInfo(
    string App,
    string AppVersion,
    string Phase,
    string Commit,
    string SourcePlatform);

public enum ArchiveExportScopeKind
{
    AllArchives = 1,
    SelectedArchives = 2,
}

public sealed record ArchiveExportScope
{
    public ArchiveExportScope(
        ArchiveExportScopeKind kind,
        IEnumerable<Guid>? archiveIds = null)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        Guid[] normalized = archiveIds?.Distinct().ToArray() ?? [];
        if (normalized.Any(id => id == Guid.Empty) ||
            kind == ArchiveExportScopeKind.SelectedArchives &&
            normalized.Length == 0 ||
            kind == ArchiveExportScopeKind.AllArchives &&
            normalized.Length != 0)
        {
            throw new ArgumentException(
                "Archive scope IDs do not match the selected scope kind.",
                nameof(archiveIds));
        }

        Kind = kind;
        ArchiveIds = normalized;
    }

    public ArchiveExportScopeKind Kind { get; }

    public IReadOnlyList<Guid> ArchiveIds { get; }

    public static ArchiveExportScope All() =>
        new(ArchiveExportScopeKind.AllArchives);

    public static ArchiveExportScope Selected(IEnumerable<Guid> archiveIds) =>
        new(ArchiveExportScopeKind.SelectedArchives, archiveIds);
}

public sealed record ArchiveCatalogRoleIdentity(
    Guid RoleIdentityReference,
    Guid IdentityId,
    GameRoleNaturalIdentity NaturalIdentity);

public sealed record ArchiveCatalogAccount(
    Guid AccountReference,
    Guid? RoleIdentityReference,
    string? DisplayName,
    string LegacyUid,
    GameServerRegion LegacyServer,
    bool IsPlaceholder,
    string? UnresolvedReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ArchiveCatalogArchive(
    Guid ArchiveReference,
    string Name,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<ArchiveCatalogAccount> Accounts);

public sealed record ArchiveCatalog(
    IReadOnlyList<ArchiveCatalogRoleIdentity> RoleIdentities,
    IReadOnlyList<ArchiveCatalogArchive> Archives)
{
    public int AccountCount => Archives.Sum(archive => archive.Accounts.Count);
}

public sealed record ArchivePortableArchiveSetting(
    Guid ArchiveReference,
    int UndoLimit);

public sealed record ArchivePortableSettings(
    bool RequireUniqueArchiveNames,
    IReadOnlyList<ArchivePortableArchiveSetting> Archives);

public sealed record ArchiveDatasetDescriptor(
    string DatasetReference,
    string Kind,
    string Format,
    string FormatVersion,
    bool Required,
    IReadOnlyList<string> Paths,
    IReadOnlyDictionary<string, long> RecordCounts,
    long UncompressedLength);

public sealed record ArchiveManifest(
    string Format,
    string FormatVersion,
    DateTimeOffset GeneratedAt,
    ArchiveGeneratorInfo Generator,
    ArchiveExportScopeKind ScopeKind,
    IReadOnlyList<Guid> ArchiveReferences,
    string CompletenessAssertion,
    IReadOnlyList<ArchiveDatasetDescriptor> Datasets,
    string PortableSettingsPath,
    string ChecksumsPath);

public sealed record ArchivePackageSummary(
    ArchiveManifest Manifest,
    ArchiveCatalog Catalog,
    ArchivePortableSettings Settings,
    long GachaCurrentFactCount,
    long GachaRevisionCount,
    long DomainFactCount,
    long DomainRevisionCount,
    long UncompressedLength,
    IReadOnlyList<ArchiveDiagnostic> Diagnostics)
{
    public bool IsComplete => Diagnostics.All(item => !item.IsPartial);
}

public sealed record ArchiveWriteResult(
    ArchivePackageSummary Summary,
    string PackageSha256);

public sealed record ArchiveDiagnostic(
    ArchiveDiagnosticCode Code,
    string Message,
    string? Path = null,
    bool IsPartial = false);

public enum ArchiveDiagnosticCode
{
    UnsupportedOptionalDataset = 1,
}

public sealed record ArchivePortableLimits
{
    public const int DefaultMaximumEntryCount = 4096;
    public const int DefaultMaximumPathBytes = 512;
    public const int DefaultMaximumManifestBytes = 2 * 1024 * 1024;
    public const int DefaultMaximumChecksumsBytes = 4 * 1024 * 1024;
    public const long DefaultMaximumEntryBytes = 512L * 1024 * 1024;
    public const long DefaultMaximumUncompressedBytes = 2L * 1024 * 1024 * 1024;
    public const int DefaultMaximumArchiveCount = 1024;
    public const int DefaultMaximumAccountCount = 4096;
    public const int DefaultMaximumGachaFactCount = 2_000_000;
    public const int DefaultMaximumGachaRevisionCount = 2_000_000;
    public const int DefaultMaximumDomainRecordCount = 100_000;
    public const int DefaultMaximumJsonDepth = 32;
    public const int DefaultMaximumNdjsonLineBytes = 128 * 1024;

    public int MaximumEntryCount { get; init; } = DefaultMaximumEntryCount;
    public int MaximumPathBytes { get; init; } = DefaultMaximumPathBytes;
    public int MaximumManifestBytes { get; init; } = DefaultMaximumManifestBytes;
    public int MaximumChecksumsBytes { get; init; } = DefaultMaximumChecksumsBytes;
    public long MaximumEntryBytes { get; init; } = DefaultMaximumEntryBytes;
    public long MaximumUncompressedBytes { get; init; } = DefaultMaximumUncompressedBytes;
    public int MaximumArchiveCount { get; init; } = DefaultMaximumArchiveCount;
    public int MaximumAccountCount { get; init; } = DefaultMaximumAccountCount;
    public int MaximumGachaFactCount { get; init; } = DefaultMaximumGachaFactCount;
    public int MaximumGachaRevisionCount { get; init; } = DefaultMaximumGachaRevisionCount;
    public int MaximumDomainRecordCount { get; init; } = DefaultMaximumDomainRecordCount;
    public int MaximumJsonDepth { get; init; } = DefaultMaximumJsonDepth;
    public int MaximumNdjsonLineBytes { get; init; } = DefaultMaximumNdjsonLineBytes;
}

public enum ArchivePackageErrorCode
{
    InvalidPackage = 1,
    UnsupportedVersion = 2,
    UnknownRequiredDataset = 3,
    InvalidPath = 4,
    DuplicatePath = 5,
    MissingEntry = 6,
    UnexpectedEntry = 7,
    LengthMismatch = 8,
    HashMismatch = 9,
    InvalidReference = 10,
    RecordCountMismatch = 11,
    ResourceLimitExceeded = 12,
    InputNotSeekable = 13,
}

public sealed class ArchivePackageException : Exception
{
    public ArchivePackageException(
        ArchivePackageErrorCode code,
        string message,
        string? path = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
        Path = path;
    }

    public ArchivePackageErrorCode Code { get; }

    public string? Path { get; }
}
