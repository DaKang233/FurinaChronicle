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

    Task<PassportLoginTokens> CompleteWebLoginAsync(
        string authenticatedCookie,
        string? loginResponseJson,
        PassportDeviceIdentity device,
        CancellationToken cancellationToken = default);

    Task<PassportDerivedTokens> GetDerivedTokensAsync(
        PassportAccount account,
        CancellationToken cancellationToken = default);

    Task<PassportSessionVerification> VerifySessionAsync(
        PassportAccount account,
        CancellationToken cancellationToken = default);
}
