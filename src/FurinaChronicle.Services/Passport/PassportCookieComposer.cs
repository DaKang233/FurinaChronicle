// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Passport;

namespace FurinaChronicle.Services.Passport;

/// <summary>
/// Builds the <c>Cookie</c> header for every authenticated MiHoYo/HoYoLAB
/// endpoint. A mainland session is identified by the v2 cookie names together
/// with the MID binding; the service answers <c>-100</c> when either is absent.
/// The v1 names stay alongside because several endpoints still read them.
/// </summary>
public static class PassportCookieComposer
{
    /// <summary>
    /// Full session cookie for the account and profile endpoints. Carries the
    /// root SToken so a session that failed to exchange derived tokens can
    /// still be recognised.
    /// </summary>
    public static string ForSession(PassportAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);
        var pairs = new List<string>(16);
        if (account.Realm == PassportRealm.Oversea)
        {
            // HoYoLAB still accepts the v1 names while its web session uses the
            // v2 ones, so both are sent.
            Add(pairs, "stuid", account.Aid);
            Add(pairs, "account_id", account.Aid);
            Add(pairs, "account_id_v2", account.Aid);
            Add(pairs, "ltuid", account.Aid);
            Add(pairs, "ltuid_v2", account.Aid);
            Add(pairs, "mid", account.Mid);
            Add(pairs, "ltmid_v2", account.Mid);
            Add(pairs, "account_mid_v2", account.Mid);
            Add(pairs, "stoken", account.Credentials.SToken);
            Add(pairs, "stoken_v2", account.Credentials.SToken);
            Add(pairs, "ltoken", account.Credentials.LToken);
            Add(pairs, "ltoken_v2", account.Credentials.LToken);
            Add(pairs, "cookie_token", account.Credentials.CookieToken);
            Add(pairs, "cookie_token_v2", account.Credentials.CookieToken);
            return string.Join("; ", pairs);
        }

        Add(pairs, "stuid", account.Aid);
        Add(pairs, "account_id", account.Aid);
        Add(pairs, "account_id_v2", account.Aid);
        Add(pairs, "ltuid", account.Aid);
        Add(pairs, "ltuid_v2", account.Aid);
        Add(pairs, "mid", account.Mid);
        Add(pairs, "ltmid_v2", account.Mid);
        Add(pairs, "account_mid_v2", account.Mid);
        Add(pairs, "stoken", account.Credentials.SToken);
        Add(pairs, "ltoken", account.Credentials.LToken);
        Add(pairs, "ltoken_v2", account.Credentials.LToken);
        Add(pairs, "cookie_token", account.Credentials.CookieToken);
        Add(pairs, "cookie_token_v2", account.Credentials.CookieToken);
        return string.Join("; ", pairs);
    }

    /// <summary>
    /// Root session cookie for the SToken role endpoint, which rejects a
    /// request that also carries derived tokens.
    /// </summary>
    public static string ForSTokenSession(PassportAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);
        var pairs = new List<string>(6);
        if (account.Realm == PassportRealm.Oversea)
        {
            Add(pairs, "stuid", account.Aid);
            Add(pairs, "ltuid", account.Aid);
            Add(pairs, "ltuid_v2", account.Aid);
            Add(pairs, "account_mid_v2", account.Mid);
            Add(pairs, "stoken", account.Credentials.SToken);
            Add(pairs, "stoken_v2", account.Credentials.SToken);
            return string.Join("; ", pairs);
        }

        Add(pairs, "stuid", account.Aid);
        Add(pairs, "mid", account.Mid);
        Add(pairs, "stoken", account.Credentials.SToken);
        return string.Join("; ", pairs);
    }

    /// <summary>
    /// Game Record cookie. It never carries the SToken: that service only
    /// reads the LToken/CookieToken pair. The caller supplies the LToken so it
    /// can use a token obtained for the current request.
    /// </summary>
    public static string ForGameRecord(
        PassportAccount account,
        string lToken,
        bool isOversea)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (!IsHeaderToken(lToken))
        {
            throw new ArgumentException(
                "The passport has no usable LToken.",
                nameof(lToken));
        }

        var pairs = new List<string>(12);
        if (isOversea)
        {
            Add(pairs, "ltuid", account.Aid);
            Add(pairs, "ltuid_v2", account.Aid);
            Add(pairs, "account_mid_v2", account.Mid);
            Add(pairs, "ltoken", lToken);
            Add(pairs, "ltoken_v2", lToken);
            Add(pairs, "account_id", account.Aid);
            Add(pairs, "account_id_v2", account.Aid);
            AddUsable(pairs, "cookie_token", account.Credentials.CookieToken);
            AddUsable(pairs, "cookie_token_v2", account.Credentials.CookieToken);
            return string.Join("; ", pairs);
        }

        Add(pairs, "stuid", account.Aid);
        Add(pairs, "ltuid", account.Aid);
        Add(pairs, "ltuid_v2", account.Aid);
        Add(pairs, "mid", account.Mid);
        Add(pairs, "ltmid_v2", account.Mid);
        Add(pairs, "account_mid_v2", account.Mid);
        Add(pairs, "ltoken", lToken);
        Add(pairs, "ltoken_v2", lToken);
        if (IsHeaderToken(account.Credentials.CookieToken))
        {
            Add(pairs, "account_id", account.Aid);
            Add(pairs, "account_id_v2", account.Aid);
            Add(pairs, "cookie_token", account.Credentials.CookieToken);
            Add(pairs, "cookie_token_v2", account.Credentials.CookieToken);
        }

        return string.Join("; ", pairs);
    }

    /// <summary>
    /// Web-side token cookie for the act/calculator endpoints. Those accept the
    /// v1 pair, so only the v2 account identifier is added for the MID binding.
    /// </summary>
    public static string ForWebToken(PassportAccount account, string cookieToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (!IsHeaderToken(cookieToken))
        {
            throw new ArgumentException(
                "The passport has no usable cookie token.",
                nameof(cookieToken));
        }

        var pairs = new List<string>(6);
        Add(pairs, "account_id", account.Aid);
        Add(pairs, "account_id_v2", account.Aid);
        Add(pairs, "account_mid_v2", account.Mid);
        Add(pairs, "cookie_token", cookieToken);
        Add(pairs, "cookie_token_v2", cookieToken);
        return string.Join("; ", pairs);
    }

    public static bool IsHeaderToken(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.All(character =>
            character is >= '!' and <= '~' and not ('"' or ',' or ';' or '\\'));

    private static void Add(ICollection<string> pairs, string name, string? value)
    {
        if (IsHeaderToken(value))
        {
            pairs.Add($"{name}={value!.Trim()}");
        }
    }

    private static void AddUsable(
        ICollection<string> pairs,
        string name,
        string? value)
    {
        Add(pairs, name, value);
    }
}
