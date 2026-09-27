using FurinaChronicle.Core.Passport;

namespace FurinaChronicle.Services.Passport;

public sealed class PassportAccountWriter(
    IPassportAccountStore accountStore,
    IMainlandPassportClient mainlandClient,
    IHoYoLabPassportClient hoYoLabClient,
    TimeProvider timeProvider)
{
    public async Task<PassportAccount> SaveAsync(
        PassportLoginTokens tokens,
        PassportLoginMethod method,
        PassportRealm realm,
        PassportDeviceIdentity device,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        DateTimeOffset now = timeProvider.GetUtcNow();
        PassportAccount? existing = (await accountStore.GetAllAsync(cancellationToken))
            .FirstOrDefault(account =>
                account.Realm == realm &&
                string.Equals(account.Aid, tokens.Aid, StringComparison.Ordinal));
        PassportCredentials? previous = existing?.Credentials;
        bool hasSToken = !string.IsNullOrWhiteSpace(tokens.SToken);
        bool hasLToken = !string.IsNullOrWhiteSpace(tokens.LToken);
        bool hasCookieToken = !string.IsNullOrWhiteSpace(tokens.CookieToken);
        var credentials = new PassportCredentials(
            hasSToken ? tokens.SToken : previous?.SToken,
            hasLToken ? tokens.LToken : previous?.LToken,
            hasCookieToken ? tokens.CookieToken : previous?.CookieToken,
            hasSToken ? now : previous?.STokenUpdatedAt,
            hasLToken ? now : previous?.LTokenUpdatedAt,
            hasCookieToken ? now : previous?.CookieTokenUpdatedAt,
            hasSToken ? now : previous?.SessionVerifiedAt);
        PassportDeviceIdentity stableDevice = existing?.Device ?? device;
        var account = new PassportAccount(
            existing?.Id ?? Guid.NewGuid(),
            tokens.Aid,
            string.IsNullOrWhiteSpace(tokens.Mid) ? existing?.Mid : tokens.Mid,
            tokens.DisplayName ?? existing?.DisplayName,
            method,
            credentials,
            stableDevice,
            existing?.CreatedAt ?? now,
            now,
            realm);

        if (account.Credentials.SToken is not null && account.Mid is not null &&
            (account.Credentials.LToken is null ||
             account.Credentials.CookieToken is null))
        {
            try
            {
                PassportDerivedTokens derived = realm switch
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
                        nameof(realm),
                        realm,
                        "Unsupported passport realm.")
                };
                credentials = credentials.WithDerivedTokens(
                    derived.LToken,
                    derived.CookieToken,
                    now);
                account = account.WithCredentials(credentials, now);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Persist the root SToken first; credential maintenance can
                // retry the derived-token exchange without another login.
            }
        }

        await accountStore.SaveAsync(account, cancellationToken);
        return account;
    }
}
