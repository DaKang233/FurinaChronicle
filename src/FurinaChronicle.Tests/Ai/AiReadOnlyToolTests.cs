// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;
using System.Text.Json;
using FurinaChronicle.Infrastructure.Ai;
using FurinaChronicle.Services.Ai;
using FurinaChronicle.Tests.Infrastructure.Persistence.Sqlite;

namespace FurinaChronicle.Tests.Ai;

public sealed class AiReadOnlyToolTests
{
    [Fact]
    public async Task GlobalPreferencesSurviveProviderSaveSelectDeleteAndRemainLocalExceptPrompt()
    {
        using var env=new Environment();var p=Provider();await env.Config.SaveProviderAsync(p);
        var preferences=new AiChatPreferences("PRIVATE_DISPLAY_NAME","Answer concisely.",true,3);
        await env.Config.SavePreferencesAsync(preferences);await env.Config.SaveProviderAsync(p with {Name="Renamed"});await env.Config.SelectAsync(p.Id,p.Models[0].Id);
        Assert.Equal(preferences,(await env.Config.LoadProvidersAsync()).Preferences);
        var client=new ToolClient();var session=new AiAnalysisSession(client,env.Config);await session.LoadSettingsAsync();
        var draft=session.CreateDraft("hello");Assert.DoesNotContain(preferences.UserDisplayName,draft.Preview);Assert.Contains(preferences.SystemPrompt,draft.Preview);
        await env.Config.DeleteProviderAsync(p.Id);Assert.Equal(preferences,(await env.Config.LoadProvidersAsync()).Preferences);
    }
    [Theory]
    [InlineData(-1)]
    [InlineData(501)]
    public async Task InvalidToolLimitsDoNotModifySavedConfiguration(int limit)
    {
        using var env=new Environment();await env.Config.SavePreferencesAsync(new());string before=await File.ReadAllTextAsync(env.File);
        await Assert.ThrowsAsync<AiAnalysisException>(()=>env.Config.SavePreferencesAsync(new(MaxToolCalls:limit)));Assert.Equal(before,await File.ReadAllTextAsync(env.File));
    }
    [Fact]
    public async Task PromptCredentialsAndChangedPromptInvalidateSendingBeforeAnyRequest()
    {
        using var env=new Environment();await env.Config.SaveProviderAsync(Provider());
        await Assert.ThrowsAsync<AiAnalysisException>(()=>env.Config.SavePreferencesAsync(new(SystemPrompt:"stoken_v2=synthetic-credential")));
        var client=new ToolClient();var session=new AiAnalysisSession(client,env.Config);await session.LoadSettingsAsync();var draft=session.CreateDraft("hello");
        await env.Config.SavePreferencesAsync(new(SystemPrompt:"new instruction"));await Assert.ThrowsAsync<AiAnalysisException>(()=>session.GenerateAsync(draft,true));Assert.Empty(client.Requests);
    }
    [Theory]
    [InlineData(false,10)]
    [InlineData(true,0)]
    public async Task DisabledOrZeroLimitNeverExecutesModelSuggestedCalls(bool enabled,int max)
    {
        using var env=new Environment();await env.Config.SaveProviderAsync(Provider());await env.Config.SavePreferencesAsync(new(AllowLocalTools:enabled,MaxToolCalls:max));
        var client=new ToolClient();var runner=new Runner();var session=new AiAnalysisSession(client,env.Config,toolRunner:runner);await session.LoadSettingsAsync();session.SetContext(Context());
        var draft=session.CreateDraft("q");Assert.Empty(draft.Tools);await session.GenerateAsync(draft,true);Assert.Equal(0,runner.Calls);Assert.False(session.Turns[0].Complete);Assert.Single(client.Requests);
    }
    [Theory]
    [InlineData(AiProtocol.OpenAiCompatible)]
    [InlineData(AiProtocol.OpenAiResponses)]
    [InlineData(AiProtocol.AnthropicMessages)]
    public async Task EachNativeProtocolCompletesBoundedToolLoopWithoutPersistingRawResultsOrContinuations(AiProtocol protocol)
    {
        using var env=new Environment();var provider=Provider(protocol);await env.Config.SaveProviderAsync(provider);await env.Config.SavePreferencesAsync(new(AllowLocalTools:true,MaxToolCalls:1));
        int calls=0;using var http=new HttpClient(new AiNativeProtocolTests.Handler((_,_)=>Task.FromResult(AiNativeProtocolTests.Response(++calls==1?AiNativeProtocolTests.Tool(protocol,true):AiNativeProtocolTests.Final(protocol,"结论 [E001]")))));
        using var client=new HttpAiChatClient(http);var runner=new Runner();var store=new JsonAiConversationStore(Path.Combine(env.Root,"chats"));
        var session=new AiAnalysisSession(client,env.Config,store,runner);await session.InitializeAsync();await session.LoadSettingsAsync();session.SetContext(Context());
        var draft=session.CreateDraft("q");Assert.Equal(3,draft.Tools.Count);Assert.Contains("max_tool_calls",draft.Preview);await session.GenerateAsync(draft,true);
        Assert.True(session.Turns[0].Complete);Assert.Equal(1,runner.Calls);Assert.Equal(2,calls);Assert.Single(session.Turns[0].ToolUses);
        string saved=await File.ReadAllTextAsync(Path.Combine(env.Root,"chats",session.ConversationId.ToString("N")+".json"));
        Assert.DoesNotContain("OPAQUE_ONLY",saved);Assert.DoesNotContain("RAW_TOOL_ONLY",saved);Assert.Contains("get_gacha_statistics",saved);
        var reopened=new AiAnalysisSession(client,env.Config,store,runner);await reopened.OpenConversationAsync(session.ConversationId);Assert.Single(reopened.Turns[0].ToolUses);
    }
    [Fact]
    public async Task LastAllowedCallDisablesMoreCallsButAllowsFinalAnswer()
    {
        using var env=new Environment();await env.Config.SaveProviderAsync(Provider());await env.Config.SavePreferencesAsync(new(AllowLocalTools:true,MaxToolCalls:1));
        var client=new ToolClient{FinalAnswer=true};var runner=new Runner();var session=new AiAnalysisSession(client,env.Config,toolRunner:runner);await session.LoadSettingsAsync();session.SetContext(Context());
        await session.GenerateAsync(session.CreateDraft("q"),true);Assert.True(session.Turns[0].Complete);Assert.Equal(1,runner.Calls);Assert.Equal(2,client.Requests.Count);
        Assert.False(client.Requests[0].DisableTools);Assert.True(client.Requests[1].DisableTools);Assert.Contains(client.Requests[1].Messages,m=>m.Role=="tool");
    }
    [Fact]
    public async Task IgnoringToolLimitCannotExecuteAnotherRead()
    {
        using var env=new Environment();await env.Config.SaveProviderAsync(Provider());await env.Config.SavePreferencesAsync(new(AllowLocalTools:true,MaxToolCalls:1));
        var client=new ToolClient();var runner=new Runner();var session=new AiAnalysisSession(client,env.Config,toolRunner:runner);await session.LoadSettingsAsync();session.SetContext(Context());
        await session.GenerateAsync(session.CreateDraft("q"),true);Assert.False(session.Turns[0].Complete);Assert.Equal(1,runner.Calls);Assert.Equal(2,client.Requests.Count);
    }
    [Fact]
    public async Task NoContextOrNoHostRunnerDoesNotAdvertiseTools()
    {
        using var env=new Environment();await env.Config.SaveProviderAsync(Provider());await env.Config.SavePreferencesAsync(new(AllowLocalTools:true));
        var a=new AiAnalysisSession(new ToolClient(),env.Config,toolRunner:new Runner());await a.LoadSettingsAsync();Assert.Empty(a.CreateDraft("q").Tools);
        var b=new AiAnalysisSession(new ToolClient(),env.Config);await b.LoadSettingsAsync();b.SetContext(Context());Assert.Empty(b.CreateDraft("q").Tools);
    }
    [Fact]
    public async Task ToolAuthorizationDoesNotBypassArchiveUidConsent()
    {
        using var env=new Environment();await env.Config.SaveProviderAsync(Provider());await env.Config.SavePreferencesAsync(new(AllowLocalTools:true));
        var client=new ToolClient();var runner=new Runner();var session=new AiAnalysisSession(client,env.Config,toolRunner:runner);await session.LoadSettingsAsync();session.SetContext(Context(true));
        await Assert.ThrowsAsync<AiAnalysisException>(()=>session.GenerateAsync(session.CreateDraft("q"),true,false));Assert.Empty(client.Requests);Assert.Equal(0,runner.Calls);
    }
    [Fact]
    public async Task GlobalPromptIsSanitizedByTheSameSingleAccountScope()
    {
        using var env=new Environment();await env.Config.SaveProviderAsync(Provider());await env.Config.SavePreferencesAsync(new(SystemPrompt:"Analyze UID 100000001"));
        var session=new AiAnalysisSession(new ToolClient(),env.Config);await session.LoadSettingsAsync();session.SetContext(Context());Assert.DoesNotContain("100000001",session.CreateDraft("q").Preview);
    }
    [Fact]
    public async Task UnknownToolAndCredentialEchoCannotReachRunnerOrNextRequest()
    {
        using var env=new Environment();await env.Config.SaveProviderAsync(Provider(),"synthetic-key");await env.Config.SavePreferencesAsync(new(AllowLocalTools:true));
        var client=new ToolClient{Name="run_shell"};var runner=new Runner();var session=new AiAnalysisSession(client,env.Config,toolRunner:runner);await session.LoadSettingsAsync();session.SetContext(Context());
        await session.GenerateAsync(session.CreateDraft("q"),true);Assert.Equal(0,runner.Calls);Assert.Single(client.Requests);Assert.False(session.Turns[0].Complete);
    }
    [Fact]
    public async Task ConcreteToolsUseOnlyWhitelistedSnapshotAndReadonlySourceCheck()
    {
        await using var db=SqliteRepositoryTestContext.Create();await db.Archives.GetAllAsync();var context=Context();var runner=new AiArchiveToolRunner(new ReadAiEvidence(db.Gacha,db.AtomicGacha));
        var result=await runner.ExecuteAsync(new("c","search_archive_evidence","{\"query\":\"\",\"dataset\":\"gacha\",\"limit\":20}"),context);
        Assert.Equal(1,result.Items);Assert.DoesNotContain(context.Evidence[0].AccountId.ToString(),result.Content);Assert.DoesNotContain("100000001",result.Content);
        var read=await runner.ExecuteAsync(new("c","read_archive_evidence","{\"reference\":\"E001\"}"),context);Assert.Contains("已不存在",read.Content);
        var foreign=await runner.ExecuteAsync(new("c","read_archive_evidence","{\"reference\":\"E999\"}"),context);Assert.Equal(0,foreign.Items);
        using var raw=db.OpenRawConnection();Assert.Equal(0,raw.ExecuteScalar<int>("SELECT COUNT(*) FROM GachaRecords"));Assert.Equal(0,raw.ExecuteScalar<int>("SELECT COUNT(*) FROM DataChangeSets"));
    }
    [Theory]
    [InlineData("{\"uid\":\"100000001\"}")]
    [InlineData("{\"reference\":\"E001\",\"sql\":\"DELETE FROM GachaRecords\"}")]
    [InlineData("{\"reference\":\"E001\",\"path\":\"C:/private/file\"}")]
    public async Task ConcreteToolsRejectExtraIdentitySqlOrFileArguments(string args)
    {
        await using var db=SqliteRepositoryTestContext.Create();var runner=new AiArchiveToolRunner(new ReadAiEvidence(db.Gacha,db.AtomicGacha));
        await Assert.ThrowsAsync<AiAnalysisException>(()=>runner.ExecuteAsync(new("c","read_archive_evidence",args),Context()));
    }
    [Fact]
    public async Task FailedReadonlyQueryIsCountedAndCannotTriggerAnotherPaidRequest()
    {
        using var env=new Environment();await env.Config.SaveProviderAsync(Provider());await env.Config.SavePreferencesAsync(new(AllowLocalTools:true));
        var client=new ToolClient();var session=new AiAnalysisSession(client,env.Config,toolRunner:new FailedRunner());await session.LoadSettingsAsync();session.SetContext(Context());
        await session.GenerateAsync(session.CreateDraft("q"),true);Assert.False(session.Turns[0].Complete);Assert.Single(client.Requests);
        Assert.Equal("只读查询未确认完成",Assert.Single(session.Turns[0].ToolUses).Summary);
    }
    private sealed class FailedRunner:IAiReadOnlyToolRunner
    {public Task<AiToolResult> ExecuteAsync(AiToolCall c,PreparedAiContext p,CancellationToken t=default)=>throw new AiAnalysisException("本地来源不可读取。");}
    [Fact]
    public async Task VersionTwoConfigurationRemainsReadableAndWritesVersionThreeToProtectGlobalPreferences()
    {
        using var env=new Environment();var profile=Provider();
        await File.WriteAllTextAsync(env.File,JsonSerializer.Serialize(new{version=2,configuration=new AiProviderConfiguration(profile.Id,[profile])},AiJson.Options));
        Assert.Equal("You",(await env.Config.LoadProvidersAsync()).Preferences.UserDisplayName);
        await env.Config.SavePreferencesAsync(new("Local label","Custom prompt",true,4));
        using var doc=JsonDocument.Parse(await File.ReadAllTextAsync(env.File));Assert.Equal(3,doc.RootElement.GetProperty("version").GetInt32());
        Assert.Equal("Custom prompt",(await env.Config.LoadProvidersAsync()).Preferences.SystemPrompt);
        Assert.Equal(profile.Id,(await env.Config.LoadAsync()).ProviderId);
    }
    private static PreparedAiContext Context(bool uids=false)
    {
        var fact=new AiEvidenceFact("E001",uids?"账号 01（UID 100000001）":"账号 01","gacha","2026-01-01",null,null,"StandardImport","Partial","合成五星");
        return new(new("genshin",uids?"archive_uid_with_confirmation":"session_aliases_only",null,null,[fact.Account],[],["Partial snapshot"],[fact]),
            [new(fact,Guid.NewGuid(),"gacha","private-record")],["100000001"],new Dictionary<string,string>{{"100000001","账号 01"}});
    }
    private static AiProviderProfile Provider(AiProtocol protocol=AiProtocol.OpenAiCompatible)
    {var model=new AiModelProfile(Guid.NewGuid(),"Test","test");return new(Guid.NewGuid(),"Test",protocol,"https://native.example/v1",[model],model.Id);}
    private sealed class Environment:IDisposable
    {
        public string Root{get;}=Path.Combine(Path.GetTempPath(),"FurinaToolTests",Guid.NewGuid().ToString("N"));public string File=>Path.Combine(Root,"settings.json");public JsonAiConfigurationStore Config{get;}
        public Environment(){Directory.CreateDirectory(Root);Config=new(File,new Secrets());}public void Dispose(){Directory.Delete(Root,true);}
    }
    private sealed class Secrets:IAiSecretStore
    {private readonly Dictionary<string,string> values=[];public Task<string?> GetAsync(string s,CancellationToken t=default)=>Task.FromResult(values.GetValueOrDefault(s));public Task SetAsync(string s,string? value,CancellationToken t=default){if(value is null)values.Remove(s);else values[s]=value;return Task.CompletedTask;}}
    private sealed class Runner:IAiReadOnlyToolRunner
    {public int Calls;public Task<AiToolResult> ExecuteAsync(AiToolCall c,PreparedAiContext p,CancellationToken t=default){Calls++;return Task.FromResult(new AiToolResult("RAW_TOOL_ONLY","已查询统计",1));}}
    private sealed class ToolClient:IAiChatClient
    {
        public List<AiChatRequest> Requests{get;}=[];public bool FinalAnswer;public string Name="get_gacha_statistics";
        public Task<IReadOnlyList<string>> ListModelsAsync(AiServiceSettings s,string? key,CancellationToken t=default)=>Task.FromResult<IReadOnlyList<string>>([]);
        public async IAsyncEnumerable<AiChatDelta> StreamAsync(AiChatRequest r,[EnumeratorCancellation]CancellationToken t=default)
        {Requests.Add(r);await Task.Yield();if(FinalAnswer&&Requests.Count>1)yield return new("结论 [E001]");else yield return new(ToolCall:new("call"+Requests.Count,Name,"{}"));yield return new(Done:true);}
    }
}
