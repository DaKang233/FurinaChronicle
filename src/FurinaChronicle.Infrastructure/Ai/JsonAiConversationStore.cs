// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Text.Json;
using FurinaChronicle.Services.Ai;

namespace FurinaChronicle.Infrastructure.Ai;

public sealed class JsonAiConversationStore(string directory) : IAiConversationStore, IAiConversationCopyCoordinator
{
    private readonly SemaphoreSlim gate = new(1,1);
    private readonly SemaphoreSlim copies = new(1,1);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid,Guid[]> active = new();
    public void Track(Guid owner,IReadOnlyCollection<Guid> scopes) => active[owner]=scopes.ToArray();
    public void Untrack(Guid owner) => active.TryRemove(owner,out _);
    public async Task<IAsyncDisposable> ProtectCopiesAsync(CancellationToken token=default)
    { await copies.WaitAsync(token); return new Lease(copies); }
    public async Task<IAsyncDisposable> ProtectDeletionAsync(Guid scopeId,CancellationToken token=default)
    {
        var lease=await ProtectCopiesAsync(token);
        try
        {
            var list=await ListAsync(token);
            if(list.UnreadableFiles>0)throw new AiConversationDeletionBlockedException(true);
            bool related=active.Values.Any(ids=>ids.Contains(scopeId));
            foreach(var item in list.Items)
            {
                var chat=await LoadAsync(item.Id,token);
                if(chat?.Context is {} c && (c.Evidence.Any(e=>e.AccountId==scopeId) || c.PrivateIdentifiers.Any(x=>Guid.TryParse(x,out var id)&&id==scopeId)))related=true;
            }
            if(related)throw new AiConversationDeletionBlockedException();
            return lease;
        }
        catch { await lease.DisposeAsync(); throw; }
    }
    private sealed class Lease(SemaphoreSlim semaphore):IAsyncDisposable
    { private bool disposed; public ValueTask DisposeAsync(){if(!disposed){disposed=true;semaphore.Release();}return ValueTask.CompletedTask;} }

    public const int MaxFileBytes = 32 * 1024 * 1024;
    private sealed record Envelope(int Version, AiConversation Conversation);
    private string PathFor(Guid id) => id == Guid.Empty ? throw new AiAnalysisException("会话标识无效。") : Path.Combine(directory, id.ToString("N") + ".json");
    public async Task<AiConversationListing> ListAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!Directory.Exists(directory)) return new([],0);
            // The file gate guarantees these are not active writes. Remove only our exact
            // uncommitted temporary-file pattern; committed/unknown files are never discarded.
            foreach (string temp in Directory.EnumerateFiles(directory,"*.tmp"))
            {
                string[] parts=Path.GetFileName(temp).Split('.');
                if(parts.Length==4 && parts[1]=="json" && Guid.TryParseExact(parts[0],"N",out _) && Guid.TryParseExact(parts[2],"N",out _)) File.Delete(temp);
            }
            var result = new List<AiConversationSummary>(); int unreadable = 0;
            foreach (string path in Directory.EnumerateFiles(directory,"*.json").Take(1001))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (result.Count >= 1000) throw new AiAnalysisException("本地会话超过 1000 个，请先整理后继续。");
                try { var c = await ReadAsync(path,cancellationToken); result.Add(new(c.Id,c.Title,c.Pinned,c.UpdatedAt,c.Turns.Count,c.Context is {} context ? context.UidAliases.Count > 0 ? "本地 UID " + string.Join("、",context.UidAliases.Keys) : "已附加游戏资料" : "")); }
                catch (AiAnalysisException) { unreadable++; }
            }
            return new(result.OrderByDescending(x=>x.Pinned).ThenByDescending(x=>x.UpdatedAt).ToArray(),unreadable);
        }
        catch (Exception e) when(e is IOException or UnauthorizedAccessException) { throw new AiAnalysisException("无法读取本地聊天目录。"); }
        finally { gate.Release(); }
    }
    public async Task<AiConversation?> LoadAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try { string path=PathFor(id); return File.Exists(path) ? await ReadAsync(path,cancellationToken) : null; }
        finally { gate.Release(); }
    }
    private static async Task<AiConversation> ReadAsync(string path,CancellationToken token)
    {
        try
        {
            if(new FileInfo(path).Length>MaxFileBytes) throw new AiAnalysisException("聊天文件超过本地上限，原文件保持不变。");
            var e=JsonSerializer.Deserialize<Envelope>(await File.ReadAllTextAsync(path,token),AiJson.Options);
            if(e?.Version!=1 || e.Conversation is not { } c || c.Id==Guid.Empty || c.Turns is null || c.Turns.Count>1000 ||
                Path.GetFileNameWithoutExtension(path)!=c.Id.ToString("N") || c.Title is null || c.Turns.Any(x=>x.Question is null || x.Answer is null || x.Attachments is null))
                throw new AiAnalysisException("聊天版本或内容无效，原文件保持不变。");
            return c;
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        { throw new AiAnalysisException("聊天文件不可读取，原文件保持不变。"); }
    }
    public async Task SaveAsync(AiConversation conversation,CancellationToken cancellationToken=default)
    {
        string path=PathFor(conversation.Id), temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        await gate.WaitAsync(cancellationToken);
        try
        {
            // Refuse to overwrite a corrupt/unknown future file, even if caller supplied its ID.
            if(File.Exists(path)) _=await ReadAsync(path,cancellationToken);
            byte[] bytes=JsonSerializer.SerializeToUtf8Bytes(new Envelope(1,conversation),AiJson.Options);
            if(bytes.Length>MaxFileBytes || conversation.Turns.Count>1000) throw new AiAnalysisException("当前会话达到本地上限，请新建会话；原历史不会自动删除。");
            Directory.CreateDirectory(directory);
            await File.WriteAllBytesAsync(temp,bytes,cancellationToken); cancellationToken.ThrowIfCancellationRequested();
            File.Move(temp,path,true);
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException) { throw new AiAnalysisException("聊天未能保存，请检查本地存储；不会自动继续发送。"); }
        finally { try { if(File.Exists(temp)) File.Delete(temp); } finally {gate.Release();} }
    }
    public async Task DeleteAsync(Guid id,CancellationToken cancellationToken=default)
    { await gate.WaitAsync(cancellationToken); try { File.Delete(PathFor(id)); } finally { gate.Release(); } }
}
