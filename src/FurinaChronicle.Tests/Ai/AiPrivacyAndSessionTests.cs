// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;
using System.Text.Json;
using FurinaChronicle.Services.Ai;

namespace FurinaChronicle.Tests.Ai;

public sealed class AiPrivacyAndSessionTests
{
    [Theory]
    [InlineData("Cookie: fake-value")]
    [InlineData("stoken=synthetic-value")]
    [InlineData("Authorization: Bearer synthetic-value")]
    [InlineData("sk-syntheticlongsecret000000")]
    public void QuestionsContainingCredentialsAreRejectedWithoutEcho(string value)
    {
        var exception = Assert.Throws<AiAnalysisException>(() => AiPrivacy.Sanitize(value));
        Assert.DoesNotContain(value, exception.ToString(), StringComparison.Ordinal);
    }
    [Fact]
    public void FreeTextRemovesLongNumbersGuidsEmailUrlsAndWindowsPaths()
    {
        string text = AiPrivacy.Sanitize("100000001 12345678 ab111111-2222-3333-4444-555555555555 test@example.invalid https://example.invalid/private C:\\private\\file");
        Assert.DoesNotContain("100000001", text); Assert.DoesNotContain("ab111111", text);
        Assert.DoesNotContain("example.invalid", text); Assert.DoesNotContain("private", text);
    }
    [Theory]
    [InlineData("http://example.invalid/v1")]
    [InlineData("https://user:secret@example.invalid/v1")]
    [InlineData("https://example.invalid/v1?key=secret")]
    [InlineData("file:///private")]
    public void InvalidOrCredentialBearingAddressesAreRejected(string address) =>
        Assert.Throws<AiAnalysisException>(() => new AiServiceSettings(BaseAddress: address, Model: "model").Validate());
    [Theory]
    [InlineData("http://127.0.0.1:11434/v1")]
    [InlineData("http://192.168.1.2:11434")]
    [InlineData("https://example.invalid/v1")]
    public void ExplicitLocalAndHttpsAddressesAreSupported(string address) =>
        new AiServiceSettings(BaseAddress: address, Model: "model").Validate();

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    public async Task NoTransportBeforeRequiredConsent(bool uids, bool approved, bool uidApproved)
    {
        var client = new Client(); var session = await Session(client, uids);
        var draft = session.CreateDraft("分析合成资料");
        await Assert.ThrowsAsync<AiAnalysisException>(() => session.GenerateAsync(draft, approved, uidApproved));
        Assert.Equal(0, client.Calls);
    }
    [Fact]
    public async Task CompleteReplyCitesOnlyExistingEvidenceAndQuestionUidBecomesStableAlias()
    {
        var client = new Client { Text = "【记录事实】已保存抽取。[E001] 这个不存在：[E999]" };
        var session = await Session(client);
        var draft = session.CreateDraft("分析 100000001 的记录");
        Assert.Contains("账号 01", draft.Question);
        Assert.DoesNotContain("100000001", draft.Preview);
        await session.GenerateAsync(draft, true);
        var turn = Assert.Single(session.Turns);
        Assert.True(turn.Complete); Assert.Equal(new[] { "E001" }, turn.ValidReferences);
        Assert.Equal(new[] { "E999" }, turn.UnknownReferences);
        Assert.Contains("未提供", turn.Status);
        Assert.DoesNotContain("synthetic-provider-key", draft.Preview);
        Assert.Equal("synthetic-provider-key", client.LastRequest!.ApiKey);
        Assert.DoesNotContain("synthetic-provider-key", client.LastRequest.ToString());
        Assert.DoesNotContain("synthetic-provider-key", JsonSerializer.Serialize(client.LastRequest));
    }
    [Fact]
    public async Task ChangingContextOrSettingsInvalidatesApprovedDraftAndConversation()
    {
        var client = new Client(); var session = await Session(client);
        var old = session.CreateDraft("第一份");
        session.SetContext(Context());
        await Assert.ThrowsAsync<AiAnalysisException>(() => session.GenerateAsync(old, true));
        var second = session.CreateDraft("第二份");
        await session.SaveSettingsAsync(session.Settings with { Model = "another-model" });
        await Assert.ThrowsAsync<AiAnalysisException>(() => session.GenerateAsync(second, true));
        Assert.Equal(0, client.Calls); Assert.Empty(session.Turns);
    }
    [Fact]
    public async Task PartialReplyIsVisibleButNeverReplayedAsCompleteHistory()
    {
        var client = new Client { Truncated = true }; var session = await Session(client);
        await session.GenerateAsync(session.CreateDraft("第一个问题"), true);
        Assert.False(session.Turns[0].Complete); Assert.NotEmpty(session.Turns[0].Answer);
        var draft = session.CreateDraft("继续");
        Assert.DoesNotContain(draft.Messages, message => message.Role == "assistant");
    }
    [Fact]
    public async Task ScopeResetCancelsAndDiscardsLateOldReply()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new Client { Gate = gate }; var session = await Session(client);
        var running = session.GenerateAsync(session.CreateDraft("旧范围"), true);
        await client.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        session.Reset(); session.SetContext(Context()); gate.SetResult(); await running;
        Assert.Empty(session.Turns); Assert.False(session.IsBusy);
    }
    [Fact]
    public async Task FollowUpRedactsUidHallucinatedByPreviousReply()
    {
        var client = new Client { Text = "模型提到号码 999888777 [E001]" }; var session = await Session(client);
        await session.GenerateAsync(session.CreateDraft("分析"), true);
        var draft = session.CreateDraft("解释刚才的结论");
        Assert.Contains(draft.Messages, message => message.Role == "assistant");
        Assert.DoesNotContain("999888777", draft.Preview);
    }
    internal static PreparedAiContext Context(bool uids = false)
    {
        var fact = new AiEvidenceFact("E001", uids ? "账号 01（UID 100000001）" : "账号 01", "gacha", "2026-11-01", null, null,
            "StandardImport", "Partial", "合成五星抽取");
        return new(new("genshin", uids ? "archive_uid_with_confirmation" : "session_aliases_only", null, null,
            [fact.Account], [], ["合成资料；非完整历史"], [fact]), [new(fact, Guid.NewGuid(), "gacha", "private-reference")],
            ["100000001"], new Dictionary<string, string> { ["100000001"] = "账号 01" });
    }
    private static async Task<AiAnalysisSession> Session(Client client, bool uids = false)
    {
        var session = new AiAnalysisSession(client, new Config()); await session.LoadSettingsAsync(); session.SetContext(Context(uids)); return session;
    }
    private sealed class Config : IAiConfigurationStore
    {
        public AiServiceSettings Settings { get; set; } = new(BaseAddress: "https://example.invalid/v1", Model: "synthetic-model");
        public Task<AiServiceSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Settings);
        public Task SaveAsync(AiServiceSettings settings, CancellationToken cancellationToken = default) { Settings = settings; return Task.CompletedTask; }
        public Task<string?> GetApiKeyAsync(AiServiceSettings settings, CancellationToken cancellationToken = default) => Task.FromResult<string?>("synthetic-provider-key");
        public Task SetApiKeyAsync(AiServiceSettings settings, string? key, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private sealed class Client : IAiChatClient
    {
        public int Calls; public AiChatRequest? LastRequest;
        public string Text = "合成结论 [E001]"; public bool Truncated;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource? Gate;
        public Task<IReadOnlyList<string>> ListModelsAsync(AiServiceSettings settings, string? apiKey, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>(["synthetic-model"]);
        public async IAsyncEnumerable<AiChatDelta> StreamAsync(AiChatRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Calls++; LastRequest = request; Started.TrySetResult();
            if (Gate is not null) await Gate.Task; else await Task.Yield();
            yield return new(Text); yield return new(Done: true, Truncated: Truncated);
        }
    }
}
