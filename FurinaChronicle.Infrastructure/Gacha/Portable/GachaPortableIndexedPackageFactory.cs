// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Buffers;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Services.Gacha.Portable;
using SQLite;

namespace FurinaChronicle.Infrastructure.Gacha.Portable;

/// <summary>
/// Builds a private SQLite index for one import session. The source ZIP and
/// index are deleted when the session is disposed.
/// </summary>
public sealed class GachaPortableIndexedPackageFactory(
    string stagingDirectory,
    GachaPortableLimits? limits = null)
    : IGachaPortableIndexedPackageFactory
{
    private const int ReadBufferSize = 64 * 1024;
    private readonly GachaPortableLimits limits = limits ?? new();
    private readonly string stagingDirectory = Path.GetFullPath(
        string.IsNullOrWhiteSpace(stagingDirectory)
            ? throw new ArgumentException(
                "A staging directory is required.",
                nameof(stagingDirectory))
            : stagingDirectory);

    public async Task<IGachaPortableIndexedPackage> OpenAsync(
        IGachaPortableStagedInput stagedInput,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stagedInput);
        Directory.CreateDirectory(stagingDirectory);
        string indexPath = Path.Combine(
            stagingDirectory,
            $"portable-index-{Guid.NewGuid():N}.db3");
        SQLiteConnection? connection = null;
        try
        {
            connection = OpenConnection(indexPath);
            CreateSchema(connection);
            await using Stream source =
                await stagedInput.OpenReadAsync(cancellationToken);
            using var archive = new ZipArchive(
                source,
                ZipArchiveMode.Read,
                leaveOpen: true);
            var reader = new GachaPortablePackageReader(limits);
            Dictionary<string, ZipArchiveEntry> entries =
                reader.IndexEntries(archive);
            GachaPortableManifestDto manifest = await reader.ReadManifestAsync(
                entries,
                cancellationToken);
            reader.ValidateManifest(manifest, entries);

            var accounts = new List<GachaPortableIndexedAccount>(
                manifest.Accounts!.Count);
            foreach (GachaPortableAccountManifestDto account in
                     manifest.Accounts.Cast<GachaPortableAccountManifestDto>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                await IndexAccountAsync(
                    connection,
                    reader,
                    account,
                    entries,
                    cancellationToken);
                var naturalIdentity = new GameRoleNaturalIdentity(
                    account.NaturalIdentity!.GameBiz!,
                    account.NaturalIdentity.Server!,
                    account.NaturalIdentity.Uid!);
                accounts.Add(new GachaPortableIndexedAccount(
                    account.AccountReference,
                    new GameRoleIdentity(
                        new GameRoleIdentityId(account.RoleIdentityId),
                        naturalIdentity),
                    account.DisplayName,
                    account.RecordCount));
            }

            int recordCount = connection.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM Records;");
            if (recordCount != manifest.Scope!.RecordCount)
            {
                throw GachaPortableRules.Error(
                    GachaPortableErrorCode.RecordCountMismatch,
                    "The indexed record count does not match the manifest.",
                    GachaPortableRules.ManifestPath);
            }

            var package = new SqliteGachaPortableIndexedPackage(
                indexPath,
                connection,
                stagedInput,
                manifest.FormatVersion!,
                manifest.GeneratedAt,
                new GachaPortableArchive(
                    manifest.SourceArchive!.ArchiveReference,
                    manifest.SourceArchive.Name!),
                accounts,
                recordCount);
            connection = null;
            return package;
        }
        catch
        {
            connection?.Dispose();
            DeleteIndexFiles(indexPath);
            throw;
        }
    }

    private static SQLiteConnection OpenConnection(string path)
    {
        return new SQLiteConnection(
            path,
            SQLiteOpenFlags.ReadWrite |
            SQLiteOpenFlags.Create |
            SQLiteOpenFlags.FullMutex,
            storeDateTimeAsTicks: true);
    }

    private static void CreateSchema(SQLiteConnection connection)
    {
        connection.Execute(
            """
            CREATE TABLE Supplements (
                AccountReference TEXT NOT NULL,
                ExternalRecordId TEXT NOT NULL,
                OccurredAtUtcTicks INTEGER NOT NULL,
                OccurredAtOffsetMinutes INTEGER NOT NULL,
                Origin TEXT NULL,
                FetchedAt TEXT NULL,
                ImportedAt TEXT NULL,
                AcquisitionBatchId TEXT NULL,
                PRIMARY KEY (AccountReference, ExternalRecordId)
            );
            """);
        connection.Execute(
            """
            CREATE TABLE Records (
                AccountReference TEXT NOT NULL,
                RecordOrdinal INTEGER NOT NULL,
                ExternalRecordId TEXT NOT NULL,
                ItemName TEXT NULL,
                ItemId TEXT NULL,
                ItemType TEXT NULL,
                GachaType TEXT NULL,
                UigfGachaType TEXT NULL,
                RankType INTEGER NULL,
                Count INTEGER NOT NULL,
                TimeUtcTicks INTEGER NOT NULL,
                TimeOffsetMinutes INTEGER NOT NULL,
                Origin INTEGER NOT NULL,
                FetchedAtUtcTicks INTEGER NULL,
                FetchedAtOffsetMinutes INTEGER NULL,
                ImportedAtUtcTicks INTEGER NULL,
                ImportedAtOffsetMinutes INTEGER NULL,
                AcquisitionBatchId TEXT NULL,
                PlanKind INTEGER NULL,
                DifferentFields TEXT NULL,
                ExpectedTargetJson TEXT NULL,
                PRIMARY KEY (AccountReference, ExternalRecordId),
                UNIQUE (AccountReference, RecordOrdinal)
            );
            """);
        connection.Execute(
            "CREATE INDEX IX_Records_Account_Ordinal " +
            "ON Records (AccountReference, RecordOrdinal);");
        connection.Execute(
            "CREATE INDEX IX_Records_Account_Plan " +
            "ON Records (AccountReference, PlanKind, RecordOrdinal);");
    }

    private async Task IndexAccountAsync(
        SQLiteConnection connection,
        GachaPortablePackageReader reader,
        GachaPortableAccountManifestDto manifest,
        IReadOnlyDictionary<string, ZipArchiveEntry> entries,
        CancellationToken cancellationToken)
    {
        await IndexSupplementsAsync(
            connection,
            reader,
            entries[manifest.SupplementPath!],
            manifest,
            cancellationToken);
        await IndexUigfAsync(
            connection,
            entries[manifest.UigfPath!],
            manifest,
            cancellationToken);
        int supplements = connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM Supplements WHERE AccountReference = ?;",
            manifest.AccountReference.ToString("D"));
        int records = connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM Records WHERE AccountReference = ?;",
            manifest.AccountReference.ToString("D"));
        if (supplements != 0 || records != manifest.RecordCount)
        {
            throw GachaPortableRules.Error(
                GachaPortableErrorCode.RecordCountMismatch,
                "The account payload record count does not match the manifest.",
                manifest.UigfPath);
        }
    }

    private async Task IndexSupplementsAsync(
        SQLiteConnection connection,
        GachaPortablePackageReader reader,
        ZipArchiveEntry entry,
        GachaPortableAccountManifestDto manifest,
        CancellationToken cancellationToken)
    {
        GachaPortablePackageReader.ValidateDeclaredPayload(
            entry,
            manifest.SupplementLength,
            manifest.SupplementPath!);
        await using Stream entryStream = entry.Open();
        await using var hashing = new HashingReadStream(entryStream);
        var line = new ArrayBufferWriter<byte>();
        byte[] buffer = ArrayPool<byte>.Shared.Rent(ReadBufferSize);
        connection.BeginTransaction();
        try
        {
            while (true)
            {
                int read = await hashing.ReadAsync(
                    buffer.AsMemory(0, ReadBufferSize),
                    cancellationToken);
                if (read == 0)
                {
                    break;
                }

                int start = 0;
                for (int index = 0; index < read; index++)
                {
                    if (buffer[index] != (byte)'\n')
                    {
                        continue;
                    }

                    AppendLine(line, buffer.AsSpan(start, index - start));
                    InsertSupplement(connection, reader, line, manifest);
                    line.Clear();
                    start = index + 1;
                }
                AppendLine(line, buffer.AsSpan(start, read - start));
            }
            if (line.WrittenCount > 0)
            {
                InsertSupplement(connection, reader, line, manifest);
            }
            GachaPortablePackageReader.ValidateHash(
                hashing,
                manifest.SupplementLength,
                manifest.SupplementSha256!,
                manifest.SupplementPath!);
            connection.Commit();
        }
        catch
        {
            connection.Rollback();
            throw;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private void AppendLine(
        ArrayBufferWriter<byte> line,
        ReadOnlySpan<byte> segment)
    {
        if (line.WrittenCount + segment.Length > limits.MaximumNdjsonLineBytes)
        {
            throw GachaPortableRules.Error(
                GachaPortableErrorCode.ResourceLimitExceeded,
                "A supplement line exceeds the Portable v1 limit.");
        }
        line.Write(segment);
    }

    private static void InsertSupplement(
        SQLiteConnection connection,
        GachaPortablePackageReader reader,
        ArrayBufferWriter<byte> line,
        GachaPortableAccountManifestDto manifest)
    {
        ReadOnlySpan<byte> json = line.WrittenSpan;
        if (json.Length > 0 && json[^1] == (byte)'\r')
        {
            json = json[..^1];
        }
        if (json.Length == 0)
        {
            throw GachaPortableRules.Error(
                GachaPortableErrorCode.InvalidPackage,
                "The supplement cannot contain empty lines.",
                manifest.SupplementPath);
        }

        GachaPortableSupplementDto supplement;
        try
        {
            supplement = JsonSerializer.Deserialize<GachaPortableSupplementDto>(
                    json,
                    reader.CreateJsonOptions())
                ?? throw new JsonException("A supplement line is null.");
        }
        catch (JsonException exception)
        {
            throw GachaPortableRules.Error(
                GachaPortableErrorCode.InvalidPackage,
                "A supplement line is invalid JSON.",
                manifest.SupplementPath,
                innerException: exception);
        }

        if (supplement.AccountReference != manifest.AccountReference ||
            string.IsNullOrWhiteSpace(supplement.ExternalRecordId))
        {
            throw GachaPortableRules.Error(
                GachaPortableErrorCode.InvalidReference,
                "A supplement reference is invalid.",
                manifest.SupplementPath,
                supplement.ExternalRecordId);
        }
        try
        {
            connection.Execute(
                """
                INSERT INTO Supplements
                    (AccountReference, ExternalRecordId,
                     OccurredAtUtcTicks, OccurredAtOffsetMinutes,
                     Origin, FetchedAt, ImportedAt, AcquisitionBatchId)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?);
                """,
                manifest.AccountReference.ToString("D"),
                supplement.ExternalRecordId,
                supplement.OccurredAtUtcTicks,
                supplement.OccurredAtOffsetMinutes,
                supplement.Origin,
                supplement.FetchedAt?.ToString("O", CultureInfo.InvariantCulture),
                supplement.ImportedAt?.ToString("O", CultureInfo.InvariantCulture),
                supplement.AcquisitionBatchId?.ToString("D"));
        }
        catch (SQLiteException exception) when (
            exception.Result == SQLite3.Result.Constraint)
        {
            throw GachaPortableRules.Error(
                GachaPortableErrorCode.DuplicateReference,
                "Duplicate supplement record reference.",
                manifest.SupplementPath,
                supplement.ExternalRecordId,
                exception);
        }
    }

    private async Task IndexUigfAsync(
        SQLiteConnection connection,
        ZipArchiveEntry entry,
        GachaPortableAccountManifestDto manifest,
        CancellationToken cancellationToken)
    {
        GachaPortablePackageReader.ValidateDeclaredPayload(
            entry,
            manifest.UigfLength,
            manifest.UigfPath!);
        await using Stream entryStream = entry.Open();
        await using var hashing = new HashingReadStream(entryStream);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(ReadBufferSize * 2);
        var parser = new UigfStreamParser(connection, manifest);
        JsonReaderState state = new(new JsonReaderOptions
        {
            MaxDepth = limits.MaximumJsonDepth,
            CommentHandling = JsonCommentHandling.Disallow,
        });
        int buffered = 0;
        bool finalBlock = false;
        connection.BeginTransaction();
        try
        {
            while (!finalBlock)
            {
                int read = await hashing.ReadAsync(
                    buffer.AsMemory(buffered, buffer.Length - buffered),
                    cancellationToken);
                finalBlock = read == 0;
                buffered += read;
                var jsonReader = new Utf8JsonReader(
                    buffer.AsSpan(0, buffered),
                    finalBlock,
                    state);
                while (jsonReader.Read())
                {
                    parser.Process(ref jsonReader);
                }

                int consumed = checked((int)jsonReader.BytesConsumed);
                int remaining = buffered - consumed;
                if (remaining > 0)
                {
                    buffer.AsSpan(consumed, remaining).CopyTo(buffer);
                }
                buffered = remaining;
                state = jsonReader.CurrentState;
                if (!finalBlock && buffered == buffer.Length)
                {
                    throw GachaPortableRules.Error(
                        GachaPortableErrorCode.ResourceLimitExceeded,
                        "A UIGF JSON token exceeds the Portable v1 limit.",
                        manifest.UigfPath);
                }
            }
            if (buffered != 0)
            {
                throw new JsonException("The UIGF payload is incomplete.");
            }
            parser.Complete();
            GachaPortablePackageReader.ValidateHash(
                hashing,
                manifest.UigfLength,
                manifest.UigfSha256!,
                manifest.UigfPath!);
            connection.Commit();
        }
        catch (GachaPortableException)
        {
            connection.Rollback();
            throw;
        }
        catch (JsonException exception)
        {
            connection.Rollback();
            throw GachaPortableRules.Error(
                GachaPortableErrorCode.InvalidPackage,
                "The UIGF payload is invalid JSON.",
                manifest.UigfPath,
                innerException: exception);
        }
        catch
        {
            connection.Rollback();
            throw;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    internal static void DeleteIndexFiles(string path)
    {
        foreach (string candidate in new[] { path, path + "-wal", path + "-shm" })
        {
            try
            {
                if (File.Exists(candidate))
                {
                    File.Delete(candidate);
                }
            }
            catch (IOException)
            {
                // Best effort on shutdown; a later cache cleanup may retry.
            }
            catch (UnauthorizedAccessException)
            {
                // Best effort on shutdown; a later cache cleanup may retry.
            }
        }
    }

    private sealed class UigfStreamParser(
        SQLiteConnection connection,
        GachaPortableAccountManifestDto manifest)
    {
        private string? property;
        private PortableUigfRecordDtoBuilder? currentRecord;
        private bool rootSeen;
        private bool infoSeen;
        private bool hk4eSeen;
        private bool inInfo;
        private bool inHk4e;
        private bool inAccount;
        private bool inList;
        private bool listSeen;
        private int accountCount;
        private int recordOrdinal;
        private long exportTimestamp;
        private string? exportApp;
        private string? exportAppVersion;
        private string? version;
        private string? uid;
        private int timezone;

        public void Process(ref Utf8JsonReader json)
        {
            switch (json.TokenType)
            {
                case JsonTokenType.PropertyName:
                    property = json.GetString();
                    return;
                case JsonTokenType.StartObject:
                    if (json.CurrentDepth == 0)
                    {
                        rootSeen = true;
                    }
                    else if (json.CurrentDepth == 1 && property == "info")
                    {
                        inInfo = true;
                        infoSeen = true;
                    }
                    else if (json.CurrentDepth == 2 && inHk4e)
                    {
                        accountCount++;
                        inAccount = true;
                    }
                    else if (json.CurrentDepth == 4 && inList)
                    {
                        currentRecord = new PortableUigfRecordDtoBuilder();
                    }
                    return;
                case JsonTokenType.EndObject:
                    if (json.CurrentDepth == 4 && currentRecord is not null)
                    {
                        InsertRecord(currentRecord.ToDto());
                        currentRecord = null;
                    }
                    else if (json.CurrentDepth == 2 && inAccount)
                    {
                        inAccount = false;
                    }
                    else if (json.CurrentDepth == 1 && inInfo)
                    {
                        inInfo = false;
                    }
                    return;
                case JsonTokenType.StartArray:
                    if (json.CurrentDepth == 1 && property == "hk4e")
                    {
                        inHk4e = true;
                        hk4eSeen = true;
                    }
                    else if (json.CurrentDepth == 3 &&
                             inAccount && property == "list")
                    {
                        inList = true;
                        listSeen = true;
                    }
                    return;
                case JsonTokenType.EndArray:
                    if (json.CurrentDepth == 3 && inList)
                    {
                        inList = false;
                    }
                    else if (json.CurrentDepth == 1 && inHk4e)
                    {
                        inHk4e = false;
                    }
                    return;
            }

            if (inInfo && json.CurrentDepth == 2)
            {
                ReadInfoScalar(ref json);
            }
            else if (inAccount && !inList && json.CurrentDepth == 3)
            {
                ReadAccountScalar(ref json);
            }
            else if (currentRecord is not null && json.CurrentDepth == 5)
            {
                currentRecord.Set(property, ReadNullableString(ref json));
            }
        }

        public void Complete()
        {
            if (!rootSeen || !infoSeen || !hk4eSeen ||
                exportTimestamp < 0 ||
                string.IsNullOrWhiteSpace(exportApp) ||
                string.IsNullOrWhiteSpace(exportAppVersion) ||
                !string.Equals(
                    version,
                    GachaPortableRules.UigfVersion,
                    StringComparison.Ordinal) ||
                accountCount != 1 ||
                !listSeen ||
                !string.Equals(
                    uid,
                    manifest.NaturalIdentity!.Uid,
                    StringComparison.Ordinal) ||
                timezone != 0 ||
                recordOrdinal != manifest.RecordCount)
            {
                throw GachaPortableRules.Error(
                    GachaPortableErrorCode.RecordMismatch,
                    "The UIGF account does not match the manifest.",
                    manifest.UigfPath);
            }
        }

        private void ReadInfoScalar(ref Utf8JsonReader json)
        {
            switch (property)
            {
                case "export_timestamp":
                    if (!json.TryGetInt64(out exportTimestamp))
                    {
                        throw new JsonException("export_timestamp is invalid.");
                    }
                    break;
                case "export_app":
                    exportApp = ReadNullableString(ref json);
                    break;
                case "export_app_version":
                    exportAppVersion = ReadNullableString(ref json);
                    break;
                case "version":
                    version = ReadNullableString(ref json);
                    break;
            }
        }

        private void ReadAccountScalar(ref Utf8JsonReader json)
        {
            switch (property)
            {
                case "uid":
                    uid = ReadNullableString(ref json);
                    break;
                case "timezone":
                    if (!json.TryGetInt32(out timezone))
                    {
                        throw new JsonException("timezone is invalid.");
                    }
                    break;
            }
        }

        private static string? ReadNullableString(ref Utf8JsonReader json) =>
            json.TokenType switch
            {
                JsonTokenType.String => json.GetString(),
                JsonTokenType.Null => null,
                _ => throw new JsonException("A JSON string was expected."),
            };

        private void InsertRecord(PortableUigfRecordDto dto)
        {
            PortableStandardRecord standard =
                GachaPortablePackageReader.ParseStandardRecord(
                    dto,
                    manifest.UigfPath!);
            SupplementIndexRow? supplement = connection
                .Query<SupplementIndexRow>(
                    """
                    SELECT * FROM Supplements
                    WHERE AccountReference = ? AND ExternalRecordId = ?;
                    """,
                    manifest.AccountReference.ToString("D"),
                    standard.ExternalRecordId)
                .SingleOrDefault();
            if (supplement is null)
            {
                throw GachaPortableRules.Error(
                    GachaPortableErrorCode.RecordMismatch,
                    "A standard record has no matching supplement.",
                    manifest.SupplementPath,
                    standard.ExternalRecordId);
            }
            GachaRecord record = GachaPortablePackageReader.ToDomainRecord(
                manifest.AccountReference,
                standard,
                supplement.ToDto(),
                manifest.SupplementPath!);
            try
            {
                connection.Execute(
                    """
                    INSERT INTO Records
                        (AccountReference, RecordOrdinal, ExternalRecordId,
                         ItemName, ItemId, ItemType, GachaType,
                         UigfGachaType, RankType, Count,
                         TimeUtcTicks, TimeOffsetMinutes, Origin,
                         FetchedAtUtcTicks, FetchedAtOffsetMinutes,
                         ImportedAtUtcTicks, ImportedAtOffsetMinutes,
                         AcquisitionBatchId)
                    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?);
                    """,
                    manifest.AccountReference.ToString("D"),
                    recordOrdinal++,
                    record.ExternalRecordId,
                    record.ItemName,
                    record.ItemId,
                    record.ItemType,
                    record.GachaType,
                    record.UigfGachaType,
                    record.RankType,
                    record.Count,
                    record.Time.UtcDateTime.Ticks,
                    checked((int)record.Time.Offset.TotalMinutes),
                    (int)record.Provenance.Origin,
                    record.Provenance.Timestamps.FetchedAt?.UtcDateTime.Ticks,
                    record.Provenance.Timestamps.FetchedAt is null
                        ? null
                        : checked((int)record.Provenance.Timestamps.FetchedAt.Value.Offset.TotalMinutes),
                    record.Provenance.Timestamps.ImportedAt?.UtcDateTime.Ticks,
                    record.Provenance.Timestamps.ImportedAt is null
                        ? null
                        : checked((int)record.Provenance.Timestamps.ImportedAt.Value.Offset.TotalMinutes),
                    record.Provenance.AcquisitionBatchId?.ToString());
            }
            catch (SQLiteException exception) when (
                exception.Result == SQLite3.Result.Constraint)
            {
                throw GachaPortableRules.Error(
                    GachaPortableErrorCode.DuplicateReference,
                    "Duplicate UIGF record ID.",
                    manifest.UigfPath,
                    standard.ExternalRecordId,
                    exception);
            }
            connection.Execute(
                "DELETE FROM Supplements " +
                "WHERE AccountReference = ? AND ExternalRecordId = ?;",
                manifest.AccountReference.ToString("D"),
                standard.ExternalRecordId);
        }
    }

    private sealed class PortableUigfRecordDtoBuilder
    {
        private readonly Dictionary<string, string?> values =
            new(StringComparer.Ordinal);

        public void Set(string? name, string? value)
        {
            if (name is not null)
            {
                values[name] = value;
            }
        }

        public PortableUigfRecordDto ToDto() => new()
        {
            UigfGachaType = Get("uigf_gacha_type"),
            GachaType = Get("gacha_type"),
            ItemId = Get("item_id"),
            Count = Get("count"),
            Time = Get("time"),
            Name = Get("name"),
            ItemType = Get("item_type"),
            RankType = Get("rank_type"),
            Id = Get("id"),
        };

        private string? Get(string name) =>
            values.TryGetValue(name, out string? value) ? value : null;
    }

    private sealed class SupplementIndexRow
    {
        public string AccountReference { get; set; } = string.Empty;
        public string ExternalRecordId { get; set; } = string.Empty;
        public long OccurredAtUtcTicks { get; set; }
        public int OccurredAtOffsetMinutes { get; set; }
        public string? Origin { get; set; }
        public string? FetchedAt { get; set; }
        public string? ImportedAt { get; set; }
        public string? AcquisitionBatchId { get; set; }

        public GachaPortableSupplementDto ToDto() => new()
        {
            AccountReference = Guid.Parse(AccountReference),
            ExternalRecordId = ExternalRecordId,
            OccurredAtUtcTicks = OccurredAtUtcTicks,
            OccurredAtOffsetMinutes = OccurredAtOffsetMinutes,
            Origin = Origin,
            FetchedAt = FetchedAt is null
                ? null
                : DateTimeOffset.Parse(FetchedAt, CultureInfo.InvariantCulture),
            ImportedAt = ImportedAt is null
                ? null
                : DateTimeOffset.Parse(ImportedAt, CultureInfo.InvariantCulture),
            AcquisitionBatchId = AcquisitionBatchId is null
                ? null
                : Guid.Parse(AcquisitionBatchId),
        };
    }
}

internal sealed class SqliteGachaPortableIndexedPackage :
    IGachaPortableIndexedPackage
{
    private const string FieldSeparator = "\u001f";
    private readonly string indexPath;
    private readonly SQLiteConnection connection;
    private readonly IGachaPortableStagedInput stagedInput;
    private bool disposed;

    public SqliteGachaPortableIndexedPackage(
        string indexPath,
        SQLiteConnection connection,
        IGachaPortableStagedInput stagedInput,
        string formatVersion,
        DateTimeOffset generatedAt,
        GachaPortableArchive sourceArchive,
        IReadOnlyList<GachaPortableIndexedAccount> accounts,
        int recordCount)
    {
        this.indexPath = indexPath;
        this.connection = connection;
        this.stagedInput = stagedInput;
        FormatVersion = formatVersion;
        GeneratedAt = generatedAt;
        SourceArchive = sourceArchive;
        Accounts = accounts;
        RecordCount = recordCount;
    }

    public string FormatVersion { get; }
    public DateTimeOffset GeneratedAt { get; }
    public GachaPortableArchive SourceArchive { get; }
    public IReadOnlyList<GachaPortableIndexedAccount> Accounts { get; }
    public int RecordCount { get; }
    public string PackageFingerprint => ComputeFingerprint();

    public IReadOnlyList<GachaRecord> ReadRecordPage(
        Guid accountReference,
        int offset,
        int pageSize)
    {
        ValidatePage(offset, pageSize);
        ThrowIfDisposed();
        return connection.Query<IndexedRecordRow>(
                """
                SELECT * FROM Records
                WHERE AccountReference = ?
                ORDER BY RecordOrdinal
                LIMIT ? OFFSET ?;
                """,
                accountReference.ToString("D"),
                pageSize,
                offset)
            .Select(row => row.ToDomain(accountReference))
            .ToArray();
    }

    public void ResetPlanEntries()
    {
        ThrowIfDisposed();
        connection.Execute(
            "UPDATE Records SET PlanKind = NULL, DifferentFields = NULL, " +
            "ExpectedTargetJson = NULL;");
    }

    public void StorePlanEntries(
        Guid accountReference,
        IReadOnlyList<GachaPortableIndexedPlanEntry> entries)
    {
        ThrowIfDisposed();
        connection.BeginTransaction();
        try
        {
            foreach (GachaPortableIndexedPlanEntry entry in entries)
            {
                int changed = connection.Execute(
                    """
                    UPDATE Records
                    SET PlanKind = ?, DifferentFields = ?, ExpectedTargetJson = ?
                    WHERE AccountReference = ? AND ExternalRecordId = ?;
                    """,
                    (int)entry.Kind,
                    entry.DifferentFields.Count == 0
                        ? null
                        : string.Join(FieldSeparator, entry.DifferentFields),
                    entry.ExpectedTarget is null
                        ? null
                        : JsonSerializer.Serialize(
                            IndexedRecordSnapshot.FromDomain(
                                entry.ExpectedTarget)),
                    accountReference.ToString("D"),
                    entry.Source.ExternalRecordId);
                if (changed != 1)
                {
                    throw new InvalidDataException(
                        "The Portable plan refers to a missing indexed record.");
                }
            }
            connection.Commit();
        }
        catch
        {
            connection.Rollback();
            throw;
        }
    }

    public IReadOnlyList<GachaPortableIndexedPlanEntry> ReadPlanPage(
        Guid accountReference,
        int offset,
        int pageSize)
    {
        ValidatePage(offset, pageSize);
        ThrowIfDisposed();
        return connection.Query<IndexedRecordRow>(
                """
                SELECT * FROM Records
                WHERE AccountReference = ?
                ORDER BY RecordOrdinal
                LIMIT ? OFFSET ?;
                """,
                accountReference.ToString("D"),
                pageSize,
                offset)
            .Select(row => row.ToPlanEntry(accountReference))
            .ToArray();
    }

    public IReadOnlyList<GachaPortableRecordConflict> ReadConflictPage(
        Guid accountReference,
        int offset,
        int pageSize)
    {
        ValidatePage(offset, pageSize);
        ThrowIfDisposed();
        return connection.Query<IndexedRecordRow>(
                """
                SELECT * FROM Records
                WHERE AccountReference = ? AND PlanKind = ?
                ORDER BY RecordOrdinal
                LIMIT ? OFFSET ?;
                """,
                accountReference.ToString("D"),
                (int)GachaPortableIndexedPlanKind.Conflict,
                pageSize,
                offset)
            .Select(row => new GachaPortableRecordConflict(
                row.ExternalRecordId,
                row.ReadDifferentFields()))
            .ToArray();
    }

    private string ComputeFingerprint()
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(
            HashAlgorithmName.SHA256);
        Append(hash, GeneratedAt.ToString("O", CultureInfo.InvariantCulture));
        Append(hash, SourceArchive.ArchiveReference.ToString("D"));
        Append(hash, SourceArchive.Name);
        foreach (GachaPortableIndexedAccount account in Accounts)
        {
            Append(hash, account.AccountReference.ToString("D"));
            Append(hash, account.RoleIdentity.Id.ToString());
            Append(hash, account.RoleIdentity.NaturalIdentity.GameBiz);
            Append(hash, account.RoleIdentity.NaturalIdentity.Server);
            Append(hash, account.RoleIdentity.NaturalIdentity.Uid);
            Append(hash, account.DisplayName);
            for (int offset = 0; offset < account.RecordCount; offset += 500)
            {
                foreach (GachaRecord record in ReadRecordPage(
                             account.AccountReference,
                             offset,
                             500))
                {
                    AppendRecord(hash, record);
                }
            }
        }
        return Convert.ToHexString(hash.GetHashAndReset())
            .ToLowerInvariant();
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        connection.Close();
        GachaPortableIndexedPackageFactory.DeleteIndexFiles(indexPath);
        await stagedInput.DisposeAsync();
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }

    private static void ValidatePage(int offset, int pageSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        if (pageSize is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize));
        }
    }

    private static void AppendRecord(IncrementalHash hash, GachaRecord record)
    {
        Append(hash, record.ExternalRecordId);
        Append(hash, record.ItemName);
        Append(hash, record.ItemId);
        Append(hash, record.ItemType);
        Append(hash, record.GachaType);
        Append(hash, record.UigfGachaType);
        Append(hash, record.RankType?.ToString(CultureInfo.InvariantCulture));
        Append(hash, record.Count.ToString(CultureInfo.InvariantCulture));
        Append(hash, record.Time.ToString("O", CultureInfo.InvariantCulture));
        Append(hash, ((int)record.Provenance.Origin)
            .ToString(CultureInfo.InvariantCulture));
        Append(hash, record.Provenance.Timestamps.ObservedAt?
            .ToString("O", CultureInfo.InvariantCulture));
        Append(hash, record.Provenance.Timestamps.FetchedAt?
            .ToString("O", CultureInfo.InvariantCulture));
        Append(hash, record.Provenance.Timestamps.ImportedAt?
            .ToString("O", CultureInfo.InvariantCulture));
        Append(hash, record.Provenance.Source?.Provider);
        Append(hash, record.Provenance.Source?.SourceRecordId);
        Append(hash, record.Provenance.Source?.SourceSnapshotId);
        Append(hash, record.Provenance.AcquisitionBatchId?.ToString());
    }

    private static void Append(IncrementalHash target, string? value)
    {
        if (value is null)
        {
            target.AppendData("-1:"u8);
            return;
        }
        target.AppendData(Encoding.UTF8.GetBytes(
            value.Length.ToString(CultureInfo.InvariantCulture)));
        target.AppendData(":"u8);
        target.AppendData(Encoding.UTF8.GetBytes(value));
    }

    private sealed class IndexedRecordRow
    {
        public string AccountReference { get; set; } = string.Empty;
        public int RecordOrdinal { get; set; }
        public string ExternalRecordId { get; set; } = string.Empty;
        public string? ItemName { get; set; }
        public string? ItemId { get; set; }
        public string? ItemType { get; set; }
        public string? GachaType { get; set; }
        public string? UigfGachaType { get; set; }
        public int? RankType { get; set; }
        public int Count { get; set; }
        public long TimeUtcTicks { get; set; }
        public int TimeOffsetMinutes { get; set; }
        public int Origin { get; set; }
        public long? FetchedAtUtcTicks { get; set; }
        public int? FetchedAtOffsetMinutes { get; set; }
        public long? ImportedAtUtcTicks { get; set; }
        public int? ImportedAtOffsetMinutes { get; set; }
        public string? AcquisitionBatchId { get; set; }
        public int? PlanKind { get; set; }
        public string? DifferentFields { get; set; }
        public string? ExpectedTargetJson { get; set; }

        public GachaRecord ToDomain(Guid accountReference) =>
            IndexedRecordSnapshot.FromRow(this).ToDomain(accountReference);

        public GachaPortableIndexedPlanEntry ToPlanEntry(Guid accountReference)
        {
            if (PlanKind is null)
            {
                throw new InvalidDataException(
                    "The indexed Portable record has not been planned.");
            }
            GachaRecord? expected = ExpectedTargetJson is null
                ? null
                : (JsonSerializer.Deserialize<IndexedRecordSnapshot>(
                    ExpectedTargetJson) ??
                   throw new InvalidDataException(
                       "The indexed expected target snapshot is invalid."))
                .ToDomain(accountReference);
            return new GachaPortableIndexedPlanEntry(
                ToDomain(accountReference),
                (GachaPortableIndexedPlanKind)PlanKind.Value,
                ReadDifferentFields(),
                expected);
        }

        public IReadOnlyList<string> ReadDifferentFields() =>
            string.IsNullOrEmpty(DifferentFields)
                ? []
                : DifferentFields.Split(
                    FieldSeparator,
                    StringSplitOptions.None);
    }

    private sealed record IndexedRecordSnapshot(
        string ExternalRecordId,
        string? ItemName,
        string? ItemId,
        string? ItemType,
        string? GachaType,
        string? UigfGachaType,
        int? RankType,
        int Count,
        long TimeUtcTicks,
        int TimeOffsetMinutes,
        int Origin,
        long? FetchedAtUtcTicks,
        int? FetchedAtOffsetMinutes,
        long? ImportedAtUtcTicks,
        int? ImportedAtOffsetMinutes,
        string? AcquisitionBatchId)
    {
        public static IndexedRecordSnapshot FromDomain(GachaRecord record) =>
            new(
                record.ExternalRecordId,
                record.ItemName,
                record.ItemId,
                record.ItemType,
                record.GachaType,
                record.UigfGachaType,
                record.RankType,
                record.Count,
                record.Time.UtcDateTime.Ticks,
                checked((int)record.Time.Offset.TotalMinutes),
                (int)record.Provenance.Origin,
                record.Provenance.Timestamps.FetchedAt?.UtcDateTime.Ticks,
                record.Provenance.Timestamps.FetchedAt is null
                    ? null
                    : checked((int)record.Provenance.Timestamps.FetchedAt.Value.Offset.TotalMinutes),
                record.Provenance.Timestamps.ImportedAt?.UtcDateTime.Ticks,
                record.Provenance.Timestamps.ImportedAt is null
                    ? null
                    : checked((int)record.Provenance.Timestamps.ImportedAt.Value.Offset.TotalMinutes),
                record.Provenance.AcquisitionBatchId?.ToString());

        public static IndexedRecordSnapshot FromRow(IndexedRecordRow row) =>
            new(
                row.ExternalRecordId,
                row.ItemName,
                row.ItemId,
                row.ItemType,
                row.GachaType,
                row.UigfGachaType,
                row.RankType,
                row.Count,
                row.TimeUtcTicks,
                row.TimeOffsetMinutes,
                row.Origin,
                row.FetchedAtUtcTicks,
                row.FetchedAtOffsetMinutes,
                row.ImportedAtUtcTicks,
                row.ImportedAtOffsetMinutes,
                row.AcquisitionBatchId);

        public GachaRecord ToDomain(Guid accountReference)
        {
            DateTimeOffset time = new DateTimeOffset(
                    TimeUtcTicks,
                    TimeSpan.Zero)
                .ToOffset(TimeSpan.FromMinutes(TimeOffsetMinutes));
            DateTimeOffset? fetched = ToTimestamp(
                FetchedAtUtcTicks,
                FetchedAtOffsetMinutes);
            DateTimeOffset? imported = ToTimestamp(
                ImportedAtUtcTicks,
                ImportedAtOffsetMinutes);
            return new GachaRecord(
                accountReference,
                ExternalRecordId,
                ItemName,
                RankType,
                time)
            {
                ItemId = ItemId,
                ItemType = ItemType,
                GachaType = GachaType,
                UigfGachaType = UigfGachaType,
                Count = Count,
                Provenance = new RecordProvenance(
                    (DataOrigin)Origin,
                    new RecordTimestamps(
                        FetchedAt: fetched,
                        ImportedAt: imported),
                    acquisitionBatchId: AcquisitionBatchId is null
                        ? null
                        : new AcquisitionBatchId(Guid.Parse(
                            AcquisitionBatchId)))
            };
        }

        private static DateTimeOffset? ToTimestamp(
            long? ticks,
            int? offsetMinutes) =>
            ticks is null
                ? null
                : new DateTimeOffset(ticks.Value, TimeSpan.Zero).ToOffset(
                    TimeSpan.FromMinutes(offsetMinutes ?? 0));
    }
}
