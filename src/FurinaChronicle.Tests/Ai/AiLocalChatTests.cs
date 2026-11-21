// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;
using System.Text;
using FurinaChronicle.Services.Ai;
using FurinaChronicle.Infrastructure.Ai;

namespace FurinaChronicle.Tests.Ai;
public sealed class AiLocalChatTests
{
    [Fact]
    public async Task OrdinaryChatSurvivesReopenAndContinuesWithoutGameDataOrKeyInFile()
    {
        using var folder=new Folder();var store=new JsonAiConversationStore(folder.Path);var client=new Client();
        var first=await Session(client,store);first.UpdateDraft("未发送的草稿");
        await first.GenerateAsync(first.CreateDraft("第一个问题"),true);
        Guid id=first.ConversationId;var second=await Session(client,store);await second.OpenConversationAsync(id);
        Assert.Single(second.Turns);Assert.Null(second.Context);Assert.Contains("第一个问题",second.CreateMarkdownReport());
        var follow=second.CreateDraft("接着分析");Assert.Contains(follow.Messages,x=>x.Role=="assistant");
        await second.GenerateAsync(follow,true);Assert.Equal(2,second.Turns.Count);
        Assert.DoesNotContain("synthetic-provider-key",await File.ReadAllTextAsync(System.IO.Path.Combine(folder.Path,id.ToString("N")+".json")));
    }
    [Fact]
    public async Task DraftsRenamePinAndEditForkAreDurableAndOriginalConversationIsUntouched()
    {
        using var folder=new Folder();var store=new JsonAiConversationStore(folder.Path);var s=await Session(new Client(),store);
        s.UpdateDraft("草稿 100000001");await s.SaveAsync();var disk=await store.LoadAsync(s.ConversationId);
        Assert.DoesNotContain("100000001",disk!.DraftText);
        await s.GenerateAsync(s.CreateDraft("首轮"),true);await s.GenerateAsync(s.CreateDraft("第二轮"),true);
        Guid original=s.ConversationId;await s.RenameAsync("测试历史");await s.TogglePinAsync();
        string question=await s.ForkFromAsync(s.Turns[1].Id);Assert.Equal("第二轮",question);Assert.Single(s.Turns);Assert.NotEqual(original,s.ConversationId);
        var untouched=await store.LoadAsync(original);Assert.Equal(2,untouched!.Turns.Count);Assert.True(untouched.Pinned);Assert.Equal("测试历史",untouched.Title);
        await s.DeleteCurrentAsync();Assert.NotNull(await store.LoadAsync(original));
    }
    [Fact]
    public async Task MoreThanTwelveTurnsRemainLocalAndHistoryPackingOnlyOmitsFromRequest()
    {
        using var folder=new Folder();var client=new Client{Text=new string('答',1600)};var s=await Session(client,new JsonAiConversationStore(folder.Path));
        for(int i=0;i<15;i++)await s.GenerateAsync(s.CreateDraft("问题 "+i),true);
        Assert.Equal(15,s.Turns.Count);var draft=s.CreateDraft("继续");Assert.True(draft.OmittedTurns>0);Assert.Equal(15,s.Turns.Count);
        Assert.True(draft.Messages.Count>3);Assert.Contains("omitted_old_turns",draft.Preview);
    }
    [Fact]
    public async Task ArchiveUidNeedsConsentAgainAfterReopeningAndCanBecomeAnonymousWithoutChangingEvidence()
    {
        using var folder=new Folder();var client=new Client();var store=new JsonAiConversationStore(folder.Path);var s=await Session(client,store);
        s.SetContext(AiPrivacyAndSessionTests.Context(true));await s.GenerateAsync(s.CreateDraft("分析"),true,true);
        var reopened=await Session(client,store);await reopened.OpenConversationAsync(s.ConversationId);
        await Assert.ThrowsAsync<AiAnalysisException>(()=>reopened.GenerateAsync(reopened.CreateDraft("继续"),true));
        int calls=client.Calls;await reopened.UseAnonymousContextAsync();var draft=reopened.CreateDraft("继续 100000001");
        Assert.False(draft.IncludesUids);Assert.DoesNotContain("100000001",draft.Preview);Assert.Single(reopened.Turns);
        Assert.Equal("E001",reopened.Context!.Evidence[0].Fact.Reference);
        await reopened.GenerateAsync(draft,true);Assert.Equal(calls+1,client.Calls);
    }
    [Fact]
    public async Task SavingFailurePreventsPaidDispatchAndKeepsMessageForRecovery()
    {
        var client=new Client();var s=await Session(client,new FailingStore());s.UpdateDraft("不要丢失");
        await s.GenerateAsync(s.CreateDraft("不要丢失"),true);Assert.Equal(0,client.Calls);Assert.False(s.Turns[0].Complete);Assert.Equal("不要丢失",s.DraftText);
        Assert.Contains("保存失败",s.Status);
    }
    [Fact]
    public async Task SwitchingDuringInitialSaveCannotDispatchOldRequestOrEraseNewDraft()
    {
        var store=new BlockingStore();var client=new Client();var s=await Session(client,store);
        var running=s.GenerateAsync(s.CreateDraft("旧问题"),true);await store.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        s.Reset();s.UpdateDraft("新会话草稿");store.Gate.TrySetResult();await running;
        Assert.Equal(0,client.Calls);Assert.Equal("新会话草稿",s.DraftText);Assert.Empty(s.Turns);
    }
    [Fact]
    public async Task PersistedUnfinishedGenerationIsShownAsInterrupted()
    {
        using var folder=new Folder();var store=new JsonAiConversationStore(folder.Path);var id=Guid.NewGuid();var now=DateTimeOffset.UtcNow;
        await store.SaveAsync(new(id,"中断测试",false,now,now,null,[new(Guid.NewGuid(),"问题","部分回答",false,"正在生成…",now,[],"synthetic")]));
        var s=await Session(new Client(),store);await s.OpenConversationAsync(id);Assert.False(s.Turns[0].Complete);Assert.Contains("中断",s.Turns[0].Status);Assert.Equal("部分回答",s.Turns[0].Answer);
    }
    [Theory]
    [InlineData("{bad}")]
    [InlineData("{\"version\":999}")]
    public async Task UnknownOrCorruptConversationIsRetainedAndCannotBeOverwritten(string text)
    {
        using var folder=new Folder();var id=Guid.NewGuid();string path=System.IO.Path.Combine(folder.Path,id.ToString("N")+".json");await File.WriteAllTextAsync(path,text);var store=new JsonAiConversationStore(folder.Path);
        Assert.Equal(1,(await store.ListAsync()).UnreadableFiles);
        await Assert.ThrowsAsync<AiAnalysisException>(()=>store.LoadAsync(id));
        await Assert.ThrowsAsync<AiAnalysisException>(()=>store.SaveAsync(new(id,"replacement",false,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,null,[])));
        Assert.Equal(text,await File.ReadAllTextAsync(path));
    }
    [Fact]
    public async Task TextAttachmentsCleanIdentifiersAndRejectJsonCredentialFields()
    {
        var reader=new AiAttachmentReader();var file=await reader.ReadAsync("records.json",new MemoryStream(Encoding.UTF8.GetBytes("{\"uid\":\"100000001\",\"value\":42}")));
        Assert.DoesNotContain("100000001",file.Text);Assert.Contains("42",file.Text);Assert.Null(file.ImageBase64);
        await Assert.ThrowsAsync<AiAnalysisException>(()=>reader.ReadAsync("secret.json",new MemoryStream(Encoding.UTF8.GetBytes("{\"api_key\":\"do-not-persist\"}"))));
        await Assert.ThrowsAsync<AiAnalysisException>(()=>reader.ReadAsync("document.pdf",new MemoryStream([1,2,3])));
    }
    [Fact]
    public async Task ImagesRequireVisionAndPrivacyReviewAndUseSummariesInPreview()
    {
        var client=new Client();var s=await Session(client,null);var image=new AiAttachment(Guid.NewGuid(),"picture.png","image/png",null,Convert.ToBase64String([1,2,3]),3,"synthetic-hash");
        s.AddAttachment(image);Assert.Throws<AiAnalysisException>(()=>s.CreateDraft("分析图片"));
        await s.SaveSettingsAsync(s.Settings with{SupportsImages=true});Assert.Throws<AiAnalysisException>(()=>s.CreateDraft("分析图片"));
        s.ReviewImages(true);var draft=s.CreateDraft("分析图片");Assert.Single(draft.Messages[^1].Images!);Assert.DoesNotContain("AQID",draft.Preview);Assert.Contains("synthetic-hash",draft.Preview);
        await s.GenerateAsync(draft,true);Assert.Single(client.Last!.Messages[^1].Images!);
    }
    [Fact]
    public async Task ProviderCannotEchoItsActualAuthorizationKeyIntoPersistedConversation()
    {
        using var folder=new Folder();var client=new Client{Text="synthetic-provider-key [E001]"};
        var s=await Session(client,new JsonAiConversationStore(folder.Path));await s.GenerateAsync(s.CreateDraft("test"),true);
        Assert.DoesNotContain("synthetic-provider-key",s.Turns[0].Answer);
        Assert.DoesNotContain("synthetic-provider-key",await File.ReadAllTextAsync(System.IO.Path.Combine(folder.Path,s.ConversationId.ToString("N")+".json")));
    }
    [Fact]
    public async Task OnlyKnownUncommittedTempFilesAreCleanedAndCommittedHistoryIsPreserved()
    {
        using var folder=new Folder();var store=new JsonAiConversationStore(folder.Path);var s=await Session(new Client(),store);
        await s.GenerateAsync(s.CreateDraft("已保存"),true);
        string orphan=System.IO.Path.Combine(folder.Path,Guid.NewGuid().ToString("N")+".json."+Guid.NewGuid().ToString("N")+".tmp");
        string foreign=System.IO.Path.Combine(folder.Path,"user-note.tmp");await File.WriteAllTextAsync(orphan,"uncommitted private fragment");await File.WriteAllTextAsync(foreign,"user file");
        Assert.Single((await store.ListAsync()).Items);Assert.False(File.Exists(orphan));Assert.True(File.Exists(foreign));Assert.NotNull(await store.LoadAsync(s.ConversationId));
    }
    [Fact]
    public async Task CompletedReplyKeepsOrdinarySuffixEvenWhenItMatchesStartOfCredential()
    {
        var s=await Session(new Client{Text="Yes"},null);await s.GenerateAsync(s.CreateDraft("Reply in English"),true);
        Assert.Equal("Yes",s.Turns[0].Answer);Assert.True(s.Turns[0].Complete);
    }
    private static async Task<AiAnalysisSession> Session(Client client,IAiConversationStore? store)
    {var s=new AiAnalysisSession(client,new Config(),store);await s.LoadSettingsAsync();return s;}
    private sealed class Config : IAiConfigurationStore
    {
        public AiServiceSettings Value=new(BaseAddress:"https://example.invalid/v1",Model:"synthetic",ContextCharacters:4000);
        public Task<AiServiceSettings> LoadAsync(CancellationToken token=default)=>Task.FromResult(Value);
        public Task SaveAsync(AiServiceSettings s,CancellationToken token=default){Value=s;return Task.CompletedTask;}
        public Task<string?> GetApiKeyAsync(AiServiceSettings s,CancellationToken token=default)=>Task.FromResult<string?>("synthetic-provider-key");
        public Task SetApiKeyAsync(AiServiceSettings s,string? key,CancellationToken token=default)=>Task.CompletedTask;
    }
    private sealed class Client : IAiChatClient
    {
        public int Calls;public string Text="合成回答 [E001]";public AiChatRequest? Last;
        public Task<IReadOnlyList<string>> ListModelsAsync(AiServiceSettings s,string? key,CancellationToken token=default)=>Task.FromResult<IReadOnlyList<string>>(["synthetic"]);
        public async IAsyncEnumerable<AiChatDelta> StreamAsync(AiChatRequest request,[EnumeratorCancellation]CancellationToken token=default)
        {Calls++;Last=request;await Task.Yield();yield return new(Text);yield return new(Done:true);}
    }
    private class FailingStore : IAiConversationStore
    {
        public Task<AiConversationListing> ListAsync(CancellationToken token=default)=>Task.FromResult(new AiConversationListing([],0));
        public Task<AiConversation?> LoadAsync(Guid id,CancellationToken token=default)=>Task.FromResult<AiConversation?>(null);
        public virtual Task SaveAsync(AiConversation c,CancellationToken token=default)=>throw new IOException("synthetic storage failure");
        public Task DeleteAsync(Guid id,CancellationToken token=default)=>Task.CompletedTask;
    }
    private sealed class BlockingStore : FailingStore
    {
        public TaskCompletionSource Started=new(TaskCreationOptions.RunContinuationsAsynchronously),Gate=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async Task SaveAsync(AiConversation c,CancellationToken token=default){Started.TrySetResult();await Gate.Task;}
    }
    private sealed class Folder : IDisposable
    {
        public string Path {get;}=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"FurinaAiChatTests",Guid.NewGuid().ToString("N"));
        public Folder()=>Directory.CreateDirectory(Path);
        public void Dispose()=>Directory.Delete(Path,true);
    }
}
