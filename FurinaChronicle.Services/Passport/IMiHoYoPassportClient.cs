using FurinaChronicle.Core.Passport;

namespace FurinaChronicle.Services.Passport;

public interface IMiHoYoPassportClient
{
    Task<PassportQrSession> CreateQrSessionAsync(
        PassportDeviceIdentity device,
        CancellationToken cancellationToken = default);

    Task<PassportQrPollResult> PollQrSessionAsync(
        PassportQrSession session,
        CancellationToken cancellationToken = default);

    Task<MobileCaptchaChallenge> SendMobileCaptchaAsync(
        string mobile,
        PassportDeviceIdentity device,
        string? aigis = null,
        CancellationToken cancellationToken = default);

    Task<PassportLoginTokens> LoginWithMobileCaptchaAsync(
        string mobile,
        string captcha,
        MobileCaptchaChallenge challenge,
        PassportDeviceIdentity device,
        CancellationToken cancellationToken = default);

    Task<PassportLoginTokens> LoginWithOverseaPasswordAsync(
        string account,
        string password,
        PassportDeviceIdentity device,
        CancellationToken cancellationToken = default);

    async Task<OverseaPasswordLoginAttempt> AttemptOverseaPasswordLoginAsync(
        string account,
        string password,
        PassportDeviceIdentity device,
        string? aigis = null,
        string? verify = null,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(aigis) || !string.IsNullOrWhiteSpace(verify))
        {
            throw new NotSupportedException(
                "This passport client does not support security-verification retries.");
        }

        PassportLoginTokens tokens = await LoginWithOverseaPasswordAsync(
            account,
            password,
            device,
            cancellationToken);
        return new OverseaPasswordLoginAttempt(tokens);
    }

    string CompleteGeetestChallenge(
        PassportGeetestChallenge challenge,
        PassportGeetestResult result)
    {
        throw new NotSupportedException();
    }

    Task<PassportAccountVerificationChallenge> PrepareAccountVerificationAsync(
        PassportAccountVerificationChallenge challenge,
        PassportDeviceIdentity device,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    Task VerifyAccountAsync(
        PassportAccountVerificationChallenge challenge,
        string captcha,
        PassportDeviceIdentity device,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    string CompleteAccountVerificationChallenge(
        PassportAccountVerificationChallenge challenge)
    {
        throw new NotSupportedException();
    }

    Task<PassportDerivedTokens> GetDerivedTokensAsync(
        PassportAccount account,
        CancellationToken cancellationToken = default);

    Task<PassportSessionVerification> VerifySessionAsync(
        PassportAccount account,
        CancellationToken cancellationToken = default);
}
