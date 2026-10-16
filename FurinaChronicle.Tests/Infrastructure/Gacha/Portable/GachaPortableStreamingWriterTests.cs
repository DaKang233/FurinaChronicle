// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Infrastructure.Gacha.Portable;
using FurinaChronicle.Infrastructure.Persistence;
using FurinaChronicle.Services.Gacha.Portable;
using FurinaChronicle.Tests.TestDoubles;
using Xunit.Abstractions;

namespace FurinaChronicle.Tests.Infrastructure.Gacha.Portable;

public sealed class GachaPortableStreamingWriterTests(ITestOutputHelper output)
{
    [Fact]
    public async Task WriteAsync_TwentyThousandGeneratedRecordsRemainSequential()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"furina-portable-{Guid.NewGuid():N}.fcgp");
        var snapshot = new GeneratedSnapshot(20_000);
        try
        {
            await using (var output = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                GachaPortableWriteResult result =
                    await new GachaPortablePackageWriter().WriteAsync(
                        output,
                        snapshot);
                Assert.Equal(20_000, result.RecordCount);
            }

            Assert.Equal(2, snapshot.EnumerationCount);
            Assert.Equal(1, snapshot.MaximumConcurrentEnumerations);
            Assert.True(new FileInfo(path).Length > 0);
        }
        finally
        {
            await snapshot.DisposeAsync();
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task IndexedReader_OneHundredThousandRecordsUsesBoundedManagedMemory()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"furina-portable-indexed-{Guid.NewGuid():N}");
        string staging = Path.Combine(directory, "session");
        string path = Path.Combine(directory, "source.fcgp");
        Directory.CreateDirectory(directory);
        var snapshot = new GeneratedSnapshot(100_000);
        try
        {
            await using (var destination = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await new GachaPortablePackageWriter().WriteAsync(
                    destination,
                    snapshot);
            }

            await using FileStream input = File.OpenRead(path);
            IGachaPortableStagedInput staged =
                await new FileGachaPortableInputStager(staging)
                    .StageAsync(input);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long baseline = GC.GetTotalMemory(forceFullCollection: true);
            long peak = baseline;
            Task<IGachaPortableIndexedPackage> opening =
                new GachaPortableIndexedPackageFactory(staging)
                    .OpenAsync(staged);
            while (!opening.IsCompleted)
            {
                peak = Math.Max(peak, GC.GetTotalMemory(false));
                await Task.Delay(5);
            }
            await using IGachaPortableIndexedPackage indexed = await opening;
            peak = Math.Max(peak, GC.GetTotalMemory(false));
            Task<GachaPortableImportPlan> planning =
                new PlanGachaPortableImport(
                    new InMemoryPlayerArchiveRepository(),
                    new InMemoryGameAccountRepository(),
                    new InMemoryGachaRecordRepository(),
                    TimeProvider.System)
                .ExecuteAsync(new GachaPortableIndexedImportPlanRequest(
                    indexed));
            while (!planning.IsCompleted)
            {
                peak = Math.Max(peak, GC.GetTotalMemory(false));
                await Task.Delay(5);
            }
            GachaPortableImportPlan plan = await planning;
            peak = Math.Max(peak, GC.GetTotalMemory(false));
            long peakDelta = Math.Max(0, peak - baseline);
            output.WriteLine(
                $"100k indexed import managed-memory delta: {peakDelta:N0} bytes; " +
                $"container: {new FileInfo(path).Length:N0} bytes.");

            Assert.Equal(100_000, indexed.RecordCount);
            Assert.Equal(100_000, Assert.Single(indexed.Accounts).RecordCount);
            Assert.Equal(100_000, plan.Preview.AddCount);
            Assert.Empty(Assert.Single(plan.Accounts).Conflicts);
            Assert.Empty(Assert.Single(plan.Accounts).Replacements);
            Assert.Equal(
                "0000000000000100000",
                Assert.Single(indexed.ReadRecordPage(
                    indexed.Accounts[0].AccountReference,
                    99_999,
                    1)).ExternalRecordId);
            Assert.True(
                peakDelta < 192L * 1024 * 1024,
                $"The 100k indexed reader used {peakDelta:N0} managed bytes above baseline.");

            await indexed.DisposeAsync();
            Assert.Empty(Directory.EnumerateFiles(staging));
        }
        finally
        {
            await snapshot.DisposeAsync();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task IndexedReader_CancellationCleansPartialIndexAndStagedInput()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"furina-portable-cancel-{Guid.NewGuid():N}");
        string staging = Path.Combine(directory, "session");
        string path = Path.Combine(directory, "source.fcgp");
        Directory.CreateDirectory(directory);
        var snapshot = new GeneratedSnapshot(100);
        try
        {
            await using (var outputStream = File.Create(path))
            {
                await new GachaPortablePackageWriter().WriteAsync(
                    outputStream,
                    snapshot);
            }
            await using FileStream input = File.OpenRead(path);
            IGachaPortableStagedInput staged =
                await new FileGachaPortableInputStager(staging)
                    .StageAsync(input);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                new GachaPortableIndexedPackageFactory(staging).OpenAsync(
                    staged,
                    cancellation.Token));
            await staged.DisposeAsync();

            Assert.Empty(Directory.EnumerateFiles(staging));
        }
        finally
        {
            await snapshot.DisposeAsync();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private sealed class GeneratedSnapshot : IGachaPortableExportSnapshot
    {
        private static readonly Guid AccountId = Guid.Parse(
            "20000000-0000-0000-0000-000000000001");
        private readonly int recordCount;
        private int activeEnumerations;

        public GeneratedSnapshot(int recordCount)
        {
            this.recordCount = recordCount;
            GameRoleNaturalIdentity naturalIdentity =
                GenshinGameRoleIdentity.Create(
                    "100000001",
                    GameServerRegion.ChinaOfficial);
            Accounts =
            [
                new GachaPortableExportAccount(
                    AccountId,
                    new GameRoleIdentity(
                        GameRoleIdentityId.FromNaturalIdentity(naturalIdentity),
                        naturalIdentity),
                    "Generated",
                    recordCount),
            ];
        }

        public DateTimeOffset GeneratedAt { get; } = new(
            2026,
            10,
            6,
            0,
            0,
            0,
            TimeSpan.Zero);

        public GachaPortableArchive SourceArchive { get; } = new(
            Guid.Parse("10000000-0000-0000-0000-000000000001"),
            "Generated Archive");

        public IReadOnlyList<GachaPortableExportAccount> Accounts { get; }

        public int EnumerationCount { get; private set; }

        public int MaximumConcurrentEnumerations { get; private set; }

        public async IAsyncEnumerable<GachaRecord> ReadRecordsAsync(
            Guid accountReference,
            [EnumeratorCancellation]
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(AccountId, accountReference);
            EnumerationCount++;
            int active = Interlocked.Increment(ref activeEnumerations);
            MaximumConcurrentEnumerations = Math.Max(
                MaximumConcurrentEnumerations,
                active);
            try
            {
                for (int index = 1; index <= recordCount; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return new GachaRecord(
                        AccountId,
                        index.ToString("D19"),
                        "Generated Item",
                        3,
                        GeneratedAt.AddSeconds(index))
                    {
                        ItemId = (10_000_000 + index).ToString(),
                        ItemType = "Weapon",
                        GachaType = "301",
                        UigfGachaType = "301",
                    };
                    if (index % 500 == 0)
                    {
                        await Task.Yield();
                    }
                }
            }
            finally
            {
                Interlocked.Decrement(ref activeEnumerations);
            }
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
