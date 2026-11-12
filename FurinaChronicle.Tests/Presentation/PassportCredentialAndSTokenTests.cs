// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.History;
using FurinaChronicle.Presentation.Management;
using FurinaChronicle.Services.Abstractions;
using FurinaChronicle.Services.Gacha;
using FurinaChronicle.Services.Gacha.Refreshing;

namespace FurinaChronicle.Tests.Presentation;

public sealed class PassportCredentialAndSTokenTests
{
    private const string SyntheticSToken = "synthetic-token-value";
    private const string SyntheticMid = "123456789012";
    private const string BoundUid = "100000001";

    [Fact]
    public void CredentialValidationMasksSensitiveValues()
    {
        Assert.Null(CredentialValidation.ValidateSToken(SyntheticSToken));
        Assert.Null(CredentialValidation.ValidateMid(SyntheticMid));
        Assert.Null(CredentialValidation.ValidateBoundUid(BoundUid));
        Assert.Contains("不能为空", CredentialValidation.ValidateSToken(" "));
        Assert.Contains("纯数字", CredentialValidation.ValidateMid("12ab"));
        Assert.Contains("9", CredentialValidation.ValidateBoundUid("123"));
        string masked = CredentialValidation.MaskMid(SyntheticMid);
        Assert.DoesNotContain(SyntheticMid, masked);
        Assert.Contains("****", masked);
    }

    [Fact]
    public async Task PassportStateListsSavesDeletesAndClearsWithoutSecretsInSnapshot()
    {
        var store = new FakeCredentialStore();
        var state = new PassportCredentialState(store);
        await state.EnterAsync();
        Assert.False(state.Snapshot.HasCredentials);

        PassportCredentialActionResult invalid = await state.SaveAsync(
            " ", SyntheticMid, BoundUid, null);
        Assert.False(invalid.Succeeded);
        Assert.Equal(0, store.SaveCallCount);

        PassportCredentialActionResult saved = await state.SaveAsync(
            SyntheticSToken, SyntheticMid, BoundUid, "测试凭据");
        Assert.True(saved.Succeeded);
        Assert.Equal(1, store.SaveCallCount);
        Assert.True(state.Snapshot.HasCredentials);
        Assert.NotNull(state.Snapshot.SelectedCredential);
        Assert.DoesNotContain(SyntheticSToken, state.Snapshot.Status);
        Assert.DoesNotContain(SyntheticMid, state.Snapshot.Status);
        Assert.DoesNotContain(SyntheticSToken, state.Snapshot.SelectedCredential!.MaskedMid);
        Assert.DoesNotContain(SyntheticMid, state.Snapshot.SelectedCredential!.MaskedMid);

        PassportCredentialActionResult deleted = await state.DeleteSelectedAsync();
        Assert.True(deleted.Succeeded);
        Assert.Equal(1, store.DeleteCallCount);
        Assert.Contains("未删除档案", deleted.Message);
        Assert.False(state.Snapshot.HasCredentials);

        await state.ClearAsync();
        Assert.Equal(1, store.ClearCallCount);
    }

    [Fact]
    public async Task PassportStateSurfacesCorruptStoreWarningAndSafeErrors()
    {
        var store = new FakeCredentialStore
        {
            LoadWarning = "本机隔离宿主凭据文件无法解密或已损坏；已按空列表退化，未删除档案。",
            ListError = new UnauthorizedAccessException("C:\\secret\\credentials.bin")
        };
        var state = new PassportCredentialState(store);

        await state.EnterAsync();

        Assert.Equal(store.LoadWarning, state.Snapshot.LoadWarning);
        Assert.NotNull(state.Snapshot.Error);
        Assert.DoesNotContain("C:\\", state.Snapshot.Error);
        Assert.DoesNotContain("secret", state.Snapshot.Error);
    }

    [Fact]
    public async Task PlanFailsWithoutCredentialOrMatchingUidWithoutCallingRefresh()
    {
        var executor = new FakeExecutor();
        var account = Account();
        var coordinator = new GachaSTokenRefreshCoordinator(
            new FakeCredentialStore(),
            new FakeAccountRepository(account),
            executor);

        GachaSTokenRefreshPlanResult noCredential = await coordinator.PlanAsync(account.Id);
        Assert.False(noCredential.Succeeded);
        Assert.Contains("没有已保存", noCredential.FailureMessage);
        Assert.Equal(0, executor.CallCount);

        var mismatchedStore = new FakeCredentialStore();
        mismatchedStore.Add(BoundUid: "999999999");
        var mismatch = new GachaSTokenRefreshCoordinator(
            mismatchedStore,
            new FakeAccountRepository(account),
            executor);
        GachaSTokenRefreshPlanResult mismatchResult = await mismatch.PlanAsync(account.Id);
        Assert.False(mismatchResult.Succeeded);
        Assert.Contains("没有绑定", mismatchResult.FailureMessage);
        Assert.Equal(0, executor.CallCount);
    }

