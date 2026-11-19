// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

namespace FurinaChronicle.Services.HistoryDomains;

/// <summary>The public, credential-free artwork addresses accepted by challenge display and caching.</summary>
public static class ChallengeArtworkUri
{
    public static Uri? Parse(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) && IsSupported(uri) ? uri : null;

    public static bool IsSupported(Uri? uri)
    {
        if (uri is not { IsAbsoluteUri: true } || uri.Scheme != Uri.UriSchemeHttps ||
            !uri.IsDefaultPort || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            return false;
        string extension = Path.GetExtension(uri.AbsolutePath).ToLowerInvariant();
        if (extension is not (".png" or ".jpg" or ".jpeg" or ".webp")) return false;
        if (uri.Host is "api.snaphutaorp.org" or "api.hutaorp.org" or "static.snaphutaorp.org")
        {
            const string prefix = "/static/raw/MonsterIcon/";
            if (!uri.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal) || extension != ".png") return false;
            string name = uri.AbsolutePath[prefix.Length..^4];
            return name.Length is > 0 and <= 150 &&
                name.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');
        }
        return uri.Host is "act-webstatic.hoyoverse.com" or "act-webstatic.mihoyo.com" or
            "fastcdn.mihoyo.com" or "uploadstatic.mihoyo.com" or "webstatic.mihoyo.com" or "enka.network";
    }
}
