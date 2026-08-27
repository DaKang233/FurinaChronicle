using FurinaChronicle.Core.Passport;
using FurinaChronicle.Services.Passport;

namespace FurinaChronicle.Tests.Services.Passport;

public sealed class PassportAccountServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 26, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task LoginWithManualCookieAsync_ParsesV2CookieAndStoresDerivedTokens()
    {
        var store = new MemoryAccountStore();
        var client = new StubPassportClient
        {
            DerivedTokens = new PassportDerivedTokens("derived-ltoken", "derived-cookie")
        };
        var service = CreateService(store, client);

        PassportAccount account = await service.LoginWithManualCookieAsync(
            "account_id=old; account_id_v2=12345; account_mid_v2=mid-1; " +
            "stoken=old-token; stoken_v2=root-token; DEVICEFP=device-fp");

        Assert.Equal(PassportLoginMethod.ManualCookie, account.LoginMethod);
        Assert.Equal("12345", account.Aid);
        Assert.Equal("root-token", account.Credentials.SToken);
        Assert.Equal("mid-1", account.Mid);
        Assert.Equal("device-fp", account.Device.DeviceFingerprint);
        Assert.Equal("derived-ltoken", account.Credentials.LToken);
        Assert.Equal("derived-cookie", account.Credentials.CookieToken);
        Assert.Equal(account, await store.GetByIdAsync(account.Id));
        Assert.DoesNotContain("root-token", account.Credentials.ToString());
        Assert.Equal(1, client.DerivedTokenCallCount);
    }

    [Fact]
    public async Task CompletePasswordLoginAsync_RecordsPasswordLoginWithoutSavingPassword()
    {
        var store = new MemoryAccountStore();
        var client = new StubPassportClient
        {
            WebLoginTokens = new PassportLoginTokens(
                "12345",
                "mid-1",
                "root-token",
                CookieToken: "cookie-token")
        };
        var service = CreateService(store, client);

        PassportAccount account = await service.CompletePasswordLoginAsync(
            "account_id=12345; cookie_token=cookie-token",
            "{\"retcode\":0}");

        Assert.Equal(PassportLoginMethod.Password, account.LoginMethod);
        Assert.Equal("root-token", account.Credentials.SToken);
        Assert.Equal("mid-1", account.Mid);
        Assert.Equal("cookie-token", account.Credentials.CookieToken);
        Assert.Equal("{\"retcode\":0}", client.WebLoginResponseJson);
    }

    [Fact]
    public async Task MobileCaptchaLogin_ReusesDeviceIdentityFromChallenge()
    {
        var store = new MemoryAccountStore();
        var client = new StubPassportClient
        {
            MobileTokens = new PassportLoginTokens(
                "12345",
                "mid-1",
                "root-token",
                "ltoken",
                "cookie-token")
        };
        var service = CreateService(store, client);

        MobileCaptchaChallenge challenge =
            await service.SendMobileCaptchaAsync("13800138000");
        PassportAccount account = await service.LoginWithMobileCaptchaAsync(
            "13800138000",
            "123456",
            challenge);

        Assert.Equal(PassportLoginMethod.MobileCaptcha, account.LoginMethod);
        Assert.Equal(challenge.DeviceId, client.MobileLoginDeviceId);
        Assert.Equal(challenge.DeviceId, account.Device.DeviceId);
    }

    [Fact]
    public async Task OverseaPasswordLogin_StoresReturnedSTokenAndUsesOverseaDevice()
    {
        var store = new MemoryAccountStore();
        var client = new StubPassportClient
        {
            OverseaPasswordTokens = new PassportLoginTokens(
                "900001",
                "mid-os",
                "stoken-os")
        };
        var service = CreateService(store, client);

        PassportAccount account = await service.LoginWithOverseaPasswordAsync(
            "traveler@example.com",
            "secret-password");

        Assert.Equal(PassportLoginMethod.Password, account.LoginMethod);
        Assert.Equal("mid-os", account.Mid);
        Assert.Equal("stoken-os", account.Credentials.SToken);
        Assert.Equal(53, account.Device.DeviceId.Length);
        Assert.Equal("traveler@example.com", client.OverseaPasswordAccount);
        Assert.Equal("secret-password", client.OverseaPasswordValue);
    }

    [Fact]
    public async Task MaintainAllAsync_RefreshesEveryStaleAccountAndPersistsRotatedSToken()
    {
        var store = new MemoryAccountStore();
        PassportAccount first = CreateAccount("1", Now - TimeSpan.FromDays(8));
        PassportAccount second = CreateAccount("2", Now - TimeSpan.FromDays(8));
        await store.SaveAsync(first);
        await store.SaveAsync(second);
        var client = new StubPassportClient
        {
            Verification = new PassportSessionVerification("rotated-token"),
            DerivedTokens = new PassportDerivedTokens("new-ltoken", "new-cookie")
        };
        var service = CreateService(store, client);

        PassportMaintenanceResult result = await service.MaintainAllAsync();

        Assert.Equal(2, result.ExaminedCount);
        Assert.Equal(2, result.UpdatedCount);
        Assert.Empty(result.FailedAccountIds);
        Assert.Equal(2, client.VerificationCallCount);
        Assert.Equal(2, client.DerivedTokenCallCount);
        PassportAccount updated = (await store.GetByIdAsync(first.Id))!;
        Assert.Equal("rotated-token", updated.Credentials.SToken);
        Assert.Equal("new-cookie", updated.Credentials.CookieToken);
        Assert.Equal(Now, updated.Credentials.SessionVerifiedAt);
    }

    [Fact]
    public async Task MaintainAllAsync_NetworkFailureDoesNotDeleteCredentialsOrStopOtherAccounts()
    {
        var store = new MemoryAccountStore();
        PassportAccount failing = CreateAccount("1", Now - TimeSpan.FromDays(8));
        PassportAccount succeeding = CreateAccount("2", Now - TimeSpan.FromDays(8));
        await store.SaveAsync(failing);
        await store.SaveAsync(succeeding);
        var client = new StubPassportClient
        {
            FailVerificationForAid = "1",
            Verification = new PassportSessionVerification(null),
            DerivedTokens = new PassportDerivedTokens("new-ltoken", "new-cookie")
        };
        var service = CreateService(store, client);

        PassportMaintenanceResult result = await service.MaintainAllAsync();

        Assert.Equal([failing.Id], result.FailedAccountIds);
        Assert.Equal("root-1", (await store.GetByIdAsync(failing.Id))!.Credentials.SToken);
        Assert.NotNull(await store.GetByIdAsync(succeeding.Id));
        Assert.Equal(2, result.UpdatedCount);
    }

    [Fact]
    public void CookieParser_MissingAccountOrToken_RejectsCookie()
    {
        Assert.Throws<FormatException>(
            () => PassportCookieParser.Parse("stoken=token; mid=mid"));
        Assert.Throws<FormatException>(
            () => PassportCookieParser.Parse("account_id=12345; mid=mid"));
    }

    [Fact]
    public async Task ReLoginWithSameAid_UpdatesExistingAccountInsteadOfCreatingDuplicate()
    {
        var store = new MemoryAccountStore();
        var client = new StubPassportClient();
        var service = CreateService(store, client);

        PassportAccount first = await service.LoginWithManualCookieAsync(
            "account_id=12345; cookie_token=old-cookie");
        PassportAccount second = await service.LoginWithManualCookieAsync(
            "account_id=12345; cookie_token=new-cookie");

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first.Device.DeviceId, second.Device.DeviceId);
        Assert.Equal("new-cookie", second.Credentials.CookieToken);
        Assert.Single(await store.GetAllAsync());
    }

    [Fact]
    public async Task Login_DerivedTokenExchangeFailure_StillPersistsRootToken()
    {
        var store = new MemoryAccountStore();
        var client = new StubPassportClient
        {
            FailDerivedTokenExchange = true
        };
        var service = CreateService(store, client);

        PassportAccount account = await service.LoginWithManualCookieAsync(
            "account_id=12345; mid=mid-1; stoken=root-token");

        Assert.Equal("root-token", account.Credentials.SToken);
        Assert.Equal(account, await store.GetByIdAsync(account.Id));
    }

    private static PassportAccountService CreateService(
        MemoryAccountStore store,
        StubPassportClient client)
    {
        return new PassportAccountService(
            store,
            client,
            new FixedTimeProvider(Now),
            new PassportCredentialMaintenanceOptions
            {
                DerivedCredentialMaxAge = TimeSpan.FromDays(1),
                SessionVerificationInterval = TimeSpan.FromDays(7)
            });
    }

    private static PassportAccount CreateAccount(
        string aid,
        DateTimeOffset updatedAt)
    {
        return new PassportAccount(
            Guid.NewGuid(),
            aid,
            $"mid-{aid}",
            null,
            PassportLoginMethod.MobileCaptcha,
            new PassportCredentials(
                $"root-{aid}",
                "old-ltoken",
                "old-cookie",
                updatedAt,
                updatedAt,
                updatedAt,
                updatedAt),
            PassportDeviceIdentity.Create(),
            updatedAt,
            updatedAt);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class MemoryAccountStore : IPassportAccountStore
    {
        private readonly Dictionary<Guid, PassportAccount> accounts = [];

        public Task<IReadOnlyList<PassportAccount>> GetAllAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<PassportAccount>>(
                accounts.Values.ToArray());
        }

        public Task<PassportAccount?> GetByIdAsync(
            Guid accountId,
            CancellationToken cancellationToken = default)
        {
            accounts.TryGetValue(accountId, out PassportAccount? account);
            return Task.FromResult(account);
        }

        public Task SaveAsync(
            PassportAccount account,
            CancellationToken cancellationToken = default)
        {
            accounts[account.Id] = account;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(
            Guid accountId,
            CancellationToken cancellationToken = default)
        {
            accounts.Remove(accountId);
            return Task.CompletedTask;
        }
    }

    private sealed class StubPassportClient : IMiHoYoPassportClient
    {
        public PassportDerivedTokens DerivedTokens { get; set; } =
            new(null, null);

        public PassportSessionVerification Verification { get; set; } =
            new(null);

        public PassportLoginTokens MobileTokens { get; set; } =
            new("1", "mid", "stoken");

        public PassportLoginTokens WebLoginTokens { get; set; } =
            new("1", "mid", "stoken");

        public PassportLoginTokens OverseaPasswordTokens { get; set; } =
            new("1", "mid", "stoken");

        public string? FailVerificationForAid { get; set; }

        public bool FailDerivedTokenExchange { get; set; }

        public int DerivedTokenCallCount { get; private set; }

        public int VerificationCallCount { get; private set; }

        public string? MobileLoginDeviceId { get; private set; }

        public string? WebLoginResponseJson { get; private set; }

        public string? OverseaPasswordAccount { get; private set; }

        public string? OverseaPasswordValue { get; private set; }

        public Task<PassportQrSession> CreateQrSessionAsync(
            PassportDeviceIdentity device,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new PassportQrSession(
                "ticket",
                "https://example.test/qr",
                device.DeviceId));
        }

        public Task<PassportQrPollResult> PollQrSessionAsync(
            PassportQrSession session,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new PassportQrPollResult(PassportQrStatus.Pending));
        }

        public Task<MobileCaptchaChallenge> SendMobileCaptchaAsync(
            string mobile,
            PassportDeviceIdentity device,
            string? aigis = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new MobileCaptchaChallenge(
                "login",
                aigis,
                device.DeviceId));
        }

        public Task<PassportLoginTokens> LoginWithMobileCaptchaAsync(
            string mobile,
            string captcha,
            MobileCaptchaChallenge challenge,
            PassportDeviceIdentity device,
            CancellationToken cancellationToken = default)
        {
            MobileLoginDeviceId = device.DeviceId;
            return Task.FromResult(MobileTokens);
        }

        public Task<PassportLoginTokens> CompleteWebLoginAsync(
            string authenticatedCookie,
            string? loginResponseJson,
            PassportDeviceIdentity device,
            CancellationToken cancellationToken = default)
        {
            WebLoginResponseJson = loginResponseJson;
            return Task.FromResult(WebLoginTokens);
        }

        public Task<PassportLoginTokens> LoginWithOverseaPasswordAsync(
            string account,
            string password,
            PassportDeviceIdentity device,
            CancellationToken cancellationToken = default)
        {
            OverseaPasswordAccount = account;
            OverseaPasswordValue = password;
            return Task.FromResult(OverseaPasswordTokens);
        }

        public Task<PassportDerivedTokens> GetDerivedTokensAsync(
            PassportAccount account,
            CancellationToken cancellationToken = default)
        {
            DerivedTokenCallCount++;
            if (FailDerivedTokenExchange)
            {
                throw new HttpRequestException("offline");
            }
            return Task.FromResult(DerivedTokens);
        }

        public Task<PassportSessionVerification> VerifySessionAsync(
            PassportAccount account,
            CancellationToken cancellationToken = default)
        {
            VerificationCallCount++;
            if (account.Aid == FailVerificationForAid)
            {
                throw new HttpRequestException("offline");
            }

            return Task.FromResult(Verification);
        }
    }
}
