// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.History;
using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Services.Archives.Portable;
using FurinaChronicle.Services.Gacha.History;
using FurinaChronicle.Services.HistoryDomains;
using FurinaChronicle.Services.HistoryDomains.Portable;
using SQLite;

namespace FurinaChronicle.Infrastructure.Archives.Portable;

public sealed record ArchiveRestoreStorageOptions(string RootDirectory)
{
    public static ArchiveRestoreStorageOptions Default { get; } = new(
        Path.Combine(Path.GetTempPath(), "FurinaChronicle", "archive-restore"));
}

public sealed class SqliteArchiveRestoreInputStager(
    ArchivePackageReader reader,
    ArchiveRestoreStorageOptions? storageOptions = null,
    ArchivePortableLimits? limits = null) : IArchiveRestoreInputStager
{
    private readonly ArchiveRestoreStorageOptions storage =
        storageOptions ?? ArchiveRestoreStorageOptions.Default;
    private readonly ArchivePortableLimits limits = limits ?? new();

    public async Task<IArchiveRestoreStagedPackage> StageAsync(
        Stream source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead)
        {
            throw new ArgumentException("The Archive input is not readable.", nameof(source));
        }

        string root = Path.GetFullPath(storage.RootDirectory);
        Directory.CreateDirectory(root);
        string sessionDirectory = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sessionDirectory);
        string packagePath = Path.Combine(sessionDirectory, "input.fca");
        string stagingPath = Path.Combine(sessionDirectory, "staging.db");
        StagingSink? sink = null;
        try
        {
            (string fingerprint, long containerLength) = await CopyAndHashAsync(
                source,
                packagePath,
                cancellationToken);
            sink = new StagingSink(stagingPath);
            ArchivePackageSummary summary;
            await using (var package = new FileStream(
                packagePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                summary = await reader.ReadAndValidateAsync(
                    package,
                    sink,
                    cancellationToken);
            }
            sink.Commit();
            sink.Dispose();
            sink = null;
            File.Delete(packagePath);
            return new StagedPackage(
                sessionDirectory,
                stagingPath,
                fingerprint,
                containerLength,
                summary);
        }
        catch
        {
            sink?.Dispose();
            DeleteSessionDirectory(root, sessionDirectory);
            throw;
        }
    }

    private async Task<(string Fingerprint, long Length)> CopyAndHashAsync(
        Stream source,
        string packagePath,
        CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var destination = new FileStream(
            packagePath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] buffer = new byte[64 * 1024];
        long total = 0;
        while (true)
        {
            int read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            total = checked(total + read);
            if (total > limits.MaximumUncompressedBytes)
            {
                throw new ArchivePackageException(
                    ArchivePackageErrorCode.ResourceLimitExceeded,
                    "The Archive container exceeds the staging input limit.");
            }
            hash.AppendData(buffer, 0, read);
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        await destination.FlushAsync(cancellationToken);
        return (
            Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(),
            total);
    }

    internal static void DeleteSessionDirectory(string root, string sessionDirectory)
    {
        string fullRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string fullSession = Path.GetFullPath(sessionDirectory);
        if (!fullSession.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The Archive restore session escaped its configured temporary root.");
        }
        if (Directory.Exists(fullSession))
        {
            Directory.Delete(fullSession, recursive: true);
        }
    }

    private sealed class StagingSink : IArchivePackageContentSink, IDisposable
    {
        private readonly SQLiteConnection connection;
        private bool completed;

        public StagingSink(string path)
        {
            connection = new SQLiteConnection(
                path,
                SQLiteOpenFlags.ReadWrite |
                SQLiteOpenFlags.Create |
                SQLiteOpenFlags.FullMutex,
                true);
            try
            {
                connection.Execute("PRAGMA synchronous = FULL;");
                connection.CreateTable<StagedFactRow>();
                connection.CreateTable<StandardExtensionRow>();
                connection.BeginTransaction();
            }
            catch
            {
                connection.Close();
                connection.Dispose();
                throw;
            }
        }

        public void SetMetadata(ArchiveCatalog catalog, ArchivePortableSettings settings)
        {
        }

        public void AddStandardExtension(
            Guid accountReference,
            string externalRecordId,
            ArchiveGachaStandardExtension extension)
        {
            InsertOrThrow(new StandardExtensionRow
            {
                AccountReference = accountReference.ToString("D"),
                ExternalRecordId = externalRecordId,
                OccurredAtUtcTicks = extension.OccurredAtUtcTicks,
                OccurredAtOffsetMinutes = extension.OccurredAtOffsetMinutes,
                ProvenanceJson = JsonSerializer.Serialize(
                    extension.Provenance,
                    ArchivePortableJson.Compact),
            });
        }

        public void AddGachaRecord(GachaRecord record) => AddFact(
            ArchiveRestoreFactKind.GachaCurrent,
            record.GameAccountId,
            record.ExternalRecordId,
            record,
            ArchiveRestoreSemanticDigest.ForGacha(record));

        public void AddGachaRevision(
            Guid accountReference,
            string externalRecordId,
            ArchiveGachaRevision revision)
        {
            var value = new GachaRevisionItem(
                revision.RevisionReference,
                Guid.Empty,
                new GachaFactReference(accountReference, externalRecordId),
                revision.ChangeKind,
                revision.BeforeVersion,
                revision.AfterVersion,
                revision.Before,
                revision.After,
                revision.CreatedAt);
            AddFact(
                ArchiveRestoreFactKind.GachaRevision,
                accountReference,
                revision.RevisionReference.ToString("D"),
                value,
                ArchiveRestoreSemanticDigest.ForGachaRevision(value));
        }

        public void AddUigfRecord(Guid accountReference, ArchiveUigfRecord record)
        {
            StandardExtensionRow extension = connection.FindWithQuery<StandardExtensionRow>(
                "SELECT * FROM StandardExtensionRow WHERE AccountReference = ? AND ExternalRecordId = ?;",
                accountReference.ToString("D"),
                record.Id) ?? throw new ArchivePackageException(
                    ArchivePackageErrorCode.InvalidReference,
                    "A standard UIGF fact has no matching history extension.");
            if (!int.TryParse(record.Count, NumberStyles.None, CultureInfo.InvariantCulture,
                    out int count) || count <= 0 ||
                !int.TryParse(record.RankType, NumberStyles.None,
                    CultureInfo.InvariantCulture, out int rank) || rank is < 3 or > 5)
            {
                throw new ArchivePackageException(
                    ArchivePackageErrorCode.InvalidPackage,
                    "A standard UIGF fact contains an invalid count or rank.");
            }
            DateTimeOffset occurred = new(
                new DateTime(extension.OccurredAtUtcTicks, DateTimeKind.Utc));
            occurred = occurred.ToOffset(
                TimeSpan.FromMinutes(extension.OccurredAtOffsetMinutes));
            RecordProvenance provenance = JsonSerializer.Deserialize<RecordProvenance>(
                extension.ProvenanceJson,
                ArchivePortableJson.Compact) ?? throw new ArchivePackageException(
                    ArchivePackageErrorCode.InvalidPackage,
                    "A standard Gacha extension contains invalid provenance.");
            var value = new GachaRecord(
                accountReference,
                record.Id,
                record.Name,
                rank,
                occurred)
            {
                ItemId = record.ItemId,
                ItemType = record.ItemType,
                GachaType = record.GachaType,
                UigfGachaType = record.UigfGachaType,
                Count = count,
                Provenance = provenance,
            };
            AddGachaRecord(value);
        }

        public void AddCharacterWeaponDataset(ArchiveCharacterWeaponDataset dataset)
        {
            foreach (ArchiveCharacterWeaponAccount account in dataset.Accounts)
            {
                foreach (ItemStateObservation value in account.ItemObservations)
                {
                    AddFact(
                        ArchiveRestoreFactKind.ItemObservation,
                        account.AccountReference,
                        value.ObservationId.ToString(),
                        value,
                        ArchiveRestoreSemanticDigest.ForItemObservation(value));
                }
                foreach (ManualAcquisitionState value in account.ManualAcquisitions)
                {
                    AddFact(
                        ArchiveRestoreFactKind.ManualAcquisition,
                        account.AccountReference,
                        value.Acquisition.AcquisitionId.ToString(),
                        value,
                        ArchiveRestoreSemanticDigest.ForManualAcquisition(value));
                }
                foreach (PortableManualAcquisitionRevision revision in
                    account.ManualRevisions)
                {
                    var value = new ManualAcquisitionRevisionItem(
                        revision.RevisionId,
                        Guid.Empty,
                        revision.ChangeKind,
                        revision.BeforeVersion,
                        revision.AfterVersion,
                        revision.Before,
                        revision.After,
                        revision.CreatedAt);
                    AddFact(
                        ArchiveRestoreFactKind.ManualAcquisitionRevision,
                        account.AccountReference,
                        revision.RevisionId.ToString("D"),
                        value,
                        ArchiveRestoreSemanticDigest.ForManualRevision(value));
                }
            }
        }

        public void AddChallengeDataset(ArchiveChallengeDataset dataset)
        {
            foreach (ArchiveChallengeAccount account in dataset.Accounts)
            {
                foreach (ChallengeObservation value in account.ChallengeObservations)
                {
                    AddFact(
                        ArchiveRestoreFactKind.ChallengeObservation,
                        account.AccountReference,
                        value.ObservationId.ToString(),
                        value,
                        ArchiveRestoreSemanticDigest.ForChallenge(value));
                }
            }
        }

        public void Commit()
        {
            connection.Commit();
            completed = true;
        }

        public void Dispose()
        {
            if (!completed)
            {
                try { connection.Rollback(); }
                catch (SQLiteException) { }
            }
            connection.Close();
            connection.Dispose();
        }

        private void AddFact<T>(
            ArchiveRestoreFactKind kind,
            Guid accountReference,
            string stableReference,
            T value,
            string semanticDigest) => InsertOrThrow(new StagedFactRow
            {
                Kind = (int)kind,
                AccountReference = accountReference.ToString("D"),
                StableReference = stableReference,
                PayloadJson = JsonSerializer.Serialize(value, ArchivePortableJson.Compact),
                SemanticDigest = semanticDigest,
            });

        private void InsertOrThrow<T>(T value)
        {
            try { connection.Insert(value); }
            catch (SQLiteException exception)
            {
                throw new ArchivePackageException(
                    ArchivePackageErrorCode.InvalidPackage,
                    "The Archive contains a duplicate staged fact.",
                    innerException: exception);
            }
        }
    }

    private sealed class StagedPackage : IArchiveRestoreStagedPackage
    {
        private const int PageSize = 500;
        private readonly string sessionDirectory;
        private readonly string stagingPath;
        private readonly SQLiteConnection connection;
        private readonly HashSet<Guid> accounts;
        private bool disposed;

        public StagedPackage(
            string sessionDirectory,
            string stagingPath,
            string packageFingerprint,
            long containerLength,
            ArchivePackageSummary summary)
        {
            this.sessionDirectory = sessionDirectory;
            this.stagingPath = stagingPath;
            PackageFingerprint = packageFingerprint;
            ContainerLength = containerLength;
            Summary = summary;
            GeneratedAt = summary.Manifest.GeneratedAt;
            Scope = summary.Manifest.DeclaredScope.Kind == ArchiveExportScopeKind.AllArchives
                ? ArchiveExportScope.All()
                : ArchiveExportScope.Selected(
                    summary.Manifest.DeclaredScope.ArchiveReferences);
            Catalog = summary.Catalog;
            Settings = summary.Settings;
            accounts = Catalog.Archives.SelectMany(item => item.Accounts)
                .Select(item => item.AccountReference).ToHashSet();
            connection = new SQLiteConnection(
                stagingPath,
                SQLiteOpenFlags.ReadOnly | SQLiteOpenFlags.FullMutex,
                true);
        }

        public string PackageFingerprint { get; }
        public ArchivePackageSummary Summary { get; }
        public long ContainerLength { get; }
        public long StagingLength => File.Exists(stagingPath)
            ? new FileInfo(stagingPath).Length
            : 0;
        public DateTimeOffset GeneratedAt { get; }
        public ArchiveExportScope Scope { get; }
        public ArchiveCatalog Catalog { get; }
        public ArchivePortableSettings Settings { get; }

        public IAsyncEnumerable<GachaRecord> ReadGachaRecordsAsync(
            Guid accountReference,
            CancellationToken cancellationToken = default) =>
            ReadFactsAsync<GachaRecord>(
                ArchiveRestoreFactKind.GachaCurrent,
                accountReference,
                cancellationToken);

        public IAsyncEnumerable<GachaRevisionItem> ReadGachaRevisionsAsync(
            Guid accountReference,
            CancellationToken cancellationToken = default) =>
            ReadFactsAsync<GachaRevisionItem>(
                ArchiveRestoreFactKind.GachaRevision,
                accountReference,
                cancellationToken);

        public IAsyncEnumerable<ItemStateObservation> ReadItemObservationsAsync(
            Guid accountReference,
            CancellationToken cancellationToken = default) =>
            ReadFactsAsync<ItemStateObservation>(
                ArchiveRestoreFactKind.ItemObservation,
                accountReference,
                cancellationToken);

        public IAsyncEnumerable<ManualAcquisitionState> ReadManualAcquisitionsAsync(
            Guid accountReference,
            CancellationToken cancellationToken = default) =>
            ReadFactsAsync<ManualAcquisitionState>(
                ArchiveRestoreFactKind.ManualAcquisition,
                accountReference,
                cancellationToken);

        public IAsyncEnumerable<ManualAcquisitionRevisionItem>
            ReadManualAcquisitionRevisionsAsync(
                Guid accountReference,
                CancellationToken cancellationToken = default) =>
            ReadFactsAsync<ManualAcquisitionRevisionItem>(
                ArchiveRestoreFactKind.ManualAcquisitionRevision,
                accountReference,
                cancellationToken);

        public IAsyncEnumerable<ChallengeObservation> ReadChallengeObservationsAsync(
            Guid accountReference,
            CancellationToken cancellationToken = default) =>
            ReadFactsAsync<ChallengeObservation>(
                ArchiveRestoreFactKind.ChallengeObservation,
                accountReference,
                cancellationToken);

        public Task<IReadOnlyDictionary<string, ArchiveIndexedFact<GachaRecord>>>
            FindGachaRecordsAsync(
                Guid accountReference,
                IReadOnlyCollection<string> externalRecordIds,
                CancellationToken cancellationToken = default) =>
            FindManyAsync<GachaRecord>(
                ArchiveRestoreFactKind.GachaCurrent,
                accountReference,
                externalRecordIds,
                cancellationToken);

        public Task<ArchiveIndexedFact<GachaRevisionItem>?> FindGachaRevisionAsync(
            Guid accountReference,
            Guid revisionId,
            CancellationToken cancellationToken = default) =>
            FindOneAsync<GachaRevisionItem>(
                ArchiveRestoreFactKind.GachaRevision,
                accountReference,
                revisionId.ToString("D"),
                cancellationToken);

        public Task<ArchiveIndexedFact<ItemStateObservation>?> FindItemObservationAsync(
            Guid accountReference,
            ItemObservationId observationId,
            CancellationToken cancellationToken = default) =>
            FindOneAsync<ItemStateObservation>(
                ArchiveRestoreFactKind.ItemObservation,
                accountReference,
                observationId.ToString(),
                cancellationToken);

        public Task<ArchiveIndexedFact<ManualAcquisitionState>?>
            FindManualAcquisitionAsync(
                Guid accountReference,
                ManualAcquisitionId acquisitionId,
                CancellationToken cancellationToken = default) =>
            FindOneAsync<ManualAcquisitionState>(
                ArchiveRestoreFactKind.ManualAcquisition,
                accountReference,
                acquisitionId.ToString(),
                cancellationToken);

        public Task<ArchiveIndexedFact<ManualAcquisitionRevisionItem>?>
            FindManualAcquisitionRevisionAsync(
                Guid accountReference,
                Guid revisionId,
                CancellationToken cancellationToken = default) =>
            FindOneAsync<ManualAcquisitionRevisionItem>(
                ArchiveRestoreFactKind.ManualAcquisitionRevision,
                accountReference,
                revisionId.ToString("D"),
                cancellationToken);

        public Task<ArchiveIndexedFact<ChallengeObservation>?>
            FindChallengeObservationAsync(
                Guid accountReference,
                ChallengeObservationId observationId,
                CancellationToken cancellationToken = default) =>
            FindOneAsync<ChallengeObservation>(
                ArchiveRestoreFactKind.ChallengeObservation,
                accountReference,
                observationId.ToString(),
                cancellationToken);

        private async IAsyncEnumerable<T> ReadFactsAsync<T>(
            ArchiveRestoreFactKind kind,
            Guid accountReference,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            EnsureAccount(accountReference);
            int offset = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                List<StagedFactRow> page = connection.Query<StagedFactRow>(
                    "SELECT * FROM StagedFactRow WHERE Kind = ? AND AccountReference = ? ORDER BY StableReference LIMIT ? OFFSET ?;",
                    (int)kind,
                    accountReference.ToString("D"),
                    PageSize,
                    offset);
                foreach (StagedFactRow row in page) yield return Deserialize<T>(row);
                if (page.Count < PageSize) yield break;
                offset = checked(offset + page.Count);
                await Task.Yield();
            }
        }

        private Task<IReadOnlyDictionary<string, ArchiveIndexedFact<T>>> FindManyAsync<T>(
            ArchiveRestoreFactKind kind,
            Guid accountReference,
            IReadOnlyCollection<string> stableReferences,
            CancellationToken cancellationToken)
        {
            EnsureAccount(accountReference);
            cancellationToken.ThrowIfCancellationRequested();
            var result = new Dictionary<string, ArchiveIndexedFact<T>>(
                StringComparer.Ordinal);
            foreach (string stableReference in stableReferences.Distinct(StringComparer.Ordinal))
            {
                StagedFactRow? row = connection.FindWithQuery<StagedFactRow>(
                    "SELECT * FROM StagedFactRow WHERE Kind = ? AND AccountReference = ? AND StableReference = ?;",
                    (int)kind,
                    accountReference.ToString("D"),
                    stableReference);
                if (row is not null)
                {
                    result.Add(
                        stableReference,
                        new ArchiveIndexedFact<T>(
                            Deserialize<T>(row),
                            row.SemanticDigest));
                }
            }
            return Task.FromResult<IReadOnlyDictionary<string, ArchiveIndexedFact<T>>>(
                result);
        }

        private Task<ArchiveIndexedFact<T>?> FindOneAsync<T>(
            ArchiveRestoreFactKind kind,
            Guid accountReference,
            string stableReference,
            CancellationToken cancellationToken)
        {
            EnsureAccount(accountReference);
            cancellationToken.ThrowIfCancellationRequested();
            StagedFactRow? row = connection.FindWithQuery<StagedFactRow>(
                "SELECT * FROM StagedFactRow WHERE Kind = ? AND AccountReference = ? AND StableReference = ? LIMIT 1;",
                (int)kind,
                accountReference.ToString("D"),
                stableReference);
            ArchiveIndexedFact<T>? result = row is null
                ? null
                : new ArchiveIndexedFact<T>(
                    Deserialize<T>(row),
                    row.SemanticDigest);
            return Task.FromResult(result);
        }

        private static T Deserialize<T>(StagedFactRow row) =>
            JsonSerializer.Deserialize<T>(row.PayloadJson, ArchivePortableJson.Compact)
            ?? throw new InvalidDataException("A staged Archive fact is invalid.");

        private void EnsureAccount(Guid accountReference)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!accounts.Contains(accountReference))
            {
                throw new ArgumentException(
                    "The account is not part of the staged Archive.",
                    nameof(accountReference));
            }
        }

        public ValueTask DisposeAsync()
        {
            if (disposed) return ValueTask.CompletedTask;
            disposed = true;
            connection.Close();
            connection.Dispose();
            string root = Directory.GetParent(sessionDirectory)?.FullName ??
                throw new InvalidOperationException(
                    "The Archive restore session has no configured root.");
            DeleteSessionDirectory(root, sessionDirectory);
            return ValueTask.CompletedTask;
        }
    }

    [Table("StagedFactRow")]
    private sealed class StagedFactRow
    {
        [PrimaryKey, AutoIncrement]
        public long Id { get; set; }
        [Indexed(Name = "IX_StagedFact_Key", Order = 1, Unique = true)]
        public int Kind { get; set; }
        [Indexed(Name = "IX_StagedFact_Key", Order = 2, Unique = true)]
        public string AccountReference { get; set; } = string.Empty;
        [Indexed(Name = "IX_StagedFact_Key", Order = 3, Unique = true)]
        public string StableReference { get; set; } = string.Empty;
        public string PayloadJson { get; set; } = string.Empty;
        public string SemanticDigest { get; set; } = string.Empty;
    }

    [Table("StandardExtensionRow")]
    private sealed class StandardExtensionRow
    {
        [PrimaryKey, AutoIncrement]
        public long Id { get; set; }
        [Indexed(Name = "IX_StandardExtension_Key", Order = 1, Unique = true)]
        public string AccountReference { get; set; } = string.Empty;
        [Indexed(Name = "IX_StandardExtension_Key", Order = 2, Unique = true)]
        public string ExternalRecordId { get; set; } = string.Empty;
        public long OccurredAtUtcTicks { get; set; }
        public int OccurredAtOffsetMinutes { get; set; }
        public string ProvenanceJson { get; set; } = string.Empty;
    }
}
