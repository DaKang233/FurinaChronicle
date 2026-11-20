// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Text.Json;
using FurinaChronicle.Presentation.Ai;
using FurinaChronicle.Presentation.History;
using FurinaChronicle.Presentation.Notifications;
using FurinaChronicle.Services.Ai;
using FurinaChronicle.Windows.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;

namespace FurinaChronicle.Windows.Pages;

public sealed partial class AiPage : UserControl
{
    private readonly AiWorkspaceState state;
    private readonly INotificationCenter notifications;
    private readonly IFilePickerService files;
    private bool rendering;
    private string lastReply = "";
    public AiPage(AiWorkspaceState state, INotificationCenter notifications, IFilePickerService files)
    {
        this.state = state; this.notifications = notifications; this.files = files;
        InitializeComponent(); state.Initialize();
        ProtocolInput.ItemsSource = new[] { "OpenAI 兼容", "Ollama" };
        ProtocolInput.SelectionChanged += (_, _) => { if (!rendering) { state.Invalidate(); OllamaInputs.Visibility = ProtocolInput.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed; } };
        AddressInput.TextChanged += OnProviderEdited; ModelInput.TextChanged += OnProviderEdited;
        state.Changed += (_, _) => { if (DispatcherQueue.HasThreadAccess) Render(); else DispatcherQueue.TryEnqueue(Render); };
        AutomationProperties.SetName(this, "AI 分析");
    }
    public async Task EnterAsync()
    {
        await state.EnterAsync();
        LoadSettingsInputs(); Render();
    }
    public void Leave() { KeyInput.Password = ""; state.Leave(); }
    private void LoadSettingsInputs()
    {
        rendering = true;
        var s = state.Session.Settings;
        ProtocolInput.SelectedIndex = (int)s.Protocol; AddressInput.Text = s.BaseAddress; ModelInput.Text = s.Model;
        BudgetInput.Value = s.ContextCharacters; OutputInput.Value = s.MaxOutputTokens;
        ContextInput.Value = s.OllamaContextTokens; CpuInput.IsChecked = s.CpuOnly; DisableThinkingInput.IsChecked = s.DisableCompatibleThinking; KeyInput.Password = "";
        OllamaInputs.Visibility = s.Protocol == AiProtocol.Ollama ? Visibility.Visible : Visibility.Collapsed;
        ServiceExpander.IsExpanded = string.IsNullOrWhiteSpace(s.Model);
        rendering = false;
    }
    private AiServiceSettings Inputs() => new((AiProtocol)Math.Max(0, ProtocolInput.SelectedIndex),
        AddressInput.Text.Trim(), ModelInput.Text.Trim(),
        double.IsFinite(BudgetInput.Value) ? (int)BudgetInput.Value : 24000,
        double.IsFinite(OutputInput.Value) ? (int)OutputInput.Value : 2048,
        double.IsFinite(ContextInput.Value) ? (int)ContextInput.Value : 4096, CpuInput.IsChecked == true, DisableThinkingInput.IsChecked == true);
    private void Render()
    {
        rendering = true;
        if (!ReferenceEquals(ArchiveInput.ItemsSource, state.Archives)) ArchiveInput.ItemsSource = state.Archives;
        ArchiveInput.SelectedItem = state.Archives.FirstOrDefault(item => item.Id == state.SelectedArchiveId);
        if (!ReferenceEquals(AccountInput.ItemsSource, state.Accounts)) AccountInput.ItemsSource = state.Accounts;
        AccountInput.SelectedItem = state.Accounts.FirstOrDefault(item => item.Id == state.SelectedAccountId);
        UidInput.Visibility = state.SelectedAccountId is null ? Visibility.Visible : Visibility.Collapsed;
        PrepareButton.IsEnabled = !state.IsBusy && state.SelectedArchiveId is not null;
        PreviewButton.IsEnabled = state.Session.Context is not null;
        SendButton.IsEnabled = !state.IsBusy && state.Session.Context is not null;
        CancelButton.IsEnabled = state.IsBusy;
        SaveButton.IsEnabled = ModelsButton.IsEnabled = ClearKeyButton.IsEnabled = !state.IsBusy;
        ExportButton.IsEnabled = !state.IsBusy && state.Session.Turns.Count > 0;
        StatusText.Text = state.Status;
        string reply = string.Join("\n\n", state.Session.Turns.Select(turn =>
            $"问题：{turn.Question}\n\n{turn.Answer}\n\n{turn.Status}"));
        if (reply != lastReply) { ReplyText.Text = reply; lastReply = reply; }
        if (!ReferenceEquals(EvidenceList.ItemsSource, state.Session.Context?.Evidence)) EvidenceList.ItemsSource = state.Session.Context?.Evidence;
        rendering = false;
    }
    private void OnProviderEdited(object sender, TextChangedEventArgs args) { if (!rendering) state.Invalidate(); }
    private void OnScopeEdited(object sender, TextChangedEventArgs args) { if (!rendering) state.Invalidate(); }
    private void OnScopeClicked(object sender, RoutedEventArgs args) { if (!rendering) state.Invalidate(); }
    private async void OnArchiveChanged(object sender, SelectionChangedEventArgs args)
    { if (!rendering && ArchiveInput.SelectedItem is HistoryChoice choice) await state.SelectArchiveAsync(choice.Id); }
    private void OnAccountChanged(object sender, SelectionChangedEventArgs args)
    { if (!rendering && AccountInput.SelectedItem is AiAccountChoice choice) state.SelectAccount(choice.Id); }
    private async void OnSaveSettings(object sender, RoutedEventArgs args) => await UiAsync(async () =>
    {
        await state.SaveSettingsAsync(Inputs(), string.IsNullOrWhiteSpace(KeyInput.Password) ? null : KeyInput.Password);
        if (state.Error is null) LoadSettingsInputs();
    });
    private async void OnClearKey(object sender, RoutedEventArgs args) => await UiAsync(async () =>
    { await state.SaveSettingsAsync(Inputs(), null, clearKey: true); KeyInput.Password = ""; });
    private async void OnModels(object sender, RoutedEventArgs args) => await UiAsync(async () =>
    {
        var models = await state.ListModelsAsync(Inputs(), string.IsNullOrWhiteSpace(KeyInput.Password) ? null : KeyInput.Password);
        if (state.Error is not null) return;
        var list = new ListView { ItemsSource = models, MaxHeight = 360, SelectionMode = ListViewSelectionMode.Single };
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "服务中的模型（未发送档案）", Content = list,
            PrimaryButtonText = "使用所选模型", CloseButtonText = "关闭", DefaultButton = ContentDialogButton.Close };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && list.SelectedItem is string model) ModelInput.Text = model;
    });
    private async Task PrepareAsync()
    {
        AiDatasets datasets = AiDatasets.None;
        if (GachaInput.IsChecked == true) datasets |= AiDatasets.Gacha;
        if (ItemsInput.IsChecked == true) datasets |= AiDatasets.Items;
        if (ChallengesInput.IsChecked == true) datasets |= AiDatasets.Challenges;
        if (ChronicleInput.IsChecked == true) datasets |= AiDatasets.Chronicle;
        await state.PrepareAsync(Date(FromInput.Text), Date(ToInput.Text), datasets, UidInput.IsChecked == true);
    }
    private async void OnPrepare(object sender, RoutedEventArgs args) => await UiAsync(PrepareAsync);
    private async void OnPreview(object sender, RoutedEventArgs args) => await UiAsync(async () =>
    {
        if (state.Session.Context is not { } context) return;
        await new ContentDialog { XamlRoot = XamlRoot, Title = "本次模型资料（尚未发送）", CloseButtonText = "关闭",
            Content = PreviewText(context.Json) }.ShowAsync();
    });
    private async void OnSend(object sender, RoutedEventArgs args) => await UiAsync(async () =>
    {
        if (Inputs() != state.Session.Settings || !string.IsNullOrWhiteSpace(KeyInput.Password))
            throw new AiAnalysisException("模型配置或密钥已修改，请先保存后重新准备资料。");
        for (int attempt = 0; attempt < 2; attempt++)
        {
            var draft = state.CreateDraft(QuestionInput.Text);
            var content = new StackPanel { Spacing = 8 };
            content.Children.Add(new TextBlock { Text = $"接收方：{draft.Settings.Endpoint}\n模型：{draft.Settings.Model}\n" +
                (draft.IncludesUids ? "整个档案的所选资料将携带各账号 UID。请明确同意，或改用按 UID 升序编号。" : "仅发送编号资料，不发送 UID。"), TextWrapping = TextWrapping.Wrap });
            content.Children.Add(PreviewText(draft.Preview));
            var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = draft.IncludesUids ? "确认档案 UID 与数据发送" : "确认本次 AI 数据发送",
                Content = content, PrimaryButtonText = draft.IncludesUids ? "同意携带 UID 并发送" : "确认发送",
                SecondaryButtonText = draft.IncludesUids ? "改用编号" : "", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close };
            AutomationProperties.SetAutomationId(dialog, "AiSendReview");
            var answer = await dialog.ShowAsync();
            if (answer == ContentDialogResult.Secondary)
            {
                UidInput.IsChecked = false; state.Invalidate(); await PrepareAsync();
                if (state.Error is not null) return;
                continue;
            }
            if (answer != ContentDialogResult.Primary) return;
            await state.GenerateAsync(draft, approved: true, uidApproved: draft.IncludesUids);
            return;
        }
    });
    private void OnCancel(object sender, RoutedEventArgs args) => state.Cancel();
    private async void OnEvidence(object sender, ItemClickEventArgs args) => await UiAsync(async () =>
    {
        if (args.ClickedItem is not AiLocalEvidence evidence) return;
        var detail = await state.ReadEvidenceAsync(evidence);
        await new ContentDialog { XamlRoot = XamlRoot, Title = "本地证据 " + evidence.Fact.Reference, CloseButtonText = "关闭",
            Content = PreviewText(detail.Availability + "\n\n" + JsonSerializer.Serialize(detail.Fact, AiJson.Options)) }.ShowAsync();
    });
    private async void OnExport(object sender, RoutedEventArgs args) => await UiAsync(async () =>
    {
        var destination = await files.PickSaveFileAsync($"furina-ai-analysis-{DateTime.Now:yyyyMMdd-HHmmss}", "Markdown", ".md");
        if (destination is null) return;
        await FileIO.WriteTextAsync(destination, state.Session.CreateMarkdownReport());
        notifications.Publish(new(NotificationSeverity.Success, NotificationCategory.System, "AI 分析", "分析报告已导出。"));
    });
    private static TextBox PreviewText(string value) => new()
    {
        IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
        Height = 340, MinWidth = 440, Text = value
    };
    private static DateOnly? Date(string value) => string.IsNullOrWhiteSpace(value) ? null :
        DateOnly.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date : throw new AiAnalysisException("日期请填写 YYYY-MM-DD，或留空。");
    private async Task UiAsync(Func<Task> work)
    {
        try { await work(); }
        catch (OperationCanceledException) { }
        catch (Exception error)
        { notifications.Publish(new(NotificationSeverity.Warning, NotificationCategory.System, "AI 分析",
            error is AiAnalysisException ? error.Message : "AI 页面操作未完成，请重试。")); }
        finally { Render(); }
    }
}
