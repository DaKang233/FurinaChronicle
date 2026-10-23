// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Infrastructure.Gacha.Portable;
using FurinaChronicle.Services.Archives.Portable;
using FurinaChronicle.Services.Gacha.History;
using FurinaChronicle.Services.HistoryDomains;
using FurinaChronicle.Services.HistoryDomains.Portable;

namespace FurinaChronicle.Infrastructure.Archives.Portable;

public sealed class ArchivePackageWriter(ArchivePortableLimits? limits = null)
    : IArchivePackageWriter
{
    internal const string ManifestPath = "manifest.json";
    internal const string CatalogPath = "datasets/catalog.v1.json";
    internal const string GachaHistoryPath = "datasets/gacha/history.v1.ndjson";
    internal const string SettingsPath = "settings/portable.v1.json";
    internal const string ChecksumsPath = "checksums.sha256";
    internal const string Format = "furina-archive";

    private static readonly HashSet<string> GachaTypes =
        ["100", "200", "301", "302", "400", "500"];
    private static readonly HashSet<string> UigfGachaTypes =
        ["100", "200", "301", "302", "500"];
    private readonly ArchivePortableLimits limits = limits ?? new();

    public async Task<ArchiveWriteResult> WriteAsync(
        Stream destination,
        IArchiveExportSnapshot snapshot,
        ArchiveGeneratorInfo generator,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(generator);
        if (!destination.CanWrite)
        {
            throw new ArgumentException("The destination is not writable.", nameof(destination));
        }

        ValidateCatalog(snapshot.Catalog);
        var entries = new Dictionary<string, EntryDescriptor>(StringComparer.Ordinal);
        var datasets = new List<ArchiveDatasetDescriptor>();
        long gachaFacts = 0;
        long gachaRevisions = 0;
        long domainFacts = 0;
        long domainRevisions = 0;

        await using var packageHash = new HashingWriteStream(destination);
        using (var zip = new ZipArchive(packageHash, ZipArchiveMode.Create, true))
        {
            EntryDescriptor catalog = await WriteJsonEntryAsync(
                zip,
                CatalogPath,
                new ArchiveCatalogWire(
                    "furina-archive-catalog",
                    "1.0",
                    snapshot.Catalog.RoleIdentities,
                    snapshot.Catalog.Archives),
                cancellationToken);
            AddEntry(entries, catalog);
            datasets.Add(new ArchiveDatasetDescriptor(
                "catalog", "archive-catalog", "furina-archive-catalog", "1.0", true,
                [CatalogPath],
                new Dictionary<string, long>
                {
                    ["archives"] = snapshot.Catalog.Archives.Count,
                    ["accounts"] = snapshot.Catalog.AccountCount,
                    ["role_identities"] = snapshot.Catalog.RoleIdentities.Count,
                },
                catalog.Length));

            var uigfPaths = new List<string>();
            long standardFacts = 0;
            foreach (ArchiveCatalogAccount account in AllAccounts(snapshot.Catalog))
            {
                if (account.RoleIdentityReference is not Guid roleReference)
                {
                    continue;
                }
                ArchiveCatalogRoleIdentity identity = snapshot.Catalog.RoleIdentities
                    .Single(item => item.RoleIdentityReference == roleReference);
                string path = GetUigfPath(account.AccountReference);
                long count = 0;
                EntryDescriptor entry = await WriteEntryAsync(
                    zip,
                    path,
                    async stream => count = await WriteUigfAsync(
                        stream,
                        snapshot,
                        account.AccountReference,
                        identity.NaturalIdentity.Uid,
                        cancellationToken),
                    cancellationToken);
                AddEntry(entries, entry);
                uigfPaths.Add(path);
                standardFacts = checked(standardFacts + count);
            }
            if (uigfPaths.Count > 0)
            {
                datasets.Add(new ArchiveDatasetDescriptor(
                    "gacha-uigf-current", "gacha-current", "uigf", "v4.2", false,
                    uigfPaths,
                    new Dictionary<string, long> { ["current_facts"] = standardFacts },
                    uigfPaths.Sum(path => entries[path].Length)));
            }

            EntryDescriptor history = await WriteEntryAsync(
                zip,
                GachaHistoryPath,
                async stream => (gachaFacts, gachaRevisions) =
                    await WriteGachaHistoryAsync(stream, snapshot, cancellationToken),
                cancellationToken);
            AddEntry(entries, history);
            datasets.Add(new ArchiveDatasetDescriptor(
                "gacha-history", "gacha-history", "furina-archive-gacha-history",
                "1.0", true, [GachaHistoryPath],
                new Dictionary<string, long>
                {
                    ["current_facts"] = gachaFacts,
                    ["revisions"] = gachaRevisions,
                },
                history.Length));

            foreach (ArchiveCatalogArchive archive in snapshot.Catalog.Archives)
            {
                DomainWriteResult characterWeapon = await WriteCharacterWeaponAsync(
                    zip, snapshot, archive, cancellationToken);
                if (characterWeapon.Entry is not null)
                {
                    AddEntry(entries, characterWeapon.Entry);
                    domainFacts += characterWeapon.Facts;
                    domainRevisions += characterWeapon.Revisions;
                    datasets.Add(ToDomainDescriptor(
                        archive.ArchiveReference,
                        "character-weapon-history",
                        characterWeapon));
                }
                DomainWriteResult challenges = await WriteChallengesAsync(
                    zip, snapshot, archive, cancellationToken);
                if (challenges.Entry is not null)
                {
                    AddEntry(entries, challenges.Entry);
                    domainFacts += challenges.Facts;
                    datasets.Add(ToDomainDescriptor(
                        archive.ArchiveReference,
                        "challenge-history",
                        challenges));
                }
            }

            EntryDescriptor settings = await WriteJsonEntryAsync(
                zip,
                SettingsPath,
                new ArchiveSettingsWire(
                    "furina-portable-settings",
                    "1.0",
                    snapshot.Settings.RequireUniqueArchiveNames,
                    snapshot.Settings.Archives),
                cancellationToken);
            AddEntry(entries, settings);
            datasets.Add(new ArchiveDatasetDescriptor(
                "portable-settings", "portable-settings", "furina-portable-settings",
                "1.0", true, [SettingsPath],
                new Dictionary<string, long> { ["archives"] = snapshot.Settings.Archives.Count },
                settings.Length));

            ArchiveManifest manifest = CreateManifest(snapshot, generator, datasets);
            EntryDescriptor manifestEntry = await WriteJsonEntryAsync(
                zip, ManifestPath, manifest, cancellationToken);
            if (manifestEntry.Length > limits.MaximumManifestBytes)
            {
                throw Error(ArchivePackageErrorCode.ResourceLimitExceeded,
                    "manifest.json exceeds the Archive limit.", ManifestPath);
            }
            AddEntry(entries, manifestEntry);

            EntryDescriptor checksums = await WriteEntryAsync(
                zip,
                ChecksumsPath,
                stream => WriteChecksumsAsync(stream, entries, cancellationToken),
                cancellationToken,
                false);
            if (checksums.Length > limits.MaximumChecksumsBytes)
            {
                throw Error(ArchivePackageErrorCode.ResourceLimitExceeded,
                    "checksums.sha256 exceeds the Archive limit.", ChecksumsPath);
            }
            EnsureTotal(entries.Values.Sum(item => item.Length) + checksums.Length);
        }

        await packageHash.FlushAsync(cancellationToken);
        string packageSha256 = packageHash.CompleteHash();
        ArchiveManifest finalManifest = CreateManifest(snapshot, generator, datasets);
        var summary = new ArchivePackageSummary(
            finalManifest, snapshot.Catalog, snapshot.Settings,
            gachaFacts, gachaRevisions, domainFacts, domainRevisions,
            entries.Values.Sum(item => item.Length), []);
        return new ArchiveWriteResult(summary, packageSha256);
    }

    private static ArchiveManifest CreateManifest(
        IArchiveExportSnapshot snapshot,
        ArchiveGeneratorInfo generator,
        IReadOnlyList<ArchiveDatasetDescriptor> datasets) => new(
            Format,
            ArchiveFormatVersion.Current.ToString(),
            snapshot.GeneratedAt,
            generator,
            new ArchiveDeclaredScope(
                snapshot.Scope.Kind,
                snapshot.Catalog.Archives
                    .Select(item => item.ArchiveReference)
                    .ToArray(),
                "supported_logical_scope"),
            datasets,
            SettingsPath,
            ChecksumsPath);

    private static IEnumerable<ArchiveCatalogAccount> AllAccounts(
        ArchiveCatalog catalog) =>
        catalog.Archives.SelectMany(item => item.Accounts);

    private async Task<(long Facts, long Revisions)> WriteGachaHistoryAsync(
        Stream destination,
        IArchiveExportSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        long facts = 0;
        long revisions = 0;
        foreach (ArchiveCatalogAccount account in AllAccounts(snapshot.Catalog))
        {
            await foreach (GachaRecord record in snapshot.ReadGachaRecordsAsync(
                account.AccountReference,
                cancellationToken))
            {
                bool standard = account.RoleIdentityReference is not null &&
                    IsStandardRecord(record);
                ArchiveGachaHistoryLine line = standard
                    ? new ArchiveGachaHistoryLine(
                        "standard_extension",
                        account.AccountReference,
                        record.ExternalRecordId,
                        null,
                        new ArchiveGachaStandardExtension(
                            record.Time.UtcDateTime.Ticks,
                            checked((int)record.Time.Offset.TotalMinutes),
                            record.Provenance),
                        null,
                        null)
                    : new ArchiveGachaHistoryLine(
                        "nonstandard_current",
                        account.AccountReference,
                        record.ExternalRecordId,
                        GetReasonCodes(
                            record,
                            account.RoleIdentityReference is not null),
                        null,
                        record,
                        null);
                await WriteLineAsync(destination, line, cancellationToken);
                if (++facts > limits.MaximumGachaFactCount)
                {
                    throw Error(ArchivePackageErrorCode.ResourceLimitExceeded,
                        "The Gacha fact count exceeds the Archive limit.",
                        GachaHistoryPath);
                }
            }

            await foreach (GachaRevisionItem revision in snapshot
                .ReadGachaRevisionsAsync(account.AccountReference, cancellationToken))
            {
                var portable = new ArchiveGachaRevision(
                    revision.RevisionId,
                    revision.ChangeKind,
                    revision.BeforeVersion,
                    revision.AfterVersion,
                    revision.Before,
                    revision.After,
                    revision.CreatedAt);
                await WriteLineAsync(
                    destination,
                    new ArchiveGachaHistoryLine(
                        "revision",
                        account.AccountReference,
                        revision.Reference.ExternalRecordId,
                        null,
                        null,
                        null,
                        portable),
                    cancellationToken);
                if (++revisions > limits.MaximumGachaRevisionCount)
                {
                    throw Error(ArchivePackageErrorCode.ResourceLimitExceeded,
                        "The Gacha revision count exceeds the Archive limit.",
                        GachaHistoryPath);
                }
            }
        }
        return (facts, revisions);
    }

    private static async Task<long> WriteUigfAsync(
        Stream destination,
        IArchiveExportSnapshot snapshot,
        Guid accountReference,
        string uid,
        CancellationToken cancellationToken)
    {
        long count = 0;
        await using var writer = new Utf8JsonWriter(destination);
        writer.WriteStartObject();
        writer.WritePropertyName("info");
        writer.WriteStartObject();
        writer.WriteNumber("export_timestamp", snapshot.GeneratedAt.ToUnixTimeSeconds());
        writer.WriteString("export_app", "FurinaChronicle");
        writer.WriteString("export_app_version", "archive-1.0");
        writer.WriteString("version", "v4.2");
        writer.WriteEndObject();
        writer.WritePropertyName("hk4e");
        writer.WriteStartArray();
        writer.WriteStartObject();
        writer.WriteString("uid", uid);
        writer.WriteNumber("timezone", 0);
        writer.WritePropertyName("list");
        writer.WriteStartArray();
        await foreach (GachaRecord record in snapshot.ReadGachaRecordsAsync(
            accountReference,
            cancellationToken))
        {
            if (!IsStandardRecord(record))
            {
                continue;
            }
            writer.WriteStartObject();
            writer.WriteString("uigf_gacha_type", record.UigfGachaType);
            writer.WriteString("gacha_type", record.GachaType);
            writer.WriteString("item_id", record.ItemId);
            writer.WriteString("count", record.Count.ToString(CultureInfo.InvariantCulture));
            writer.WriteString("time", record.Time.ToString(
                "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            writer.WriteString("name", record.ItemName ?? string.Empty);
            writer.WriteString("item_type", record.ItemType ?? string.Empty);
            writer.WriteString("rank_type", record.RankType?.ToString(
                CultureInfo.InvariantCulture) ?? string.Empty);
            writer.WriteString("id", record.ExternalRecordId);
            writer.WriteEndObject();
            count++;
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.WriteEndArray();
        writer.WriteEndObject();
        await writer.FlushAsync(cancellationToken);
        return count;
    }

    private async Task<DomainWriteResult> WriteCharacterWeaponAsync(
        ZipArchive zip,
        IArchiveExportSnapshot snapshot,
        ArchiveCatalogArchive archive,
        CancellationToken cancellationToken)
    {
        var accounts = new List<ArchiveCharacterWeaponAccount>();
        long facts = 0;
        long revisions = 0;
        foreach (ArchiveCatalogAccount account in archive.Accounts)
        {
            IReadOnlyList<ItemStateObservation> observations = await CollectAsync(
                snapshot.ReadItemObservationsAsync(account.AccountReference, cancellationToken),
                cancellationToken);
            IReadOnlyList<ManualAcquisitionState> acquisitions = await CollectAsync(
                snapshot.ReadManualAcquisitionsAsync(account.AccountReference, cancellationToken),
                cancellationToken);
            IReadOnlyList<ManualAcquisitionRevisionItem> accountRevisions =
                await CollectAsync(
                    snapshot.ReadManualAcquisitionRevisionsAsync(
                        account.AccountReference,
                        cancellationToken),
                    cancellationToken);
            if (observations.Count + acquisitions.Count + accountRevisions.Count == 0)
            {
                continue;
            }
            facts = checked(facts + observations.Count + acquisitions.Count);
            revisions = checked(revisions + accountRevisions.Count);
            EnsureDomainCount(facts + revisions);
            accounts.Add(new ArchiveCharacterWeaponAccount(
                account.AccountReference,
                observations,
                acquisitions,
                accountRevisions.Select(ToPortableRevision).ToArray()));
        }
        if (accounts.Count == 0)
        {
            return new DomainWriteResult(null, 0, 0);
        }
        string path = GetDomainPath(archive.ArchiveReference, "character-weapon-history");
        EntryDescriptor entry = await WriteJsonEntryAsync(
            zip,
            path,
            new ArchiveCharacterWeaponDataset(
                "furina-domain-history", "1.0", archive.ArchiveReference, accounts),
            cancellationToken);
        return new DomainWriteResult(entry, facts, revisions);
    }

    private static PortableManualAcquisitionRevision ToPortableRevision(
        ManualAcquisitionRevisionItem revision) => new(
        revision.RevisionId,
        revision.ChangeKind,
        revision.BeforeVersion,
        revision.AfterVersion,
        revision.Before,
        revision.After,
        revision.CreatedAt);

    private async Task<DomainWriteResult> WriteChallengesAsync(
        ZipArchive zip,
        IArchiveExportSnapshot snapshot,
        ArchiveCatalogArchive archive,
        CancellationToken cancellationToken)
    {
        var accounts = new List<ArchiveChallengeAccount>();
        long facts = 0;
        foreach (ArchiveCatalogAccount account in archive.Accounts)
        {
            IReadOnlyList<ChallengeObservation> observations = await CollectAsync(
                snapshot.ReadChallengeObservationsAsync(
                    account.AccountReference,
                    cancellationToken),
                cancellationToken);
            if (observations.Count == 0)
            {
                continue;
            }
            facts = checked(facts + observations.Count);
            EnsureDomainCount(facts);
            accounts.Add(new ArchiveChallengeAccount(
                account.AccountReference,
                observations));
        }
        if (accounts.Count == 0)
        {
            return new DomainWriteResult(null, 0, 0);
        }
        string path = GetDomainPath(archive.ArchiveReference, "challenge-history");
        EntryDescriptor entry = await WriteJsonEntryAsync(
            zip,
            path,
            new ArchiveChallengeDataset(
                "furina-domain-history", "1.0", archive.ArchiveReference, accounts),
            cancellationToken);
        return new DomainWriteResult(entry, facts, 0);
    }

    private static ArchiveDatasetDescriptor ToDomainDescriptor(
        Guid archiveReference,
        string kind,
        DomainWriteResult result) => new(
            $"{kind}-{archiveReference:D}".ToLowerInvariant(),
            kind,
            "furina-domain-history",
            "1.0",
            false,
            [result.Entry!.Path],
            new Dictionary<string, long>
            {
                ["facts"] = result.Facts,
                ["revisions"] = result.Revisions,
            },
            result.Entry.Length);

    private async Task WriteLineAsync(
        Stream destination,
        ArchiveGachaHistoryLine line,
        CancellationToken cancellationToken)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(
            line,
            ArchivePortableJson.Compact);
        if (bytes.Length > limits.MaximumNdjsonLineBytes)
        {
            throw Error(ArchivePackageErrorCode.ResourceLimitExceeded,
                "A Gacha history line exceeds the Archive limit.",
                GachaHistoryPath);
        }
        await destination.WriteAsync(bytes, cancellationToken);
        await destination.WriteAsync("\n"u8.ToArray(), cancellationToken);
    }

    private async Task<EntryDescriptor> WriteJsonEntryAsync<T>(
        ZipArchive zip,
        string path,
        T value,
        CancellationToken cancellationToken) =>
        await WriteEntryAsync(
            zip,
            path,
            stream => JsonSerializer.SerializeAsync(
                stream,
                value,
                ArchivePortableJson.Display,
                cancellationToken),
            cancellationToken);

    private async Task<EntryDescriptor> WriteEntryAsync(
        ZipArchive zip,
        string path,
        Func<Stream, Task> write,
        CancellationToken cancellationToken,
        bool enforceEntryLimit = true)
    {
        ValidatePath(path);
        ZipArchiveEntry entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        await using Stream target = entry.Open();
        await using var hashing = new HashingWriteStream(target);
        await write(hashing);
        await hashing.FlushAsync(cancellationToken);
        if (enforceEntryLimit && hashing.ByteCount > limits.MaximumEntryBytes)
        {
            throw Error(ArchivePackageErrorCode.ResourceLimitExceeded,
                $"Archive entry exceeds the limit: {path}.", path);
        }
        return new EntryDescriptor(path, hashing.ByteCount, hashing.CompleteHash());
    }

    private static async Task WriteChecksumsAsync(
        Stream destination,
        IReadOnlyDictionary<string, EntryDescriptor> entries,
        CancellationToken cancellationToken)
    {
        foreach (EntryDescriptor entry in entries.Values
            .OrderBy(item => item.Path, StringComparer.Ordinal))
        {
            byte[] line = Encoding.UTF8.GetBytes($"{entry.Sha256}  {entry.Path}\n");
            await destination.WriteAsync(line, cancellationToken);
        }
    }

    private async Task<IReadOnlyList<T>> CollectAsync<T>(
        IAsyncEnumerable<T> source,
        CancellationToken cancellationToken)
    {
        var result = new List<T>();
        await foreach (T item in source.WithCancellation(cancellationToken))
        {
            result.Add(item);
            EnsureDomainCount(result.Count);
        }
        return result;
    }

    private void ValidateCatalog(ArchiveCatalog catalog)
    {
        if (catalog.Archives.Count > limits.MaximumArchiveCount ||
            catalog.AccountCount > limits.MaximumAccountCount)
        {
            throw Error(ArchivePackageErrorCode.ResourceLimitExceeded,
                "The Archive catalog exceeds its parent-object limit.");
        }
        Guid[] archives = catalog.Archives
            .Select(item => item.ArchiveReference).ToArray();
        Guid[] accounts = AllAccounts(catalog)
            .Select(item => item.AccountReference).ToArray();
        HashSet<Guid> identities = catalog.RoleIdentities
            .Select(item => item.RoleIdentityReference).ToHashSet();
        if (archives.Any(id => id == Guid.Empty) ||
            archives.Distinct().Count() != archives.Length ||
            catalog.Archives.Any(item => item.Name is null) ||
            accounts.Any(id => id == Guid.Empty) ||
            accounts.Distinct().Count() != accounts.Length ||
            identities.Count != catalog.RoleIdentities.Count ||
            identities.Contains(Guid.Empty))
        {
            throw Error(ArchivePackageErrorCode.InvalidPackage,
                "The Archive catalog contains invalid or duplicate identities.");
        }
        if (AllAccounts(catalog).Any(account =>
            account.RoleIdentityReference is Guid reference &&
            !identities.Contains(reference)))
        {
            throw Error(ArchivePackageErrorCode.InvalidReference,
                "An account references an unknown role identity.");
        }
    }

    private static bool IsStandardRecord(GachaRecord record) =>
        record.GameAccountId != Guid.Empty &&
        !string.IsNullOrWhiteSpace(record.ExternalRecordId) &&
        record.ExternalRecordId.Length <= 19 &&
        record.ExternalRecordId.All(char.IsAsciiDigit) &&
        !string.IsNullOrWhiteSpace(record.ItemId) &&
        record.GachaType is string gachaType && GachaTypes.Contains(gachaType) &&
        record.UigfGachaType is string uigfType && UigfGachaTypes.Contains(uigfType) &&
        record.Count > 0 &&
        record.RankType is null or >= 3 and <= 5;

    private static IReadOnlyList<string> GetReasonCodes(
        GachaRecord record,
        bool resolvedAccount)
    {
        var result = new List<string>();
        if (!resolvedAccount) result.Add("unresolved_account");
        if (string.IsNullOrWhiteSpace(record.ItemId)) result.Add("missing_item_id");
        if (string.IsNullOrWhiteSpace(record.GachaType) ||
            !GachaTypes.Contains(record.GachaType)) result.Add("invalid_gacha_type");
        if (string.IsNullOrWhiteSpace(record.UigfGachaType) ||
            !UigfGachaTypes.Contains(record.UigfGachaType))
            result.Add("invalid_uigf_gacha_type");
        if (string.IsNullOrWhiteSpace(record.ExternalRecordId) ||
            record.ExternalRecordId.Length > 19 ||
            !record.ExternalRecordId.All(char.IsAsciiDigit))
            result.Add("invalid_external_record_id");
        if (record.Count <= 0 || record.RankType is not null and (< 3 or > 5))
            result.Add("invalid_standard_value");
        return result.Count == 0 ? ["not_uigf_compatible"] : result;
    }

    internal static string GetUigfPath(Guid accountReference) =>
        $"datasets/gacha/{accountReference:D}/gacha.uigf.json".ToLowerInvariant();

    internal static string GetDomainPath(Guid archiveReference, string kind) =>
        $"datasets/domain/{archiveReference:D}/{kind}.v1.json".ToLowerInvariant();

    private void AddEntry(
        IDictionary<string, EntryDescriptor> entries,
        EntryDescriptor entry)
    {
        if (!entries.TryAdd(entry.Path, entry))
        {
            throw Error(ArchivePackageErrorCode.DuplicatePath,
                $"Duplicate Archive path: {entry.Path}.", entry.Path);
        }
        if (entries.Count + 1 > limits.MaximumEntryCount)
        {
            throw Error(ArchivePackageErrorCode.ResourceLimitExceeded,
                "The Archive entry count exceeds the limit.");
        }
    }

    private void EnsureTotal(long bytes)
    {
        if (bytes > limits.MaximumUncompressedBytes)
        {
            throw Error(ArchivePackageErrorCode.ResourceLimitExceeded,
                "The Archive uncompressed size exceeds the limit.");
        }
    }

    private void EnsureDomainCount(long count)
    {
        if (count > limits.MaximumDomainRecordCount)
        {
            throw Error(ArchivePackageErrorCode.ResourceLimitExceeded,
                "A domain dataset exceeds the Archive record limit.");
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

    private static ArchivePackageException Error(
        ArchivePackageErrorCode code,
        string message,
        string? path = null) => new(code, message, path);

    private sealed record EntryDescriptor(string Path, long Length, string Sha256);

    private sealed record DomainWriteResult(
        EntryDescriptor? Entry,
        long Facts,
        long Revisions);
}
