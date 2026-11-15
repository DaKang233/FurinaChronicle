// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.History;
using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Core.Passport;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Services.Abstractions;
using FurinaChronicle.Services.History;
using FurinaChronicle.Services.Passport;

namespace FurinaChronicle.Services.HistoryDomains.Collecting;

public enum CharacterWeaponCollectionStatus
{
    Received,
    Empty,
    UnsupportedRealm,
    TargetUnavailable,
    RoleNotBound,
    ResponseRoleMismatch,
    AuthenticationRequired,
    AccessDenied,
    RateLimited,
    Unavailable,
    SourceRejected,
    InvalidResponse,
    VerificationRequired,
}

/// <summary>
/// One official character list: the roster is complete for characters while
/// weapons only cover the equipped ones, so the two kinds keep separate scope.
/// </summary>
public sealed record CharacterWeaponSnapshot(
    IReadOnlyList<InventoryStateEntry> Characters,
    DataCompleteness CharacterCompleteness,
    IReadOnlyList<InventoryStateEntry> Weapons,
    DataCompleteness WeaponCompleteness,
    DateTimeOffset FetchedAt);

public sealed record CharacterWeaponSourceResult(
    CharacterWeaponCollectionStatus Status,
    CharacterWeaponSnapshot? Snapshot = null,
    TimeSpan? RetryAfter = null,
    int? HttpStatus = null,
    int? ProviderCode = null);

public sealed record CharacterWeaponCollectionResult(
    CharacterWeaponCollectionStatus Status,
    ArchiveOperationResult? Commit = null,
    TimeSpan? RetryAfter = null,
    int? HttpStatus = null,
    int? ProviderCode = null,
    CharacterWeaponSnapshot? Snapshot = null);

