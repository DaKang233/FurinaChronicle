// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.History;
using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Core.Passport;
using FurinaChronicle.Infrastructure.Persistence.Sqlite;
using FurinaChronicle.Services.HistoryDomains.Collecting;
using FurinaChronicle.Services.Passport;

namespace FurinaChronicle.Infrastructure.HistoryDomains.Collecting;

public sealed record CharacterWeaponValidationResult(
    CharacterWeaponCollectionResult First,
    CharacterWeaponCollectionResult? Repeated,
    IReadOnlyList<ItemStateObservation> ReopenedObservations);

/// <summary>
/// Isolated live validation for the character and weapon provider. The first
/// collection and the unchanged repeat run against a temporary archive copy so
/// the business database is never touched by the validation entry.
/// </summary>
public sealed class ValidateCharacterWeaponCollection(
    IMiHoYoAccountProfileClient profile,
    ICharacterWeaponSource source,
    TimeProvider clock)
{
    public async Task<CharacterWeaponValidationResult> ExecuteAsync(
        GameAccount target,
        PassportAccount passport,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();
        DateTimeOffset now = clock.GetUtcNow();
        var archive = new PlayerArchive(
            Guid.NewGuid(), "Character validation", now, now);
        var copy = target with
        {
            Id = Guid.NewGuid(),
            PlayerArchiveId = archive.Id,
            DisplayName = null,
            CreatedAt = now,
            UpdatedAt = now,
        };

        DirectoryInfo directory = Directory.CreateTempSubdirectory(
            "furina-character-");
        string path = Path.Combine(directory.FullName, "validation.db");
        try
        {
            CharacterWeaponCollectionResult first;
            CharacterWeaponCollectionResult repeated;
            await using (var database = new FurinaDatabase(
                new SqliteDatabaseOptions(path)))
            {
                var accounts = new SqliteGameAccountRepository(database);
                await new SqlitePlayerArchiveRepository(database)
                    .AddAsync(archive, cancellationToken);
                await accounts.AddAsync(copy, cancellationToken);

                var collect = new CollectCharacterWeapon(
                    accounts, profile, source,
                    new SqliteGachaAtomicChangeStore(database), clock);
                first = await collect.ExecuteAsync(
                    archive.Id, copy.Id, passport, cancellationToken);
                if (first.Commit?.Status != ChangeExecutionStatus.Applied)
                {
                    return new(first, null, []);
                }

                repeated = await collect.ExecuteAsync(
                    archive.Id, copy.Id, passport, cancellationToken);
            }

            await using var reopened = new FurinaDatabase(
                new SqliteDatabaseOptions(path));
            var observations = await new SqliteGachaAtomicChangeStore(reopened)
                .GetItemObservationsAsync(copy.Id, cancellationToken: cancellationToken);
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
