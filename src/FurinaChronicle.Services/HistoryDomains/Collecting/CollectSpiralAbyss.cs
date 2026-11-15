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
    VerificationCanceled,
    VerificationFailed,
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
    int? ProviderCode = null,
    GameRecordVerificationStage? VerificationStage = null,
    bool? VerificationSessionCookieSent = null);

public enum SpiralAbyssPeriod
{
    Current = 1,
    Previous = 2,
}

public enum GameRecordVerificationStage
{
    Registration,
    Submission,
    Retry,
}

public delegate Task<PassportGeetestResult?> GameRecordVerificationPrompt(
    PassportGeetestChallenge challenge, CancellationToken cancellationToken);

public interface ISpiralAbyssSource
{
    Task<SpiralAbyssSourceResult> FetchAsync(
        PassportAccount passport,
        GameRoleNaturalIdentity role,
        SpiralAbyssPeriod period,
        CancellationToken cancellationToken,
        GameRecordVerificationPrompt? verificationPrompt = null);
}

public sealed record SpiralAbyssCollectionResult(
    SpiralAbyssCollectionStatus Status,
    ArchiveOperationResult? Commit = null,
    TimeSpan? RetryAfter = null,
    int? HttpStatus = null,
    int? ProviderCode = null,
    GameRecordVerificationStage? VerificationStage = null,
    bool? VerificationSessionCookieSent = null);

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
        CancellationToken cancellationToken = default,
        SpiralAbyssPeriod period = SpiralAbyssPeriod.Current,
        GameRecordVerificationPrompt? verificationPrompt = null)
    {

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
        string gameBiz = isOversea ? GenshinGameRoleIdentity.GlobalGameBiz : GenshinGameRoleIdentity.MainlandGameBiz;
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

        SpiralAbyssSourceResult fetched = await source.FetchAsync(
            passport, role, period, cancellationToken, verificationPrompt);
        if (fetched.Status != SpiralAbyssCollectionStatus.Received)
        {
            return new(fetched.Status, RetryAfter: fetched.RetryAfter,
                HttpStatus: fetched.HttpStatus, ProviderCode: fetched.ProviderCode,
                VerificationStage: fetched.VerificationStage,
                VerificationSessionCookieSent: fetched.VerificationSessionCookieSent);
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
                new DataSourceReference(provider: isOversea ? "hoyolab" : "miyoushe"),
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

        return new(SpiralAbyssCollectionStatus.Received, committed, VerificationStage: fetched.VerificationStage,
                VerificationSessionCookieSent: fetched.VerificationSessionCookieSent);
    }

    private static bool Matches(PassportGameRole candidate, GameRoleNaturalIdentity role) =>
        string.Equals(candidate.GameBiz.Trim(), role.GameBiz, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(candidate.Region.Trim(), role.Server, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(candidate.Uid.Trim(), role.Uid, StringComparison.Ordinal);
}
