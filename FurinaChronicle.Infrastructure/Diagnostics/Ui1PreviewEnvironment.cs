// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.History;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Infrastructure.Persistence.Sqlite;
using FurinaChronicle.Services.Archives;
using FurinaChronicle.Services.Gacha;
using FurinaChronicle.Services.Gacha.Writing;

namespace FurinaChronicle.Infrastructure.Diagnostics;

// Explicit development composition. Never discovers or opens the production database or credentials.
public sealed class Ui1PreviewEnvironment : IAsyncDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private FileStream? lease;
    private bool initialized;
    public string RootDirectory { get; }
    public string DatabasePath => Path.Combine(RootDirectory, "ui1-preview.db3");
    public string MetadataDirectory => Path.Combine(RootDirectory, "metadata");
    public string ImageCacheDirectory => Path.Combine(RootDirectory, "images");
    public string TemporaryDirectory => Path.Combine(RootDirectory, "temp");
    public FurinaDatabase Database { get; }
    public SqlitePlayerArchiveRepository Archives { get; }
    public SqliteGameAccountRepository Accounts { get; }
    public SqliteGachaRecordRepository Records { get; }
    public SqliteGachaAtomicChangeStore Changes { get; }

    public Ui1PreviewEnvironment(string platformDataDirectory, string hostName)
    {
        if (!Path.IsPathFullyQualified(platformDataDirectory))
            throw new ArgumentException("An absolute platform directory is required.", nameof(platformDataDirectory));
        if (hostName is not ("WinUI" or "Maui"))
            throw new ArgumentException("Unknown preview host.", nameof(hostName));
        RootDirectory = Path.Combine(platformDataDirectory, "UI1", hostName);
        Database = new(new SqliteDatabaseOptions(DatabasePath));
        Archives = new(Database);
        Accounts = new(Database);
        Records = new(Database);
        Changes = new(Database);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (initialized) return;
            // Hold the lease for the environment lifetime, including every query and seed transaction.
            lease ??= new FileStream(Path.Combine(RootDirectory, "session.lock"), FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None);
            foreach (string path in new[] { MetadataDirectory, ImageCacheDirectory, TemporaryDirectory })
                Directory.CreateDirectory(path);
            await Database.InitializeAsync(cancellationToken).ConfigureAwait(false);
            await SeedAsync(cancellationToken).ConfigureAwait(false);
            initialized = true;
        }
        finally { gate.Release(); }
    }

    public string? GetCachedIconPath(string? resourceKey)
    {
        if (string.IsNullOrWhiteSpace(resourceKey) || !resourceKey.All(char.IsAsciiLetterOrDigit)) return null;
        string path = Path.Combine(ImageCacheDirectory, resourceKey + ".png");
        return File.Exists(path) ? path : null;
    }

    private async Task SeedAsync(CancellationToken token)
    {
        var create = new CreatePlayerArchive(Archives);
        var add = new AddGameAccount(Archives, Accounts);
        foreach ((string name, int scenario) in new[]
        {
            ("01 合成档案", 0), ("02 同 UID 独立副本", 1), ("03 空档案", 2)
        })
        {
            PlayerArchive archive = (await Archives.GetAllAsync(token).ConfigureAwait(false))
                .FirstOrDefault(a => a.Name == name) ?? await create.ExecuteAsync(name, token).ConfigureAwait(false);
            if (scenario == 2) continue;
            int accountCount = scenario == 0 ? 3 : 1;
            for (int accountIndex = 0; accountIndex < accountCount; accountIndex++)
            {
                string uid = $"10000000{accountIndex + 1}";
                string label = accountIndex switch { 0 => "A 合成账号", 1 => "B 合成账号", _ => "C 空账号" };
                GameAccount account = (await Accounts.GetByArchiveIdAsync(archive.Id, token).ConfigureAwait(false))
                    .FirstOrDefault(a => a.Uid == uid) ??
                    await add.ExecuteAsync(archive.Id, uid, GameServerRegion.ChinaOfficial, label, token).ConfigureAwait(false);
                int count = scenario == 1 ? 3 : accountIndex switch { 0 => 61, 1 => 7, _ => 0 };
                if (count == 0) continue;
                DateTimeOffset time = new(2026, 10, 1, 12, 0, 0, TimeSpan.FromHours(8));
                GachaRecord[] records = Enumerable.Range(1, count).Select(i => new GachaRecord(
                    account.Id, (100000 + i).ToString(), $"合成物品 {scenario}-{accountIndex}-{i:00}",
                    i % 10 == 0 ? 5 : i % 3 == 0 ? 4 : 3, time.AddMinutes(i))
                {
                    ItemId = $"demo{i % 4}", ItemType = i % 10 == 0 ? "角色" : "武器",
                    GachaType = "301", UigfGachaType = "301",
                    Provenance = i % 2 == 0 ? new(DataOrigin.UserEntered) : RecordProvenance.Unknown
                }).ToArray();
                var committer = new CommitGachaRecords(Changes);
                CommitGachaRecordsResult result = await committer.ExecuteAsync(new(
                    new OperationId(Guid.NewGuid()), DataChangeOperationKind.Import, DataOrigin.UserEntered,
                    "UI-1 synthetic fixture", time, time, records,
                    GachaRecordConflictPolicy.PreserveExisting, true, CaptureUndo: false), token).ConfigureAwait(false);
                if (result.Status is not (ChangeExecutionStatus.Applied or ChangeExecutionStatus.NoOp))
                    throw new InvalidOperationException("UI-1 synthetic initialization failed.");
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await Database.DisposeAsync().ConfigureAwait(false);
            lease?.Dispose();
            lease = null;
        }
        finally { gate.Release(); }
    }
}
