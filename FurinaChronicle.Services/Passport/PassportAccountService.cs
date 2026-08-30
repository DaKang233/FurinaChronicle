using FurinaChronicle.Core.Passport;

namespace FurinaChronicle.Services.Passport;

public sealed class PassportAccountService(
    IPassportAccountStore accountStore,
    IMiHoYoPassportClient passportClient,
    TimeProvider timeProvider,
    PassportCredentialMaintenanceOptions? maintenanceOptions = null)
{
    private readonly PassportCredentialMaintenanceOptions options =
        maintenanceOptions ?? new PassportCredentialMaintenanceOptions();

    public Task<PassportQrSession> BeginQrLoginAsync(
        CancellationToken cancellationToken = default)
    {
        return passportClient.CreateQrSessionAsync(
            PassportDeviceIdentity.Create(),
            cancellationToken);
    }

    public async Task<(PassportQrStatus Status, PassportAccount? Account)> PollQrLoginAsync(
        PassportQrSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        PassportQrPollResult result = await passportClient.PollQrSessionAsync(
            session,
            cancellationToken);

        if (result.Status != PassportQrStatus.Confirmed)
        {
            return (result.Status, null);
        }

        PassportLoginTokens tokens = result.Tokens
            ?? throw new InvalidDataException("Confirmed QR login did not return credentials.");
        PassportAccount account = await SaveNewAccountAsync(
            tokens,
            PassportLoginMethod.QrCode,
            PassportRealm.MainlandChina,
            new PassportDeviceIdentity(session.DeviceId, DeviceFingerprint: null),
            cancellationToken);
        return (result.Status, account);
    }

    public async Task<MobileCaptchaChallenge> SendMobileCaptchaAsync(
        string mobile,
        string? aigis = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mobile);
        PassportDeviceIdentity device = PassportDeviceIdentity.Create();
        MobileCaptchaChallenge challenge = await passportClient.SendMobileCaptchaAsync(
            mobile.Trim(),
            device,
            aigis,
            cancellationToken);
        return challenge with { DeviceId = device.DeviceId };
    }

    public async Task<PassportAccount> LoginWithMobileCaptchaAsync(
        string mobile,
        string captcha,
        MobileCaptchaChallenge challenge,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mobile);
        ArgumentException.ThrowIfNullOrWhiteSpace(captcha);
        ArgumentNullException.ThrowIfNull(challenge);

        PassportDeviceIdentity device = new(
            challenge.DeviceId,
            DeviceFingerprint: null);
        PassportLoginTokens tokens = await passportClient.LoginWithMobileCaptchaAsync(
            mobile.Trim(),
            captcha.Trim(),
            challenge,
            device,
            cancellationToken);
        return await SaveNewAccountAsync(
            tokens,
            PassportLoginMethod.MobileCaptcha,
            PassportRealm.MainlandChina,
            device,
            cancellationToken);
    }

    public async Task<PassportAccount> LoginWithOverseaPasswordAsync(
        string account,
        string password,
        CancellationToken cancellationToken = default)
    {
        return await LoginWithOverseaPasswordCoreAsync(
            account,
            password,
            verificationHandler: null,
            cancellationToken);
    }

    public async Task<PassportAccount> LoginWithOverseaPasswordAsync(
        string account,
        string password,
        IPassportSecurityVerificationHandler verificationHandler,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(verificationHandler);
        return await LoginWithOverseaPasswordCoreAsync(
            account,
            password,
            verificationHandler,
            cancellationToken);
    }

    private async Task<PassportAccount> LoginWithOverseaPasswordCoreAsync(
        string account,
        string password,
        IPassportSecurityVerificationHandler? verificationHandler,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(account);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        PassportDeviceIdentity device = PassportDeviceIdentity.CreateOversea();
        string? aigis = null;
        string? verify = null;

        for (int attemptNumber = 0; attemptNumber < 5; attemptNumber++)
        {
            OverseaPasswordLoginAttempt attempt =
                await passportClient.AttemptOverseaPasswordLoginAsync(
                account.Trim(),
                password,
                device,
                aigis,
                verify,
                cancellationToken);

            if (attempt.Tokens is { } tokens)
            {
                return await SaveNewAccountAsync(
                    tokens,
                    PassportLoginMethod.OverseaPassword,
                    PassportRealm.Oversea,
                    device,
                    cancellationToken);
            }

            if (verificationHandler is null)
            {
                throw CreateSecurityVerificationException(attempt);
            }

            if (attempt.GeetestChallenge is { } geetestChallenge)
            {
                PassportGeetestResult? result =
                    await verificationHandler.VerifyGeetestAsync(
                        geetestChallenge,
                        cancellationToken);
                if (result is null)
                {
                    throw new OperationCanceledException(
                        "已取消 GeeTest 安全验证。",
                        cancellationToken);
                }

                aigis = passportClient.CompleteGeetestChallenge(
                    geetestChallenge,
                    result);
                continue;
            }

            if (attempt.AccountVerificationChallenge is { } accountChallenge)
            {
                PassportAccountVerificationChallenge prepared =
                    await passportClient.PrepareAccountVerificationAsync(
                        accountChallenge,
                        device,
                        cancellationToken);
                string? captcha =
                    await verificationHandler.RequestAccountVerificationCodeAsync(
                        prepared,
                        cancellationToken);
                if (string.IsNullOrWhiteSpace(captcha))
                {
                    throw new OperationCanceledException(
                        "已取消账号安全验证码输入。",
                        cancellationToken);
                }

                await passportClient.VerifyAccountAsync(
                    prepared,
                    captcha.Trim(),
                    device,
                    cancellationToken);
                verify = passportClient.CompleteAccountVerificationChallenge(
                    prepared);
                continue;
            }

            throw new InvalidOperationException(
                $"HoYoLAB 登录失败 ({attempt.Retcode}): {attempt.Message ?? "Unknown error"}");
        }

        throw new InvalidOperationException(
            "HoYoLAB 安全验证次数过多，请重新开始登录。");
    }

    private static InvalidOperationException CreateSecurityVerificationException(
        OverseaPasswordLoginAttempt attempt)
    {
        bool requiresVerification =
            attempt.GeetestChallenge is not null ||
            attempt.AccountVerificationChallenge is not null;
        return new InvalidOperationException(requiresVerification
            ? "HoYoLAB 要求额外的安全验证，请使用支持 GeeTest/验证码的登录界面。"
            : $"HoYoLAB 登录失败 ({attempt.Retcode}): {attempt.Message ?? "Unknown error"}");
    }

    public Task<PassportAccount> LoginWithManualCookieAsync(
        string cookie,
        CancellationToken cancellationToken = default)
    {
        return SaveCookieLoginAsync(
            cookie,
            PassportLoginMethod.ManualCookie,
            cancellationToken);
    }

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
        PassportAccount account,
        CancellationToken cancellationToken)
    {
        PassportCredentials credentials = account.Credentials;
        if (credentials.SToken is null || account.Mid is null)
        {
            return new MaintenanceOutcome(Changed: false, Failed: false);
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        bool changed = false;
        bool failed = false;
        if (account.Realm == PassportRealm.MainlandChina &&
            IsExpired(credentials.SessionVerifiedAt, options.SessionVerificationInterval, now))
        {
            try
            {
                PassportSessionVerification verification =
                    await passportClient.VerifySessionAsync(account, cancellationToken);
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
                // Verification failure does not prevent direct SToken token exchange.
                failed = true;
            }
        }

        bool derivedExpired =
            credentials.LToken is null ||
            credentials.CookieToken is null ||
            IsExpired(credentials.LTokenUpdatedAt, options.DerivedCredentialMaxAge, now) ||
            IsExpired(credentials.CookieTokenUpdatedAt, options.DerivedCredentialMaxAge, now);

        if (derivedExpired)
        {
            try
            {
                PassportDerivedTokens derived =
                    await passportClient.GetDerivedTokensAsync(account, cancellationToken);
                if (derived.LToken is not null || derived.CookieToken is not null)
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
                await accountStore.SaveAsync(account, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                return new MaintenanceOutcome(Changed: false, Failed: true);
            }
        }

        return new MaintenanceOutcome(changed, failed);
    }

    private async Task<PassportAccount> SaveCookieLoginAsync(
        string cookie,
        PassportLoginMethod method,
        CancellationToken cancellationToken)
    {
        PassportLoginTokens tokens = PassportCookieParser.Parse(cookie);
        return await SaveNewAccountAsync(
            tokens,
            method,
            PassportRealm.MainlandChina,
            PassportDeviceIdentity.Create() with
            {
                DeviceFingerprint = tokens.DeviceFingerprint
            },
            cancellationToken);
    }

    private async Task<PassportAccount> SaveNewAccountAsync(
        PassportLoginTokens tokens,
        PassportLoginMethod method,
        PassportRealm realm,
        PassportDeviceIdentity device,
        CancellationToken cancellationToken)
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
            (account.Credentials.LToken is null || account.Credentials.CookieToken is null))
        {
            try
            {
                PassportDerivedTokens derived =
                    await passportClient.GetDerivedTokensAsync(account, cancellationToken);
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
                // Persist the root SToken first; startup maintenance can retry
                // derived-token exchange without requiring another login.
            }
        }

        await accountStore.SaveAsync(account, cancellationToken);
        return account;
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
