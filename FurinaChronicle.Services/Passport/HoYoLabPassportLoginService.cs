using FurinaChronicle.Core.Passport;

namespace FurinaChronicle.Services.Passport;

public sealed class HoYoLabPassportLoginService(
    IHoYoLabPassportClient passportClient,
    PassportAccountWriter accountWriter)
{
    public Task<PassportAccount> LoginWithPasswordAsync(
        string account,
        string password,
        CancellationToken cancellationToken = default)
    {
        return LoginWithPasswordCoreAsync(
            account,
            password,
            verificationHandler: null,
            cancellationToken);
    }

    public Task<PassportAccount> LoginWithPasswordAsync(
        string account,
        string password,
        IPassportSecurityVerificationHandler verificationHandler,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(verificationHandler);
        return LoginWithPasswordCoreAsync(
            account,
            password,
            verificationHandler,
            cancellationToken);
    }

    private async Task<PassportAccount> LoginWithPasswordCoreAsync(
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
                await passportClient.AttemptPasswordLoginAsync(
                    account.Trim(),
                    password,
                    device,
                    aigis,
                    verify,
                    cancellationToken);

            if (attempt.Tokens is { } tokens)
            {
                return await accountWriter.SaveAsync(
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
                $"HoYoLAB 登录失败 ({attempt.Retcode}): " +
                $"{attempt.Message ?? "Unknown error"}");
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
            : $"HoYoLAB 登录失败 ({attempt.Retcode}): " +
              $"{attempt.Message ?? "Unknown error"}");
    }
}
