// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.History;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Infrastructure.Ai;
using FurinaChronicle.Infrastructure.Persistence.Sqlite;
using FurinaChronicle.Presentation.Gacha;
using FurinaChronicle.Services.Ai;
using FurinaChronicle.Services.Gacha.History;
using FurinaChronicle.Services.History;
using FurinaChronicle.Tests.Infrastructure.Persistence.Sqlite;

namespace FurinaChronicle.Tests.Ai;
public sealed class AiChatDeletionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ParentPurgeIsBlockedUntilRelatedChatsAreManuallyDeleted(bool archiveScope)
    {
        await using var db=SqliteRepositoryTestContext.Create();using var files=new Folder();var chats=new JsonAiConversationStore(files.Path);
        var archive=SqliteRepositoryTestContext.CreateArchive();await db.Archives.AddAsync(archive);
        var account=SqliteRepositoryTestContext.CreateAccount(archive.Id);await db.Accounts.AddAsync(account);
        var chat=Conversation(archive.Id,account.Id);await chats.SaveAsync(chat);
        var guarded=new SqliteGachaAtomicChangeStore(db.Database,null,chats);
        Guid scope=archiveScope?archive.Id:account.Id;var now=DateTimeOffset.UtcNow;var operation=OperationId.New();
        Task<GachaAtomicChangeResult> Purge()=>archiveScope?guarded.PurgeArchiveAsync(new(operation,scope,now,now,"synthetic purge")):guarded.PurgeAccountAsync(new(operation,scope,now,now,"synthetic purge"));
        await Assert.ThrowsAsync<AiConversationDeletionBlockedException>(()=>Purge());Assert.NotNull(await db.Accounts.GetByIdAsync(account.Id));Assert.NotNull(await db.Archives.GetByIdAsync(archive.Id));
        await chats.DeleteAsync(chat.Id);var result=await Purge();Assert.Equal(ChangeExecutionStatus.Applied,result.Status);Assert.Null(await db.Accounts.GetByIdAsync(account.Id));
        await chats.SaveAsync(chat);Assert.Equal(ChangeExecutionStatus.AlreadyCommitted,(await Purge()).Status);
    }
    [Fact]
    public async Task RecordPurgeSurfacesSpecificPromptAndDoesNotChangeFactOrVersion()
    {
        await using var db=SqliteRepositoryTestContext.Create();using var files=new Folder();var chats=new JsonAiConversationStore(files.Path);
        var archive=SqliteRepositoryTestContext.CreateArchive();await db.Archives.AddAsync(archive);var account=SqliteRepositoryTestContext.CreateAccount(archive.Id);await db.Accounts.AddAsync(account);
        var now=DateTimeOffset.UtcNow;var record=new GachaRecord(account.Id,"synthetic-record","合成物品",5,now){GachaType="301",ItemId="10000089"};
        await db.AtomicGacha.CommitAsync(new(OperationId.New(),DataChangeOperationKind.Import,DataOrigin.StandardImport,"synthetic",now,now,[new GachaFactMutation(new(account.Id,record.ExternalRecordId),record)],captureUndo:false));
        var guarded=new SqliteGachaAtomicChangeStore(db.Database,null,chats);var reference=new GachaFactReference(account.Id,record.ExternalRecordId);var before=await guarded.GetCurrentAsync(reference);
        var chat=Conversation(archive.Id,account.Id);await chats.SaveAsync(chat);var coordinator=new GachaRecordOperationCoordinator(new ManageGachaHistory(guarded),guarded);
        var plan=await coordinator.PlanAsync(reference,GachaRecordOperationKind.IrreversibleDelete);var result=await coordinator.ExecuteAsync(plan.Plan!);
        Assert.Equal(GachaRecordOperationOutcomeKind.Conflict,result.Kind);Assert.Contains("手动删除相关会话",result.Message);Assert.Equal(before,await guarded.GetCurrentAsync(reference));
        await chats.DeleteAsync(chat.Id);Assert.True((await coordinator.ExecuteAsync(plan.Plan!)).IsSuccess);Assert.Null(await guarded.GetCurrentAsync(reference));
    }
    [Fact]
    public async Task PreparingCopiesAndDeletionAreSerializedAndActiveCopiesCannotBeBypassedByRemovingFile()
    {
        using var folder=new Folder();var chats=new JsonAiConversationStore(folder.Path);Guid account=Guid.NewGuid(),archive=Guid.NewGuid();
        var lease=await chats.ProtectCopiesAsync();var deletion=chats.ProtectDeletionAsync(account);
        Assert.False(deletion.IsCompleted);await chats.SaveAsync(Conversation(archive,account));await lease.DisposeAsync();
        await Assert.ThrowsAsync<AiConversationDeletionBlockedException>(()=>deletion);
        foreach(string path in Directory.GetFiles(folder.Path,"*.json"))File.Delete(path);
        Guid owner=Guid.NewGuid();chats.Track(owner,[account]);await Assert.ThrowsAsync<AiConversationDeletionBlockedException>(()=>chats.ProtectDeletionAsync(account));
        chats.Untrack(owner);await using var permit=await chats.ProtectDeletionAsync(account);
    }
    [Fact]
    public async Task CorruptChatPreventsUnverifiablePurgeAndUnrelatedScopeIsAllowed()
    {
        using var folder=new Folder();var chats=new JsonAiConversationStore(folder.Path);await chats.SaveAsync(Conversation(Guid.NewGuid(),Guid.NewGuid()));
        await using(var permit=await chats.ProtectDeletionAsync(Guid.NewGuid())){}
        await File.WriteAllTextAsync(System.IO.Path.Combine(folder.Path,Guid.NewGuid().ToString("N")+".json"),"corrupt");
        await Assert.ThrowsAsync<AiConversationDeletionBlockedException>(()=>chats.ProtectDeletionAsync(Guid.NewGuid()));
    }
    [Fact]
    public async Task RepeatingCommittedRecordPurgeStillReturnsReceiptWhenNewChatsExist()
    {
        await using var db=SqliteRepositoryTestContext.Create();using var files=new Folder();var chats=new JsonAiConversationStore(files.Path);
        var archive=SqliteRepositoryTestContext.CreateArchive();await db.Archives.AddAsync(archive);var account=SqliteRepositoryTestContext.CreateAccount(archive.Id);await db.Accounts.AddAsync(account);
        var now=DateTimeOffset.UtcNow;var reference=new GachaFactReference(account.Id,"repeat-delete");var record=new GachaRecord(account.Id,reference.ExternalRecordId,"合成",5,now){GachaType="301",ItemId="10000089"};
        await db.AtomicGacha.CommitAsync(new(OperationId.New(),DataChangeOperationKind.Import,DataOrigin.StandardImport,"synthetic",now,now,[new(reference,record)],captureUndo:false));
        var guarded=new SqliteGachaAtomicChangeStore(db.Database,null,chats);var current=await guarded.GetCurrentAsync(reference);
        var request=new GachaIrreversibleDeleteRequest(OperationId.New(),reference,current!.Version,now,now);
        Assert.Equal(ChangeExecutionStatus.Applied,(await guarded.IrreversiblyDeleteAsync(request)).Status);
        await chats.SaveAsync(Conversation(archive.Id,account.Id));
        Assert.Equal(ChangeExecutionStatus.AlreadyCommitted,(await guarded.IrreversiblyDeleteAsync(request)).Status);
    }
    private static AiConversation Conversation(Guid archive,Guid account)
    {
        var context=new AiContextState(new("genshin","session_aliases_only",null,null,["账号 01"],[],[],[]),[],[archive.ToString(),account.ToString()],new Dictionary<string,string>());
        return new(Guid.NewGuid(),"合成会话",false,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,context,[]);
    }
    private sealed class Folder:IDisposable
    {public string Path{get;}=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"FurinaChatDeletionTests",Guid.NewGuid().ToString("N"));public Folder()=>Directory.CreateDirectory(Path);public void Dispose()=>Directory.Delete(Path,true);}
}
