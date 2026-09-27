using FurinaChronicle.Core.Passport;

namespace FurinaChronicle.Services.Passport;

public sealed record PassportLoginTokens(
    string Aid,
    string? Mid,
    string? SToken,
    string? LToken = null,
    string? CookieToken = null,
    string? DisplayName = null,
    string? DeviceFingerprint = null);

public sealed record PassportGeetestChallenge(
    string State,
    string Gt,
    string Challenge,
    bool IsOversea = true);

public sealed record PassportGeetestResult(
    string Challenge,
    string Validate);

public sealed record PassportAccountVerificationChallenge(
    string State,
    string Ticket,
    string? Destination = null,
    HoYoLabVerificationMethod Method = HoYoLabVerificationMethod.Email);

public enum HoYoLabVerificationMethod
{
    Mobile = 1,
    Email = 2
}

public sealed record OverseaPasswordLoginAttempt(
    PassportLoginTokens? Tokens,
    PassportGeetestChallenge? GeetestChallenge = null,
    PassportAccountVerificationChallenge? AccountVerificationChallenge = null,
    int Retcode = 0,
    string? Message = null)
{
    public bool IsSuccess => Tokens is not null;
}

public interface IPassportSecurityVerificationHandler
{
    Task<PassportGeetestResult?> VerifyGeetestAsync(
        PassportGeetestChallenge challenge,
        CancellationToken cancellationToken = default);

    Task<string?> RequestAccountVerificationCodeAsync(
        PassportAccountVerificationChallenge challenge,
        CancellationToken cancellationToken = default);
}

public sealed record PassportDerivedTokens(
    string? LToken,
    string? CookieToken);

public sealed record PassportSessionVerification(string? RefreshedSToken);

public sealed record MobileCaptchaChallenge(
    string ActionType,
    string? Aigis,
    string DeviceId);

public sealed record PassportQrSession(
    string Ticket,
    string Url,
    string DeviceId);

public enum PassportQrStatus
{
    Pending = 0,
    Scanned = 1,
    Confirmed = 2,
    Expired = 3
}

public sealed record PassportQrPollResult(
    PassportQrStatus Status,
    PassportLoginTokens? Tokens = null);

public sealed record PassportMaintenanceResult(
    int ExaminedCount,
    int UpdatedCount,
    IReadOnlyList<Guid> FailedAccountIds);

public sealed class PassportCredentialMaintenanceOptions
{
    public TimeSpan DerivedCredentialMaxAge { get; init; } = TimeSpan.FromDays(1);

    public TimeSpan SessionVerificationInterval { get; init; } = TimeSpan.FromDays(7);
}
