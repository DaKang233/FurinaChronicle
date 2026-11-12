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

public sealed record ChallengeModeValidationResult(
    ChallengeModeCollectionResult First,
    ChallengeModeCollectionResult? Repeated,
    IReadOnlyList<ChallengeObservation> ReopenedObservations);

/// <summary>
/// Isolated live validation for a non-Abyss challenge mode. The business
/// database is never touched by the validation entry.
/// </summary>
public sealed class ValidateChallengeModeCollection(
    IMiHoYoAccountProfileClient profile,
    IChallengeModeSource source,
    TimeProvider clock)
{
    public async Task<ChallengeModeValidationResult> ExecuteAsync(
        GameAccount target,
        PassportAccount passport,
        ChallengeMode mode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();
        DateTimeOffset now = clock.GetUtcNow();
        var archive = new PlayerArchive(
            Guid.NewGuid(), "Challenge mode validation", now, now);
        var copy = target with
        {
            Id = Guid.NewGuid(),
            PlayerArchiveId = archive.Id,
            DisplayName = null,
            CreatedAt = now,
            UpdatedAt = now,
        };

        DirectoryInfo directory = Directory.CreateTempSubdirectory(
            "furina-challenge-mode-");
        string path = Path.Combine(directory.FullName, "validation.db");
        try
        {
            ChallengeModeCollectionResult first;
            ChallengeModeCollectionResult repeated;
            await using (var database = new FurinaDatabase(
                new SqliteDatabaseOptions(path)))
            {
                var accounts = new SqliteGameAccountRepository(database);
                var history = new SqliteGachaAtomicChangeStore(database);
                await new SqlitePlayerArchiveRepository(database)
                    .AddAsync(archive, cancellationToken);
                await accounts.AddAsync(copy, cancellationToken);

                var collect = new CollectChallengeMode(
                    accounts, profile, source, history, clock);
                first = await collect.ExecuteAsync(
                    archive.Id, copy.Id, passport, mode, cancellationToken);
                if (first.Commit?.Status != ChangeExecutionStatus.Applied)
                {
                    return new(first, null, []);
                }

                repeated = await collect.ExecuteAsync(
                    archive.Id, copy.Id, passport, mode, cancellationToken);
            }

            await using var reopened = new FurinaDatabase(
                new SqliteDatabaseOptions(path));
            var observations = await new SqliteGachaAtomicChangeStore(reopened)
                .GetChallengeObservationsAsync(
                    copy.Id, mode, cancellationToken);
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
