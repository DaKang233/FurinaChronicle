// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Infrastructure.Gacha.Portable;
using FurinaChronicle.Services.Gacha.Portable;

namespace FurinaChronicle.Tests.Infrastructure.Gacha.Portable;

public sealed class GachaPortableStreamingWriterTests
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
