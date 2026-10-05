// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Buffers;
using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Services.Gacha.Portable;

namespace FurinaChronicle.Infrastructure.Gacha.Portable;

public sealed class GachaPortablePackageReader(
    GachaPortableLimits? limits = null)
    : IGachaPortablePackageReader
{
    private readonly GachaPortableLimits limits = limits ?? new();

    public async Task<GachaPortableReadResult> ReadAsync(
        Stream source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead)
        {
            throw new ArgumentException(
                "The source stream is not readable.",
                nameof(source));
        }

        if (!source.CanSeek)
        {
            throw GachaPortableRules.Error(
                GachaPortableErrorCode.InputNotSeekable,
                "Portable v1 requires a seekable input stream.");
        }

        try
        {
            using var archive = new ZipArchive(
                source,
                ZipArchiveMode.Read,
                leaveOpen: true);
            Dictionary<string, ZipArchiveEntry> entries = IndexEntries(archive);
            GachaPortableManifestDto manifest = await ReadManifestAsync(
                entries,
                cancellationToken);
            ValidateManifest(manifest, entries);

            var accounts = new List<GachaPortableAccount>(
                manifest.Accounts!.Count);
            foreach (GachaPortableAccountManifestDto? accountDto in
                     manifest.Accounts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                accounts.Add(await ReadAccountAsync(
                    accountDto!,
                    entries,
                    cancellationToken));
            }

            var package = new GachaPortablePackage(
                manifest.GeneratedAt,
                new GachaPortableArchive(
                    manifest.SourceArchive!.ArchiveReference,
                    manifest.SourceArchive.Name!),
                accounts);
            if (package.RecordCount != manifest.Scope!.RecordCount)
            {
                throw GachaPortableRules.Error(
                    GachaPortableErrorCode.RecordCountMismatch,
                    "The package record count does not match the manifest.",
                    GachaPortableRules.ManifestPath);
            }

            return new GachaPortableReadResult(
                manifest.FormatVersion!,
                package);
        }
        catch (GachaPortableException)
        {
            throw;
        }
        catch (InvalidDataException exception)
        {
            throw GachaPortableRules.Error(
                GachaPortableErrorCode.InvalidPackage,
                "The file is not a valid Gacha Portable ZIP package.",
                innerException: exception);
        }
    }

    private Dictionary<string, ZipArchiveEntry> IndexEntries(
        ZipArchive archive)
    {
        int maximumEntries = 1 + (2 * limits.MaximumAccountCount);
        if (archive.Entries.Count > maximumEntries)
        {
            throw GachaPortableRules.Error(
                GachaPortableErrorCode.ResourceLimitExceeded,
                "The ZIP entry count exceeds the Portable v1 limit.");
        }

        var entries = new Dictionary<string, ZipArchiveEntry>(
            StringComparer.Ordinal);
        long totalLength = 0;
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string path = entry.FullName;
            GachaPortableRules.ValidateEntryPath(path);
            if (entry.Name.Length == 0 || !entries.TryAdd(path, entry))
            {
                throw GachaPortableRules.Error(
                    GachaPortableErrorCode.DuplicateReference,
                    $"Duplicate or directory ZIP entry: {path}.",
                    path);
            }

            if (entry.Length > limits.MaximumEntryBytes &&
                !string.Equals(
                    path,
                    GachaPortableRules.ManifestPath,
                    StringComparison.Ordinal))
            {
                throw GachaPortableRules.Error(
                    GachaPortableErrorCode.ResourceLimitExceeded,
                    $"ZIP entry exceeds the Portable v1 limit: {path}.",
                    path);
            }

            totalLength = checked(totalLength + entry.Length);
            if (totalLength > limits.MaximumUncompressedBytes)
            {
                throw GachaPortableRules.Error(
                    GachaPortableErrorCode.ResourceLimitExceeded,
                    "The ZIP uncompressed size exceeds the Portable v1 limit.");
            }
        }

        return entries;
    }

    private async Task<GachaPortableManifestDto> ReadManifestAsync(
        IReadOnlyDictionary<string, ZipArchiveEntry> entries,
        CancellationToken cancellationToken)
    {
        if (!entries.TryGetValue(
                GachaPortableRules.ManifestPath,
                out ZipArchiveEntry? entry))
        {
            throw GachaPortableRules.Error(
                GachaPortableErrorCode.MissingEntry,
                "manifest.json is missing.",
                GachaPortableRules.ManifestPath);
        }

        if (entry.Length > limits.MaximumManifestBytes)
        {
            throw GachaPortableRules.Error(
                GachaPortableErrorCode.ResourceLimitExceeded,
                "manifest.json exceeds the Portable v1 limit.",
                GachaPortableRules.ManifestPath);
        }

        try
        {
            await using Stream stream = entry.Open();
            return await JsonSerializer.DeserializeAsync<GachaPortableManifestDto>(
                    stream,
                    CreateJsonOptions(),
                    cancellationToken)
                ?? throw GachaPortableRules.Error(
                    GachaPortableErrorCode.InvalidPackage,
                    "manifest.json cannot be null.",
                    GachaPortableRules.ManifestPath);
        }
        catch (JsonException exception)
        {
            throw GachaPortableRules.Error(
                GachaPortableErrorCode.InvalidPackage,
                "manifest.json is invalid JSON.",
                GachaPortableRules.ManifestPath,
                innerException: exception);
        }
    }

    private void ValidateManifest(
        GachaPortableManifestDto manifest,
        IReadOnlyDictionary<string, ZipArchiveEntry> entries)
    {
        if (!string.Equals(
                manifest.Format,
                GachaPortableRules.Format,
                StringComparison.Ordinal) ||
            !string.Equals(
                manifest.Game,
                GachaPortableRules.Game,
                StringComparison.Ordinal))
        {
            throw GachaPortableRules.Error(
                GachaPortableErrorCode.InvalidPackage,
                "The manifest format or game is not supported.",
                GachaPortableRules.ManifestPath);
        }

        if (!Version.TryParse(manifest.FormatVersion, out Version? version))
        {
            throw GachaPortableRules.Error(
                GachaPortableErrorCode.InvalidPackage,
                "format_version is invalid.",
                GachaPortableRules.ManifestPath);
        }

        if (version.Major != 1)
        {
            throw GachaPortableRules.Error(
                GachaPortableErrorCode.UnsupportedVersion,
                $"Unsupported Gacha Portable version: {version}.",
                GachaPortableRules.ManifestPath);
        }

        if (manifest.GeneratedAt == default ||
            manifest.SourceArchive is null ||
            manifest.SourceArchive.ArchiveReference == Guid.Empty ||
            manifest.SourceArchive.Name is null ||
            manifest.Scope is null ||
            !string.Equals(
                manifest.Scope.Kind,
                "selected_accounts",
                StringComparison.Ordinal) ||
            !string.Equals(
                manifest.Scope.CompletenessAssertion,
                "none",
                StringComparison.Ordinal) ||
            manifest.Accounts is null ||
            manifest.Accounts.Count is < 1 ||
            manifest.Accounts.Count > limits.MaximumAccountCount ||
            manifest.Scope.AccountCount != manifest.Accounts.Count ||
            manifest.Scope.RecordCount < 0 ||
            manifest.Scope.RecordCount > limits.MaximumRecordCount)
        {
            throw GachaPortableRules.Error(
                GachaPortableErrorCode.InvalidPackage,
                "The manifest source, scope, or account count is invalid.",
                GachaPortableRules.ManifestPath);
        }

        var expectedPaths = new HashSet<string>(StringComparer.Ordinal)
        {
            GachaPortableRules.ManifestPath,
        };
        var accountReferences = new HashSet<Guid>();
        var naturalIdentities = new HashSet<string>(StringComparer.Ordinal);
        var identitiesById = new Dictionary<Guid, string>();
        int recordCount = 0;
        foreach (GachaPortableAccountManifestDto? account in manifest.Accounts)
        {
            ValidateAccountManifest(
                account,
                expectedPaths,
                accountReferences,
                naturalIdentities,
                identitiesById);
            recordCount = checked(recordCount + account!.RecordCount);
        }

        if (recordCount != manifest.Scope.RecordCount ||
            expectedPaths.Count != entries.Count ||
            expectedPaths.Any(path => !entries.ContainsKey(path)))
        {
            throw GachaPortableRules.Error(
                GachaPortableErrorCode.UnexpectedEntry,
                "The ZIP entries or record counts do not match the manifest.",
                GachaPortableRules.ManifestPath);
        }
    }

    private void ValidateAccountManifest(
        GachaPortableAccountManifestDto? account,
        ISet<string> expectedPaths,
        ISet<Guid> accountReferences,
        ISet<string> naturalIdentities,
        IDictionary<Guid, string> identitiesById)
    {
        if (account is null ||
            account.AccountReference == Guid.Empty ||
            account.RoleIdentityId == Guid.Empty ||
            account.NaturalIdentity is null ||
            account.RecordCount < 0 ||
            account.RecordCount > limits.MaximumRecordCount ||
            account.UigfLength < 0 ||
            account.SupplementLength < 0 ||
            account.UigfLength > limits.MaximumEntryBytes ||
            account.SupplementLength > limits.MaximumEntryBytes ||
            !GachaPortableRules.IsSha256(account.UigfSha256) ||
            !GachaPortableRules.IsSha256(account.SupplementSha256))
        {
            throw GachaPortableRules.Error(
                GachaPortableErrorCode.InvalidPackage,
                "An account manifest is invalid.",
                GachaPortableRules.ManifestPath);
        }

        if (!accountReferences.Add(account.AccountReference))
        {
            throw GachaPortableRules.Error(
                GachaPortableErrorCode.DuplicateReference,
                "Duplicate account_ref in manifest.",
                GachaPortableRules.ManifestPath);
        }

        GameRoleNaturalIdentity naturalIdentity;
        try
        {
            naturalIdentity = new GameRoleNaturalIdentity(
                account.NaturalIdentity.GameBiz!,
                account.NaturalIdentity.Server!,
                account.NaturalIdentity.Uid!);
        }
        catch (Exception exception) when (
            exception is ArgumentException or ArgumentNullException)
        {
            throw GachaPortableRules.Error(
                GachaPortableErrorCode.UnsupportedAccount,
                "The natural identity is invalid.",
                GachaPortableRules.ManifestPath,
                innerException: exception);
        }

        if (!string.Equals(
                naturalIdentity.GameBiz,
                account.NaturalIdentity.GameBiz,
                StringComparison.Ordinal) ||
            !string.Equals(
                naturalIdentity.Server,
                account.NaturalIdentity.Server,
                StringComparison.Ordinal) ||
            !string.Equals(
                naturalIdentity.Uid,
                account.NaturalIdentity.Uid,
                StringComparison.Ordinal))
        {
            throw GachaPortableRules.Error(
                GachaPortableErrorCode.UnsupportedAccount,
                "The natural identity is not canonically encoded.",
                GachaPortableRules.ManifestPath);
        }

        GachaPortableRules.ValidateNaturalIdentity(
            naturalIdentity,
            GachaPortableRules.ManifestPath);
        string naturalName = naturalIdentity.ToDeterministicName();
        if (!naturalIdentities.Add(naturalName))
        {
            throw GachaPortableRules.Error(
                GachaPortableErrorCode.DuplicateReference,
                "Duplicate natural identity in a source archive.",
                GachaPortableRules.ManifestPath);
        }

        if (identitiesById.TryGetValue(
                account.RoleIdentityId,
                out string? existing) &&
            !string.Equals(existing, naturalName, StringComparison.Ordinal))
        {
            throw GachaPortableRules.Error(
                GachaPortableErrorCode.InvalidReference,
                "A role identity GUID refers to different natural identities.",
                GachaPortableRules.ManifestPath);
        }

        identitiesById[account.RoleIdentityId] = naturalName;
        string expectedUigf =
            GachaPortableRules.GetUigfPath(account.AccountReference);
        string expectedSupplement =
            GachaPortableRules.GetSupplementPath(account.AccountReference);
        if (!string.Equals(account.UigfPath, expectedUigf, StringComparison.Ordinal) ||
            !string.Equals(
                account.SupplementPath,
                expectedSupplement,
                StringComparison.Ordinal) ||
            !expectedPaths.Add(expectedUigf) ||
            !expectedPaths.Add(expectedSupplement))
        {
            throw GachaPortableRules.Error(
                GachaPortableErrorCode.InvalidPath,
                "Account payload paths are not canonical.",
                GachaPortableRules.ManifestPath);
        }
    }

    private async Task<GachaPortableAccount> ReadAccountAsync(
        GachaPortableAccountManifestDto manifest,
        IReadOnlyDictionary<string, ZipArchiveEntry> entries,
        CancellationToken cancellationToken)
    {
        List<PortableStandardRecord> standardRecords = await ReadUigfAsync(
            entries[manifest.UigfPath!],
            manifest,
            cancellationToken);
        Dictionary<string, GachaPortableSupplementDto> supplements =
            await ReadSupplementsAsync(
                entries[manifest.SupplementPath!],
                manifest,
                cancellationToken);
        if (standardRecords.Count != manifest.RecordCount ||
            supplements.Count != manifest.RecordCount)
        {
            throw GachaPortableRules.Error(
                GachaPortableErrorCode.RecordCountMismatch,
                "The account payload record count does not match the manifest.",
                manifest.UigfPath);
        }

        var records = new List<GachaRecord>(standardRecords.Count);
        foreach (PortableStandardRecord standard in standardRecords)
        {
            if (!supplements.Remove(
                    standard.ExternalRecordId,
                    out GachaPortableSupplementDto? supplement))
            {
                throw GachaPortableRules.Error(
                    GachaPortableErrorCode.RecordMismatch,
                    "A standard record has no matching supplement.",
                    manifest.SupplementPath,
                    standard.ExternalRecordId);
            }

            records.Add(ToDomainRecord(
                manifest.AccountReference,
                standard,
                supplement,
                manifest.SupplementPath!));
        }

        if (supplements.Count != 0)
        {
            throw GachaPortableRules.Error(
                GachaPortableErrorCode.RecordMismatch,
                "The supplement contains orphan records.",
                manifest.SupplementPath);
        }

        var naturalIdentity = new GameRoleNaturalIdentity(
            manifest.NaturalIdentity!.GameBiz!,
            manifest.NaturalIdentity.Server!,
            manifest.NaturalIdentity.Uid!);
        return new GachaPortableAccount(
            manifest.AccountReference,
            new GameRoleIdentity(
                new GameRoleIdentityId(manifest.RoleIdentityId),
                naturalIdentity),
            manifest.DisplayName,
            records);
    }

    private async Task<List<PortableStandardRecord>> ReadUigfAsync(
        ZipArchiveEntry entry,
        GachaPortableAccountManifestDto manifest,
        CancellationToken cancellationToken)
    {
        ValidateDeclaredPayload(entry, manifest.UigfLength, manifest.UigfPath!);
        PortableUigfDocumentDto document;
        await using (Stream entryStream = entry.Open())
        await using (var hashing = new HashingReadStream(entryStream))
        {
            try
            {
                document = await JsonSerializer.DeserializeAsync<PortableUigfDocumentDto>(
                        hashing,
                        CreateJsonOptions(),
                        cancellationToken)
                    ?? throw GachaPortableRules.Error(
                        GachaPortableErrorCode.InvalidPackage,
                        "The UIGF payload cannot be null.",
                        manifest.UigfPath);
            }
            catch (JsonException exception)
            {
                throw GachaPortableRules.Error(
                    GachaPortableErrorCode.InvalidPackage,
                    "The UIGF payload is invalid JSON.",
                    manifest.UigfPath,
                    innerException: exception);
            }

            ValidateHash(
                hashing,
                manifest.UigfLength,
                manifest.UigfSha256!,
                manifest.UigfPath!);
        }

        if (document.Info is null ||
            document.Info.ExportTimestamp < 0 ||
            string.IsNullOrWhiteSpace(document.Info.ExportApp) ||
            string.IsNullOrWhiteSpace(document.Info.ExportAppVersion) ||
            !string.Equals(
                document.Info.Version,
                GachaPortableRules.UigfVersion,
                StringComparison.Ordinal) ||
            document.Hk4e is not { Count: 1 } ||
            document.Hk4e[0] is not PortableUigfAccountDto account ||
            !string.Equals(
                account.Uid,
                manifest.NaturalIdentity!.Uid,
                StringComparison.Ordinal) ||
            account.Timezone != 0 ||
            account.List is null)
        {
            throw GachaPortableRules.Error(
                GachaPortableErrorCode.RecordMismatch,
                "The UIGF account does not match the manifest.",
                manifest.UigfPath);
        }

        var records = new List<PortableStandardRecord>(account.List.Count);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (PortableUigfRecordDto? record in account.List)
        {
            PortableStandardRecord parsed = ParseStandardRecord(
                record,
                manifest.UigfPath!);
            if (!ids.Add(parsed.ExternalRecordId))
            {
                throw GachaPortableRules.Error(
                    GachaPortableErrorCode.DuplicateReference,
                    "Duplicate UIGF record ID.",
                    manifest.UigfPath,
                    parsed.ExternalRecordId);
            }

            records.Add(parsed);
        }

        return records;
    }

    private static PortableStandardRecord ParseStandardRecord(
        PortableUigfRecordDto? record,
        string path)
    {
        if (record is null ||
            string.IsNullOrWhiteSpace(record.Id) ||
            record.Id.Length > 19 ||
            !record.Id.All(char.IsAsciiDigit) ||
            string.IsNullOrWhiteSpace(record.ItemId) ||
            string.IsNullOrWhiteSpace(record.GachaType) ||
            string.IsNullOrWhiteSpace(record.UigfGachaType) ||
            !int.TryParse(
                record.Count,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int count) ||
            count <= 0 ||
            !DateTime.TryParseExact(
                record.Time,
                GachaPortableRules.TimeFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTime utcTime))
        {
            throw GachaPortableRules.Error(
                GachaPortableErrorCode.InvalidPackage,
                "A UIGF record is invalid.",
                path,
                record?.Id);
        }

        int? rankType = null;
        if (record.RankType is not null)
        {
            if (!int.TryParse(
                    record.RankType,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int rank) ||
                rank is < 3 or > 5)
            {
                throw GachaPortableRules.Error(
                    GachaPortableErrorCode.InvalidPackage,
                    "A UIGF rank_type is invalid.",
                    path,
                    record.Id);
            }

            rankType = rank;
        }

        var temporary = new GachaRecord(
            Guid.Empty,
            record.Id,
            record.Name,
            rankType,
            new DateTimeOffset(utcTime, TimeSpan.Zero))
        {
            ItemId = record.ItemId,
            ItemType = record.ItemType,
            GachaType = record.GachaType,
            UigfGachaType = record.UigfGachaType,
            Count = count,
        };
        GachaPortableRules.ValidateRecord(temporary, Guid.Empty, path);
        return new PortableStandardRecord(
            record.Id,
            record.ItemId,
            record.Name,
            record.ItemType,
            rankType,
            record.GachaType,
            record.UigfGachaType,
            count,
            new DateTimeOffset(utcTime, TimeSpan.Zero));
    }

    private async Task<Dictionary<string, GachaPortableSupplementDto>>
        ReadSupplementsAsync(
            ZipArchiveEntry entry,
            GachaPortableAccountManifestDto manifest,
            CancellationToken cancellationToken)
    {
        ValidateDeclaredPayload(
            entry,
            manifest.SupplementLength,
            manifest.SupplementPath!);
        var supplements = new Dictionary<string, GachaPortableSupplementDto>(
            StringComparer.Ordinal);
        await using Stream entryStream = entry.Open();
        await using var hashing = new HashingReadStream(entryStream);
        var line = new ArrayBufferWriter<byte>();
        byte[] buffer = new byte[8192];
        while (true)
        {
            int read = await hashing.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            int segmentStart = 0;
            for (int index = 0; index < read; index++)
            {
                if (buffer[index] != (byte)'\n')
                {
                    continue;
                }

                AppendLine(line, buffer.AsSpan(segmentStart, index - segmentStart));
                AddSupplement(line, manifest, supplements);
                line.Clear();
                segmentStart = index + 1;
            }

            AppendLine(line, buffer.AsSpan(segmentStart, read - segmentStart));
        }

        if (line.WrittenCount > 0)
        {
            AddSupplement(line, manifest, supplements);
        }

        ValidateHash(
            hashing,
            manifest.SupplementLength,
            manifest.SupplementSha256!,
            manifest.SupplementPath!);
        return supplements;
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

    private void AddSupplement(
        ArrayBufferWriter<byte> line,
        GachaPortableAccountManifestDto manifest,
        IDictionary<string, GachaPortableSupplementDto> supplements)
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
                    CreateJsonOptions())
                ?? throw GachaPortableRules.Error(
                    GachaPortableErrorCode.InvalidPackage,
                    "A supplement line cannot be null.",
                    manifest.SupplementPath);
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

        if (!supplements.TryAdd(supplement.ExternalRecordId, supplement))
        {
            throw GachaPortableRules.Error(
                GachaPortableErrorCode.DuplicateReference,
                "Duplicate supplement record reference.",
                manifest.SupplementPath,
                supplement.ExternalRecordId);
        }
    }

    private static GachaRecord ToDomainRecord(
        Guid accountReference,
        PortableStandardRecord standard,
        GachaPortableSupplementDto supplement,
        string path)
    {
        if (supplement.OccurredAtUtcTicks < DateTime.MinValue.Ticks ||
            supplement.OccurredAtUtcTicks > DateTime.MaxValue.Ticks ||
            supplement.OccurredAtOffsetMinutes is < -840 or > 840)
        {
            throw GachaPortableRules.Error(
                GachaPortableErrorCode.RecordMismatch,
                "The exact occurrence time is invalid.",
                path,
                standard.ExternalRecordId);
        }

        TimeSpan offset = TimeSpan.FromMinutes(
            supplement.OccurredAtOffsetMinutes);
        DateTimeOffset exactTime;
        try
        {
            exactTime = new DateTimeOffset(
                    supplement.OccurredAtUtcTicks,
                    TimeSpan.Zero)
                .ToOffset(offset);
        }
        catch (ArgumentException exception)
        {
            throw GachaPortableRules.Error(
                GachaPortableErrorCode.RecordMismatch,
                "The exact occurrence time cannot use its declared offset.",
                path,
                standard.ExternalRecordId,
                exception);
        }

        long standardSecond = standard.Time.UtcDateTime.Ticks /
            TimeSpan.TicksPerSecond;
        long exactSecond = exactTime.UtcDateTime.Ticks /
            TimeSpan.TicksPerSecond;
        if (standardSecond != exactSecond)
        {
            throw GachaPortableRules.Error(
                GachaPortableErrorCode.RecordMismatch,
                "The UIGF time and exact supplement time disagree.",
                path,
                standard.ExternalRecordId);
        }

        DataOrigin origin = GachaPortableRules.ParseOrigin(
            supplement.Origin,
            path,
            standard.ExternalRecordId);
        AcquisitionBatchId? batchId = supplement.AcquisitionBatchId switch
        {
            null => null,
            Guid value when value != Guid.Empty => new AcquisitionBatchId(value),
            _ => throw GachaPortableRules.Error(
                GachaPortableErrorCode.InvalidReference,
                "acquisition_batch_id cannot be empty.",
                path,
                standard.ExternalRecordId),
        };
        var provenance = new RecordProvenance(
            origin,
            new RecordTimestamps(
                FetchedAt: supplement.FetchedAt,
                ImportedAt: supplement.ImportedAt),
            acquisitionBatchId: batchId);
        return new GachaRecord(
            accountReference,
            standard.ExternalRecordId,
            standard.ItemName,
            standard.RankType,
            exactTime)
        {
            ItemId = standard.ItemId,
            ItemType = standard.ItemType,
            GachaType = standard.GachaType,
            UigfGachaType = standard.UigfGachaType,
            Count = standard.Count,
            Provenance = provenance,
        };
    }

    private static void ValidateDeclaredPayload(
        ZipArchiveEntry entry,
        long expectedLength,
        string path)
    {
        if (entry.Length != expectedLength)
        {
            throw GachaPortableRules.Error(
                GachaPortableErrorCode.LengthMismatch,
                $"Payload length does not match manifest: {path}.",
                path);
        }
    }

    private static void ValidateHash(
        HashingReadStream stream,
        long expectedLength,
        string expectedHash,
        string path)
    {
        string actualHash = stream.CompleteHash();
        if (stream.ByteCount != expectedLength)
        {
            throw GachaPortableRules.Error(
                GachaPortableErrorCode.LengthMismatch,
                $"Payload read length does not match manifest: {path}.",
                path);
        }

        if (!string.Equals(actualHash, expectedHash, StringComparison.Ordinal))
        {
            throw GachaPortableRules.Error(
                GachaPortableErrorCode.HashMismatch,
                $"Payload hash does not match manifest: {path}.",
                path);
        }
    }

    private JsonSerializerOptions CreateJsonOptions() => new()
    {
        MaxDepth = limits.MaximumJsonDepth,
        PropertyNameCaseInsensitive = false,
    };

    private sealed record PortableStandardRecord(
        string ExternalRecordId,
        string ItemId,
        string? ItemName,
        string? ItemType,
        int? RankType,
        string GachaType,
        string UigfGachaType,
        int Count,
        DateTimeOffset Time);
}
