using FurinaChronicle.Core.Passport;
using FurinaChronicle.Services.Passport;

namespace FurinaChronicle.Infrastructure.Passport;

public sealed class MainlandPassportClient(MiHoYoPassportClient inner)
    : IMainlandPassportClient
{
    public Task<PassportQrSession> CreateQrSessionAsync(
        PassportDeviceIdentity device,
        CancellationToken cancellationToken = default) =>
        inner.CreateQrSessionAsync(device, cancellationToken);

    public Task<PassportQrPollResult> PollQrSessionAsync(
        PassportQrSession session,
        CancellationToken cancellationToken = default) =>
        inner.PollQrSessionAsync(session, cancellationToken);

    public Task<MobileCaptchaChallenge> SendMobileCaptchaAsync(
        string mobile,
        PassportDeviceIdentity device,
        string? aigis = null,
        CancellationToken cancellationToken = default) =>
        inner.SendMobileCaptchaAsync(mobile, device, aigis, cancellationToken);

    public Task<PassportLoginTokens> LoginWithMobileCaptchaAsync(
        string mobile,
        string captcha,
        MobileCaptchaChallenge challenge,
        PassportDeviceIdentity device,
        CancellationToken cancellationToken = default) =>
        inner.LoginWithMobileCaptchaAsync(
            mobile,
            captcha,
            challenge,
            device,
            cancellationToken);

    public Task<PassportDerivedTokens> GetDerivedTokensAsync(
        PassportAccount account,
        CancellationToken cancellationToken = default) =>
        inner.GetDerivedTokensAsync(account, cancellationToken);

    public Task<PassportSessionVerification> VerifySessionAsync(
        PassportAccount account,
        CancellationToken cancellationToken = default) =>
        inner.VerifySessionAsync(account, cancellationToken);
}
