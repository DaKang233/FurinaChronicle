// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Passport;

namespace FurinaChronicle.Services.Passport;

public sealed class MainlandPassportLoginService(
    IMainlandPassportClient passportClient,
    PassportAccountWriter accountWriter,
    IDeviceFingerprintProvider deviceFingerprintProvider)
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
        string? fingerprint = await ResolveFingerprintAsync(
            session.DeviceId, tokens.DeviceFingerprint, cancellationToken);
        PassportAccount account = await accountWriter.SaveAsync(
            tokens with { DeviceFingerprint = fingerprint },
            PassportLoginMethod.QrCode,
            PassportRealm.MainlandChina,
            new PassportDeviceIdentity(session.DeviceId, fingerprint),
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
        string? fingerprint = await ResolveFingerprintAsync(
            device.DeviceId, tokens.DeviceFingerprint, cancellationToken);
        return await accountWriter.SaveAsync(
            tokens with { DeviceFingerprint = fingerprint },
            PassportLoginMethod.MobileCaptcha,
            PassportRealm.MainlandChina,
            device with { DeviceFingerprint = fingerprint },
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

    private async Task<string?> ResolveFingerprintAsync(
        string deviceId,
        string? provided,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(provided))
        {
            return provided;
        }

        // The login response normally does not carry x-rpc-device_fp, but the
        // game-record endpoints require the session to present one.
        return await deviceFingerprintProvider.GetFingerprintAsync(
            deviceId,
            cancellationToken);
    }
}
