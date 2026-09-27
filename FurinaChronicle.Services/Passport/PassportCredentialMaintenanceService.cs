using FurinaChronicle.Core.Passport;

namespace FurinaChronicle.Services.Passport;

public sealed class PassportCredentialMaintenanceService(
    IPassportAccountStore accountStore,
    IMainlandPassportClient mainlandClient,
    IHoYoLabPassportClient hoYoLabClient,
    TimeProvider timeProvider,
    PassportCredentialMaintenanceOptions? maintenanceOptions = null)
{
    private readonly PassportCredentialMaintenanceOptions options =
        maintenanceOptions ?? new PassportCredentialMaintenanceOptions();

    public async Task<PassportMaintenanceResult> MaintainAllAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<PassportAccount> accounts =
            await accountStore.GetAllAsync(cancellationToken);
        var failures = new List<Guid>();
        int updatedCount = 0;

        foreach (PassportAccount account in accounts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MaintenanceOutcome outcome = await MaintainAsync(
                account,
                cancellationToken);
            if (outcome.Changed)
            {
                updatedCount++;
            }
            if (outcome.Failed)
            {
                failures.Add(account.Id);
            }
        }

        return new PassportMaintenanceResult(
            accounts.Count,
            updatedCount,
            failures);
    }

    private async Task<MaintenanceOutcome> MaintainAsync(
        PassportAccount original,
        CancellationToken cancellationToken)
    {
        PassportAccount account = original;
        PassportCredentials credentials = account.Credentials;
        if (credentials.SToken is null || account.Mid is null)
        {
            return new MaintenanceOutcome(Changed: false, Failed: false);
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        bool changed = false;
        bool failed = false;
        if (account.Realm == PassportRealm.MainlandChina &&
            IsExpired(
                credentials.SessionVerifiedAt,
                options.SessionVerificationInterval,
                now))
        {
            try
            {
                PassportSessionVerification verification =
                    await mainlandClient.VerifySessionAsync(
                        account,
                        cancellationToken);
                credentials = credentials.WithVerifiedSession(
                    verification.RefreshedSToken,
                    now);
                account = account.WithCredentials(credentials, now);
                changed = true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Verification failure does not prevent token exchange.
                failed = true;
            }
        }

        bool derivedExpired =
            credentials.LToken is null ||
            credentials.CookieToken is null ||
            IsExpired(
                credentials.LTokenUpdatedAt,
                options.DerivedCredentialMaxAge,
                now) ||
            IsExpired(
                credentials.CookieTokenUpdatedAt,
                options.DerivedCredentialMaxAge,
                now);

        if (derivedExpired)
        {
            try
            {
                PassportDerivedTokens derived = account.Realm switch
                {
                    PassportRealm.MainlandChina =>
                        await mainlandClient.GetDerivedTokensAsync(
                            account,
                            cancellationToken),
                    PassportRealm.Oversea =>
                        await hoYoLabClient.GetDerivedTokensAsync(
                            account,
                            cancellationToken),
                    _ => throw new ArgumentOutOfRangeException(
                        nameof(account.Realm),
                        account.Realm,
                        "Unsupported passport realm.")
                };
                if (derived.LToken is not null ||
                    derived.CookieToken is not null)
                {
                    credentials = credentials.WithDerivedTokens(
                        derived.LToken,
                        derived.CookieToken,
                        now);
                    account = account.WithCredentials(credentials, now);
                    changed = true;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                failed = true;
            }
        }

        if (changed)
        {
            try
            {
                bool persisted = await accountStore.TrySaveIfUnchangedAsync(
                    original,
                    account,
                    cancellationToken);
                if (!persisted)
                {
                    // Interactive login or deletion won the race. The stale
                    // maintenance result must never overwrite that mutation.
                    return new MaintenanceOutcome(
                        Changed: false,
                        Failed: false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                return new MaintenanceOutcome(
                    Changed: false,
                    Failed: true);
            }
        }

        return new MaintenanceOutcome(changed, failed);
    }

    private static bool IsExpired(
        DateTimeOffset? timestamp,
        TimeSpan maxAge,
        DateTimeOffset now)
    {
        return timestamp is null || now - timestamp.Value >= maxAge;
    }

    private sealed record MaintenanceOutcome(bool Changed, bool Failed);
}