public interface ICharacterWeaponSource
{
    Task<CharacterWeaponSourceResult> FetchAsync(
        PassportAccount passport,
        GameRoleNaturalIdentity role,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Explicit, single-copy character and weapon state collection. The host owns
/// credential selection; the use case validates role ownership and commits
/// immutable observations into one archive account copy.
/// </summary>
public sealed class CollectCharacterWeapon(
    IGameAccountRepository accounts,
    IMiHoYoAccountProfileClient profiles,
    ICharacterWeaponSource source,
    IArchiveDomainHistoryStore history,
    TimeProvider timeProvider)
{
    public async Task<CharacterWeaponCollectionResult> ExecuteAsync(
        Guid archiveId,
        Guid gameAccountId,
        PassportAccount passport,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(passport);
        cancellationToken.ThrowIfCancellationRequested();
        DateTimeOffset startedAt = timeProvider.GetUtcNow();
        GameAccount? account = await accounts.GetByIdAsync(
            gameAccountId, cancellationToken);
        if (account?.PlayerArchiveId != archiveId ||
            account.RoleIdentity is not { } identity)
        {
            return new(CharacterWeaponCollectionStatus.TargetUnavailable);
        }

        GameRoleNaturalIdentity role = identity.NaturalIdentity;
        bool isOversea = passport.Realm == PassportRealm.Oversea;
        string gameBiz = isOversea
            ? GenshinGameRoleIdentity.GlobalGameBiz
            : GenshinGameRoleIdentity.MainlandGameBiz;
        if (role.GameBiz != gameBiz)
        {
            return new(CharacterWeaponCollectionStatus.UnsupportedRealm);
        }

        PassportAccountProfile profile = await profiles.GetAsync(
            passport, cancellationToken);
        if (profile.Roles.Count(candidate => Matches(candidate, role)) != 1)
        {
            return new(CharacterWeaponCollectionStatus.RoleNotBound);
        }

        CharacterWeaponSourceResult fetched = await source.FetchAsync(
            passport, role, cancellationToken);
        if (fetched.Status != CharacterWeaponCollectionStatus.Received)
        {
            return new(
                fetched.Status,
                RetryAfter: fetched.RetryAfter,
                HttpStatus: fetched.HttpStatus,
                ProviderCode: fetched.ProviderCode);
        }
        if (fetched.Snapshot is not { } snapshot)
        {
            return new(CharacterWeaponCollectionStatus.InvalidResponse);
        }
        if (snapshot.Characters.Count == 0 && snapshot.Weapons.Count == 0)
        {
            return new(CharacterWeaponCollectionStatus.Empty);
        }

        ItemStateObservation? latestCharacters =
            (await history.GetItemObservationPageAsync(
                new ItemObservationPageQuery(
                    gameAccountId,
                    InventoryItemKind.Character,
                    Limit: 1),
                cancellationToken)).Items.FirstOrDefault();
        ItemStateObservation? latestWeapons =
            (await history.GetItemObservationPageAsync(
                new ItemObservationPageQuery(
                    gameAccountId,
                    InventoryItemKind.Weapon,
                    Limit: 1),
                cancellationToken)).Items.FirstOrDefault();
        bool charactersChanged = latestCharacters is null || !IsSameState(
            latestCharacters, snapshot.CharacterCompleteness, snapshot.Characters);
        bool weaponsChanged = latestWeapons is null || !IsSameState(
            latestWeapons, snapshot.WeaponCompleteness, snapshot.Weapons);
        if (!charactersChanged && !weaponsChanged)
        {
            return new(
                CharacterWeaponCollectionStatus.Received,
                new ArchiveOperationResult(
                    ChangeExecutionStatus.NoOp,
                    ChangeSetId: null,
                    AffectedRecordCount: 0,
                    ConflictReason: "Character and weapon state is unchanged."),
                Snapshot: snapshot);
        }

        var provenance = new RecordProvenance(
            DataOrigin.OfficialApi,
            new RecordTimestamps(
                ObservedAt: snapshot.FetchedAt,
                FetchedAt: snapshot.FetchedAt),
            new DataSourceReference(
                provider: isOversea ? "hoyolab" : "miyoushe"),
            AcquisitionBatchId.New());
        var observations = new List<ItemStateObservation>(2);
        if (charactersChanged)
        {
            observations.Add(new ItemStateObservation(
                ItemObservationId.New(),
                gameAccountId,
                InventoryItemKind.Character,
                snapshot.CharacterCompleteness,
                snapshot.Characters,
                provenance));
        }
        if (weaponsChanged)
        {
            observations.Add(new ItemStateObservation(
                ItemObservationId.New(),
                gameAccountId,
                InventoryItemKind.Weapon,
                snapshot.WeaponCompleteness,
                snapshot.Weapons,
                provenance));
        }

        ArchiveOperationResult committed = await history.CommitItemObservationsAsync(
            new ItemObservationCommitRequest(
                OperationId.New(),
                DataChangeOperationKind.Refresh,
                DataOrigin.OfficialApi,
                "Refresh character and weapon state",
                startedAt,
                timeProvider.GetUtcNow(),
                observations),
            cancellationToken);

        return new(
            CharacterWeaponCollectionStatus.Received,
            committed,
            Snapshot: snapshot);
    }

    private static bool IsSameState(
        ItemStateObservation previous,
        DataCompleteness completeness,
        IReadOnlyList<InventoryStateEntry> entries)
    {
        if (previous.CollectionCompleteness != completeness ||
            previous.Entries.Count != entries.Count)
        {
            return false;
        }

        var current = entries.ToDictionary(
            entry => entry.StableItemKey,
            StringComparer.Ordinal);
        foreach (InventoryStateEntry earlier in previous.Entries)
        {
            if (!current.TryGetValue(earlier.StableItemKey, out InventoryStateEntry? now) ||
                now.Quantity != earlier.Quantity ||
                now.Level != earlier.Level ||
                now.Progression != earlier.Progression ||
                now.FieldCoverage != earlier.FieldCoverage ||
                now.FieldCompleteness != earlier.FieldCompleteness)
            {
                return false;
            }
        }
        return true;
    }

    private static bool Matches(
        PassportGameRole candidate,
        GameRoleNaturalIdentity role) =>
        string.Equals(
            candidate.GameBiz.Trim(),
            role.GameBiz,
            StringComparison.OrdinalIgnoreCase) &&
        string.Equals(
            candidate.Region.Trim(),
            role.Server,
            StringComparison.OrdinalIgnoreCase) &&
        string.Equals(
            candidate.Uid.Trim(),
            role.Uid,
            StringComparison.Ordinal);
}
