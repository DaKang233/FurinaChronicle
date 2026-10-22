// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;
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
            snapshotDatabase = new FurinaDatabase(new SqliteDatabaseOptions(snapshotPath));
            await snapshotDatabase.InitializeAsync(cancellationToken);

            var archives = new SqlitePlayerArchiveRepository(snapshotDatabase);
            var accounts = new SqliteGameAccountRepository(snapshotDatabase);
            var gacha = new SqliteGachaRecordRepository(snapshotDatabase);
            var history = new SqliteGachaAtomicChangeStore(snapshotDatabase);
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
                new ArchivePortableSettings(true, settings),
                gacha,
                history);
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
        SqliteGachaAtomicChangeStore history)
        : IArchiveExportSnapshot
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
    }
}
