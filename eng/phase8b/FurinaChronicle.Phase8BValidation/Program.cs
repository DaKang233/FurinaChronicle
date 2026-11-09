// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.History;
using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Infrastructure.HistoryDomains.Portable;
using FurinaChronicle.Infrastructure.Persistence.Sqlite;
using FurinaChronicle.Services.History;
using FurinaChronicle.Services.HistoryDomains;
using FurinaChronicle.Services.HistoryDomains.Portable;
using SQLite;

return await Phase8BValidationTool.RunAsync(args);

internal static class Phase8BValidationTool
{
    private static readonly Encoding Utf8NoBom =
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private static readonly DateTimeOffset BaseTime =
        new(2026, 10, 20, 9, 30, 15, TimeSpan.FromHours(8));

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length < 2)
        {
            PrintUsage();
            return 1;
        }

        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "generate" => await GenerateAsync(Path.GetFullPath(args[1])),
                "compare" when args.Length == 3 => await CompareAsync(
                    Path.GetFullPath(args[1]),
                    Path.GetFullPath(args[2])),
                "inspect" => await InspectAsync(Path.GetFullPath(args[1])),
                "backup" when args.Length == 3 => await BackupAsync(
                    Path.GetFullPath(args[1]),
                    Path.GetFullPath(args[2])),
                _ => InvalidArguments()
            };
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Validation tool failed: {exception.Message}");
            return 3;
        }
    }

    private static int InvalidArguments()
    {
        PrintUsage();
        return 1;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  generate <output-directory>");
        Console.WriteLine("  compare <expected.json> <actual.json>");
        Console.WriteLine("  inspect <portable.json>");
        Console.WriteLine("  backup <source.db3> <new-destination.db3>");
    }

    private static async Task<int> GenerateAsync(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        string workDirectory = Path.Combine(
            Path.GetTempPath(),
            "FurinaChronicle-8B7",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDirectory);
        try
        {
            await using ValidationContext context =
                await ValidationContext.CreateAsync(workDirectory);
            PlayerArchive archive = await context.PopulateAsync();
            var entries = new List<ValidationFileManifest>();
            foreach ((ArchiveDomainDatasetKind kind, string name) in new[]
                     {
                         (ArchiveDomainDatasetKind.CharacterWeaponHistory,
                             "character-weapon-history.valid.json"),
                         (ArchiveDomainDatasetKind.ChallengeHistory,
                             "challenge-history.valid.json")
                     })
            {
                string path = Path.Combine(outputDirectory, name);
                await using (FileStream destination = File.Create(path))
                {
                    await context.Files.ExportAsync(
                        archive.Id,
                        kind,
                        destination,
                        BaseTime.AddDays(1));
                }
                ArchiveDomainPortablePackage package =
                    await ReadPackageAsync(context.Files, path);
                entries.Add(await ManifestAsync(path, package, expectedValid: true));
                if (kind == ArchiveDomainDatasetKind.CharacterWeaponHistory)
                {
                    entries.Add(await WriteTamperedAsync(
                        path,
                        Path.Combine(outputDirectory, "character-weapon-history.bad-fingerprint.json")));
                    entries.Add(await WriteFutureMajorAsync(
                        path,
                        Path.Combine(outputDirectory, "character-weapon-history.future-major.json")));
                    entries.Add(await WriteConflictingContentAsync(
                        path,
                        Path.Combine(outputDirectory, "character-weapon-history.conflict.json")));
                    entries.Add(await WriteTruncatedAsync(
                        path,
                        Path.Combine(outputDirectory, "character-weapon-history.truncated.json")));
                }
            }

            string oversized = Path.Combine(
                outputDirectory,
                "portable.over-limit.invalid.json");
            await File.WriteAllTextAsync(
                oversized,
                new string('x', checked((int)ArchiveDomainPortableFileService.MaximumFileBytes + 1)),
                Utf8NoBom);
            entries.Add(await ManifestAsync(
                oversized,
                package: null,
                expectedValid: false,
                "exceeds the reader byte limit"));

            var manifest = new ValidationManifest(
                "FurinaChronicle Phase 8B.7 synthetic portable samples",
                BaseTime,
                "All identities and facts are synthetic. Success samples were emitted by the production exporter.",
                entries);
            string manifestPath = Path.Combine(outputDirectory, "manifest.json");
            await File.WriteAllTextAsync(
                manifestPath,
                JsonSerializer.Serialize(manifest, DisplayJson()),
                Utf8NoBom);
            await File.WriteAllTextAsync(
                Path.Combine(outputDirectory, "README.md"),
                SampleReadme(),
                Utf8NoBom);
            Console.WriteLine(manifestPath);
            return 0;
        }
        finally
        {
            if (Directory.Exists(workDirectory))
            {
                Directory.Delete(workDirectory, recursive: true);
            }
        }
    }

    private static async Task<int> CompareAsync(string expectedPath, string actualPath)
    {
        await using ValidationContext context = await ValidationContext.CreateAsync();
        ArchiveDomainPortablePackage expected =
            await ReadPackageAsync(context.Files, expectedPath);
        ArchiveDomainPortablePackage actual =
            await ReadPackageAsync(context.Files, actualPath);
        ArchiveDomainPortableSemanticComparison comparison =
            ArchiveDomainPortableSemanticComparer.Compare(expected, actual);
        var report = new
        {
            comparison.Equivalent,
            comparison.ExpectedDigest,
            comparison.ActualDigest,
            comparison.Differences,
            ExpectedFile = Path.GetFileName(expectedPath),
            ActualFile = Path.GetFileName(actualPath)
        };
        Console.WriteLine(JsonSerializer.Serialize(report, DisplayJson()));
        return comparison.Equivalent ? 0 : 2;
    }

    private static async Task<int> InspectAsync(string path)
    {
        await using ValidationContext context = await ValidationContext.CreateAsync();
        ArchiveDomainPortablePackage package =
            await ReadPackageAsync(context.Files, path);
        var report = new
        {
            package.DatasetKind,
            Version = $"{package.FormatMajor}.{package.FormatMinor}",
            package.SourceArchiveName,
            AccountCount = package.Accounts.Count,
            ItemObservationCount = package.Accounts.Sum(item => item.ItemObservations.Count),
            ManualAcquisitionCount = package.Accounts.Sum(item => item.ManualAcquisitions.Count),
            ManualRevisionCount = package.Accounts.Sum(item => item.ManualRevisions.Count),
            ChallengeObservationCount = package.Accounts.Sum(item => item.ChallengeObservations.Count),
            SemanticDigest = ArchiveDomainPortableSemanticComparer.ComputeSemanticDigest(package),
            FileSha256 = await FileSha256Async(path),
            FileBytes = new FileInfo(path).Length
        };
        Console.WriteLine(JsonSerializer.Serialize(report, DisplayJson()));
        return 0;
    }

    private static async Task<int> BackupAsync(
        string sourcePath,
        string destinationPath)
    {
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException(
                "The source SQLite database does not exist.",
                sourcePath);
        }
        if (File.Exists(destinationPath))
        {
            throw new IOException(
                "The backup destination already exists; it will not be overwritten.");
        }
        string? destinationDirectory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        var source = new SQLiteAsyncConnection(
            sourcePath,
            SQLiteOpenFlags.ReadOnly | SQLiteOpenFlags.FullMutex,
            storeDateTimeAsTicks: true);
        try
        {
            await source.SetBusyTimeoutAsync(TimeSpan.FromSeconds(10));
            int schema = await source.ExecuteScalarAsync<int>(
                "PRAGMA user_version;");
            int applicationId = await source.ExecuteScalarAsync<int>(
                "PRAGMA application_id;");
            string integrity = await source.ExecuteScalarAsync<string>(
                "PRAGMA quick_check;");
            if (!string.Equals(integrity, "ok", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"The source database did not pass PRAGMA quick_check: {integrity}.");
            }
            await source.BackupAsync(destinationPath, "main");
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                Source = sourcePath,
                Destination = destinationPath,
                SchemaVersion = schema,
                ApplicationId = $"0x{applicationId:X8}",
                IntegrityCheck = integrity,
                DestinationBytes = new FileInfo(destinationPath).Length,
                DestinationSha256 = await FileSha256Async(destinationPath)
            }, DisplayJson()));
            return 0;
        }
        finally
        {
            await source.CloseAsync();
        }
    }

    private static async Task<ArchiveDomainPortablePackage> ReadPackageAsync(
        ArchiveDomainPortableFileService files,
        string path)
    {
        await using FileStream source = File.OpenRead(path);
        return await files.ReadAsync(source);
    }

    private static async Task<ValidationFileManifest> WriteTamperedAsync(
        string source,
        string destination)
    {
        string json = await File.ReadAllTextAsync(source, Encoding.UTF8);
        json = json.Replace(
            "CHAR_10000089",
            "CHAR_10000088",
            StringComparison.Ordinal);
        await File.WriteAllTextAsync(destination, json, Utf8NoBom);
        return await ManifestAsync(
            destination,
            package: null,
            expectedValid: false,
            "content changed without updating the fingerprint");
    }

    private static async Task<ValidationFileManifest> WriteFutureMajorAsync(
        string source,
        string destination)
    {
        JsonObject document = JsonNode.Parse(
            await File.ReadAllTextAsync(source, Encoding.UTF8))!.AsObject();
        JsonObject package = document["package"]!.AsObject();
        package["format_major"] = 99;
        RefreshFingerprint(package);
        await File.WriteAllTextAsync(
            destination,
            document.ToJsonString(DisplayJson()),
            Utf8NoBom);
        return await ManifestAsync(
            destination,
            package: null,
            expectedValid: false,
            "valid fingerprint but unsupported future major version");
    }

    private static async Task<ValidationFileManifest> WriteConflictingContentAsync(
        string source,
        string destination)
    {
        JsonObject document = JsonNode.Parse(
            await File.ReadAllTextAsync(source, Encoding.UTF8))!.AsObject();
        JsonObject package = document["package"]!.AsObject();
        JsonObject entry = package["accounts"]![0]!["item_observations"]![0]!["entries"]![0]!.AsObject();
        entry["quantity"] = 999;
        RefreshFingerprint(package);
        await File.WriteAllTextAsync(
            destination,
            document.ToJsonString(DisplayJson()),
            Utf8NoBom);
        return await ManifestAsync(
            destination,
            package: null,
            expectedValid: true,
            "same stable reference with deliberately different content; use as a merge conflict input");
    }

    private static async Task<ValidationFileManifest> WriteTruncatedAsync(
        string source,
        string destination)
    {
        byte[] content = await File.ReadAllBytesAsync(source);
        await File.WriteAllBytesAsync(destination, content[..(content.Length / 2)]);
        return await ManifestAsync(
            destination,
            package: null,
            expectedValid: false,
            "truncated JSON");
    }

    private static void RefreshFingerprint(JsonObject package)
    {
        package["package_fingerprint"] = string.Empty;
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            WriteCanonicalNode(writer, package);
        }
        package["package_fingerprint"] = Convert.ToHexString(
            SHA256.HashData(stream.ToArray())).ToLowerInvariant();
    }

    private static void WriteCanonicalNode(Utf8JsonWriter writer, JsonNode? node)
    {
        if (node is null)
        {
            writer.WriteNullValue();
        }
        else if (node is JsonObject objectNode)
        {
            writer.WriteStartObject();
            foreach ((string name, JsonNode? value) in objectNode.OrderBy(
                         item => item.Key,
                         StringComparer.Ordinal))
            {
                writer.WritePropertyName(name);
                WriteCanonicalNode(writer, value);
            }
            writer.WriteEndObject();
        }
        else if (node is JsonArray arrayNode)
        {
            writer.WriteStartArray();
            foreach (JsonNode? item in arrayNode)
            {
                WriteCanonicalNode(writer, item);
            }
            writer.WriteEndArray();
        }
        else
        {
            node.WriteTo(writer);
        }
    }

    private static async Task<ValidationFileManifest> ManifestAsync(
        string path,
        ArchiveDomainPortablePackage? package,
        bool expectedValid,
        string? purpose = null) => new(
        Path.GetFileName(path),
        new FileInfo(path).Length,
        await FileSha256Async(path),
        package is null
            ? null
            : ArchiveDomainPortableSemanticComparer.ComputeSemanticDigest(package),
        expectedValid,
        purpose);

    private static async Task<string> FileSha256Async(string path)
    {
        await using FileStream input = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(input))
            .ToLowerInvariant();
    }

    private static JsonSerializerOptions DisplayJson() => new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private static string SampleReadme() => """
        # Phase 8B.7 synthetic Portable samples

        All identities and facts in this directory are synthetic. The two
        `*.valid.json` files are generated through the production domain store
        and production Portable exporter. The other files are labelled negative
        or conflict inputs in `manifest.json`.

        File SHA-256 proves which physical file was transferred. It is not a
        semantic comparison and does not prove authenticity. Use the `compare`
        command for semantic verification after a device re-export.
        """;

    private sealed record ValidationManifest(
        string Name,
        DateTimeOffset GeneratedFromFixtureTime,
        string Notice,
        IReadOnlyList<ValidationFileManifest> Files);

    private sealed record ValidationFileManifest(
        string FileName,
        long FileBytes,
        string FileSha256,
        string? SemanticDigest,
        bool ExpectedValid,
        string? Purpose);

    private sealed class ValidationContext : IAsyncDisposable
    {
        private readonly string directory;

        private ValidationContext(string directory, FurinaDatabase database)
        {
            this.directory = directory;
            Database = database;
            Archives = new SqlitePlayerArchiveRepository(database);
            Accounts = new SqliteGameAccountRepository(database);
            Store = new SqliteGachaAtomicChangeStore(database);
            Files = new ArchiveDomainPortableFileService(
                Archives,
                Accounts,
                Store);
        }

        public FurinaDatabase Database { get; }
        public SqlitePlayerArchiveRepository Archives { get; }
        public SqliteGameAccountRepository Accounts { get; }
        public SqliteGachaAtomicChangeStore Store { get; }
        public ArchiveDomainPortableFileService Files { get; }

        public static async Task<ValidationContext> CreateAsync(
            string? directory = null)
        {
            directory ??= Path.Combine(
                Path.GetTempPath(),
                "FurinaChronicle-8B7",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var database = new FurinaDatabase(new SqliteDatabaseOptions(
                Path.Combine(directory, "validation.db3")));
            await database.InitializeAsync();
            return new ValidationContext(directory, database);
        }

        public async Task<PlayerArchive> PopulateAsync()
        {
            var archive = new PlayerArchive(
                Guid.Parse("81000000-0000-0000-0000-000000000001"),
                "8B.7 合成验收档案",
                BaseTime,
                BaseTime);
            await Archives.AddAsync(archive);
            GameAccount first = await AddAccountAsync(
                archive.Id,
                Guid.Parse("82000000-0000-0000-0000-000000000001"),
                "800000001",
                GameServerRegion.Asia,
                "合成账号一");
            GameAccount second = await AddAccountAsync(
                archive.Id,
                Guid.Parse("82000000-0000-0000-0000-000000000002"),
                "700000002",
                GameServerRegion.Europe,
                "合成账号二");

            await CommitItemsAsync(first, second);
            await CommitManualHistoryAsync(first, second);
            await CommitChallengesAsync(first, second);
            return archive;
        }

        private async Task<GameAccount> AddAccountAsync(
            Guid archiveId,
            Guid accountId,
            string uid,
            GameServerRegion region,
            string nickname)
        {
            GameRoleNaturalIdentity natural =
                GenshinGameRoleIdentity.Create(uid, region);
            var account = new GameAccount(
                accountId,
                archiveId,
                uid,
                region,
                nickname,
                false,
                BaseTime,
                BaseTime,
                new GameRoleIdentity(
                    GameRoleIdentityId.FromNaturalIdentity(natural),
                    natural));
            await Accounts.AddAsync(account);
            return account;
        }

        private async Task CommitItemsAsync(
            GameAccount first,
            GameAccount second)
        {
            ItemStateObservation[] observations =
            [
                ItemObservation(first.Id, 1, InventoryItemKind.Character,
                    "CHAR_10000089", 1, null, null,
                    DataCompleteness.Partial, BaseTime),
                ItemObservation(first.Id, 2, InventoryItemKind.Character,
                    "CHAR_10000089", 1, 90, 2,
                    DataCompleteness.Complete, BaseTime.AddHours(3)),
                ItemObservation(second.Id, 3, InventoryItemKind.Weapon,
                    "WEAPON_15514", 2, null, null,
                    DataCompleteness.Partial, BaseTime.AddMinutes(10)),
                ItemObservation(second.Id, 4, InventoryItemKind.Weapon,
                    "WEAPON_15514", 3, 90, 1,
                    DataCompleteness.Complete, BaseTime.AddHours(5))
            ];
            ArchiveOperationResult result = await Store.CommitItemObservationsAsync(
                new ItemObservationCommitRequest(
                    OperationId.New(),
                    DataChangeOperationKind.Refresh,
                    DataOrigin.OfficialApi,
                    "8B.7 synthetic item observations",
                    BaseTime,
                    BaseTime.AddMinutes(1),
                    observations));
            EnsureApplied(result, "item observations");
        }

        private async Task CommitManualHistoryAsync(
            GameAccount first,
            GameAccount second)
        {
            ManualAcquisitionId firstId = new(
                Guid.Parse("84000000-0000-0000-0000-000000000001"));
            var firstFact = new ManualAcquisition(
                firstId,
                first.Id,
                InventoryItemKind.Character,
                "CHAR_10000089",
                OccurrenceTime.At(BaseTime.AddDays(-30), TimePrecision.Second),
                ManualAcquisitionReason.Other,
                "初始人工记录",
                new RecordProvenance(DataOrigin.UserEntered));
            var secondFact = new ManualAcquisition(
                new ManualAcquisitionId(
                    Guid.Parse("84000000-0000-0000-0000-000000000002")),
                second.Id,
                InventoryItemKind.Weapon,
                "WEAPON_15514",
                OccurrenceTime.At(
                    new DateTimeOffset(2026, 9, 1, 0, 0, 0,
                        TimeSpan.FromHours(-5)),
                    TimePrecision.Day),
                ManualAcquisitionReason.EventSelection,
                "合成活动",
                new RecordProvenance(DataOrigin.UserEntered));
            ArchiveOperationResult insert = await Store.CommitManualAcquisitionsAsync(
                new ManualAcquisitionCommitRequest(
                    OperationId.New(),
                    DataChangeOperationKind.Import,
                    "8B.7 synthetic manual acquisitions",
                    BaseTime.AddMinutes(2),
                    BaseTime.AddMinutes(3),
                    [
                        new ManualAcquisitionMutation(firstId, first.Id, firstFact),
                        new ManualAcquisitionMutation(
                            secondFact.AcquisitionId,
                            second.Id,
                            secondFact)
                    ]));
            EnsureApplied(insert, "manual acquisitions");

            ManualAcquisitionState current = AssertSingle(
                await Store.GetManualAcquisitionsAsync(first.Id));
            var corrected = new ManualAcquisition(
                firstId,
                first.Id,
                InventoryItemKind.Character,
                "CHAR_10000089",
                OccurrenceTime.Within(
                    BaseTime.AddDays(-35),
                    BaseTime.AddDays(-29),
                    TimePrecision.Week),
                ManualAcquisitionReason.AnniversarySelection,
                "合成周年庆自选",
                new RecordProvenance(DataOrigin.UserEntered));
            ArchiveOperationResult correction =
                await Store.CommitManualAcquisitionsAsync(
                    new ManualAcquisitionCommitRequest(
                        OperationId.New(),
                        DataChangeOperationKind.Correction,
                        "8B.7 synthetic correction",
                        BaseTime.AddMinutes(4),
                        BaseTime.AddMinutes(5),
                        [new ManualAcquisitionMutation(
                            firstId,
                            first.Id,
                            corrected,
                            current.Version)]));
            EnsureApplied(correction, "manual acquisition correction");
        }

        private async Task CommitChallengesAsync(
            GameAccount first,
            GameAccount second)
        {
            ChallengeObservation[] observations =
            [
                Challenge(first.Id, 1, 6, BaseTime.AddDays(-2), false),
                Challenge(first.Id, 2, 9, BaseTime.AddDays(-1), false),
                Challenge(first.Id, 3, 6, BaseTime, false),
                Challenge(first.Id, 4, 9, BaseTime.AddHours(-6), false),
                Challenge(second.Id, 5, 3, BaseTime.AddHours(2), true)
            ];
            ArchiveOperationResult result =
                await Store.CommitChallengeObservationsAsync(
                    new ChallengeObservationCommitRequest(
                        OperationId.New(),
                        DataChangeOperationKind.Refresh,
                        DataOrigin.OfficialApi,
                        "8B.7 synthetic challenge observations",
                        BaseTime.AddMinutes(6),
                        BaseTime.AddMinutes(7),
                        observations));
            EnsureApplied(result, "challenge observations");
        }

        private static ItemStateObservation ItemObservation(
            Guid accountId,
            int sequence,
            InventoryItemKind kind,
            string typeId,
            int quantity,
            int? level,
            int? progression,
            DataCompleteness completeness,
            DateTimeOffset observedAt)
        {
            InventoryFieldCoverage coverage = InventoryFieldCoverage.Quantity;
            if (level is not null)
            {
                coverage |= InventoryFieldCoverage.Level;
            }
            if (progression is not null)
            {
                coverage |= InventoryFieldCoverage.Progression;
            }
            return new ItemStateObservation(
                new ItemObservationId(SequenceGuid(83000000, sequence)),
                accountId,
                kind,
                completeness,
                [new InventoryStateEntry(
                    kind,
                    typeId,
                    instanceId: null,
                    quantity,
                    level,
                    progression,
                    coverage,
                    completeness)],
                new RecordProvenance(
                    DataOrigin.OfficialApi,
                    new RecordTimestamps(
                        ObservedAt: observedAt,
                        FetchedAt: observedAt.AddSeconds(10)),
                    new DataSourceReference(
                        "phase8b7-synthetic-provider",
                        sourceRecordId: $"item-{sequence}",
                        sourceSnapshotId: $"snapshot-{sequence}"),
                    new AcquisitionBatchId(SequenceGuid(83100000, sequence))));
        }

        private static ChallengeObservation Challenge(
            Guid accountId,
            int sequence,
            int stars,
            DateTimeOffset observedAt,
            bool unknownCompletion) => new(
            new ChallengeObservationId(SequenceGuid(85000000, sequence)),
            accountId,
            ChallengeMode.SpiralAbyss,
            "2026-10-upper",
            BaseTime.AddDays(-5),
            BaseTime.AddDays(10),
            stars == 9 ? DataCompleteness.Complete : DataCompleteness.Partial,
            [new SpiralAbyssFloorResult(
                12,
                stars == 9 ? DataCompleteness.Complete : DataCompleteness.Partial,
                Enumerable.Range(1, stars / 3).Select(chamber =>
                    new SpiralAbyssChamberResult(
                        chamber,
                        3,
                        unknownCompletion
                            ? OccurrenceTime.Unknown
                            : OccurrenceTime.At(
                                observedAt.AddMinutes(chamber),
                                TimePrecision.Second))))],
            new RecordProvenance(
                DataOrigin.OfficialApi,
                new RecordTimestamps(
                    ObservedAt: observedAt,
                    FetchedAt: observedAt.AddSeconds(20)),
                new DataSourceReference(
                    "phase8b7-synthetic-provider",
                    sourceRecordId: $"abyss-{sequence}")));

        private static Guid SequenceGuid(int prefix, int sequence) =>
            Guid.Parse($"{prefix:D8}-0000-0000-0000-{sequence:D12}");

        private static T AssertSingle<T>(IReadOnlyList<T> items) =>
            items.Count == 1
                ? items[0]
                : throw new InvalidOperationException(
                    $"Expected one item, received {items.Count}.");

        private static void EnsureApplied(
            ArchiveOperationResult result,
            string operation)
        {
            if (result.Status != ChangeExecutionStatus.Applied)
            {
                throw new InvalidOperationException(
                    $"The synthetic {operation} operation returned {result.Status}.");
            }
        }

        public async ValueTask DisposeAsync()
        {
            await Database.DisposeAsync();
            Directory.Delete(directory, recursive: true);
        }
    }
}
