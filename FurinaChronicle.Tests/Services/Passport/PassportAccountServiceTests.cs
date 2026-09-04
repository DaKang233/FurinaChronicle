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

        Assert.Equal(PassportLoginMethod.OverseaPassword, account.LoginMethod);
        Assert.Equal(PassportRealm.Oversea, account.Realm);
        Assert.Equal("mid-os", account.Mid);
        Assert.Equal("stoken-os", account.Credentials.SToken);
        Assert.Equal(53, account.Device.DeviceId.Length);
        Assert.Equal("traveler@example.com", client.OverseaPasswordAccount);
        Assert.Equal("secret-password", client.OverseaPasswordValue);
    }

    [Fact]
    public async Task OverseaPasswordLogin_CompletesGeetestWithSameDevice()
    {
        var store = new MemoryAccountStore();
        var client = new StubPassportClient();
        client.OverseaAttempts.Enqueue(new OverseaPasswordLoginAttempt(
            Tokens: null,
            GeetestChallenge: new PassportGeetestChallenge(
                "opaque-state",
                "gt-1",
                "challenge-1"),
            Retcode: -3101,
            Message: "risk"));
        client.OverseaAttempts.Enqueue(new OverseaPasswordLoginAttempt(
            new PassportLoginTokens("900001", "mid-os", "stoken-os")));
        var handler = new StubSecurityVerificationHandler();
        var service = CreateService(store, client);

        PassportAccount account = await service.LoginWithOverseaPasswordAsync(
            "traveler@example.com",
            "secret-password",
            handler);

        Assert.Equal("stoken-os", account.Credentials.SToken);
        Assert.Equal(2, client.OverseaAttemptDeviceIds.Count);
        Assert.Equal(
            client.OverseaAttemptDeviceIds[0],
            client.OverseaAttemptDeviceIds[1]);
        Assert.Equal("completed-aigis", client.LastAigis);
        Assert.Equal(1, handler.GeetestCallCount);
    }

    [Fact]
    public async Task OverseaPasswordLogin_CompletesAccountVerificationAndReplaysVerify()
    {
        var store = new MemoryAccountStore();
        var client = new StubPassportClient();
        client.OverseaAttempts.Enqueue(new OverseaPasswordLoginAttempt(
            Tokens: null,
            AccountVerificationChallenge: new PassportAccountVerificationChallenge(
                "opaque-state",
                "ticket-1"),
            Retcode: -3208,
            Message: "account verification required"));
        client.OverseaAttempts.Enqueue(new OverseaPasswordLoginAttempt(
            new PassportLoginTokens("900001", "mid-os", "stoken-os")));
        var handler = new StubSecurityVerificationHandler();
        var service = CreateService(store, client);

        PassportAccount account = await service.LoginWithOverseaPasswordAsync(
            "traveler@example.com",
            "secret-password",
            handler);

        Assert.Equal("stoken-os", account.Credentials.SToken);
        Assert.Equal(2, client.OverseaAttemptDeviceIds.Count);
        Assert.Equal(
            client.OverseaAttemptDeviceIds[0],
            client.OverseaAttemptDeviceIds[1]);
        Assert.Equal(1, client.PrepareAccountVerificationCallCount);
        Assert.Equal(1, client.VerifyAccountCallCount);
        Assert.Equal("123456", client.VerifiedCaptcha);
        Assert.Equal("completed-verify", client.LastVerify);
        Assert.Equal("t***@example.com", handler.AccountVerificationDestination);
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
    public async Task MaintainAllAsync_DeletedWhileRequestIsRunning_DoesNotRecreateAccount()
    {
        var store = new MemoryAccountStore();
        PassportAccount account = CreateAccount(
            "1",
            Now - TimeSpan.FromDays(8));
        await store.SaveAsync(account);
        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new StubPassportClient
        {
            Verification = new PassportSessionVerification("rotated-token"),
            DerivedTokens = new PassportDerivedTokens("new-ltoken", "new-cookie"),
            DerivedTokenRequestStarted = started,
            ContinueDerivedTokenRequest = resume
        };
        var service = CreateService(store, client);

        Task<PassportMaintenanceResult> maintenanceTask =
            service.MaintainAllAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await store.DeleteAsync(account.Id);
        resume.SetResult();
        PassportMaintenanceResult result = await maintenanceTask;

        Assert.Null(await store.GetByIdAsync(account.Id));
        Assert.Equal(0, result.UpdatedCount);
        Assert.Empty(result.FailedAccountIds);
    }

    [Fact]
    public async Task MaintainAllAsync_ReloginWhileRequestIsRunning_PreservesFreshCredentials()
    {
        var store = new MemoryAccountStore();
        PassportAccount account = CreateAccount(
            "1",
            Now - TimeSpan.FromDays(8));
        await store.SaveAsync(account);
        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new StubPassportClient
        {
            Verification = new PassportSessionVerification("rotated-token"),
            DerivedTokens = new PassportDerivedTokens("new-ltoken", "new-cookie"),
            DerivedTokenRequestStarted = started,
            ContinueDerivedTokenRequest = resume
        };
        var service = CreateService(store, client);

        Task<PassportMaintenanceResult> maintenanceTask =
            service.MaintainAllAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        DateTimeOffset reloginTime = Now + TimeSpan.FromSeconds(1);
        var relogged = new PassportAccount(
            account.Id,
            account.Aid,
            account.Mid,
            account.DisplayName,
            PassportLoginMethod.MobileCaptcha,
            new PassportCredentials(
                "fresh-root",
                "fresh-ltoken",
                "fresh-cookie",
                reloginTime,
                reloginTime,
                reloginTime,
                reloginTime),
            account.Device,
            account.CreatedAt,
            reloginTime,
            account.Realm);
        await store.SaveAsync(relogged);
        resume.SetResult();
        PassportMaintenanceResult result = await maintenanceTask;

        PassportAccount stored = (await store.GetByIdAsync(account.Id))!;
        Assert.Equal("fresh-root", stored.Credentials.SToken);
        Assert.Equal("fresh-cookie", stored.Credentials.CookieToken);
        Assert.Equal(reloginTime, stored.UpdatedAt);
        Assert.Equal(0, result.UpdatedCount);
        Assert.Empty(result.FailedAccountIds);
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
    public async Task ReLoginWithCookieOnly_PreservesOmittedCredentialsAndMetadata()
    {
        var store = new MemoryAccountStore();
        PassportAccount existing = CreateAccount(
            "12345",
            Now - TimeSpan.FromDays(8));
        await store.SaveAsync(existing);
        var client = new StubPassportClient();
        var service = CreateService(store, client);

        PassportAccount updated = await service.LoginWithManualCookieAsync(
            "account_id=12345; cookie_token=new-cookie");

        Assert.Equal(existing.Id, updated.Id);
        Assert.Equal(existing.Device, updated.Device);
        Assert.Equal(existing.Mid, updated.Mid);
        Assert.Equal("root-12345", updated.Credentials.SToken);
        Assert.Equal("old-ltoken", updated.Credentials.LToken);
        Assert.Equal("new-cookie", updated.Credentials.CookieToken);
        Assert.Equal(existing.Credentials.STokenUpdatedAt, updated.Credentials.STokenUpdatedAt);
        Assert.Equal(existing.Credentials.LTokenUpdatedAt, updated.Credentials.LTokenUpdatedAt);
        Assert.Equal(Now, updated.Credentials.CookieTokenUpdatedAt);
        Assert.Equal(existing.Credentials.SessionVerifiedAt, updated.Credentials.SessionVerifiedAt);
        Assert.Equal(0, client.DerivedTokenCallCount);
    }

    [Fact]
    public async Task LoginWithSameAidInDifferentRealms_CreatesSeparateAccounts()
    {
        var store = new MemoryAccountStore();
        PassportAccount mainland = CreateAccount(
            "900001",
            Now - TimeSpan.FromDays(1));
        await store.SaveAsync(mainland);
        var client = new StubPassportClient
        {
            OverseaPasswordTokens = new PassportLoginTokens(
                "900001",
                "mid-os",
                "stoken-os")
        };
        var service = CreateService(store, client);

        PassportAccount oversea = await service.LoginWithOverseaPasswordAsync(
            "traveler@example.com",
            "secret-password");

        Assert.NotEqual(mainland.Id, oversea.Id);
        Assert.Equal(PassportRealm.MainlandChina, mainland.Realm);
        Assert.Equal(PassportRealm.Oversea, oversea.Realm);
        Assert.Equal(2, (await store.GetAllAsync()).Count);
    }

    [Fact]
    public async Task ConfirmedQrRelogin_PreservesDerivedCredentials()
    {
        var store = new MemoryAccountStore();
        PassportAccount existing = CreateAccount(
            "12345",
            Now - TimeSpan.FromDays(8));
        await store.SaveAsync(existing);
        var client = new StubPassportClient
        {
            QrPollResult = new PassportQrPollResult(
                PassportQrStatus.Confirmed,
                new PassportLoginTokens(
                    "12345",
                    "mid-12345",
                    "new-root"))
        };
        var service = CreateService(store, client);

        (PassportQrStatus status, PassportAccount? account) =
            await service.PollQrLoginAsync(new PassportQrSession(
                "ticket",
                "https://example.test/qr",
                "temporary-device"));

        Assert.Equal(PassportQrStatus.Confirmed, status);
        Assert.NotNull(account);
        Assert.Equal(existing.Id, account.Id);
        Assert.Equal(existing.Device, account.Device);
        Assert.Equal("new-root", account.Credentials.SToken);
        Assert.Equal("old-ltoken", account.Credentials.LToken);
        Assert.Equal("old-cookie", account.Credentials.CookieToken);
        Assert.Equal(0, client.DerivedTokenCallCount);
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

    private static TestPassportServices CreateService(
        MemoryAccountStore store,
        StubPassportClient client)
    {
        var writer = new PassportAccountWriter(
            store,
            client,
            client,
            new FixedTimeProvider(Now));
        return new TestPassportServices(
            new MainlandPassportLoginService(client, writer),
            new HoYoLabPassportLoginService(client, writer),
            new PassportCredentialMaintenanceService(
                store,
                client,
                client,
                new FixedTimeProvider(Now),
                new PassportCredentialMaintenanceOptions
                {
                    DerivedCredentialMaxAge = TimeSpan.FromDays(1),
                    SessionVerificationInterval = TimeSpan.FromDays(7)
                }));
    }

    private sealed class TestPassportServices(
        MainlandPassportLoginService mainland,
        HoYoLabPassportLoginService hoYoLab,
        PassportCredentialMaintenanceService maintenance)
    {
        public Task<PassportAccount> LoginWithManualCookieAsync(string cookie) =>
            mainland.LoginWithManualCookieAsync(cookie);

        public Task<MobileCaptchaChallenge> SendMobileCaptchaAsync(string mobile) =>
            mainland.SendMobileCaptchaAsync(mobile);

        public Task<PassportAccount> LoginWithMobileCaptchaAsync(
            string mobile,
            string captcha,
            MobileCaptchaChallenge challenge) =>
            mainland.LoginWithMobileCaptchaAsync(mobile, captcha, challenge);

        public Task<PassportAccount> LoginWithOverseaPasswordAsync(
            string account,
            string password) =>
            hoYoLab.LoginWithPasswordAsync(account, password);

        public Task<PassportAccount> LoginWithOverseaPasswordAsync(
            string account,
            string password,
            IPassportSecurityVerificationHandler verificationHandler) =>
            hoYoLab.LoginWithPasswordAsync(
                account,
                password,
                verificationHandler);

        public Task<(PassportQrStatus Status, PassportAccount? Account)> PollQrLoginAsync(
            PassportQrSession session) =>
            mainland.PollQrLoginAsync(session);

        public Task<PassportMaintenanceResult> MaintainAllAsync() =>
            maintenance.MaintainAllAsync();
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

        public Task<bool> TrySaveIfUnchangedAsync(
            PassportAccount original,
            PassportAccount updated,
            CancellationToken cancellationToken = default)
        {
            if (!accounts.TryGetValue(original.Id, out PassportAccount? current) ||
                !SameSnapshot(current, original))
            {
                return Task.FromResult(false);
            }

            accounts[updated.Id] = updated;
            return Task.FromResult(true);
        }

        public Task DeleteAsync(
            Guid accountId,
            CancellationToken cancellationToken = default)
        {
            accounts.Remove(accountId);
            return Task.CompletedTask;
        }

        private static bool SameSnapshot(
            PassportAccount left,
            PassportAccount right)
        {
            return left.Id == right.Id &&
                left.UpdatedAt == right.UpdatedAt &&
                left.Realm == right.Realm &&
                left.LoginMethod == right.LoginMethod &&
                left.Aid == right.Aid &&
                left.Mid == right.Mid &&
                left.Device == right.Device &&
                left.Credentials.SToken == right.Credentials.SToken &&
                left.Credentials.LToken == right.Credentials.LToken &&
                left.Credentials.CookieToken ==
                    right.Credentials.CookieToken &&
                left.Credentials.SessionVerifiedAt ==
                    right.Credentials.SessionVerifiedAt;
        }
    }

    private sealed class StubSecurityVerificationHandler
        : IPassportSecurityVerificationHandler
    {
        public int GeetestCallCount { get; private set; }

        public string? AccountVerificationDestination { get; private set; }

        public Task<PassportGeetestResult?> VerifyGeetestAsync(
            PassportGeetestChallenge challenge,
            CancellationToken cancellationToken = default)
        {
            GeetestCallCount++;
            return Task.FromResult<PassportGeetestResult?>(new(
                "validated-challenge",
                "validate-token"));
        }

        public Task<string?> RequestAccountVerificationCodeAsync(
            PassportAccountVerificationChallenge challenge,
            CancellationToken cancellationToken = default)
        {
            AccountVerificationDestination = challenge.Destination;
            return Task.FromResult<string?>("123456");
        }
    }

    private sealed class StubPassportClient :
        IMainlandPassportClient,
        IHoYoLabPassportClient
    {
        public PassportDerivedTokens DerivedTokens { get; set; } =
            new(null, null);

        public PassportSessionVerification Verification { get; set; } =
            new(null);

        public PassportLoginTokens MobileTokens { get; set; } =
            new("1", "mid", "stoken");

        public PassportLoginTokens OverseaPasswordTokens { get; set; } =
            new("1", "mid", "stoken");

        public PassportQrPollResult QrPollResult { get; set; } =
            new(PassportQrStatus.Pending);

        public string? FailVerificationForAid { get; set; }

        public bool FailDerivedTokenExchange { get; set; }

        public TaskCompletionSource? DerivedTokenRequestStarted { get; set; }

        public TaskCompletionSource? ContinueDerivedTokenRequest { get; set; }

        public int DerivedTokenCallCount { get; private set; }

        public int VerificationCallCount { get; private set; }

        public string? MobileLoginDeviceId { get; private set; }

        public string? OverseaPasswordAccount { get; private set; }

        public string? OverseaPasswordValue { get; private set; }

        public Queue<OverseaPasswordLoginAttempt> OverseaAttempts { get; } = [];

        public List<string> OverseaAttemptDeviceIds { get; } = [];

        public string? LastAigis { get; private set; }

        public string? LastVerify { get; private set; }

        public int PrepareAccountVerificationCallCount { get; private set; }

        public int VerifyAccountCallCount { get; private set; }

        public string? VerifiedCaptcha { get; private set; }

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
            return Task.FromResult(QrPollResult);
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

        public Task<OverseaPasswordLoginAttempt> AttemptPasswordLoginAsync(
            string account,
            string password,
            PassportDeviceIdentity device,
            string? aigis = null,
            string? verify = null,
            CancellationToken cancellationToken = default)
        {
            OverseaPasswordAccount = account;
            OverseaPasswordValue = password;
            OverseaAttemptDeviceIds.Add(device.DeviceId);
            LastAigis = aigis;
            LastVerify = verify;
            if (OverseaAttempts.Count > 0)
            {
                return Task.FromResult(OverseaAttempts.Dequeue());
            }

            return Task.FromResult(new OverseaPasswordLoginAttempt(
                OverseaPasswordTokens));
        }

        public string CompleteGeetestChallenge(
            PassportGeetestChallenge challenge,
            PassportGeetestResult result)
        {
            return "completed-aigis";
        }

        public Task<PassportAccountVerificationChallenge> PrepareAccountVerificationAsync(
            PassportAccountVerificationChallenge challenge,
            PassportDeviceIdentity device,
            CancellationToken cancellationToken = default)
        {
            PrepareAccountVerificationCallCount++;
            return Task.FromResult(challenge with
            {
                Destination = "t***@example.com"
            });
        }

        public Task VerifyAccountAsync(
            PassportAccountVerificationChallenge challenge,
            string captcha,
            PassportDeviceIdentity device,
            CancellationToken cancellationToken = default)
        {
            VerifyAccountCallCount++;
            VerifiedCaptcha = captcha;
            return Task.CompletedTask;
        }

        public string CompleteAccountVerificationChallenge(
            PassportAccountVerificationChallenge challenge)
        {
            return "completed-verify";
        }

        public async Task<PassportDerivedTokens> GetDerivedTokensAsync(
            PassportAccount account,
            CancellationToken cancellationToken = default)
        {
            DerivedTokenCallCount++;
            if (FailDerivedTokenExchange)
            {
                throw new HttpRequestException("offline");
            }

            DerivedTokenRequestStarted?.TrySetResult();
            if (ContinueDerivedTokenRequest is not null)
            {
                await ContinueDerivedTokenRequest.Task.WaitAsync(
                    cancellationToken);
            }

            return DerivedTokens;
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
