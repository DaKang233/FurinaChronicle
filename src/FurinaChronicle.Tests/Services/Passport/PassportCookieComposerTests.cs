// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Passport;
using FurinaChronicle.Services.Passport;

namespace FurinaChronicle.Tests.Services.Passport;

public sealed class PassportCookieComposerTests
{
    [Fact]
    public void ForSession_BindsTheV2NamesAndMidForMainland()
    {
        string cookie = PassportCookieComposer.ForSession(Mainland());

        Assert.Contains("stuid=12345", cookie);
        Assert.Contains("account_id=12345", cookie);
        Assert.Contains("account_id_v2=12345", cookie);
        Assert.Contains("ltuid=12345", cookie);
        Assert.Contains("ltuid_v2=12345", cookie);
        Assert.Contains("mid=mid-1", cookie);
        Assert.Contains("ltmid_v2=mid-1", cookie);
        Assert.Contains("account_mid_v2=mid-1", cookie);
        Assert.Contains("stoken=root-token", cookie);
        Assert.Contains("ltoken=ltoken-value", cookie);
        Assert.Contains("ltoken_v2=ltoken-value", cookie);
        Assert.Contains("cookie_token=cookie-value", cookie);
        Assert.Contains("cookie_token_v2=cookie-value", cookie);
    }

    [Fact]
    public void ForSTokenSession_OmitsDerivedTokens()
    {
        string cookie = PassportCookieComposer.ForSTokenSession(Mainland());

        Assert.Equal("stuid=12345; mid=mid-1; stoken=root-token", cookie);
    }

    [Fact]
    public void ForGameRecord_CarriesTheMidBindingButNeverTheSToken()
    {
        string cookie = PassportCookieComposer.ForGameRecord(
            Mainland(),
            "ltoken-value",
            isOversea: false);

        Assert.Contains("ltuid=12345", cookie);
        Assert.Contains("ltuid_v2=12345", cookie);
        Assert.Contains("ltmid_v2=mid-1", cookie);
        Assert.Contains("account_mid_v2=mid-1", cookie);
        Assert.Contains("ltoken=ltoken-value", cookie);
        Assert.Contains("ltoken_v2=ltoken-value", cookie);
        Assert.Contains("cookie_token_v2=cookie-value", cookie);
        Assert.DoesNotContain("stoken", cookie);
    }

    [Fact]
    public void ForGameRecord_RejectsAnUnusableToken()
    {
        Assert.Throws<ArgumentException>(() =>
            PassportCookieComposer.ForGameRecord(Mainland(), "bad;token", false));
    }

    [Fact]
    public void ForGameRecord_OverseaKeepsBothNameSets()
    {
        string cookie = PassportCookieComposer.ForGameRecord(
            Mainland(PassportRealm.Oversea),
            "ltoken-value",
            isOversea: true);

        Assert.Contains("ltuid=12345", cookie);
        Assert.Contains("ltuid_v2=12345", cookie);
        Assert.Contains("ltoken=ltoken-value", cookie);
        Assert.Contains("ltoken_v2=ltoken-value", cookie);
        Assert.Contains("account_id=12345", cookie);
        Assert.Contains("account_id_v2=12345", cookie);
        Assert.Contains("cookie_token=cookie-value", cookie);
        Assert.Contains("cookie_token_v2=cookie-value", cookie);
        Assert.Contains("account_mid_v2=mid-1", cookie);
        Assert.DoesNotContain("stoken", cookie);
    }

    [Fact]
    public void ForSession_OverseaCarriesBothNameSets()
    {
        string cookie = PassportCookieComposer.ForSession(
            Mainland(PassportRealm.Oversea));

        Assert.Contains("stuid=12345", cookie);
        Assert.Contains("ltuid=12345", cookie);
        Assert.Contains("ltuid_v2=12345", cookie);
        Assert.Contains("stoken=root-token", cookie);
        Assert.Contains("stoken_v2=root-token", cookie);
        Assert.Contains("ltoken_v2=ltoken-value", cookie);
    }

    private static PassportAccount Mainland(
        PassportRealm realm = PassportRealm.MainlandChina)
    {
        DateTimeOffset now = DateTimeOffset.UnixEpoch;
        return new PassportAccount(
            Guid.NewGuid(),
            "12345",
            "mid-1",
            null,
            PassportLoginMethod.QrCode,
            new PassportCredentials(
                "root-token",
                "ltoken-value",
                "cookie-value",
                now,
                now,
                now,
                now),
            new PassportDeviceIdentity("device-1", null),
            now,
            now,
            realm);
    }
}
