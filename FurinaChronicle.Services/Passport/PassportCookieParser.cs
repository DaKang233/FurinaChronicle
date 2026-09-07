namespace FurinaChronicle.Services.Passport;

public static class PassportCookieParser
{
    public static PassportLoginTokens Parse(string cookie)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cookie);

        Dictionary<string, string> values = cookie
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.Split('=', count: 2, StringSplitOptions.TrimEntries))
            .Where(parts => parts.Length == 2 && parts[0].Length > 0)
            .GroupBy(parts => parts[0], StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Last()[1],
                StringComparer.OrdinalIgnoreCase);

        string? aid = First(values, "account_id_v2", "account_id", "stuid", "ltuid_v2", "ltuid");
        if (string.IsNullOrWhiteSpace(aid))
        {
            throw new FormatException("Cookie does not contain an account ID.");
        }

        string? sToken = First(values, "stoken_v2", "stoken");
        string? lToken = First(values, "ltoken_v2", "ltoken");
        string? cookieToken = First(values, "cookie_token_v2", "cookie_token");
        if (sToken is null && lToken is null && cookieToken is null)
        {
            throw new FormatException("Cookie does not contain a supported login token.");
        }

        return new PassportLoginTokens(
            aid,
            First(values, "mid", "account_mid_v2", "account_mid"),
            sToken,
            lToken,
            cookieToken,
            DeviceFingerprint: First(values, "DEVICEFP"));
    }

    private static string? First(
        IReadOnlyDictionary<string, string> values,
        params string[] keys)
    {
        foreach (string key in keys)
        {
            if (values.TryGetValue(key, out string? value) &&
                !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }
}
