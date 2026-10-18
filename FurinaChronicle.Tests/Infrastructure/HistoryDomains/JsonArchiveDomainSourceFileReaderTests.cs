// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Text;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.History;
using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Infrastructure.HistoryDomains.Importing;
using FurinaChronicle.Infrastructure.HistoryDomains.Portable;
using FurinaChronicle.Services.History;
using FurinaChronicle.Services.HistoryDomains;
using FurinaChronicle.Services.HistoryDomains.Portable;
using FurinaChronicle.Tests.Infrastructure.Persistence.Sqlite;

namespace FurinaChronicle.Tests.Infrastructure.HistoryDomains;

public sealed class JsonArchiveDomainSourceFileReaderTests
{
    [Fact]
    public async Task CollectorFiles_FlowThroughSqliteAndIndependentExports()
    {
        await using SqliteRepositoryTestContext context =
            SqliteRepositoryTestContext.Create();
        await context.Database.InitializeAsync();
        PlayerArchive archive = SqliteRepositoryTestContext.CreateArchive();
        GameAccount account = SqliteRepositoryTestContext.CreateAccount(
            archive.Id);
        await context.Archives.AddAsync(archive);
        await context.Accounts.AddAsync(account);
        var reader = new JsonArchiveDomainSourceFileReader();

        IReadOnlyList<ItemStateObservation> items =
            await reader.ReadItemObservationsAsync(
                Utf8(ItemFixture),
                account.Id);
        IReadOnlyList<ChallengeObservation> challenges =
            await reader.ReadChallengeObservationsAsync(
                Utf8(ChallengeFixture),
                account.Id);
        Assert.Equal(
            ChangeExecutionStatus.Applied,
            (await context.AtomicGacha.CommitItemObservationsAsync(
                new ItemObservationCommitRequest(
                    OperationId.New(),
                    DataChangeOperationKind.Refresh,
                    DataOrigin.OfficialApi,
                    "collector item snapshot",
                    TestTime(),
                    TestTime().AddSeconds(1),
                    items))).Status);
        Assert.Equal(
            ChangeExecutionStatus.Applied,
            (await context.AtomicGacha.CommitChallengeObservationsAsync(
                new ChallengeObservationCommitRequest(
                    OperationId.New(),
                    DataChangeOperationKind.Refresh,
                    DataOrigin.OfficialApi,
                    "collector challenge snapshot",
                    TestTime(),
                    TestTime().AddSeconds(1),
                    challenges))).Status);

        var files = new ArchiveDomainPortableFileService(
            context.Archives,
            context.Accounts,
            context.AtomicGacha);
        using var itemExport = new MemoryStream();
        await files.ExportAsync(
            archive.Id,
            ArchiveDomainDatasetKind.CharacterWeaponHistory,
            itemExport,
            TestTime());
        itemExport.Position = 0;
        ArchiveDomainPortablePackage itemPackage =
            await files.ReadAsync(itemExport);
        Assert.Equal(2, itemPackage.Accounts.Single().ItemObservations.Count);

        using var challengeExport = new MemoryStream();
        await files.ExportAsync(
            archive.Id,
            ArchiveDomainDatasetKind.ChallengeHistory,
            challengeExport,
            TestTime());
        challengeExport.Position = 0;
        ArchiveDomainPortablePackage challengePackage =
            await files.ReadAsync(challengeExport);
        ChallengeObservation exported = Assert.Single(
            challengePackage.Accounts.Single().ChallengeObservations);
        Assert.Equal(OccurrenceTimeKind.Unknown,
            exported.SpiralAbyssFloors.Single().Chambers[1].CompletedAt.Kind);
        Assert.Equal(TimeSpan.FromHours(8),
            exported.Provenance.Timestamps.ObservedAt!.Value.Offset);
    }

    [Fact]
    public async Task CollectorFile_RejectsUnsupportedModeAndOversizeInput()
    {
        var reader = new JsonArchiveDomainSourceFileReader();
        string unsupported = ChallengeFixture.Replace(
            "\"mode\": 1",
            "\"mode\": 2",
            StringComparison.Ordinal);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            reader.ReadChallengeObservationsAsync(
                Utf8(unsupported),
                Guid.NewGuid()));

        byte[] oversized = new byte[
            JsonArchiveDomainSourceFileReader.MaximumFileBytes + 1];
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            reader.ReadItemObservationsAsync(
                new MemoryStream(oversized),
                Guid.NewGuid()));
    }

    private static MemoryStream Utf8(string value) =>
        new(Encoding.UTF8.GetBytes(value));

    private static DateTimeOffset TestTime() =>
        new(2026, 10, 18, 10, 0, 0, TimeSpan.FromHours(8));

    private const string ItemFixture = """
        {
          "format": "furina-collector-fixture",
          "format_major": 1,
          "dataset": "character-weapon",
          "item_observations": [
            {
              "observation_id": "11111111-1111-4111-8111-111111111111",
              "kind": 1,
              "completeness": 2,
              "entries": [
                {
                  "type_id": "CHAR_10000089",
                  "instance_id": null,
                  "quantity": 1,
                  "level": 90,
                  "progression": 2,
                  "field_coverage": 7,
                  "field_completeness": 2
                }
              ],
              "source": {
                "observed_at": "2026-10-18T10:00:00+08:00",
                "fetched_at": "2026-10-18T10:00:02+08:00",
                "provider": "miyoushe",
                "source_record_id": null,
                "source_snapshot_id": "characters-20261018",
                "acquisition_batch_id": "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"
              }
            },
            {
              "observation_id": "22222222-2222-4222-8222-222222222222",
              "kind": 2,
              "completeness": 1,
              "entries": [
                {
                  "type_id": "WEAPON_15514",
                  "instance_id": null,
                  "quantity": 2,
                  "level": null,
                  "progression": null,
                  "field_coverage": 1,
                  "field_completeness": 1
                }
              ],
              "source": {
                "observed_at": "2026-10-18T10:00:00+08:00",
                "fetched_at": null,
                "provider": "miyoushe",
                "source_record_id": null,
                "source_snapshot_id": "weapons-20261018",
                "acquisition_batch_id": null
              }
            }
          ],
          "challenge_observations": []
        }
        """;

    private const string ChallengeFixture = """
        {
          "format": "furina-collector-fixture",
          "format_major": 1,
          "dataset": "spiral-abyss",
          "item_observations": [],
          "challenge_observations": [
            {
              "observation_id": "33333333-3333-4333-8333-333333333333",
              "mode": 1,
              "season_id": "2026-10-upper",
              "period_start": "2026-10-16T04:00:00+08:00",
              "period_end": "2026-11-01T03:59:59+08:00",
              "completeness": 1,
              "floors": [
                {
                  "floor": 12,
                  "completeness": 1,
                  "chambers": [
                    {
                      "chamber": 1,
                      "stars": 3,
                      "completed_at": {
                        "kind": 1,
                        "start": "2026-10-18T09:30:15+08:00",
                        "end": null,
                        "precision": 1
                      }
                    },
                    {
                      "chamber": 2,
                      "stars": 2,
                      "completed_at": {
                        "kind": 0,
                        "start": null,
                        "end": null,
                        "precision": null
                      }
                    }
                  ]
                }
              ],
              "source": {
                "observed_at": "2026-10-18T10:00:00+08:00",
                "fetched_at": "2026-10-18T10:00:03+08:00",
                "provider": "miyoushe",
                "source_record_id": "abyss-2026-10-upper",
                "source_snapshot_id": null,
                "acquisition_batch_id": null
              }
            }
          ]
        }
        """;
}
