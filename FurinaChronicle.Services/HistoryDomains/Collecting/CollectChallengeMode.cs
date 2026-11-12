// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.History;
using FurinaChronicle.Core.Passport;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Services.Abstractions;
using FurinaChronicle.Services.History;
using FurinaChronicle.Services.Passport;

namespace FurinaChronicle.Services.HistoryDomains.Collecting;

/// <summary>
/// One non-Abyss challenge observation. The theater exposes round medals and
/// finish times; the Stygian onslaught exposes per-challenge seconds, so both
/// use the shared stage shape instead of forcing an Abyss floor structure.
/// </summary>
public sealed record ChallengeModeSnapshot(
    ChallengeMode Mode,
    string SeasonId,
    DateTimeOffset? PeriodStart,
    DateTimeOffset? PeriodEnd,
    DataCompleteness Completeness,
    IReadOnlyList<ChallengeStageResult> Stages,
    int? Difficulty,
    int? MedalCount,
    DateTimeOffset FetchedAt);

public sealed record ChallengeModeSourceResult(
    SpiralAbyssCollectionStatus Status,
    ChallengeModeSnapshot? Snapshot = null,
    TimeSpan? RetryAfter = null,
    int? HttpStatus = null,
    int? ProviderCode = null);

public interface IChallengeModeSource
{
    Task<ChallengeModeSourceResult> FetchAsync(
        ChallengeMode mode,
        PassportAccount passport,
        GameRoleNaturalIdentity role,
        CancellationToken cancellationToken = default);
}

public sealed record ChallengeModeCollectionResult(
    SpiralAbyssCollectionStatus Status,
    ArchiveOperationResult? Commit = null,
    TimeSpan? RetryAfter = null,
    int? HttpStatus = null,
    int? ProviderCode = null);

/// <summary>
/// Explicit, single-copy collection for the theater and the Stygian onslaught.
/// Role ownership is re-checked before any transport request.
/// </summary>
public sealed class CollectChallengeMode(
    IGameAccountRepository accounts,
    IMiHoYoAccountProfileClient profiles,
    IChallengeModeSource source,
    IArchiveDomainHistoryStore history,
    TimeProvider timeProvider)
{
    public async Task<ChallengeModeCollectionResult> ExecuteAsync(
        Guid archiveId,
        Guid gameAccountId,
        PassportAccount passport,
        ChallengeMode mode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(passport);
        if (mode is not (
            ChallengeMode.ImaginariumTheater or
            ChallengeMode.StygianOnslaught))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }
        cancellationToken.ThrowIfCancellationRequested();
        DateTimeOffset startedAt = timeProvider.GetUtcNow();
        GameAccount? account = await accounts.GetByIdAsync(
            gameAccountId, cancellationToken);
        if (account?.PlayerArchiveId != archiveId ||
            account.RoleIdentity is not { } identity)
        {
            return new(SpiralAbyssCollectionStatus.TargetUnavailable);
        }

        GameRoleNaturalIdentity role = identity.NaturalIdentity;
        bool isOversea = passport.Realm == PassportRealm.Oversea;
        string gameBiz = isOversea
            ? GenshinGameRoleIdentity.GlobalGameBiz
            : GenshinGameRoleIdentity.MainlandGameBiz;
        if (role.GameBiz != gameBiz)
        {
            return new(SpiralAbyssCollectionStatus.UnsupportedRealm);
        }

        PassportAccountProfile profile = await profiles.GetAsync(
            passport, cancellationToken);
        if (profile.Roles.Count(candidate => Matches(candidate, role)) != 1)
        {
            return new(SpiralAbyssCollectionStatus.RoleNotBound);
        }

        ChallengeModeSourceResult fetched = await source.FetchAsync(
            mode, passport, role, cancellationToken);
        if (fetched.Status != SpiralAbyssCollectionStatus.Received)
        {
            return new(
                fetched.Status,
                RetryAfter: fetched.RetryAfter,
                HttpStatus: fetched.HttpStatus,
                ProviderCode: fetched.ProviderCode);
        }
        if (fetched.Snapshot is not { } snapshot ||
            snapshot.Mode != mode)
        {
            return new(SpiralAbyssCollectionStatus.InvalidResponse);
        }
        if (snapshot.Stages.Count == 0)
        {
            return new(SpiralAbyssCollectionStatus.Empty);
        }

        var observation = new ChallengeObservation(
            ChallengeObservationId.New(),
            gameAccountId,
            snapshot.Mode,
            snapshot.SeasonId,
            snapshot.PeriodStart,
            snapshot.PeriodEnd,
            snapshot.Completeness,
            spiralAbyssFloors: [],
            new RecordProvenance(
                DataOrigin.OfficialApi,
                new RecordTimestamps(
                    ObservedAt: snapshot.FetchedAt,
                    FetchedAt: snapshot.FetchedAt),
                new DataSourceReference(
                    provider: isOversea ? "hoyolab" : "miyoushe"),
                AcquisitionBatchId.New()),
            snapshot.Stages,
            snapshot.Difficulty,
            snapshot.MedalCount);

        ArchiveOperationResult committed =
            await history.CommitChallengeObservationsAsync(
                new ChallengeObservationCommitRequest(
                    OperationId.New(),
                    DataChangeOperationKind.Refresh,
                    DataOrigin.OfficialApi,
                    $"Refresh {snapshot.Mode}",
                    startedAt,
                    timeProvider.GetUtcNow(),
                    [observation],
                    expectedTarget: account),
                cancellationToken);

        return new(SpiralAbyssCollectionStatus.Received, committed);
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
        string.Equals(candidate.Uid.Trim(), role.Uid, StringComparison.Ordinal);
}
