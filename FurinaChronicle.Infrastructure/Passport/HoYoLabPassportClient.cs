using FurinaChronicle.Core.Passport;
using FurinaChronicle.Services.Passport;

namespace FurinaChronicle.Infrastructure.Passport;

public sealed class HoYoLabPassportClient(MiHoYoPassportClient inner)
    : IHoYoLabPassportClient
{
    public Task<OverseaPasswordLoginAttempt> AttemptPasswordLoginAsync(
        string account,
        string password,
        PassportDeviceIdentity device,
        string? aigis = null,
        string? verify = null,
        CancellationToken cancellationToken = default) =>
        inner.AttemptPasswordLoginAsync(
            account,
            password,
            device,
            aigis,
            verify,
            cancellationToken);

    public string CompleteGeetestChallenge(
        PassportGeetestChallenge challenge,
        PassportGeetestResult result) =>
        inner.CompleteGeetestChallenge(challenge, result);

    public Task<PassportAccountVerificationChallenge> PrepareAccountVerificationAsync(
        PassportAccountVerificationChallenge challenge,
        PassportDeviceIdentity device,
        CancellationToken cancellationToken = default) =>
        inner.PrepareAccountVerificationAsync(challenge, device, cancellationToken);

    public Task VerifyAccountAsync(
        PassportAccountVerificationChallenge challenge,
        string captcha,
        PassportDeviceIdentity device,
        CancellationToken cancellationToken = default) =>
        inner.VerifyAccountAsync(challenge, captcha, device, cancellationToken);

    public string CompleteAccountVerificationChallenge(
        PassportAccountVerificationChallenge challenge) =>
        inner.CompleteAccountVerificationChallenge(challenge);

    public Task<PassportDerivedTokens> GetDerivedTokensAsync(
        PassportAccount account,
        CancellationToken cancellationToken = default) =>
        inner.GetDerivedTokensAsync(account, cancellationToken);
}
