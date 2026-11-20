// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Text;
using System.Text.Json;
using FurinaChronicle.App.Exporting;
using FurinaChronicle.Presentation.Ai;
using FurinaChronicle.Presentation.History;
using FurinaChronicle.Presentation.Notifications;
using FurinaChronicle.Services.Ai;

namespace FurinaChronicle.App.Ai;

public sealed class AiAnalysisPage : ContentPage
{
    private readonly AiWorkspaceState state;
    private readonly INotificationCenter notifications;
    private readonly Picker protocol = new() { Title = "接口类型", ItemsSource = new[] { "OpenAI 兼容", "Ollama" } };
    private readonly Entry address = new() { Placeholder = "服务 API 基址（例如 https://服务地址/v1）", AutomationId = "AiBaseAddress" };
    private readonly Entry model = new() { Placeholder = "模型名称", AutomationId = "AiModel" };
    private readonly Picker modelChoices = new() { Title = "连接后可选择模型" };
    private readonly Entry key = new() { Placeholder = "API 密钥（留空保留此服务已有密钥）", IsPassword = true, IsTextPredictionEnabled = false, IsSpellCheckEnabled = false };
    private readonly Entry budget = new() { Placeholder = "资料上限（字符，不是 token）", Keyboard = Keyboard.Numeric };
    private readonly Entry output = new() { Placeholder = "输出上限（token）", Keyboard = Keyboard.Numeric };
    private readonly Entry context = new() { Placeholder = "Ollama 上下文（token）", Keyboard = Keyboard.Numeric };
    private readonly CheckBox cpu = new(), disableThinking = new();
    private readonly Picker archives = new() { Title = "档案", ItemDisplayBinding = new Binding("Label"), AutomationId = "AiArchive" };
    private readonly Picker accounts = new() { Title = "分析范围", ItemDisplayBinding = new Binding("Label"), AutomationId = "AiAccount" };
    private readonly Entry from = new() { Placeholder = "UTC 起始日期 YYYY-MM-DD（可留空）" };
    private readonly Entry through = new() { Placeholder = "UTC 结束日期 YYYY-MM-DD（含当日，可留空）" };
    private readonly CheckBox gacha = new() { IsChecked = true }, items = new() { IsChecked = true }, challenges = new() { IsChecked = true }, chronicle = new() { IsChecked = true }, uids = new() { IsChecked = true };
    private readonly Editor question = new() { Placeholder = "分析问题或继续追问", HeightRequest = 100, AutomationId = "AiQuestion" };
    private readonly Editor reply = new() { IsReadOnly = true, HeightRequest = 340, AutomationId = "AiReply" };
    private readonly Label status = new() { AutomationId = "AiStatus" };
    private readonly Button save = new() { Text = "保存配置与密钥" }, check = new() { Text = "查看模型（不发送档案）" }, clearKey = new() { Text = "移除此服务密钥" };
    private readonly Button prepare = new() { Text = "准备本次资料", AutomationId = "AiPrepare" }, preview = new() { Text = "查看资料与证据" }, send = new() { Text = "审查并发送", AutomationId = "AiSend" }, cancel = new() { Text = "取消", AutomationId = "AiCancel" }, export = new() { Text = "导出 Markdown 报告" };
    private readonly CollectionView evidence = new() { SelectionMode = SelectionMode.Single, HeightRequest = 180 };
    private readonly VerticalStackLayout configuration = new() { Spacing = 8 }, ollama = new() { Spacing = 8 };
    private readonly View uidChoice;
    private bool rendering, presentingReview;
    public AiAnalysisPage(AiWorkspaceState state, INotificationCenter notifications)
    {
        this.state = state; this.notifications = notifications; state.Initialize();
        Title = "AI 分析";
        var body = new VerticalStackLayout { Spacing = 10, Padding = new Thickness(16) };
        body.Add(new Label { Text = "AI 分析", FontSize = 26, FontAttributes = FontAttributes.Bold });
        body.Add(new Label { Text = "围绕本地资料分析和追问。发送前审查完整正文；模型结果不会写回领域事实。" });
        var toggle = new Button { Text = "模型服务配置" }; toggle.Clicked += (_, _) => configuration.IsVisible = !configuration.IsVisible; body.Add(toggle);
        foreach (var control in new View[] { protocol, address, model, modelChoices, key,
            new Label { Text = "资料上限（字符）／输出上限（token）" }, budget, output }) configuration.Add(control);
        configuration.Add(Check(disableThinking, "关闭兼容服务的思考模式（DeepSeek 等支持 thinking 参数的服务；其他服务请勿勾选）"));
        ollama.Add(context); ollama.Add(Check(cpu, "仅 CPU（节省显存）")); configuration.Add(ollama);
        configuration.Add(save); configuration.Add(check); configuration.Add(clearKey); body.Add(configuration);
        body.Add(archives); body.Add(accounts); body.Add(from); body.Add(through);
        body.Add(Check(gacha, "抽卡统计与记录")); body.Add(Check(items, "角色与武器历程"));
        body.Add(Check(challenges, "周期挑战")); body.Add(Check(chronicle, "游戏历程节点"));
        uidChoice = Check(uids, "档案分析默认携带 UID；发送前明确确认，也可改为按 UID 升序编号。"); body.Add(uidChoice);
        body.Add(new Label { Text = "单账号始终不发送 UID。昵称、档案名、备注与凭据不进入资料；问题中的长号码会隐藏。" });
        body.Add(prepare); body.Add(preview); body.Add(cancel); body.Add(status); body.Add(question); body.Add(send); body.Add(export);
        body.Add(new Label { Text = "模型分析（请结合证据复核）", FontAttributes = FontAttributes.Bold }); body.Add(reply);
        body.Add(new Label { Text = "本次资料的本地证据", FontAttributes = FontAttributes.Bold }); body.Add(evidence);
        evidence.ItemTemplate = new DataTemplate(() =>
        {
            var id = new Label { FontAttributes = FontAttributes.Bold }; id.SetBinding(Label.TextProperty, "Fact.Reference");
            var label = new Label { LineBreakMode = LineBreakMode.WordWrap }; label.SetBinding(Label.TextProperty, "Fact.Description");
            return new VerticalStackLayout { Padding = 8, Children = { id, label } };
        });
        Content = new ScrollView { Content = body };
        state.Changed += (_, _) => MainThread.BeginInvokeOnMainThread(Render);
        archives.SelectedIndexChanged += async (_, _) => { if (!rendering && archives.SelectedItem is HistoryChoice choice) await state.SelectArchiveAsync(choice.Id); };
        accounts.SelectedIndexChanged += (_, _) => { if (!rendering && accounts.SelectedItem is AiAccountChoice choice) state.SelectAccount(choice.Id); };
        foreach (var entry in new[] { from, through, address, model }) entry.TextChanged += (_, _) => Invalidate();
        foreach (var box in new[] { gacha, items, challenges, chronicle, uids }) box.CheckedChanged += (_, _) => Invalidate();
        protocol.SelectedIndexChanged += (_, _) => { if (!rendering) { ollama.IsVisible = protocol.SelectedIndex == 1; state.Invalidate(); } };
        modelChoices.SelectedIndexChanged += (_, _) => { if (modelChoices.SelectedItem is string selected) model.Text = selected; };
        save.Clicked += async (_, _) => await UiAsync(async () => { await state.SaveSettingsAsync(Inputs(), string.IsNullOrWhiteSpace(key.Text) ? null : key.Text); if (state.Error is null) LoadSettings(); });
        clearKey.Clicked += async (_, _) => await UiAsync(async () => { await state.SaveSettingsAsync(Inputs(), null, true); key.Text = ""; });
        check.Clicked += async (_, _) => await UiAsync(async () => { modelChoices.ItemsSource = (await state.ListModelsAsync(Inputs(), string.IsNullOrWhiteSpace(key.Text) ? null : key.Text)).ToArray(); });
        prepare.Clicked += async (_, _) => await UiAsync(PrepareAsync);
        preview.Clicked += async (_, _) => await UiAsync(async () => { if (state.Session.Context is { } data) await ReviewAsync("本次资料（尚未发送）", data.Json, "关闭"); });
        cancel.Clicked += (_, _) => state.Cancel(); send.Clicked += async (_, _) => await UiAsync(SendAsync);
        export.Clicked += async (_, _) => await UiAsync(async () =>
        {
            await using var source = new MemoryStream(Encoding.UTF8.GetBytes(state.Session.CreateMarkdownReport()));
            var result = await PlatformExportFileSaver.Default.SaveAsync($"furina-ai-analysis-{DateTime.Now:yyyyMMdd-HHmmss}.md", source);
            if (result.IsSuccessful) Notify("分析报告已导出。", NotificationSeverity.Success);
            else if (!result.IsCanceled) throw new AiAnalysisException("报告未能保存，请重试。");
        });
        evidence.SelectionChanged += async (_, args) => await UiAsync(async () =>
        {
            if (args.CurrentSelection.FirstOrDefault() is not AiLocalEvidence reference) return;
            var detail = await state.ReadEvidenceAsync(reference);
            await ReviewAsync("本地证据 " + reference.Fact.Reference, detail.Availability + "\n\n" + JsonSerializer.Serialize(detail.Fact, AiJson.Options), "关闭");
            evidence.SelectedItem = null;
        });
    }
    protected override async void OnAppearing()
    { base.OnAppearing(); if (presentingReview) return; await state.EnterAsync(); LoadSettings(); Render(); }
    protected override void OnDisappearing()
    { if (!presentingReview) { key.Text = ""; state.Leave(); } base.OnDisappearing(); }
    private void LoadSettings()
    {
        rendering = true; var settings = state.Session.Settings;
        protocol.SelectedIndex = (int)settings.Protocol; address.Text = settings.BaseAddress; model.Text = settings.Model;
        budget.Text = settings.ContextCharacters.ToString(CultureInfo.InvariantCulture); output.Text = settings.MaxOutputTokens.ToString(CultureInfo.InvariantCulture);
        context.Text = settings.OllamaContextTokens.ToString(CultureInfo.InvariantCulture); cpu.IsChecked = settings.CpuOnly; disableThinking.IsChecked = settings.DisableCompatibleThinking; key.Text = "";
        configuration.IsVisible = string.IsNullOrWhiteSpace(settings.Model); ollama.IsVisible = settings.Protocol == AiProtocol.Ollama; rendering = false;
    }
    private AiServiceSettings Inputs() => new((AiProtocol)Math.Max(0, protocol.SelectedIndex), address.Text?.Trim() ?? "", model.Text?.Trim() ?? "",
        Number(budget.Text, 24000), Number(output.Text, 2048), Number(context.Text, 4096), cpu.IsChecked, disableThinking.IsChecked);
    private void Render()
    {
        rendering = true;
        if (archives.ItemsSource is not HistoryChoice[] archiveRows || !archiveRows.SequenceEqual(state.Archives)) archives.ItemsSource = state.Archives.ToArray();
        archives.SelectedItem = state.Archives.FirstOrDefault(item => item.Id == state.SelectedArchiveId);
        if (accounts.ItemsSource is not AiAccountChoice[] rows || !rows.SequenceEqual(state.Accounts)) accounts.ItemsSource = state.Accounts.ToArray();
        accounts.SelectedItem = state.Accounts.FirstOrDefault(item => item.Id == state.SelectedAccountId);
        uidChoice.IsVisible = state.SelectedAccountId is null;
        prepare.IsEnabled = !state.IsBusy && state.SelectedArchiveId is not null;
        preview.IsEnabled = state.Session.Context is not null; send.IsEnabled = !state.IsBusy && state.Session.Context is not null;
        cancel.IsEnabled = state.IsBusy; save.IsEnabled = check.IsEnabled = clearKey.IsEnabled = !state.IsBusy;
        export.IsEnabled = !state.IsBusy && state.Session.Turns.Count > 0;
        status.Text = state.Status;
        string text = string.Join("\n\n", state.Session.Turns.Select(turn => $"问题：{turn.Question}\n\n{turn.Answer}\n\n{turn.Status}"));
        if (reply.Text != text) reply.Text = text;
        if (!ReferenceEquals(evidence.ItemsSource, state.Session.Context?.Evidence)) evidence.ItemsSource = state.Session.Context?.Evidence;
        rendering = false;
    }
    private void Invalidate() { if (!rendering) state.Invalidate(); }
    private Task PrepareAsync()
    {
        AiDatasets datasets = AiDatasets.None;
        if (gacha.IsChecked) datasets |= AiDatasets.Gacha; if (items.IsChecked) datasets |= AiDatasets.Items;
        if (challenges.IsChecked) datasets |= AiDatasets.Challenges; if (chronicle.IsChecked) datasets |= AiDatasets.Chronicle;
        return state.PrepareAsync(Date(from.Text), Date(through.Text), datasets, uids.IsChecked);
    }
    private async Task SendAsync()
    {
        if (Inputs() != state.Session.Settings || !string.IsNullOrWhiteSpace(key.Text)) throw new AiAnalysisException("配置或密钥已修改，请先保存后重新准备资料。");
        for (int attempt = 0; attempt < 2; attempt++)
        {
            var draft = state.CreateDraft(question.Text ?? "");
            string notice = $"接收方：{draft.Settings.Endpoint}\n模型：{draft.Settings.Model}\n" + (draft.IncludesUids
                ? "本次会携带所选档案各账号 UID。请明确同意，或改用按 UID 升序编号。" : "本次仅发送编号资料，不发送 UID。");
            var choice = await ReviewAsync(draft.IncludesUids ? "确认档案 UID 与数据发送" : "确认本次 AI 数据发送",
                notice + "\n\n" + draft.Preview, draft.IncludesUids ? "同意携带 UID 并发送" : "确认发送", draft.IncludesUids ? "改用编号" : null);
            if (choice == AiReviewChoice.Anonymous)
            { uids.IsChecked = false; state.Invalidate(); await PrepareAsync(); if (state.Error is not null) return; continue; }
            if (choice == AiReviewChoice.Send) await state.GenerateAsync(draft, true, draft.IncludesUids);
            return;
        }
    }
    private async Task<AiReviewChoice> ReviewAsync(string title, string text, string primary, string? secondary = null)
    {
        presentingReview = true;
        try
        {
            var page = new AiTextReviewPage(title, text, primary, secondary);
            await Navigation.PushModalAsync(page); return await page.Completion;
        }
        finally { presentingReview = false; }
    }
    private async Task UiAsync(Func<Task> work)
    {
        try { await work(); } catch (OperationCanceledException) { }
        catch (Exception error) { Notify(error is AiAnalysisException ? error.Message : "AI 页面操作未完成，请重试。", NotificationSeverity.Warning); }
        finally { Render(); }
    }
    private void Notify(string message, NotificationSeverity severity) => notifications.Publish(new(severity, NotificationCategory.System, "AI 分析", message));
    private static int Number(string? value, int fallback) => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int number) ? number : fallback;
    private static DateOnly? Date(string? value) => string.IsNullOrWhiteSpace(value) ? null :
        DateOnly.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : throw new AiAnalysisException("日期请使用 YYYY-MM-DD 或留空。");
    private static View Check(CheckBox box, string text)
    {
        var row = new Grid { ColumnSpacing = 6, ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) } };
        row.Add(box);
        var label = new Label { Text = text, VerticalOptions = LayoutOptions.Center, LineBreakMode = LineBreakMode.WordWrap };
        Grid.SetColumn(label, 1); row.Children.Add(label); return row;
    }
}
internal enum AiReviewChoice { Cancel, Send, Anonymous }
internal sealed class AiTextReviewPage : ContentPage
{
    private readonly TaskCompletionSource<AiReviewChoice> result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool closing;
    public Task<AiReviewChoice> Completion => result.Task;
    public AiTextReviewPage(string title, string text, string primary, string? secondary)
    {
        Title = title;
        var layout = new Grid { Padding = 16, RowSpacing = 10, RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto) } };
        layout.Add(new Label { Text = title, FontSize = 22, FontAttributes = FontAttributes.Bold });
        var preview = new Editor { Text = text, IsReadOnly = true, AutomationId = "AiSendReview" }; Grid.SetRow(preview, 1); layout.Children.Add(preview);
        var buttons = new VerticalStackLayout { Spacing = 8 };
        void Add(string label, AiReviewChoice choice)
        {
            var button = new Button { Text = label }; button.Clicked += async (_, _) =>
            { if (closing) return; closing = true; await Navigation.PopModalAsync(); result.TrySetResult(choice); }; buttons.Add(button);
        }
        Add(primary, AiReviewChoice.Send); if (secondary is not null) Add(secondary, AiReviewChoice.Anonymous); Add("取消", AiReviewChoice.Cancel);
        Grid.SetRow(buttons, 2); layout.Children.Add(buttons); Content = layout;
    }
    protected override void OnDisappearing() { if (!closing) result.TrySetResult(AiReviewChoice.Cancel); base.OnDisappearing(); }
}
