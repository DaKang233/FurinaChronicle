namespace FurinaChronicle.Services.Gacha.Refreshing;

public static class GachaRefreshUrl
{
    private static readonly string[] SupportedHosts =
    [
        "public-operation-hk4e.mihoyo.com",
        "public-operation-hk4e-sg.hoyoverse.com"
    ];

    public static Uri Parse(string input)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input);
        string trimmed = input.Trim().Trim('\'', '"');
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri) ||
            uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new FormatException("Gacha URL must be an absolute HTTPS URL.");
        }

        if (!SupportedHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase) &&
            !uri.Host.EndsWith(".mihoyo.com", StringComparison.OrdinalIgnoreCase) &&
            !uri.Host.EndsWith(".hoyoverse.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new FormatException("Gacha URL host is not supported.");
        }

        IReadOnlyDictionary<string, string> query = ParseQuery(uri.Query);
        if (!query.TryGetValue("authkey", out string? authKey) ||
            string.IsNullOrWhiteSpace(authKey))
        {
            throw new FormatException("Gacha URL does not contain an authkey.");
        }

        return uri;
    }

    public static IReadOnlyDictionary<string, string> ParseQuery(string query)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string pair in query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = pair.Split('=', count: 2);
            string key = Uri.UnescapeDataString(parts[0].Replace('+', ' '));
            string value = parts.Length == 2
                ? Uri.UnescapeDataString(parts[1].Replace('+', ' '))
                : string.Empty;
            values[key] = value;
        }

        return values;
    }
}
