using FurinaChronicle.Core.Passport;

namespace FurinaChronicle.Services.Passport;

public sealed class MainlandPassportLoginService(
    IMainlandPassportClient passportClient,
    PassportAccountWriter accountWriter)
{
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
            ?? throw new InvalidDataException(
                "Confirmed QR login did not return credentials.");
        PassportAccount account = await accountWriter.SaveAsync(
            tokens,
            PassportLoginMethod.QrCode,
            PassportRealm.MainlandChina,
            new PassportDeviceIdentity(
                session.DeviceId,
                DeviceFingerprint: null),
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
        MobileCaptchaChallenge challenge =
            await passportClient.SendMobileCaptchaAsync(
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
        PassportLoginTokens tokens =
            await passportClient.LoginWithMobileCaptchaAsync(
                mobile.Trim(),
                captcha.Trim(),
                challenge,
                device,
                cancellationToken);
        return await accountWriter.SaveAsync(
            tokens,
            PassportLoginMethod.MobileCaptcha,
            PassportRealm.MainlandChina,
            device,
            cancellationToken);
    }

    public Task<PassportAccount> LoginWithManualCookieAsync(
        string cookie,
        CancellationToken cancellationToken = default)
    {
        PassportLoginTokens tokens = PassportCookieParser.Parse(cookie);
        return accountWriter.SaveAsync(
            tokens,
            PassportLoginMethod.ManualCookie,
            PassportRealm.MainlandChina,
            PassportDeviceIdentity.Create() with
            {
                DeviceFingerprint = tokens.DeviceFingerprint
            },
            cancellationToken);
    }
}
