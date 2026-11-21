// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Presentation.History;
using FurinaChronicle.Presentation.Notifications;
using FurinaChronicle.Services.Abstractions;
using FurinaChronicle.Services.Ai;

namespace FurinaChronicle.Presentation.Ai;

public sealed record AiAccountChoice(Guid? Id, string Label);

public sealed class AiWorkspaceState(IPlayerArchiveRepository archives, IGameAccountRepository accounts,
    BuildAiContext builder, AiAnalysisSession session, ReadAiEvidence reader, INotificationCenter notifications, IAiConversationCopyCoordinator? copies = null)
{
    private CancellationTokenSource? operation;
    private int generation;
    public AiAnalysisSession Session => session;
    public IReadOnlyList<HistoryChoice> Archives { get; private set; } = [];
    public IReadOnlyList<AiAccountChoice> Accounts { get; private set; } = [];
    public Guid? SelectedArchiveId { get; private set; }
    public Guid? SelectedAccountId { get; private set; }
    public AiAnalysisScope? PreparedScope { get; private set; }
    public bool IsBusy => operation is not null || session.IsBusy;
    public string? Error { get; private set; }
    public string Status { get; private set; } = "选择范围后准备资料；仅在确认发送时联系模型服务。";
    public event EventHandler? Changed;
    public void Initialize() => session.Changed += OnSessionChanged;
    private void OnSessionChanged(object? sender, EventArgs args) { Status = session.Status; Changed?.Invoke(this, EventArgs.Empty); }
    public Task EnterAsync() => RunAsync(async token =>
    {
        await session.InitializeAsync(token);
        await session.LoadSettingsAsync(token);
        var list = await archives.GetAllAsync(token);
        token.ThrowIfCancellationRequested();
        Archives = list.Select(item => new HistoryChoice(item.Id, HistoryLabels.ArchiveChoice(item))).ToArray();
        SelectedArchiveId = list.FirstOrDefault(item => item.Id == SelectedArchiveId)?.Id ?? list.FirstOrDefault()?.Id;
        await LoadAccountsAsync(token);
    }, replace: true);
    public Task SelectArchiveAsync(Guid id) => RunAsync(async token =>
    {
        PreparedScope = null; SelectedArchiveId = id; SelectedAccountId = null;
        await LoadAccountsAsync(token);
    }, replace: true);
    private async Task LoadAccountsAsync(CancellationToken token)
    {
        var list = SelectedArchiveId is Guid id ? await accounts.GetByArchiveIdAsync(id, token) : [];
        token.ThrowIfCancellationRequested();
        Accounts = new[] { new AiAccountChoice(null, "整个档案（全部账号）") }.Concat(list.Select(item =>
            new AiAccountChoice(item.Id, HistoryLabels.AccountLabel(item)))).ToArray();
        if (SelectedAccountId is null || !list.Any(item => item.Id == SelectedAccountId)) SelectedAccountId = list.FirstOrDefault()?.Id;
        Status = "可直接聊天；需要游戏分析时附加档案资料。";
    }
    public void SelectAccount(Guid? id) { if (id == SelectedAccountId) return; SelectedAccountId = id; PreparedScope = null; Changed?.Invoke(this, EventArgs.Empty); }
    public void Invalidate()
    { generation++; operation?.Cancel(); session.InvalidateDrafts(); PreparedScope = null; Changed?.Invoke(this, EventArgs.Empty); }
    public void Leave() { operation?.Cancel(); session.InvalidateDrafts(); }
    public void Cancel() { operation?.Cancel(); session.Cancel(); }
    public Task SaveSettingsAsync(AiServiceSettings settings, string? key, bool clearKey = false) =>
        RunAsync(async token => { PreparedScope = null; await session.SaveSettingsAsync(settings, key, clearKey, token); });
    public async Task<IReadOnlyList<string>> ListModelsAsync(AiServiceSettings? settings = null, string? keyOverride = null)
    {
        IReadOnlyList<string> result = [];
        await RunAsync(async token => { result = await session.ListModelsAsync(settings, keyOverride, token); Status = $"连接成功，可用模型 {result.Count} 个；没有发送档案资料。"; });
        return result;
    }
    public Task PrepareAsync(DateOnly? from, DateOnly? to, AiDatasets datasets, bool allowArchiveUids) => RunAsync(async token =>
    {
        if (SelectedArchiveId is not Guid archive) throw new AiAnalysisException("请先选择档案。");
        var scope = new AiAnalysisScope(archive, SelectedAccountId, from, to, datasets, AllowArchiveUids: allowArchiveUids);
        int current = generation;
        await using var copyLease = copies is null ? null : await copies.ProtectCopiesAsync(token);
        var context = await builder.ExecuteAsync(scope, session.Settings.ContextCharacters, token);
        token.ThrowIfCancellationRequested();
        if (current != generation) return;
        bool fresh = session.Turns.Count == 0 && session.Context is null;
        var pending = fresh ? session.PendingAttachments.ToArray() : [];
        string draft = fresh ? session.DraftText : "";
        if (!fresh) await session.NewConversationAsync(token);
        session.SetContext(context);
        foreach (var attachment in pending) session.AddAttachment(attachment);
        session.UpdateDraft(draft);
        await session.SaveAsync(token); PreparedScope = scope;
    });
    public AiSendDraft CreateDraft(string question) => session.CreateDraft(question);
    public async Task GenerateAsync(AiSendDraft draft, bool approved, bool uidApproved)
    {
        await session.GenerateAsync(draft, approved, uidApproved);
        if (session.Turns.LastOrDefault() is { } last)
            notifications.Publish(new(last.Complete ? NotificationSeverity.Success : NotificationSeverity.Warning,
                NotificationCategory.System, "AI 分析", last.Status) { TaskKey = "ai-analysis" });
    }
    public Task<AiEvidenceView> ReadEvidenceAsync(AiLocalEvidence evidence) => reader.ExecuteAsync(evidence);
    private async Task RunAsync(Func<CancellationToken, Task> work, bool replace = false)
    {
        if (IsBusy && !replace) return;
        if (replace) { operation?.Cancel(); session.InvalidateDrafts(); }
        int request = ++generation;
        using var cancel = new CancellationTokenSource(); operation = cancel;
        Error = null; Changed?.Invoke(this, EventArgs.Empty);
        try { await work(cancel.Token); }
        catch (OperationCanceledException) { if (request == generation) Status = "已取消。"; }
        catch (Exception error)
        {
            if (request != generation) return;
            Error = error is AiAnalysisException ? error.Message : "AI 功能操作未完成，请检查所选范围、配置或本地存储。";
            Status = Error;
            notifications.Publish(new(NotificationSeverity.Warning, NotificationCategory.System, "AI 分析", Error));
        }
        finally { if (ReferenceEquals(operation, cancel)) operation = null; Changed?.Invoke(this, EventArgs.Empty); }
    }
}
