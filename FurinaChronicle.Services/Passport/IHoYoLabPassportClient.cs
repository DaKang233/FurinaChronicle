using FurinaChronicle.Core.Passport;

namespace FurinaChronicle.Services.Passport;

public interface IHoYoLabPassportClient
{
    Task<OverseaPasswordLoginAttempt> AttemptPasswordLoginAsync(
        string account,
        string password,
        PassportDeviceIdentity device,
        string? aigis = null,
        string? verify = null,
        CancellationToken cancellationToken = default);

    string CompleteGeetestChallenge(
        PassportGeetestChallenge challenge,
        PassportGeetestResult result);

    Task<PassportAccountVerificationChallenge> PrepareAccountVerificationAsync(
        PassportAccountVerificationChallenge challenge,
        PassportDeviceIdentity device,
        CancellationToken cancellationToken = default);

    Task VerifyAccountAsync(
        PassportAccountVerificationChallenge challenge,
        string captcha,
        PassportDeviceIdentity device,
        CancellationToken cancellationToken = default);

    string CompleteAccountVerificationChallenge(
        PassportAccountVerificationChallenge challenge);

    Task<PassportDerivedTokens> GetDerivedTokensAsync(
        PassportAccount account,
        CancellationToken cancellationToken = default);
}
