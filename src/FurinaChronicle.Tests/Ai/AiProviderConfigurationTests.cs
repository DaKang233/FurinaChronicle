// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FurinaChronicle.Infrastructure.Ai;
using FurinaChronicle.Presentation.Ai;
using FurinaChronicle.Services.Ai;

namespace FurinaChronicle.Tests.Ai;

public sealed class AiProviderConfigurationTests
{
    [Fact]
    public async Task SameEndpointProvidersHaveIndependentKeysAndModelOptions()
    {
        using var env=new Environment();var a=Provider("First");var b=Provider("Second");
        await env.Store.SaveProviderAsync(a,"synthetic-first");await env.Store.SaveProviderAsync(b,"synthetic-second");
        Assert.Equal("synthetic-first",await env.Store.GetApiKeyAsync(a.Resolve()));
        Assert.Equal("synthetic-second",await env.Store.GetApiKeyAsync(b.Resolve()));
        var other=new AiModelProfile(Guid.NewGuid(),"Vision","vision",8000,4096,SupportsImages:true);
        a=a with {Models=a.Models.Append(other).ToArray()};await env.Store.SaveProviderAsync(a);
        await env.Store.SelectAsync(a.Id,other.Id);
        var current=await env.Store.LoadAsync();Assert.Equal("vision",current.Model);Assert.True(current.SupportsImages);Assert.Equal(8000,current.ContextCharacters);
        Assert.Equal("synthetic-first",await env.Store.GetApiKeyAsync(current));
        var text=await File.ReadAllTextAsync(env.File);Assert.DoesNotContain("synthetic-first",text);Assert.DoesNotContain("synthetic-second",text);
        var reopened=new JsonAiConfigurationStore(env.File,env.Keys);Assert.Equal(current,await reopened.LoadAsync());
    }
    [Fact]
    public async Task EndpointAndProtocolEditsNeverForwardOldKey()
    {
        using var env=new Environment();var a=Provider("First");await env.Store.SaveProviderAsync(a,"synthetic-key");
        Assert.Null(await env.Store.GetApiKeyAsync(a.Resolve() with {BaseAddress="https://other.example/v1"}));
        Assert.Null(await env.Store.GetApiKeyAsync(a.Resolve() with {Protocol=AiProtocol.Ollama}));
        var edited=a with {BaseAddress="https://other.example/v1"};await env.Store.SaveProviderAsync(edited);
        Assert.Null(await env.Store.GetApiKeyAsync(edited.Resolve()));
        Assert.Equal("synthetic-key",await env.Store.GetApiKeyAsync(a.Resolve()));
    }
    [Fact]
    public async Task LegacyMigrationPreservesSettingsAndMovesKeyOnlyAfterConfigurationCommits()
    {
        using var env=new Environment();var legacy=new AiServiceSettings(BaseAddress:"https://legacy.example/v1",Model:"old-model",SupportsImages:true);
        await File.WriteAllTextAsync(env.File,JsonSerializer.Serialize(legacy,AiJson.Options));
        string slot=LegacySlot(legacy);await env.Keys.SetAsync(slot,"synthetic-legacy-key");
        var current=await env.Store.LoadAsync();
        Assert.Equal(legacy,current with {ProviderId=null,ModelProfileId=null});Assert.NotNull(current.ProviderId);
        Assert.Equal("synthetic-legacy-key",await env.Store.GetApiKeyAsync(current));Assert.Null(await env.Keys.GetAsync(slot));
        var again=await env.Store.LoadProvidersAsync();Assert.Single(again.Providers);Assert.Equal(current,again.Current);
        Assert.Equal(2,JsonDocument.Parse(await File.ReadAllTextAsync(env.File)).RootElement.GetProperty("version").GetInt32());
    }
    [Fact]
    public async Task CanceledMigrationKeepsOriginalFileAndOriginalKey()
    {
        using var env=new Environment();var legacy=new AiServiceSettings(BaseAddress:"https://legacy.example/v1",Model:"old-model");
        string original=JsonSerializer.Serialize(legacy,AiJson.Options);await File.WriteAllTextAsync(env.File,original);
        string slot=LegacySlot(legacy);await env.Keys.SetAsync(slot,"synthetic-legacy-key");
        using var cancel=new CancellationTokenSource();env.Keys.AfterSet=()=>cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>env.Store.LoadProvidersAsync(cancel.Token));
        env.Keys.AfterSet=null;
        Assert.Equal(original,await File.ReadAllTextAsync(env.File));Assert.Equal("synthetic-legacy-key",await env.Keys.GetAsync(slot));
        Assert.Single(env.Keys.Values);Assert.Empty(Directory.GetFiles(env.Root,"*.tmp"));
        Assert.Equal("old-model",(await env.Store.LoadAsync()).Model);
    }
    [Theory]
    [InlineData("{\"version\":99,\"configuration\":{}}")]
    [InlineData("{broken")]
    [InlineData("{}")]
    [InlineData("{\"version\":2,\"configuration\":{\"providers\":[null]}}")]
    public async Task UnknownOrCorruptConfigurationCannotBeSilentlyReplaced(string original)
    {
        using var env=new Environment();await File.WriteAllTextAsync(env.File,original);
        await Assert.ThrowsAsync<AiAnalysisException>(()=>env.Store.LoadProvidersAsync());
        await Assert.ThrowsAsync<AiAnalysisException>(()=>env.Store.SaveProviderAsync(Provider("New")));
        Assert.Equal(original,await File.ReadAllTextAsync(env.File));
    }
    [Fact]
    public async Task InvalidSelectionDoesNotAlterConfigurationOrUseAnotherProvider()
    {
        using var env=new Environment();var a=Provider("First");await env.Store.SaveProviderAsync(a);
        string original=await File.ReadAllTextAsync(env.File);
        await Assert.ThrowsAsync<AiAnalysisException>(()=>env.Store.SelectAsync(a.Id,Guid.NewGuid()));
        await Assert.ThrowsAsync<AiAnalysisException>(()=>env.Store.SelectAsync(Guid.NewGuid(),a.Models[0].Id));
        Assert.Equal(original,await File.ReadAllTextAsync(env.File));
    }
    [Fact]
    public async Task DeletingCurrentProviderDoesNotSelectAnotherRecipientAndRemovesCurrentKey()
    {
        using var env=new Environment();var a=Provider("First");var b=Provider("Second");
        await env.Store.SaveProviderAsync(a,"synthetic-key");await env.Store.SaveProviderAsync(b);await env.Store.DeleteProviderAsync(a.Id);
        var config=await env.Store.LoadProvidersAsync();Assert.Null(config.SelectedProviderId);Assert.Single(config.Providers);
        Assert.Equal("",config.Current.Model);Assert.Null(await env.Store.GetApiKeyAsync(a.Resolve()));
    }
    [Fact]
    public async Task SaveFailureRestoresPreviousSecureKey()
    {
        using var env=new Environment();var a=Provider("First");await env.Store.SaveProviderAsync(a,"synthetic-original");
        using var cancel=new CancellationTokenSource();env.Keys.AfterSet=()=>cancel.Cancel();
        string original=await File.ReadAllTextAsync(env.File);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>env.Store.SaveProviderAsync(a,"synthetic-new",token:cancel.Token));
        env.Keys.AfterSet=null;
        Assert.Equal(original,await File.ReadAllTextAsync(env.File));Assert.Equal("synthetic-original",await env.Store.GetApiKeyAsync(a.Resolve()));
    }
    [Fact]
    public async Task SettingsEditorRetainsMultipleModelsAndDoesNotCopyVisionCapability()
    {
        using var env=new Environment();var client=new Client();var state=new AiSettingsState(env.Store,client);await state.LoadAsync();
        await state.SaveProfileAsync("First","Text",state.Settings with {Model="text",SupportsImages=true});
        var first=state.Settings;state.NewModel();Assert.False(state.Settings.SupportsImages);
        await state.SaveProfileAsync("First","Small",state.Settings with {Model="small"});
        Assert.Equal(2,state.EditingProvider!.Models.Count);
        state.EditModel(first.ModelProfileId!.Value);Assert.Equal("text",state.Settings.Model);Assert.True(state.Settings.SupportsImages);
        await state.DeleteModelAsync();Assert.Single(state.EditingProvider.Models);Assert.Equal("small",state.Settings.Model);
        Assert.Empty(client.Requests);
    }
    [Fact]
    public async Task SessionSwitchUsesCorrectKeyInvalidatesPreviewAndRetainsHistoricalAttribution()
    {
        using var env=new Environment();var a=Provider("First");var b=Provider("Second");
        await env.Store.SaveProviderAsync(a,"synthetic-first");await env.Store.SaveProviderAsync(b,"synthetic-second");
        var client=new Client();var chats=new JsonAiConversationStore(Path.Combine(env.Root,"chats"));var session=new AiAnalysisSession(client,env.Store,chats);
        await session.InitializeAsync();await session.LoadSettingsAsync();var stale=session.CreateDraft("previous recipient");
        await session.SelectModelAsync(b.Id,b.Models[0].Id);Assert.Empty(client.Requests);
        await Assert.ThrowsAsync<AiAnalysisException>(()=>session.GenerateAsync(stale,true));
        await session.GenerateAsync(session.CreateDraft("first message"),true);
        Assert.Equal("synthetic-second",client.Requests[0].ApiKey);Assert.Equal("Second",session.Turns[0].ProviderName);
        await session.SelectModelAsync(a.Id,a.Models[0].Id);await session.GenerateAsync(session.CreateDraft("follow up"),true);
        Assert.Equal("synthetic-first",client.Requests[1].ApiKey);Assert.Equal("Second",session.Turns[0].ProviderName);Assert.Equal("First",session.Turns[1].ProviderName);
        Assert.Contains(client.Requests[1].Messages,m=>m.Content.Contains("first message"));
        var saved=await chats.LoadAsync(session.ConversationId);Assert.Equal(a.Id,saved!.ProviderId);
        // The other chat/default can change independently; this conversation restores its own choice.
        await env.Store.SelectAsync(b.Id,b.Models[0].Id);
        var reopened=new AiAnalysisSession(client,env.Store,chats);await reopened.InitializeAsync();await reopened.LoadSettingsAsync();
        Assert.Equal(a.Id,reopened.Settings.ProviderId);Assert.Equal(2,reopened.Turns.Count);
        Assert.Equal("Second",reopened.Turns[0].ProviderName);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DeletedModelReferenceBlocksRestoredConversationUntilExplicitChoice(bool deleteProvider)
    {
        using var env=new Environment();var a=Provider("First");var b=Provider("Second");await env.Store.SaveProviderAsync(a);await env.Store.SaveProviderAsync(b);
        var chats=new JsonAiConversationStore(Path.Combine(env.Root,"chats"));var client=new Client();var session=new AiAnalysisSession(client,env.Store,chats);
        await session.InitializeAsync();await session.LoadSettingsAsync();await session.NewConversationAsync();await session.GenerateAsync(session.CreateDraft("saved"),true);
        if(deleteProvider)await env.Store.DeleteProviderAsync(a.Id);
        else await env.Store.SaveProviderAsync(a with {Models=[],SelectedModelId=null});
        await env.Store.SelectAsync(b.Id,b.Models[0].Id);
        var reopened=new AiAnalysisSession(client,env.Store,chats);await reopened.InitializeAsync();await reopened.LoadSettingsAsync();
        Assert.True(reopened.SelectionMissing);Assert.Single(reopened.Turns);
        Assert.Throws<AiAnalysisException>(()=>reopened.CreateDraft("blocked"));Assert.Single(client.Requests);
        await reopened.SelectModelAsync(b.Id,b.Models[0].Id);Assert.False(reopened.SelectionMissing);await reopened.GenerateAsync(reopened.CreateDraft("allowed"),true);
        Assert.Equal(2,client.Requests.Count);
    }
    [Fact]
    public async Task RecipientEditBetweenReviewAndSendRequiresNewReview()
    {
        using var env=new Environment();var a=Provider("First");await env.Store.SaveProviderAsync(a);
        var client=new Client();var session=new AiAnalysisSession(client,env.Store);await session.LoadSettingsAsync();var draft=session.CreateDraft("test");
        await env.Store.SaveProviderAsync(a with {BaseAddress="https://other.example/v1"});
        await Assert.ThrowsAsync<AiAnalysisException>(()=>session.GenerateAsync(draft,true));Assert.Empty(client.Requests);Assert.Empty(session.Turns);
    }
    [Fact]
    public async Task DiscoveryUsesOnlyEditingProviderKeyAndSendsNoChat()
    {
        using var env=new Environment();var a=Provider("First");var b=Provider("Second");await env.Store.SaveProviderAsync(a,"synthetic-first");await env.Store.SaveProviderAsync(b,"synthetic-second");
        var client=new Client();var state=new AiSettingsState(env.Store,client);await state.LoadAsync();state.EditProvider(b.Id);
        var models=await state.ModelsAsync(state.Settings);Assert.Equal("synthetic-second",client.DiscoveryKey);Assert.Single(models);Assert.Empty(client.Requests);
    }
    [Fact]
    public async Task LegacySettingsFacadeBindsKeyToItsCurrentProviderInsteadOfOverwritingOriginal()
    {
        using var env=new Environment();var a=Provider("First");var b=Provider("Second");
        await env.Store.SaveProviderAsync(a,"synthetic-first");await env.Store.SaveProviderAsync(b,"synthetic-second");await env.Store.SelectAsync(b.Id,b.Models[0].Id);
        var legacy=new AiServiceSettings(BaseAddress:b.BaseAddress,Model:"updated-model");
        await env.Store.SaveAsync(legacy);await env.Store.SetApiKeyAsync(legacy,"synthetic-updated");
        Assert.Equal(b.Id,(await env.Store.LoadAsync()).ProviderId);Assert.Equal("updated-model",(await env.Store.LoadAsync()).Model);
        Assert.Equal("synthetic-updated",await env.Store.GetApiKeyAsync(legacy));
        Assert.Equal("synthetic-updated",await env.Store.GetApiKeyAsync(b.Resolve()));
        Assert.Equal("synthetic-first",await env.Store.GetApiKeyAsync(a.Resolve()));
    }
    [Fact]
    public async Task ConfigurationReadFailureWhileOpeningCannotEraseStoredChatOnLeaveSave()
    {
        using var env=new Environment();var a=Provider("First");await env.Store.SaveProviderAsync(a);
        var chats=new JsonAiConversationStore(Path.Combine(env.Root,"chats"));var client=new Client();var session=new AiAnalysisSession(client,env.Store,chats);
        await session.InitializeAsync();await session.LoadSettingsAsync();await session.NewConversationAsync();
        await session.GenerateAsync(session.CreateDraft("important synthetic history"),true);session.UpdateDraft("unfinished draft");await session.SaveAsync();
        var reopened=new AiAnalysisSession(client,env.Store,chats);await reopened.InitializeAsync();await reopened.NewConversationAsync();
        string future="{\"version\":99,\"configuration\":{}}";await File.WriteAllTextAsync(env.File,future);
        await Assert.ThrowsAsync<AiAnalysisException>(()=>reopened.OpenConversationAsync(session.ConversationId));
        // UI catches the safe settings error and saves on navigation/close.
        await reopened.SaveAsync();var kept=await chats.LoadAsync(session.ConversationId);
        Assert.Single(kept!.Turns);Assert.Equal("important synthetic history",kept.Turns[0].Question);
        Assert.Equal("synthetic answer",kept.Turns[0].Answer);Assert.Equal("unfinished draft",kept.DraftText);
        Assert.Equal(a.Id,kept.ProviderId);Assert.Equal(future,await File.ReadAllTextAsync(env.File));
    }
    private static AiProviderProfile Provider(string name)
    {var model=new AiModelProfile(Guid.NewGuid(),"Chat","chat");return new(Guid.NewGuid(),name,AiProtocol.OpenAiCompatible,"https://same.example/v1",[model],model.Id);}
    private static string LegacySlot(AiServiceSettings s)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{s.Protocol}:{s.Endpoint.AbsoluteUri}")));
    private sealed class Environment : IDisposable
    {
        public string Root {get;}=Path.Combine(Path.GetTempPath(),"FurinaAiProviders",Guid.NewGuid().ToString("N"));
        public string File=>Path.Combine(Root,"settings.json");
        public Keys Keys {get;}=new();
        public JsonAiConfigurationStore Store {get;}
        public Environment(){Directory.CreateDirectory(Root);Store=new(File,Keys);}
        public void Dispose(){if(Directory.Exists(Root))Directory.Delete(Root,true);}
    }
    private sealed class Keys : IAiSecretStore
    {
        public Dictionary<string,string> Values {get;}=[];public Action? AfterSet {get;set;}
        public Task<string?> GetAsync(string slot,CancellationToken token=default)=>Task.FromResult(Values.GetValueOrDefault(slot));
        public Task SetAsync(string slot,string? value,CancellationToken token=default){token.ThrowIfCancellationRequested();if(value is null)Values.Remove(slot);else Values[slot]=value;AfterSet?.Invoke();return Task.CompletedTask;}
    }
    private sealed class Client : IAiChatClient
    {
        public List<AiChatRequest> Requests {get;}=[];public string? DiscoveryKey {get;private set;}
        public Task<IReadOnlyList<string>> ListModelsAsync(AiServiceSettings s,string? key,CancellationToken token=default){DiscoveryKey=key;return Task.FromResult<IReadOnlyList<string>>(["chat"]);}
        public async IAsyncEnumerable<AiChatDelta> StreamAsync(AiChatRequest r,[System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token=default)
        {Requests.Add(r);await Task.Yield();yield return new("synthetic answer");yield return new(Done:true);}
    }
}
