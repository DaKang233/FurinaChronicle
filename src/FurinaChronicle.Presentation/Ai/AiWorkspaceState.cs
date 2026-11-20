// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Presentation.History;
using FurinaChronicle.Presentation.Notifications;
using FurinaChronicle.Services.Abstractions;
using FurinaChronicle.Services.Ai;

namespace FurinaChronicle.Presentation.Ai;

public sealed record AiAccountChoice(Guid? Id, string Label);

public sealed class AiWorkspaceState(IPlayerArchiveRepository archives, IGameAccountRepository accounts,
    BuildAiContext builder, AiAnalysisSession session, ReadAiEvidence reader, INotificationCenter notifications)
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
        session.Reset(); PreparedScope = null;
        await session.LoadSettingsAsync(token);
        var list = await archives.GetAllAsync(token);
        token.ThrowIfCancellationRequested();
        Archives = list.Select(item => new HistoryChoice(item.Id, HistoryLabels.ArchiveChoice(item))).ToArray();
        SelectedArchiveId = list.FirstOrDefault(item => item.Id == SelectedArchiveId)?.Id ?? list.FirstOrDefault()?.Id;
        await LoadAccountsAsync(token);
    }, replace: true);
    public Task SelectArchiveAsync(Guid id) => RunAsync(async token =>
    {
        session.Reset(); PreparedScope = null; SelectedArchiveId = id; SelectedAccountId = null;
        await LoadAccountsAsync(token);
    }, replace: true);
    private async Task LoadAccountsAsync(CancellationToken token)
    {
        var list = SelectedArchiveId is Guid id ? await accounts.GetByArchiveIdAsync(id, token) : [];
        token.ThrowIfCancellationRequested();
        Accounts = new[] { new AiAccountChoice(null, "整个档案（全部账号）") }.Concat(list.Select(item =>
            new AiAccountChoice(item.Id, HistoryLabels.AccountLabel(item)))).ToArray();
        if (SelectedAccountId is null || !list.Any(item => item.Id == SelectedAccountId)) SelectedAccountId = list.FirstOrDefault()?.Id;
        Status = list.Count == 0 ? "当前档案没有可分析的账号。" : "选择数据类别与日期后准备资料。";
    }
    public void SelectAccount(Guid? id) { SelectedAccountId = id; Invalidate(); }
    public void Invalidate()
    { generation++; operation?.Cancel(); session.Reset(); PreparedScope = null; Changed?.Invoke(this, EventArgs.Empty); }
    public void Leave() => Invalidate();
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
        session.Reset();
        var scope = new AiAnalysisScope(archive, SelectedAccountId, from, to, datasets, AllowArchiveUids: allowArchiveUids);
        int current = generation;
        var context = await builder.ExecuteAsync(scope, session.Settings.ContextCharacters, token);
        token.ThrowIfCancellationRequested();
        if (current != generation) return;
        session.SetContext(context); PreparedScope = scope;
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
        if (replace) { operation?.Cancel(); session.Reset(); }
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
