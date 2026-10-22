// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FurinaChronicle.Services.Archives.Portable;

namespace FurinaChronicle.Infrastructure.Archives.Portable;

public sealed class ArchivePackageReader(ArchivePortableLimits? limits = null)
    : IArchivePackageReader
{
    private readonly ArchivePortableLimits limits = limits ?? new();

    public async Task<ArchivePackageSummary> ReadAndValidateAsync(
        Stream source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead || !source.CanSeek)
        {
            throw Error(
                ArchivePackageErrorCode.InputNotSeekable,
                "Archive validation requires a readable, seekable stream.");
        }

        source.Position = 0;
        using var zip = new ZipArchive(source, ZipArchiveMode.Read, true);
        IReadOnlyDictionary<string, ZipArchiveEntry> entries = IndexEntries(zip);
        Dictionary<string, string> checksums = await ReadChecksumsAsync(
            Require(entries, ArchivePackageWriter.ChecksumsPath),
            cancellationToken);
        ValidateChecksumCoverage(entries, checksums);
        await VerifyChecksumsAsync(entries, checksums, cancellationToken);

        ArchiveManifest manifest = await ReadJsonAsync<ArchiveManifest>(
            Require(entries, ArchivePackageWriter.ManifestPath),
            limits.MaximumManifestBytes,
            cancellationToken);
        ValidateManifest(manifest, entries);
        ArchiveVersionDispatcher.RequireSupportedArchive(manifest.FormatVersion);

        var diagnostics = new List<ArchiveDiagnostic>();
        foreach (ArchiveDatasetDescriptor dataset in manifest.Datasets)
        {
            if (ArchiveVersionDispatcher.IsKnownDataset(
                dataset.Format,
                dataset.FormatVersion))
            {
                continue;
            }
            if (dataset.Required)
            {
                throw Error(
                    ArchivePackageErrorCode.UnknownRequiredDataset,
                    $"Required dataset {dataset.DatasetReference} is unsupported.");
            }
            diagnostics.Add(new ArchiveDiagnostic(
                ArchiveDiagnosticCode.UnsupportedOptionalDataset,
                $"Optional dataset {dataset.DatasetReference} is unsupported.",
                dataset.Paths.FirstOrDefault(),
                IsPartial: true));
        }

        ArchiveCatalogWire catalogWire = await ReadJsonAsync<ArchiveCatalogWire>(
            Require(entries, ArchivePackageWriter.CatalogPath),
            limits.MaximumEntryBytes,
            cancellationToken);
        if (catalogWire.Format != "furina-archive-catalog" ||
            catalogWire.FormatVersion != "1.0")
        {
            throw Error(
                ArchivePackageErrorCode.UnsupportedVersion,
                "The Archive catalog format is unsupported.",
                ArchivePackageWriter.CatalogPath);
        }
        ArchiveCatalog catalog = catalogWire.ToModel();
        ValidateCatalog(catalog, manifest);

        ArchiveSettingsWire settingsWire = await ReadJsonAsync<ArchiveSettingsWire>(
            Require(entries, manifest.PortableSettingsPath),
            limits.MaximumEntryBytes,
            cancellationToken);
        if (settingsWire.Format != "furina-portable-settings" ||
            settingsWire.FormatVersion != "1.0")
        {
            throw Error(
                ArchivePackageErrorCode.UnsupportedVersion,
                "The portable settings format is unsupported.",
                manifest.PortableSettingsPath);
        }
        ArchivePortableSettings settings = settingsWire.ToModel();
        ValidateSettings(settings, catalog);

        HashSet<Guid> accountReferences = catalog.Archives
            .SelectMany(item => item.Accounts)
            .Select(item => item.AccountReference)
            .ToHashSet();
        GachaHistoryValidation gachaHistory = await ValidateGachaHistoryAsync(
            Require(entries, ArchivePackageWriter.GachaHistoryPath),
            accountReferences,
            cancellationToken);
        long uigfFacts = 0;
        var uigfSequences = new Dictionary<Guid, SequenceSummary>();
        foreach (ArchiveDatasetDescriptor dataset in manifest.Datasets.Where(item =>
            item.Format == "uigf" && item.FormatVersion == "v4.2"))
        {
            foreach (string path in dataset.Paths)
            {
                UigfValidation validated = await ValidateUigfAsync(
                    Require(entries, path),
                    path,
                    catalog,
                    cancellationToken);
                uigfFacts += validated.Sequence.Count;
                if (!uigfSequences.TryAdd(validated.AccountReference, validated.Sequence))
                {
                    throw Error(ArchivePackageErrorCode.DuplicatePath,
                        "An account has more than one embedded UIGF payload.", path);
                }
            }
            RequireCount(dataset, "current_facts", uigfFacts);
        }

        long domainFacts = 0;
        long domainRevisions = 0;
        foreach (ArchiveDatasetDescriptor dataset in manifest.Datasets.Where(item =>
            item.Format == "furina-domain-history" && item.FormatVersion == "1.0"))
        {
            foreach (string path in dataset.Paths)
            {
                DomainCounts counts = await ValidateDomainAsync(
                    Require(entries, path),
                    dataset.Kind,
                    catalog,
                    cancellationToken);
                domainFacts += counts.Facts;
                domainRevisions += counts.Revisions;
            }
        }

        ArchiveDatasetDescriptor history = manifest.Datasets.Single(item =>
            item.Format == "furina-archive-gacha-history" &&
            item.FormatVersion == "1.0");
        ValidateStandardOneToOne(gachaHistory.StandardSequences, uigfSequences);
        RequireCount(history, "current_facts", gachaHistory.Facts);
        RequireCount(history, "revisions", gachaHistory.Revisions);
        EnsureCount(gachaHistory.Facts, limits.MaximumGachaFactCount, "Gacha facts");
        EnsureCount(gachaHistory.Revisions, limits.MaximumGachaRevisionCount, "Gacha revisions");
        EnsureCount(domainFacts + domainRevisions,
            limits.MaximumDomainRecordCount * Math.Max(1, catalog.Archives.Count),
            "domain records");

        return new ArchivePackageSummary(
            manifest,
            catalog,
            settings,
            gachaHistory.Facts,
            gachaHistory.Revisions,
            domainFacts,
            domainRevisions,
            entries.Values.Where(item => item.FullName != ArchivePackageWriter.ChecksumsPath)
                .Sum(item => item.Length),
            diagnostics);
    }

    private IReadOnlyDictionary<string, ZipArchiveEntry> IndexEntries(ZipArchive zip)
    {
        if (zip.Entries.Count > limits.MaximumEntryCount)
        {
            throw Error(ArchivePackageErrorCode.ResourceLimitExceeded,
                "The Archive contains too many entries.");
        }
        var result = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        long total = 0;
        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            ValidatePath(entry.FullName);
            if (!result.TryAdd(entry.FullName, entry))
            {
                throw Error(ArchivePackageErrorCode.DuplicatePath,
                    $"Duplicate Archive path: {entry.FullName}.", entry.FullName);
            }
            if (entry.Length > limits.MaximumEntryBytes &&
                entry.FullName != ArchivePackageWriter.ChecksumsPath)
            {
                throw Error(ArchivePackageErrorCode.ResourceLimitExceeded,
                    $"Archive entry exceeds the limit: {entry.FullName}.", entry.FullName);
            }
            total = checked(total + entry.Length);
            if (total > limits.MaximumUncompressedBytes)
            {
                throw Error(ArchivePackageErrorCode.ResourceLimitExceeded,
                    "The Archive uncompressed size exceeds the limit.");
            }
        }
        return result;
    }

    private async Task<Dictionary<string, string>> ReadChecksumsAsync(
        ZipArchiveEntry entry,
        CancellationToken cancellationToken)
    {
        byte[] bytes = await ReadBoundedAsync(
            entry,
            limits.MaximumChecksumsBytes,
            cancellationToken);
        string content = Encoding.UTF8.GetString(bytes);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        string previous = string.Empty;
        foreach (string line in content.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length < 67 || line[64..66] != "  ")
            {
                throw Error(ArchivePackageErrorCode.InvalidPackage,
                    "checksums.sha256 contains an invalid line.", entry.FullName);
            }
            string hash = line[..64];
            string path = line[66..];
            ValidatePath(path);
            if (!IsSha256(hash) ||
                string.CompareOrdinal(previous, path) >= 0 ||
                !result.TryAdd(path, hash))
            {
                throw Error(ArchivePackageErrorCode.InvalidPackage,
                    "checksums.sha256 is not a unique ordinal path list.", entry.FullName);
            }
            previous = path;
        }
        return result;
    }

    private static void ValidateChecksumCoverage(
        IReadOnlyDictionary<string, ZipArchiveEntry> entries,
        IReadOnlyDictionary<string, string> checksums)
    {
        string[] expected = entries.Keys
            .Where(path => path != ArchivePackageWriter.ChecksumsPath)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        if (!expected.SequenceEqual(
            checksums.Keys.OrderBy(path => path, StringComparer.Ordinal),
            StringComparer.Ordinal))
        {
            throw Error(ArchivePackageErrorCode.MissingEntry,
                "The checksum list does not cover exactly every logical entry.");
        }
    }

    private static async Task VerifyChecksumsAsync(
        IReadOnlyDictionary<string, ZipArchiveEntry> entries,
        IReadOnlyDictionary<string, string> checksums,
        CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[64 * 1024];
        foreach ((string path, string expected) in checksums)
        {
            await using Stream stream = entries[path].Open();
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            while (true)
            {
                int read = await stream.ReadAsync(buffer, cancellationToken);
                if (read == 0) break;
                hash.AppendData(buffer.AsSpan(0, read));
            }
            string actual = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(expected),
                Encoding.ASCII.GetBytes(actual)))
            {
                throw Error(ArchivePackageErrorCode.HashMismatch,
                    $"Archive entry hash mismatch: {path}.", path);
            }
        }
    }

    private void ValidateManifest(
        ArchiveManifest manifest,
        IReadOnlyDictionary<string, ZipArchiveEntry> entries)
    {
        if (manifest.Format != ArchivePackageWriter.Format ||
            manifest.Datasets is null ||
            manifest.PortableSettingsPath != ArchivePackageWriter.SettingsPath ||
            manifest.ChecksumsPath != ArchivePackageWriter.ChecksumsPath)
        {
            throw Error(ArchivePackageErrorCode.InvalidPackage,
                "The Archive manifest header is invalid.",
                ArchivePackageWriter.ManifestPath);
        }
        var datasetRefs = new HashSet<string>(StringComparer.Ordinal);
        var declaredPaths = new HashSet<string>(StringComparer.Ordinal)
        {
            ArchivePackageWriter.ManifestPath,
        };
        foreach (ArchiveDatasetDescriptor dataset in manifest.Datasets)
        {
            if (string.IsNullOrWhiteSpace(dataset.DatasetReference) ||
                !datasetRefs.Add(dataset.DatasetReference) ||
                dataset.Paths.Count == 0 ||
                dataset.UncompressedLength < 0)
            {
                throw Error(ArchivePackageErrorCode.InvalidPackage,
                    "The Archive manifest contains an invalid dataset descriptor.");
            }
            long length = 0;
            foreach (string path in dataset.Paths)
            {
                ValidatePath(path);
                if (!declaredPaths.Add(path))
                {
                    throw Error(ArchivePackageErrorCode.DuplicatePath,
                        $"A dataset path is declared more than once: {path}.", path);
                }
                length = checked(length + Require(entries, path).Length);
            }
            if (length != dataset.UncompressedLength)
            {
                throw Error(ArchivePackageErrorCode.LengthMismatch,
                    $"Dataset length mismatch: {dataset.DatasetReference}.");
            }
        }
        string[] actual = entries.Keys
            .Where(path => path != ArchivePackageWriter.ChecksumsPath)
            .OrderBy(path => path, StringComparer.Ordinal).ToArray();
        if (!declaredPaths.OrderBy(path => path, StringComparer.Ordinal)
            .SequenceEqual(actual, StringComparer.Ordinal))
        {
            throw Error(ArchivePackageErrorCode.UnexpectedEntry,
                "The Archive contains an entry not declared by its manifest.");
        }
    }

    private void ValidateCatalog(ArchiveCatalog catalog, ArchiveManifest manifest)
    {
        if (catalog.Archives.Count > limits.MaximumArchiveCount ||
            catalog.AccountCount > limits.MaximumAccountCount)
        {
            throw Error(ArchivePackageErrorCode.ResourceLimitExceeded,
                "The Archive catalog exceeds its parent-object limit.");
        }
        Guid[] archives = catalog.Archives.Select(item => item.ArchiveReference).ToArray();
        Guid[] accounts = catalog.Archives.SelectMany(item => item.Accounts)
            .Select(item => item.AccountReference).ToArray();
        Guid[] roleReferences = catalog.RoleIdentities
            .Select(item => item.RoleIdentityReference).ToArray();
        if (archives.Any(id => id == Guid.Empty) ||
            archives.Distinct().Count() != archives.Length ||
            accounts.Any(id => id == Guid.Empty) ||
            accounts.Distinct().Count() != accounts.Length ||
            roleReferences.Any(id => id == Guid.Empty) ||
            roleReferences.Distinct().Count() != roleReferences.Length ||
            !archives.Order().SequenceEqual(manifest.ArchiveReferences.Order()))
        {
            throw Error(ArchivePackageErrorCode.InvalidReference,
                "The Archive catalog parent references are invalid.");
        }
        HashSet<Guid> roles = roleReferences.ToHashSet();
        if (catalog.Archives.SelectMany(item => item.Accounts).Any(account =>
            account.RoleIdentityReference is Guid role && !roles.Contains(role)))
        {
            throw Error(ArchivePackageErrorCode.InvalidReference,
                "An account references an unknown role identity.");
        }
    }

    private static void ValidateSettings(
        ArchivePortableSettings settings,
        ArchiveCatalog catalog)
    {
        HashSet<Guid> archives = catalog.Archives
            .Select(item => item.ArchiveReference).ToHashSet();
        if (settings.Archives.Count != archives.Count ||
            settings.Archives.Select(item => item.ArchiveReference).Distinct().Count() !=
                settings.Archives.Count ||
            settings.Archives.Any(item =>
                !archives.Contains(item.ArchiveReference) ||
                item.UndoLimit is < 0 or > 1000))
        {
            throw Error(ArchivePackageErrorCode.InvalidReference,
                "Portable settings do not match the Archive catalog.");
        }
    }

    private async Task<GachaHistoryValidation> ValidateGachaHistoryAsync(
        ZipArchiveEntry entry,
        IReadOnlySet<Guid> accountReferences,
        CancellationToken cancellationToken)
    {
        long facts = 0;
        long revisions = 0;
        var standardKeys = new Dictionary<Guid, SequenceAccumulator>();
        await using Stream stream = entry.Open();
        using var reader = new StreamReader(
            stream,
            new UTF8Encoding(false, true),
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 64 * 1024,
            leaveOpen: false);
        while (await reader.ReadLineAsync(cancellationToken) is string line)
        {
            if (line.Length == 0) continue;
            if (Encoding.UTF8.GetByteCount(line) > limits.MaximumNdjsonLineBytes)
            {
                throw Error(ArchivePackageErrorCode.ResourceLimitExceeded,
                    "A Gacha history line exceeds the Archive limit.", entry.FullName);
            }
            ArchiveGachaHistoryLine value;
            try
            {
                value = JsonSerializer.Deserialize<ArchiveGachaHistoryLine>(
                    line,
                    ArchivePortableJson.Compact) ?? throw new JsonException();
            }
            catch (JsonException exception)
            {
                throw new ArchivePackageException(
                    ArchivePackageErrorCode.InvalidPackage,
                    "A Gacha history line is invalid.",
                    entry.FullName,
                    exception);
            }
            if (!accountReferences.Contains(value.AccountReference) ||
                string.IsNullOrWhiteSpace(value.ExternalRecordId))
            {
                throw Error(ArchivePackageErrorCode.InvalidReference,
                    "A Gacha history line has an invalid account or fact reference.",
                    entry.FullName);
            }
            switch (value.Kind)
            {
                case "standard_extension" when value.Extension is not null &&
                    value.Record is null && value.Revision is null:
                    if (!standardKeys.TryGetValue(
                        value.AccountReference,
                        out SequenceAccumulator? keys))
                    {
                        keys = new SequenceAccumulator();
                        standardKeys[value.AccountReference] = keys;
                    }
                    keys.Append(value.ExternalRecordId);
                    facts++;
                    break;
                case "nonstandard_current" when value.Record is not null &&
                    value.Extension is null && value.Revision is null:
                    ValidateGachaFactReference(
                        value.Record,
                        value.AccountReference,
                        value.ExternalRecordId,
                        entry.FullName);
                    facts++;
                    break;
                case "revision" when value.Revision is not null &&
                    value.Record is null && value.Extension is null:
                    if (value.Revision.Before is not null)
                    {
                        ValidateGachaFactReference(
                            value.Revision.Before,
                            value.AccountReference,
                            value.ExternalRecordId,
                            entry.FullName);
                    }
                    if (value.Revision.After is not null)
                    {
                        ValidateGachaFactReference(
                            value.Revision.After,
                            value.AccountReference,
                            value.ExternalRecordId,
                            entry.FullName);
                    }
                    revisions++;
                    break;
                default:
                    throw Error(ArchivePackageErrorCode.InvalidPackage,
                        "A Gacha history line kind does not match its payload.",
                        entry.FullName);
            }
        }
        try
        {
            return new GachaHistoryValidation(
                facts,
                revisions,
                standardKeys.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value.Complete()));
        }
        finally
        {
            foreach (SequenceAccumulator accumulator in standardKeys.Values)
            {
                accumulator.Dispose();
            }
        }
    }

    private async Task<UigfValidation> ValidateUigfAsync(
        ZipArchiveEntry entry,
        string path,
        ArchiveCatalog catalog,
        CancellationToken cancellationToken)
    {
        Guid accountReference = ParseAccountReferenceFromUigfPath(path);
        ArchiveCatalogAccount catalogAccount = catalog.Archives
            .SelectMany(item => item.Accounts)
            .SingleOrDefault(item => item.AccountReference == accountReference)
            ?? throw Error(ArchivePackageErrorCode.InvalidReference,
                "An embedded UIGF path references an unknown account.", path);
        ArchiveCatalogRoleIdentity identity = catalog.RoleIdentities.Single(item =>
            item.RoleIdentityReference == catalogAccount.RoleIdentityReference);

        await using Stream stream = entry.Open();
        byte[] buffer = new byte[64 * 1024];
        int remaining = 0;
        var state = new JsonReaderState(new JsonReaderOptions
        {
            MaxDepth = limits.MaximumJsonDepth,
            CommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false,
        });
        string? pendingProperty = null;
        string? version = null;
        string? uid = null;
        string? recordId = null;
        string? itemId = null;
        int infoDepth = -1;
        int hk4eDepth = -1;
        int accountDepth = -1;
        int listDepth = -1;
        int recordDepth = -1;
        int accountCount = 0;
        bool sawList = false;
        using var sequence = new SequenceAccumulator();
        try
        {
            while (true)
            {
                int read = await stream.ReadAsync(
                    buffer.AsMemory(remaining),
                    cancellationToken);
                int available = remaining + read;
                bool final = read == 0;
                var reader = new Utf8JsonReader(
                    buffer.AsSpan(0, available),
                    final,
                    state);
                while (reader.Read())
                {
                    switch (reader.TokenType)
                    {
                        case JsonTokenType.PropertyName:
                            pendingProperty = reader.GetString();
                            break;
                        case JsonTokenType.StartObject:
                            if (pendingProperty == "info" && infoDepth < 0)
                                infoDepth = reader.CurrentDepth;
                            else if (hk4eDepth >= 0 && accountDepth < 0 &&
                                reader.CurrentDepth == hk4eDepth + 1)
                            {
                                accountDepth = reader.CurrentDepth;
                                accountCount++;
                            }
                            else if (listDepth >= 0 && recordDepth < 0 &&
                                reader.CurrentDepth == listDepth + 1)
                            {
                                recordDepth = reader.CurrentDepth;
                                recordId = null;
                                itemId = null;
                            }
                            pendingProperty = null;
                            break;
                        case JsonTokenType.StartArray:
                            if (pendingProperty == "hk4e" && hk4eDepth < 0)
                                hk4eDepth = reader.CurrentDepth;
                            else if (pendingProperty == "list" && accountDepth >= 0)
                            {
                                listDepth = reader.CurrentDepth;
                                sawList = true;
                            }
                            pendingProperty = null;
                            break;
                        case JsonTokenType.String:
                            if (recordDepth >= 0)
                            {
                                if (pendingProperty == "id") recordId = reader.GetString();
                                else if (pendingProperty == "item_id") itemId = reader.GetString();
                            }
                            else if (accountDepth >= 0 && pendingProperty == "uid")
                                uid = reader.GetString();
                            else if (infoDepth >= 0 && pendingProperty == "version")
                                version = reader.GetString();
                            pendingProperty = null;
                            break;
                        case JsonTokenType.EndObject:
                            if (reader.CurrentDepth == recordDepth)
                            {
                                if (string.IsNullOrWhiteSpace(recordId) ||
                                    string.IsNullOrWhiteSpace(itemId))
                                {
                                    throw new JsonException(
                                        "A UIGF record is missing id or item_id.");
                                }
                                sequence.Append(recordId);
                                if (sequence.Count > limits.MaximumGachaFactCount)
                                {
                                    throw Error(
                                        ArchivePackageErrorCode.ResourceLimitExceeded,
                                        "The embedded UIGF record count exceeds the limit.",
                                        path);
                                }
                                recordDepth = -1;
                            }
                            else if (reader.CurrentDepth == accountDepth)
                                accountDepth = -1;
                            else if (reader.CurrentDepth == infoDepth)
                                infoDepth = -1;
                            pendingProperty = null;
                            break;
                        case JsonTokenType.EndArray:
                            if (reader.CurrentDepth == listDepth) listDepth = -1;
                            else if (reader.CurrentDepth == hk4eDepth) hk4eDepth = -1;
                            pendingProperty = null;
                            break;
                        default:
                            if (reader.TokenType is JsonTokenType.Number or
                                JsonTokenType.True or JsonTokenType.False or
                                JsonTokenType.Null)
                                pendingProperty = null;
                            break;
                    }
                }
                int consumed = checked((int)reader.BytesConsumed);
                remaining = available - consumed;
                if (remaining > 0)
                    buffer.AsSpan(consumed, remaining).CopyTo(buffer);
                state = reader.CurrentState;
                if (final)
                {
                    if (remaining != 0)
                        throw new JsonException("The UIGF JSON is truncated.");
                    break;
                }
                if (remaining == buffer.Length)
                {
                    throw Error(
                        ArchivePackageErrorCode.ResourceLimitExceeded,
                        "A UIGF JSON token exceeds the streaming limit.",
                        path);
                }
            }
        }
        catch (JsonException exception)
        {
            throw new ArchivePackageException(
                ArchivePackageErrorCode.InvalidPackage,
                "The embedded UIGF 4.2 document is invalid.",
                path,
                exception);
        }

        if (version != "v4.2" || accountCount != 1 || !sawList ||
            uid != identity.NaturalIdentity.Uid)
        {
            throw Error(ArchivePackageErrorCode.InvalidReference,
                "The embedded UIGF identity or structure conflicts with the catalog.",
                path);
        }
        return new UigfValidation(
            accountReference,
            sequence.Complete());
    }

    private async Task<DomainCounts> ValidateDomainAsync(
        ZipArchiveEntry entry,
        string kind,
        ArchiveCatalog catalog,
        CancellationToken cancellationToken)
    {
        HashSet<Guid> accounts = catalog.Archives.SelectMany(item => item.Accounts)
            .Select(item => item.AccountReference).ToHashSet();
        if (kind == "character-weapon-history")
        {
            ArchiveCharacterWeaponDataset value =
                await ReadJsonAsync<ArchiveCharacterWeaponDataset>(
                    entry, limits.MaximumEntryBytes, cancellationToken);
            ValidateDomainHeader(value.Format, value.FormatVersion,
                value.ArchiveReference, entry.FullName, catalog);
            if (value.Accounts.Select(item => item.AccountReference).Distinct().Count() !=
                    value.Accounts.Count ||
                value.Accounts.Any(item =>
                    !accounts.Contains(item.AccountReference) ||
                    item.ItemObservations.Any(fact =>
                        fact.GameAccountId != item.AccountReference) ||
                    item.ManualAcquisitions.Any(fact =>
                        fact.Acquisition.GameAccountId != item.AccountReference) ||
                    item.ManualRevisions.Any(revision =>
                        (revision.Before is not null &&
                            revision.Before.GameAccountId != item.AccountReference) ||
                        (revision.After is not null &&
                            revision.After.GameAccountId != item.AccountReference))))
            {
                throw Error(ArchivePackageErrorCode.InvalidReference,
                    "The character/weapon dataset contains an invalid account reference.",
                    entry.FullName);
            }
            return new DomainCounts(
                value.Accounts.Sum(item =>
                    item.ItemObservations.Count + item.ManualAcquisitions.Count),
                value.Accounts.Sum(item => item.ManualRevisions.Count));
        }
        if (kind == "challenge-history")
        {
            ArchiveChallengeDataset value = await ReadJsonAsync<ArchiveChallengeDataset>(
                entry, limits.MaximumEntryBytes, cancellationToken);
            ValidateDomainHeader(value.Format, value.FormatVersion,
                value.ArchiveReference, entry.FullName, catalog);
            if (value.Accounts.Select(item => item.AccountReference).Distinct().Count() !=
                    value.Accounts.Count ||
                value.Accounts.Any(item =>
                    !accounts.Contains(item.AccountReference) ||
                    item.ChallengeObservations.Any(fact =>
                        fact.GameAccountId != item.AccountReference)))
            {
                throw Error(ArchivePackageErrorCode.InvalidReference,
                    "The challenge dataset contains an invalid account reference.",
                    entry.FullName);
            }
            return new DomainCounts(
                value.Accounts.Sum(item => item.ChallengeObservations.Count),
                0);
        }
        return new DomainCounts(0, 0);
    }

    private static void ValidateDomainHeader(
        string format,
        string version,
        Guid archiveReference,
        string path,
        ArchiveCatalog catalog)
    {
        if (format != "furina-domain-history" || version != "1.0" ||
            !catalog.Archives.Any(item => item.ArchiveReference == archiveReference) ||
            !path.Contains(archiveReference.ToString("D"), StringComparison.Ordinal))
        {
            throw Error(ArchivePackageErrorCode.InvalidReference,
                "The domain dataset header conflicts with its catalog or path.", path);
        }
    }

    private async Task<T> ReadJsonAsync<T>(
        ZipArchiveEntry entry,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        byte[] bytes = await ReadBoundedAsync(entry, maximumBytes, cancellationToken);
        try
        {
            return JsonSerializer.Deserialize<T>(bytes, ArchivePortableJson.Display)
                ?? throw new JsonException("The JSON payload is null.");
        }
        catch (JsonException exception)
        {
            throw new ArchivePackageException(
                ArchivePackageErrorCode.InvalidPackage,
                $"Archive JSON is invalid: {entry.FullName}.",
                entry.FullName,
                exception);
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(
        ZipArchiveEntry entry,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        if (entry.Length > maximumBytes || entry.Length > int.MaxValue)
        {
            throw Error(ArchivePackageErrorCode.ResourceLimitExceeded,
                $"Archive entry exceeds its read limit: {entry.FullName}.",
                entry.FullName);
        }
        await using Stream stream = entry.Open();
        using var buffer = new MemoryStream((int)entry.Length);
        await stream.CopyToAsync(buffer, 64 * 1024, cancellationToken);
        return buffer.ToArray();
    }

    private static void RequireCount(
        ArchiveDatasetDescriptor dataset,
        string name,
        long actual)
    {
        if (!dataset.RecordCounts.TryGetValue(name, out long expected) ||
            expected != actual)
        {
            throw Error(ArchivePackageErrorCode.RecordCountMismatch,
                $"Dataset count mismatch for {dataset.DatasetReference}/{name}.");
        }
    }

    private static void EnsureCount(long actual, long maximum, string label)
    {
        if (actual > maximum)
        {
            throw Error(ArchivePackageErrorCode.ResourceLimitExceeded,
                $"{label} exceed the Archive limit.");
        }
    }

    private void ValidatePath(string path)
    {
        if (string.IsNullOrEmpty(path) ||
            path.StartsWith("/", StringComparison.Ordinal) ||
            path.Contains('\\') ||
            Encoding.UTF8.GetByteCount(path) > limits.MaximumPathBytes ||
            path.Split('/').Any(segment => segment.Length == 0 ||
                segment is "." or ".."))
        {
            throw Error(ArchivePackageErrorCode.InvalidPath,
                $"Invalid Archive path: {path}.", path);
        }
    }

    private static Guid ParseAccountReferenceFromUigfPath(string path)
    {
        string[] segments = path.Split('/');
        if (segments.Length != 4 || segments[0] != "datasets" ||
            segments[1] != "gacha" || segments[3] != "gacha.uigf.json" ||
            !Guid.TryParseExact(segments[2], "D", out Guid result) ||
            result == Guid.Empty)
        {
            throw Error(ArchivePackageErrorCode.InvalidPath,
                "The embedded UIGF path is invalid.", path);
        }
        return result;
    }

    private static void ValidateGachaFactReference(
        FurinaChronicle.Core.Gacha.GachaRecord record,
        Guid accountReference,
        string externalRecordId,
        string path)
    {
        if (record.GameAccountId != accountReference ||
            !string.Equals(
                record.ExternalRecordId,
                externalRecordId,
                StringComparison.Ordinal))
        {
            throw Error(
                ArchivePackageErrorCode.InvalidReference,
                "A Gacha history payload conflicts with its account or fact reference.",
                path);
        }
    }

    private static void ValidateStandardOneToOne(
        IReadOnlyDictionary<Guid, SequenceSummary> history,
        IReadOnlyDictionary<Guid, SequenceSummary> uigf)
    {
        byte[] emptyHash = SHA256.HashData([]);
        foreach (Guid account in history.Keys.Concat(uigf.Keys).Distinct())
        {
            SequenceSummary left = history.TryGetValue(account, out var historyValue)
                ? historyValue
                : new SequenceSummary(0, emptyHash);
            SequenceSummary right = uigf.TryGetValue(account, out var uigfValue)
                ? uigfValue
                : new SequenceSummary(0, emptyHash);
            if (left.Count != right.Count ||
                !CryptographicOperations.FixedTimeEquals(left.Hash, right.Hash))
            {
                throw Error(ArchivePackageErrorCode.RecordCountMismatch,
                    "UIGF facts and standard Gacha extensions are not one-to-one.");
            }
        }
    }

    private static ZipArchiveEntry Require(
        IReadOnlyDictionary<string, ZipArchiveEntry> entries,
        string path) =>
        entries.TryGetValue(path, out ZipArchiveEntry? entry)
            ? entry
            : throw Error(ArchivePackageErrorCode.MissingEntry,
                $"Required Archive entry is missing: {path}.", path);

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static ArchivePackageException Error(
        ArchivePackageErrorCode code,
        string message,
        string? path = null) => new(code, message, path);

    private readonly record struct DomainCounts(long Facts, long Revisions);

    private sealed record GachaHistoryValidation(
        long Facts,
        long Revisions,
        IReadOnlyDictionary<Guid, SequenceSummary> StandardSequences);

    private sealed record UigfValidation(
        Guid AccountReference,
        SequenceSummary Sequence);

    private sealed record SequenceSummary(long Count, byte[] Hash);

    private sealed class SequenceAccumulator : IDisposable
    {
        private readonly IncrementalHash hash =
            IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private bool completed;

        public long Count { get; private set; }

        public void Append(string key)
        {
            if (completed) throw new InvalidOperationException();
            byte[] bytes = Encoding.UTF8.GetBytes(key);
            Span<byte> length = stackalloc byte[sizeof(int)];
            BitConverter.TryWriteBytes(length, bytes.Length);
            hash.AppendData(length);
            hash.AppendData(bytes);
            Count++;
        }

        public SequenceSummary Complete()
        {
            if (completed) throw new InvalidOperationException();
            completed = true;
            byte[] result = hash.GetHashAndReset();
            hash.Dispose();
            return new SequenceSummary(Count, result);
        }

        public void Dispose()
        {
            if (!completed)
            {
                hash.Dispose();
                completed = true;
            }
        }
    }
}
