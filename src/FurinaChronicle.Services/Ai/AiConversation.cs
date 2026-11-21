// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

namespace FurinaChronicle.Services.Ai;

public sealed record AiAttachment(Guid Id, string Name, string MediaType, string? Text, string? ImageBase64,
    int Bytes, string Sha256, bool PrivacyReviewed = false)
{
    public bool IsImage => ImageBase64 is not null;
    public string Label => $"{Name} · {Bytes / 1024d:F1} KiB" + (IsImage && !PrivacyReviewed ? " · 待确认图片隐私" : "");
}
public sealed record AiImage(string MediaType, string Base64, string Sha256);
public sealed record AiContextState(AiContextPacket Packet, IReadOnlyList<AiLocalEvidence> Evidence,
    IReadOnlyList<string> PrivateIdentifiers, IReadOnlyDictionary<string,string> UidAliases);
public sealed record AiStoredTurn(Guid Id, string Question, string Answer, bool Complete, string Status,
    DateTimeOffset CreatedAt, IReadOnlyList<AiAttachment> Attachments, string Model, string ProviderName = "", IReadOnlyList<AiToolUse>? ToolUses = null);
public sealed record AiConversation(Guid Id, string Title, bool Pinned, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    AiContextState? Context, IReadOnlyList<AiStoredTurn> Turns, string DraftText = "", IReadOnlyList<AiAttachment>? DraftAttachments = null, Guid? ProviderId = null, Guid? ModelProfileId = null);
public sealed record AiConversationSummary(Guid Id, string Title, bool Pinned, DateTimeOffset UpdatedAt, int Turns, string LocalScope = "")
{
    public string Label => (Pinned ? "★ " : "") + Title + (LocalScope.Length > 0 ? " · " + LocalScope : "");
}
public sealed record AiConversationListing(IReadOnlyList<AiConversationSummary> Items, int UnreadableFiles);
public interface IAiConversationStore
{
    Task<AiConversationListing> ListAsync(CancellationToken cancellationToken = default);
    Task<AiConversation?> LoadAsync(Guid id, CancellationToken cancellationToken = default);
    Task SaveAsync(AiConversation conversation, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

// One local gate covers creation of app-managed game-data copies and irreversible deletion.
// The optional consumer must never participate in normal domain reads or cloud operations.
public interface IAiConversationCopyCoordinator
{
    Task<IAsyncDisposable> ProtectCopiesAsync(CancellationToken token = default);
    Task<IAsyncDisposable> ProtectDeletionAsync(Guid scopeId, CancellationToken token = default);
    void Track(Guid owner, IReadOnlyCollection<Guid> scopes);
    void Untrack(Guid owner);
}

public sealed class AiConversationDeletionBlockedException(bool unreadable = false) : InvalidOperationException(unreadable
    ? "部分本地 AI 会话无法读取，无法确认是否保留待删数据。请先处理这些会话文件，再永久删除。"
    : "该数据仍被本地 AI 会话引用。请先进入 AI 聊天，按本地 UID 查找并手动删除相关会话，再执行永久删除。");
