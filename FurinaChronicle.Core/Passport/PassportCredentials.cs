namespace FurinaChronicle.Core.Passport;

public sealed class PassportCredentials
{
    public PassportCredentials(
        string? sToken,
        string? lToken,
        string? cookieToken,
        DateTimeOffset? sTokenUpdatedAt,
        DateTimeOffset? lTokenUpdatedAt,
        DateTimeOffset? cookieTokenUpdatedAt,
        DateTimeOffset? sessionVerifiedAt)
    {
        SToken = Normalize(sToken);
        LToken = Normalize(lToken);
        CookieToken = Normalize(cookieToken);
        STokenUpdatedAt = SToken is null ? null : sTokenUpdatedAt;
        LTokenUpdatedAt = LToken is null ? null : lTokenUpdatedAt;
        CookieTokenUpdatedAt = CookieToken is null ? null : cookieTokenUpdatedAt;
        SessionVerifiedAt = sessionVerifiedAt;

        if (SToken is null && LToken is null && CookieToken is null)
        {
            throw new ArgumentException("At least one passport credential is required.");
        }
    }

    public string? SToken { get; }

    public string? LToken { get; }

    public string? CookieToken { get; }

    public DateTimeOffset? STokenUpdatedAt { get; }

    public DateTimeOffset? LTokenUpdatedAt { get; }

    public DateTimeOffset? CookieTokenUpdatedAt { get; }

    public DateTimeOffset? SessionVerifiedAt { get; }

    public PassportCredentials WithDerivedTokens(
        string? lToken,
        string? cookieToken,
        DateTimeOffset updatedAt)
    {
        return new PassportCredentials(
            SToken,
            lToken ?? LToken,
            cookieToken ?? CookieToken,
            STokenUpdatedAt,
            lToken is null ? LTokenUpdatedAt : updatedAt,
            cookieToken is null ? CookieTokenUpdatedAt : updatedAt,
            SessionVerifiedAt);
    }

    public PassportCredentials WithVerifiedSession(
        string? refreshedSToken,
        DateTimeOffset verifiedAt)
    {
        string? nextSToken = Normalize(refreshedSToken) ?? SToken;
        return new PassportCredentials(
            nextSToken,
            LToken,
            CookieToken,
            string.Equals(nextSToken, SToken, StringComparison.Ordinal)
                ? STokenUpdatedAt
                : verifiedAt,
            LTokenUpdatedAt,
            CookieTokenUpdatedAt,
            verifiedAt);
    }

    public override string ToString()
    {
        return $"PassportCredentials(SToken={Describe(SToken)}, LToken={Describe(LToken)}, CookieToken={Describe(CookieToken)})";
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string Describe(string? value)
    {
        return value is null ? "missing" : "present";
    }
}