    [Fact]
    public async Task PlanFreezesFullModeAndExecuteUsesSnapshot()
    {
        var store = new FakeCredentialStore();
        store.Add();
        var account = Account();
        var executor = new FakeExecutor();
        var coordinator = new GachaSTokenRefreshCoordinator(
            store, new FakeAccountRepository(account), executor);

        GachaSTokenRefreshPlanResult planned = await coordinator.PlanAsync(account.Id);
        Assert.True(planned.Succeeded);
        Assert.Equal(GachaImportMode.Full, planned.Plan!.Mode);
        Assert.Equal(account.Id, planned.Plan.TargetAccount.Id);
        Assert.DoesNotContain(SyntheticSToken, planned.Plan.ConfirmationText);

        GachaSTokenRefreshOutcome outcome = await coordinator.ExecuteAsync(planned.Plan);

        Assert.True(outcome.Succeeded);
        Assert.Equal(1, executor.CallCount);
        Assert.Equal(GachaRefreshSource.SToken, executor.LastRequest!.Source);
        Assert.Equal(GachaImportMode.Full, executor.LastRequest.Mode);
        Assert.Equal(account.Id, executor.LastRequest.GameAccount.Id);
        Assert.Contains("新增 1", outcome.Message);
    }

    [Fact]
    public async Task InvalidCredentialMarksStoreUnusableWithoutLeakingValues()
    {
        var store = new FakeCredentialStore();
        store.Add();
        var account = Account();
        var executor = new FakeExecutor
        {
            Error = new InvalidOperationException("genAuthKey failed (123): synthetic body")
        };
        var coordinator = new GachaSTokenRefreshCoordinator(
            store, new FakeAccountRepository(account), executor);
        GachaSTokenRefreshPlanResult planned = await coordinator.PlanAsync(account.Id);

        GachaSTokenRefreshOutcome outcome =
            await coordinator.ExecuteAsync(planned.Plan!);

        Assert.False(outcome.Succeeded);
        Assert.Contains("失效", outcome.Message);
        Assert.DoesNotContain(SyntheticSToken, outcome.Message);
        Assert.DoesNotContain(SyntheticMid, outcome.Message);
        Assert.Equal(1, store.MarkUnusableCallCount);
        Assert.False(store.LastWasUnusable);
    }

    [Fact]
    public async Task NetworkFailureIsReportedWithoutMarkingCredentialInvalid()
    {
        var store = new FakeCredentialStore();
        store.Add();
        var account = Account();
        var executor = new FakeExecutor { Error = new HttpRequestException("offline") };
        var coordinator = new GachaSTokenRefreshCoordinator(
            store, new FakeAccountRepository(account), executor);
        GachaSTokenRefreshPlanResult planned = await coordinator.PlanAsync(account.Id);

        GachaSTokenRefreshOutcome outcome =
            await coordinator.ExecuteAsync(planned.Plan!);

        Assert.False(outcome.Succeeded);
        Assert.Contains("网络", outcome.Message);
        Assert.Equal(0, store.MarkUnusableCallCount);
    }

    [Fact]
    public async Task MissingCredentialAtExecuteTimeDoesNotCallRefresh()
    {
        var store = new FakeCredentialStore();
        store.Add();
        var account = Account();
        var executor = new FakeExecutor();
        var coordinator = new GachaSTokenRefreshCoordinator(
            store, new FakeAccountRepository(account), executor);
        GachaSTokenRefreshPlanResult planned = await coordinator.PlanAsync(account.Id);
        await store.DeleteAsync(store.Entries[0].Id);

        GachaSTokenRefreshOutcome outcome =
            await coordinator.ExecuteAsync(planned.Plan!);

        Assert.False(outcome.Succeeded);
        Assert.Contains("未开始网络刷新", outcome.Message);
        Assert.Equal(0, executor.CallCount);
    }

