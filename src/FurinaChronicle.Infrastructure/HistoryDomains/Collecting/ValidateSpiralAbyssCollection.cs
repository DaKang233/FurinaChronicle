// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.History;
using FurinaChronicle.Core.Passport;
using FurinaChronicle.Infrastructure.Persistence.Sqlite;
using FurinaChronicle.Services.HistoryDomains.Collecting;
using FurinaChronicle.Services.Passport;

namespace FurinaChronicle.Infrastructure.HistoryDomains.Collecting;

public sealed record SpiralAbyssValidationResult(
    SpiralAbyssCollectionResult First,
    SpiralAbyssCollectionResult? Repeated,
    IReadOnlyList<ChallengeObservation> ReopenedObservations);

public sealed class ValidateSpiralAbyssCollection(
    IMiHoYoAccountProfileClient profile,
    ISpiralAbyssSource source,
    TimeProvider clock)
{
    public async Task<SpiralAbyssValidationResult> ExecuteAsync(
        GameAccount target,
        PassportAccount passport,
        SpiralAbyssPeriod period,
        CancellationToken cancellationToken = default,
        GameRecordVerificationPrompt? verificationPrompt = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = clock.GetUtcNow();
        var archive = new PlayerArchive(Guid.NewGuid(), "Spiral Abyss validation", now, now);
        var copy = target with
        {
            Id = Guid.NewGuid(),
            PlayerArchiveId = archive.Id,
            DisplayName = null,
            CreatedAt = now,
            UpdatedAt = now,
        };

        DirectoryInfo directory = Directory.CreateTempSubdirectory("furina-challenge-");
        string path = Path.Combine(directory.FullName, "validation.db");
        try
        {
            SpiralAbyssCollectionResult first;
            SpiralAbyssCollectionResult repeated;
            await using (var database = new FurinaDatabase(new SqliteDatabaseOptions(path)))
            {
                var accounts = new SqliteGameAccountRepository(database);
                var history = new SqliteGachaAtomicChangeStore(database);
                await new SqlitePlayerArchiveRepository(database).AddAsync(archive, cancellationToken);
                await accounts.AddAsync(copy, cancellationToken);

                var collect = new CollectSpiralAbyss(accounts, profile, source, history, clock);
                first = await collect.ExecuteAsync(archive.Id, copy.Id, passport, cancellationToken, period, verificationPrompt);
                if (first.Commit?.Status != ChangeExecutionStatus.Applied)
                {
                    return new(first, null, []);
                }

                await Task.Delay(TimeSpan.FromSeconds(3), clock, cancellationToken);
                repeated = await collect.ExecuteAsync(archive.Id, copy.Id, passport, cancellationToken, period);
            }

            await using var reopened = new FurinaDatabase(new SqliteDatabaseOptions(path));
            var observations = await new SqliteGachaAtomicChangeStore(reopened)
                .GetChallengeObservationsAsync(copy.Id, cancellationToken: cancellationToken);
            return new(first, repeated, observations);
        }
        finally
        {
            foreach (string suffix in new[] { "", "-wal", "-shm", ".initialize.lock" })
            {
                File.Delete(path + suffix);
            }
            directory.Delete();
        }
    }
}
