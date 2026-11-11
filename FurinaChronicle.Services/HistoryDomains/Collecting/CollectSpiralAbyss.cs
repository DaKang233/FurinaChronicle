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

public enum SpiralAbyssCollectionStatus
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
    NotUnlocked,
}

public sealed record SpiralAbyssSnapshot(
    string SeasonId,
    DateTimeOffset? PeriodStart,
    DateTimeOffset? PeriodEnd,
    IReadOnlyList<SpiralAbyssFloorResult> Floors,
    DateTimeOffset FetchedAt);

public sealed record SpiralAbyssSourceResult(
    SpiralAbyssCollectionStatus Status,
    SpiralAbyssSnapshot? Snapshot = null,
    TimeSpan? RetryAfter = null,
    int? HttpStatus = null,
    int? ProviderCode = null);

public interface ISpiralAbyssSource
{
    Task<SpiralAbyssSourceResult> FetchCurrentAsync(
        PassportAccount passport,
        GameRoleNaturalIdentity role,
        CancellationToken cancellationToken);
}

public sealed record SpiralAbyssCollectionResult(
    SpiralAbyssCollectionStatus Status,
    ArchiveOperationResult? Commit = null,
    TimeSpan? RetryAfter = null,
    int? HttpStatus = null,
    int? ProviderCode = null);

/// <summary>
/// Explicit, single-copy collection. The host owns credential selection and
/// must keep this use case isolated until live transport and deletion gates pass.
/// </summary>
public sealed class CollectSpiralAbyss(
    IGameAccountRepository accounts,
    IMiHoYoAccountProfileClient profiles,
    ISpiralAbyssSource source,
    IArchiveDomainHistoryStore history,
    TimeProvider timeProvider)
{
    public async Task<SpiralAbyssCollectionResult> ExecuteAsync(
        Guid archiveId,
        Guid gameAccountId,
        PassportAccount passport,
        CancellationToken cancellationToken = default)
    {
        if (passport.Realm != PassportRealm.MainlandChina)
        {
            return new(SpiralAbyssCollectionStatus.UnsupportedRealm);
        }

        DateTimeOffset startedAt = timeProvider.GetUtcNow();
        GameAccount? account = await accounts.GetByIdAsync(
            gameAccountId, cancellationToken);
        if (account?.PlayerArchiveId != archiveId ||
            account.RoleIdentity is not { } identity)
        {
            return new(SpiralAbyssCollectionStatus.TargetUnavailable);
        }

        GameRoleNaturalIdentity role = identity.NaturalIdentity;
        if (role.GameBiz != "hk4e_cn")
        {
            return new(SpiralAbyssCollectionStatus.UnsupportedRealm);
        }

        PassportAccountProfile profile = await profiles.GetAsync(
            passport, cancellationToken);
        if (profile.Roles.Count(candidate => Matches(candidate, role)) != 1)
        {
            return new(SpiralAbyssCollectionStatus.RoleNotBound);
        }

        SpiralAbyssSourceResult fetched = await source.FetchCurrentAsync(
            passport, role, cancellationToken);
        if (fetched.Status != SpiralAbyssCollectionStatus.Received)
        {
            return new(fetched.Status, RetryAfter: fetched.RetryAfter,
                HttpStatus: fetched.HttpStatus, ProviderCode: fetched.ProviderCode);
        }
        if (fetched.Snapshot is not { } snapshot)
        {
            return new(SpiralAbyssCollectionStatus.InvalidResponse);
        }

        var observation = new ChallengeObservation(
            ChallengeObservationId.New(),
            gameAccountId,
            ChallengeMode.SpiralAbyss,
            snapshot.SeasonId,
            snapshot.PeriodStart,
            snapshot.PeriodEnd,
            DataCompleteness.Partial,
            snapshot.Floors,
            new RecordProvenance(
                DataOrigin.OfficialApi,
                new RecordTimestamps(
                    ObservedAt: snapshot.FetchedAt,
                    FetchedAt: snapshot.FetchedAt),
                new DataSourceReference(provider: "miyoushe"),
                AcquisitionBatchId.New()));

        ArchiveOperationResult committed = await history.CommitChallengeObservationsAsync(
            new ChallengeObservationCommitRequest(
                OperationId.New(),
                DataChangeOperationKind.Refresh,
                DataOrigin.OfficialApi,
                "Refresh Spiral Abyss",
                startedAt,
                timeProvider.GetUtcNow(),
                [observation],
                expectedTarget: account),
            cancellationToken);

        return new(SpiralAbyssCollectionStatus.Received, committed);
    }

    private static bool Matches(PassportGameRole candidate, GameRoleNaturalIdentity role) =>
        string.Equals(candidate.GameBiz.Trim(), role.GameBiz, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(candidate.Region.Trim(), role.Server, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(candidate.Uid.Trim(), role.Uid, StringComparison.Ordinal);
}
