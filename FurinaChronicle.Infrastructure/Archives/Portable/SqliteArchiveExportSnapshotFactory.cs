// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Infrastructure.Persistence.Sqlite;
using FurinaChronicle.Services.Abstractions;
using FurinaChronicle.Services.Archives.Portable;
using FurinaChronicle.Services.Gacha.History;
using FurinaChronicle.Services.HistoryDomains;

namespace FurinaChronicle.Infrastructure.Archives.Portable;

public sealed record ArchiveSnapshotStorageOptions(string RootDirectory)
{
    public static ArchiveSnapshotStorageOptions Default { get; } = new(
        Path.Combine(
            Path.GetTempPath(),
            "FurinaChronicle",
            "archive-export"));
}

public sealed class SqliteArchiveExportSnapshotFactory(
    FurinaDatabase database,
    TimeProvider timeProvider,
    ArchiveSnapshotStorageOptions? storageOptions = null)
    : IArchiveExportSnapshotFactory
{
    private readonly ArchiveSnapshotStorageOptions storage =
        storageOptions ?? ArchiveSnapshotStorageOptions.Default;

    public async Task<IArchiveExportSnapshot> OpenAsync(
        ArchiveExportScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        await database.InitializeAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        string root = Path.GetFullPath(storage.RootDirectory);
        Directory.CreateDirectory(root);
        string sessionDirectory = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sessionDirectory);
        string snapshotPath = Path.Combine(sessionDirectory, "archive-snapshot.db");
        FurinaDatabase? snapshotDatabase = null;
        try
        {
            await database.Connection.BackupAsync(snapshotPath, "main");
            cancellationToken.ThrowIfCancellationRequested();
            string stateToken = await ComputeFileSha256Async(
                snapshotPath,
                cancellationToken);
            long snapshotLength = new FileInfo(snapshotPath).Length;
            snapshotDatabase = new FurinaDatabase(new SqliteDatabaseOptions(snapshotPath));
            await snapshotDatabase.InitializeAsync(cancellationToken);

            var archives = new SqlitePlayerArchiveRepository(snapshotDatabase);
            var accounts = new SqliteGameAccountRepository(snapshotDatabase);
            var gacha = new SqliteGachaRecordRepository(snapshotDatabase);
            var history = new SqliteGachaAtomicChangeStore(snapshotDatabase);
            var portableSettings =
                new SqlitePortableApplicationSettingsStore(snapshotDatabase);
            IReadOnlyList<PlayerArchive> selected = SelectArchives(
                await archives.GetAllAsync(cancellationToken),
                scope);
            var catalogArchives = new List<ArchiveCatalogArchive>(selected.Count);
            var identities = new Dictionary<Guid, ArchiveCatalogRoleIdentity>();
            var settings = new List<ArchivePortableArchiveSetting>(selected.Count);
            foreach (PlayerArchive archive in selected)
            {
                IReadOnlyList<GameAccount> archiveAccounts =
                    await accounts.GetByArchiveIdAsync(archive.Id, cancellationToken);
                var catalogAccounts = new List<ArchiveCatalogAccount>(archiveAccounts.Count);
                foreach (GameAccount account in archiveAccounts)
                {
                    Guid? roleReference = account.RoleIdentity?.Id.Value;
                    if (account.RoleIdentity is GameRoleIdentity identity)
                    {
                        var catalogIdentity = new ArchiveCatalogRoleIdentity(
                            identity.Id.Value,
                            identity.Id.Value,
                            identity.NaturalIdentity);
                        if (identities.TryGetValue(identity.Id.Value, out var existing) &&
                            existing != catalogIdentity)
                        {
                            throw new InvalidDataException(
                                "A shared role identity GUID maps to conflicting natural identities.");
                        }
                        identities[identity.Id.Value] = catalogIdentity;
                    }
                    catalogAccounts.Add(new ArchiveCatalogAccount(
                        account.Id,
                        roleReference,
                        account.DisplayName,
                        account.Uid,
                        account.ServerRegion,
                        account.IsPlaceholder,
                        account.RoleIdentity is null
                            ? account.IsPlaceholder
                                ? "placeholder_account"
                                : "legacy_unresolved_identity"
                            : null,
                        account.CreatedAt,
                        account.UpdatedAt));
                }
                catalogArchives.Add(new ArchiveCatalogArchive(
                    archive.Id,
                    archive.Name,
                    archive.CreatedAt,
                    archive.UpdatedAt,
                    catalogAccounts));
                settings.Add(new ArchivePortableArchiveSetting(
                    archive.Id,
                    await history.GetUndoLimitAsync(archive.Id, cancellationToken)));
            }

            return new Snapshot(
                snapshotDatabase,
                sessionDirectory,
                scope,
                timeProvider.GetUtcNow(),
                new ArchiveCatalog(
                    identities.Values
                        .OrderBy(item => item.RoleIdentityReference)
                        .ToArray(),
                    catalogArchives),
                new ArchivePortableSettings(
                    await portableSettings.GetRequireUniqueArchiveNamesAsync(
                        cancellationToken),
                    settings),
                gacha,
                history,
                stateToken,
                snapshotLength);
        }
        catch
        {
            if (snapshotDatabase is not null)
            {
                await snapshotDatabase.DisposeAsync();
            }
            DeleteSessionDirectory(root, sessionDirectory);
            throw;
        }
    }

    private static async Task<string> ComputeFileSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static IReadOnlyList<PlayerArchive> SelectArchives(
        IReadOnlyList<PlayerArchive> archives,
        ArchiveExportScope scope)
    {
        if (scope.Kind == ArchiveExportScopeKind.AllArchives)
        {
            return archives.OrderBy(item => item.Id).ToArray();
        }
        HashSet<Guid> requested = scope.ArchiveIds.ToHashSet();
        PlayerArchive[] selected = archives
            .Where(item => requested.Contains(item.Id))
            .OrderBy(item => item.Id)
            .ToArray();
        if (selected.Length != requested.Count)
        {
            throw new KeyNotFoundException(
                "At least one selected Archive does not exist in the snapshot.");
        }
        return selected;
    }

    private static void DeleteSessionDirectory(string root, string sessionDirectory)
    {
        string fullRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string fullSession = Path.GetFullPath(sessionDirectory);
        if (!fullSession.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The Archive export session escaped its configured temporary root.");
        }
        if (Directory.Exists(fullSession))
        {
            Directory.Delete(fullSession, recursive: true);
        }
    }

    private sealed class Snapshot(
        FurinaDatabase database,
        string sessionDirectory,
        ArchiveExportScope scope,
        DateTimeOffset generatedAt,
        ArchiveCatalog catalog,
        ArchivePortableSettings settings,
        IGachaRecordRepository gacha,
        SqliteGachaAtomicChangeStore history,
        string stateToken,
        long snapshotLength)
        : IArchiveRestoreTargetSnapshot
    {
        private const int PageSize = 500;
        private readonly HashSet<Guid> accountReferences = catalog.Archives
            .SelectMany(item => item.Accounts)
            .Select(item => item.AccountReference)
            .ToHashSet();
        private bool disposed;
        public DateTimeOffset GeneratedAt { get; } = generatedAt;
        public ArchiveExportScope Scope { get; } = scope;
        public ArchiveCatalog Catalog { get; } = catalog;
        public ArchivePortableSettings Settings { get; } = settings;
        public int SchemaVersion { get; } = 8;
        public string StateToken { get; } = stateToken;
        public long SnapshotLength { get; } = snapshotLength;

        public async IAsyncEnumerable<GachaRecord> ReadGachaRecordsAsync(
            Guid accountReference,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            EnsureAccount(accountReference);
            int offset = 0;
            while (true)
            {
                IReadOnlyList<GachaRecord> page = await gacha.GetPageAsync(
                    accountReference,
                    offset,
                    PageSize,
                    cancellationToken);
                foreach (GachaRecord item in page) yield return item;
                if (page.Count < PageSize) yield break;
                offset = checked(offset + page.Count);
            }
        }

        public async IAsyncEnumerable<GachaRevisionItem> ReadGachaRevisionsAsync(
            Guid accountReference,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            EnsureAccount(accountReference);
            foreach (GachaRevisionItem item in
                await history.GetRevisionsByAccountAsync(
                    accountReference,
                    cancellationToken))
            {
                yield return item;
            }
        }

        public async IAsyncEnumerable<ItemStateObservation> ReadItemObservationsAsync(
            Guid accountReference,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            EnsureAccount(accountReference);
            int offset = 0;
            while (true)
            {
                ArchiveDomainPage<ItemStateObservation> page =
                    await history.GetItemObservationPageAsync(
                        new ItemObservationPageQuery(
                            accountReference,
                            Offset: offset,
                            Limit: PageSize),
                        cancellationToken);
                foreach (ItemStateObservation item in page.Items) yield return item;
                if (offset + page.Items.Count >= page.TotalCount) yield break;
                offset = checked(offset + page.Items.Count);
            }
        }

        public async IAsyncEnumerable<ManualAcquisitionState>
            ReadManualAcquisitionsAsync(
                Guid accountReference,
                [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            EnsureAccount(accountReference);
            int offset = 0;
            while (true)
            {
                ArchiveDomainPage<ManualAcquisitionState> page =
                    await history.GetManualAcquisitionPageAsync(
                        new ManualAcquisitionPageQuery(
                            accountReference,
                            Offset: offset,
                            Limit: PageSize),
                        cancellationToken);
                foreach (ManualAcquisitionState item in page.Items) yield return item;
                if (offset + page.Items.Count >= page.TotalCount) yield break;
                offset = checked(offset + page.Items.Count);
            }
        }

        public async IAsyncEnumerable<ManualAcquisitionRevisionItem>
            ReadManualAcquisitionRevisionsAsync(
                Guid accountReference,
                [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            EnsureAccount(accountReference);
            foreach (ManualAcquisitionRevisionItem item in
                await history.GetManualAcquisitionRevisionsAsync(
                    accountReference,
                    cancellationToken))
            {
                yield return item;
            }
        }

        public async IAsyncEnumerable<ChallengeObservation>
            ReadChallengeObservationsAsync(
                Guid accountReference,
                [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            EnsureAccount(accountReference);
            int offset = 0;
            while (true)
            {
                ArchiveDomainPage<ChallengeObservation> page =
                    await history.GetChallengeObservationPageAsync(
                        new ChallengeObservationPageQuery(
                            accountReference,
                            Offset: offset,
                            Limit: PageSize),
                        cancellationToken);
                foreach (ChallengeObservation item in page.Items) yield return item;
                if (offset + page.Items.Count >= page.TotalCount) yield break;
                offset = checked(offset + page.Items.Count);
            }
        }

        public async Task<IReadOnlyDictionary<string, ArchiveIndexedFact<GachaRecord>>>
            FindGachaRecordsAsync(
                Guid accountReference,
                IReadOnlyCollection<string> externalRecordIds,
                CancellationToken cancellationToken = default)
        {
            EnsureAccount(accountReference);
            IReadOnlyList<GachaRecord> records = await gacha
                .GetByExternalRecordIdsAsync(
                    accountReference,
                    externalRecordIds,
                    cancellationToken);
            return records.ToDictionary(
                item => item.ExternalRecordId,
                item => new ArchiveIndexedFact<GachaRecord>(
                    item,
                    ArchiveRestoreSemanticDigest.ForGacha(item)),
                StringComparer.Ordinal);
        }

        public async Task<ArchiveIndexedFact<GachaRevisionItem>?>
            FindGachaRevisionAsync(
                Guid accountReference,
                Guid revisionId,
                CancellationToken cancellationToken = default)
        {
            EnsureAccount(accountReference);
            GachaRevisionItem? value = await history.FindRevisionAsync(
                accountReference,
                revisionId,
                cancellationToken);
            return value is null
                ? null
                : new ArchiveIndexedFact<GachaRevisionItem>(
                    value,
                    ArchiveRestoreSemanticDigest.ForGachaRevision(value));
        }

        public async Task<ArchiveIndexedFact<ItemStateObservation>?>
            FindItemObservationAsync(
                Guid accountReference,
                ItemObservationId observationId,
                CancellationToken cancellationToken = default)
        {
            EnsureAccount(accountReference);
            ItemStateObservation? value =
                await history.FindItemObservationAsync(
                    accountReference,
                    observationId,
                    cancellationToken);
            return value is null
                ? null
                : new ArchiveIndexedFact<ItemStateObservation>(
                    value,
                    ArchiveRestoreSemanticDigest.ForItemObservation(value));
        }

        public async Task<ArchiveIndexedFact<ManualAcquisitionState>?>
            FindManualAcquisitionAsync(
                Guid accountReference,
                ManualAcquisitionId acquisitionId,
                CancellationToken cancellationToken = default)
        {
            EnsureAccount(accountReference);
            ManualAcquisitionState? value =
                await history.FindManualAcquisitionAsync(
                    accountReference,
                    acquisitionId,
                    cancellationToken);
            return value is null
                ? null
                : new ArchiveIndexedFact<ManualAcquisitionState>(
                    value,
                    ArchiveRestoreSemanticDigest.ForManualAcquisition(value));
        }

        public async Task<ArchiveIndexedFact<ManualAcquisitionRevisionItem>?>
            FindManualAcquisitionRevisionAsync(
                Guid accountReference,
                Guid revisionId,
                CancellationToken cancellationToken = default)
        {
            EnsureAccount(accountReference);
            ManualAcquisitionRevisionItem? value =
                await history.FindManualAcquisitionRevisionAsync(
                    accountReference,
                    revisionId,
                    cancellationToken);
            return value is null
                ? null
                : new ArchiveIndexedFact<ManualAcquisitionRevisionItem>(
                    value,
                    ArchiveRestoreSemanticDigest.ForManualRevision(value));
        }

        public async Task<ArchiveIndexedFact<ChallengeObservation>?>
            FindChallengeObservationAsync(
                Guid accountReference,
                ChallengeObservationId observationId,
                CancellationToken cancellationToken = default)
        {
            EnsureAccount(accountReference);
            ChallengeObservation? value =
                await history.FindChallengeObservationAsync(
                    accountReference,
                    observationId,
                    cancellationToken);
            return value is null
                ? null
                : new ArchiveIndexedFact<ChallengeObservation>(
                    value,
                    ArchiveRestoreSemanticDigest.ForChallenge(value));
        }

        public async Task<IReadOnlyList<ArchiveRestoreActiveTombstone>>
            FindActiveTombstonesAsync(
                IReadOnlyCollection<ArchiveRestoreTombstoneProbe> probes,
                CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(probes);
            var result = new List<ArchiveRestoreActiveTombstone>();
            foreach (ArchiveRestoreTombstoneProbe probe in probes.Distinct())
            {
                cancellationToken.ThrowIfCancellationRequested();
                string[] keys = CreateTombstoneKeys(probe).ToArray();
                string placeholders = string.Join(", ", keys.Select(_ => "?"));
                List<object> arguments = [.. keys.Cast<object>()];
                TombstoneLookupRow? row = (await database.Connection
                        .QueryAsync<TombstoneLookupRow>(
                            $"SELECT TombstoneKey, TombstoneVersion FROM LocalOnlyTombstones WHERE IsActive = 1 AND TombstoneKey IN ({placeholders}) ORDER BY TombstoneVersion DESC LIMIT 1;",
                            arguments.ToArray()))
                    .SingleOrDefault();
                if (row is not null)
                {
                    result.Add(new ArchiveRestoreActiveTombstone(
                        probe,
                        row.TombstoneKey,
                        row.TombstoneVersion));
                }
            }
            return result;
        }

        private static IEnumerable<string> CreateTombstoneKeys(
            ArchiveRestoreTombstoneProbe probe)
        {
            if (probe.Kind is ArchiveRestoreFactKind.GachaCurrent or
                ArchiveRestoreFactKind.GachaRevision)
            {
                if (probe.TargetRoleIdentityId is Guid identityId)
                {
                    yield return HashTombstone(
                        $"gacha|{probe.TargetArchiveId:D}|{identityId:D}|{probe.StableReference}");
                }
                yield return HashTombstone(
                    $"gacha|{probe.TargetArchiveId:D}|local-account|{probe.TargetAccountId:D}|{probe.StableReference}");
                yield break;
            }
            string domainKind = probe.Kind switch
            {
                ArchiveRestoreFactKind.ItemObservation => "item_observation",
                ArchiveRestoreFactKind.ManualAcquisition or
                    ArchiveRestoreFactKind.ManualAcquisitionRevision =>
                    "manual_acquisition",
                ArchiveRestoreFactKind.ChallengeObservation =>
                    "challenge_observation",
                _ => throw new ArgumentOutOfRangeException(nameof(probe)),
            };
            yield return probe.TargetRoleIdentityId is Guid roleId
                ? $"domain-role|{probe.TargetArchiveId:D}|{roleId:D}|{domainKind}|{probe.StableReference}"
                : $"domain-account|{probe.TargetArchiveId:D}|{probe.TargetAccountId:D}|{domainKind}|{probe.StableReference}";
        }

        private static string HashTombstone(string canonical) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
                .ToLowerInvariant();

        private void EnsureAccount(Guid accountReference)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!accountReferences.Contains(accountReference))
            {
                throw new ArgumentException(
                    "The account is not part of this Archive export snapshot.",
                    nameof(accountReference));
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (disposed) return;
            disposed = true;
            await database.DisposeAsync();
            string root = Directory.GetParent(sessionDirectory)?.FullName ??
                throw new InvalidOperationException(
                    "The Archive snapshot session has no configured root.");
            DeleteSessionDirectory(root, sessionDirectory);
        }

        private sealed class TombstoneLookupRow
        {
            public string TombstoneKey { get; set; } = string.Empty;
            public long TombstoneVersion { get; set; }
        }
    }
}

public sealed class SqliteArchiveRestoreTargetSnapshotFactory(
    SqliteArchiveExportSnapshotFactory exportSnapshotFactory)
    : IArchiveRestoreTargetSnapshotFactory
{
    public async Task<IArchiveRestoreTargetSnapshot> OpenAsync(
        CancellationToken cancellationToken = default)
    {
        IArchiveExportSnapshot snapshot = await exportSnapshotFactory.OpenAsync(
            ArchiveExportScope.All(),
            cancellationToken);
        return snapshot as IArchiveRestoreTargetSnapshot ??
            throw new InvalidOperationException(
                "The SQLite Archive snapshot does not expose restore planning metadata.");
    }
}