    private static GameAccount Account() => new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Guid.Parse("22222222-2222-2222-2222-222222222222"),
        BoundUid,
        GameServerRegion.ChinaOfficial,
        "测试账号",
        false,
        DateTimeOffset.Now.AddDays(-1),
        DateTimeOffset.Now);

    private sealed class FakeCredentialStore : IIsolatedCredentialStore
    {
        public List<IsolatedCredentialSummary> Entries { get; } = [];
        public string? LoadWarning { get; set; }
        public Exception? ListError { get; set; }
        public int SaveCallCount { get; private set; }
        public int DeleteCallCount { get; private set; }
        public int ClearCallCount { get; private set; }
        public int MarkUnusableCallCount { get; private set; }
        public bool LastWasUnusable { get; private set; } = true;

        public void Add(string BoundUid = PassportCredentialAndSTokenTests.BoundUid)
        {
            Entries.Add(new IsolatedCredentialSummary(
                Guid.NewGuid(),
                "测试凭据",
                CredentialValidation.MaskMid(SyntheticMid),
                BoundUid,
                DateTimeOffset.Now,
                true,
                null));
        }

        public Task<IReadOnlyList<IsolatedCredentialSummary>> ListAsync(
            CancellationToken cancellationToken = default)
        {
            if (ListError is not null)
            {
                return Task.FromException<IReadOnlyList<IsolatedCredentialSummary>>(ListError);
            }

            return Task.FromResult<IReadOnlyList<IsolatedCredentialSummary>>(
                Entries.ToArray());
        }

        public Task<Guid> SaveAsync(
            string sToken,
            string mid,
            string boundRoleUid,
            string? label,
            CancellationToken cancellationToken = default)
        {
            SaveCallCount++;
            Guid id = Guid.NewGuid();
            Entries.Add(new IsolatedCredentialSummary(
                id,
                label,
                CredentialValidation.MaskMid(mid),
                boundRoleUid,
                DateTimeOffset.Now,
                true,
                null));
            return Task.FromResult(id);
        }

        public Task DeleteAsync(Guid accountId, CancellationToken cancellationToken = default)
        {
            DeleteCallCount++;
            Entries.RemoveAll(item => item.Id == accountId);
            return Task.CompletedTask;
        }

        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            ClearCallCount++;
            Entries.Clear();
            return Task.CompletedTask;
        }

        public Task MarkUnusableAsync(
            Guid accountId,
            string safeReason,
            CancellationToken cancellationToken = default)
        {
            MarkUnusableCallCount++;
            LastWasUnusable = false;
            Entries.RemoveAll(item => item.Id == accountId);
            Entries.Add(new IsolatedCredentialSummary(
                accountId,
                "测试凭据",
                CredentialValidation.MaskMid(SyntheticMid),
                BoundUid,
                DateTimeOffset.Now,
                false,
                safeReason));
            return Task.CompletedTask;
        }

        public Task MarkUsableAsync(
            Guid accountId,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeAccountRepository(GameAccount account) : IGameAccountRepository
    {
        public Task<GameAccount?> GetByIdAsync(
            Guid gameAcountId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<GameAccount?>(gameAcountId == account.Id ? account : null);

        public Task<IReadOnlyList<GameAccount>> GetByArchiveIdAsync(
            Guid archiveId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<GameAccount>>([account]);

        public Task<GameAccount?> GetByArchiveIdAndNaturalIdentityAsync(
            Guid archiveId,
            GameRoleNaturalIdentity naturalIdentity,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<GameAccount?>(null);

        public Task<GameRoleIdentity?> GetRoleIdentityByNaturalIdentityAsync(
            GameRoleNaturalIdentity naturalIdentity,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<GameRoleIdentity?>(null);

        public Task<GameRoleIdentity?> GetRoleIdentityByIdAsync(
            GameRoleIdentityId roleIdentityId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<GameRoleIdentity?>(null);

        public Task<IReadOnlyList<GameAccount>> GetUnresolvedAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<GameAccount>>([]);

        public Task AddAsync(
            GameAccount account,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UpdateAsync(
            GameAccount account,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeExecutor : IGachaSTokenRefreshExecutor
    {
        public Exception? Error { get; set; }
        public int CallCount { get; private set; }
        public GachaRefreshRequest? LastRequest { get; private set; }

        public Task<GachaRefreshResult> ExecuteAsync(
            GachaRefreshRequest request,
            IProgress<GachaRefreshProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastRequest = request;
            if (Error is not null) return Task.FromException<GachaRefreshResult>(Error);
            return Task.FromResult(new GachaRefreshResult(1, 1, 0, 0, 1, 0));
        }
    }
}
