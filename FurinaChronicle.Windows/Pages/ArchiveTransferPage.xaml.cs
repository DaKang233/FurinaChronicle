// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.History;
using FurinaChronicle.Presentation.Management;
using FurinaChronicle.Services.Archives.Portable;
using FurinaChronicle.Services.History;
using FurinaChronicle.Windows.Composition;
using FurinaChronicle.Windows.Shell;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;

namespace FurinaChronicle.Windows.Pages;

public sealed partial class ArchiveTransferPage : UserControl
{
    private readonly ArchiveTransferState state;
    private readonly IFilePickerService filePicker;
    private readonly WindowsVersionInfo version;
    private bool rendering;
    private static readonly string[] ModeLabels =
        ["Merge（补充/合并）", "Scoped Replace（明确范围替换）"];
    private static readonly string[] KindLabels =
        ["整个档案", "单个账号", "单一领域"];
    private static readonly string[] DomainLabels =
        ["抽卡", "角色/武器观察", "手工获取", "挑战观察"];
    private static readonly ArchiveReplaceDomain[] Domains =
    [
        ArchiveReplaceDomain.Gacha,
        ArchiveReplaceDomain.ItemObservation,
        ArchiveReplaceDomain.ManualAcquisition,
        ArchiveReplaceDomain.ChallengeObservation
    ];
    private Guid? replaceArchiveId;
    private Guid? replaceAccountId;
    private int replaceKindIndex;

    public ArchiveTransferPage(
        ArchiveTransferState state,
        IFilePickerService filePicker,
        WindowsVersionInfo version)
    {
        this.state = state;
        this.filePicker = filePicker;
        this.version = version;
        InitializeComponent();
        rendering = true;
        ExportAllRadio.IsChecked = true;
        RecoveryModePicker.ItemsSource = ModeLabels;
        RecoveryModePicker.SelectedIndex = 0;
        ReplaceKindPicker.ItemsSource = KindLabels;
        ReplaceKindPicker.SelectedIndex = 0;
        ReplaceDomainPicker.ItemsSource = DomainLabels;
        ReplaceDomainPicker.SelectedIndex = 0;
        rendering = false;
    }

    public event EventHandler? BackRequested;

    public event EventHandler? RestoreApplied;

    public async Task EnterAsync(CancellationToken cancellationToken = default)
    {
        await state.EnterAsync(cancellationToken);
        Render();
    }

    private void Render()
    {
        rendering = true;
        try
        {
            ArchiveTransferSnapshot snapshot = state.Snapshot;
            StatusText.Text = snapshot.Status;
            ErrorText.Text = snapshot.Error ?? string.Empty;
            ErrorText.Visibility = string.IsNullOrWhiteSpace(snapshot.Error)
                ? Visibility.Collapsed
                : Visibility.Visible;
            ExportAllRadio.IsChecked = snapshot.ExportAllArchives;
            ExportSelectedRadio.IsChecked = !snapshot.ExportAllArchives;
            ExportArchivePicker.ItemsSource = snapshot.Archives;
            ExportArchivePicker.SelectedItem = snapshot.Archives.FirstOrDefault(
                archive => archive.Id == snapshot.SelectedArchiveId);
            ExportArchivePicker.IsEnabled = !snapshot.ExportAllArchives && !snapshot.IsBusy;
            ExportScopeText.Text = snapshot.ExportAllArchives
                ? "范围：全部档案"
                : snapshot.SelectedArchive is null
                    ? "范围：请选择档案"
                    : $"范围：档案“{snapshot.SelectedArchive.Name}”";
            PackageSummaryText.Text = snapshot.PackageSummary;
            PlanSummaryText.Text = snapshot.PlanSummary;
            MappingSummaryText.Text = snapshot.MappingSummary;
            DiagnosticSummaryText.Text = snapshot.DiagnosticSummary;
            RecoveryPointText.Text = snapshot.RecoveryPointSummary;
            ApplySettingsCheck.IsChecked = snapshot.ApplyPortableSettings;
            ApplySettingsCheck.IsEnabled = snapshot.HasPackage && !snapshot.IsBusy;
            RecoveryModePicker.SelectedIndex =
                snapshot.Mode == ArchiveRestoreMode.Replace ? 1 : 0;
            RecoveryModePicker.IsEnabled = snapshot.HasPackage && !snapshot.IsBusy;
            ReplaceScopeEditor.Visibility = snapshot.IsReplaceMode
                ? Visibility.Visible
                : Visibility.Collapsed;
            RenderReplaceScopeEditor(snapshot);
            ReplaceScopeList.ItemsSource = snapshot.ReplaceSelections
                .Select(selection => new ReplaceScopeRow(
                    selection,
                    state.DescribeReplaceSelection(selection)))
                .ToArray();
            ExportButton.IsEnabled = !snapshot.IsBusy &&
                (snapshot.ExportAllArchives || snapshot.SelectedArchive is not null);
            OpenButton.IsEnabled = !snapshot.IsBusy;
            ReplanButton.IsEnabled = snapshot.HasPackage && !snapshot.IsBusy;
            ResolveMappingsButton.IsEnabled = snapshot.HasPlan && !snapshot.IsBusy &&
                HasUnresolvedMappings(snapshot.Plan);
            ApplyButton.IsEnabled = !snapshot.IsBusy && snapshot.Plan is { } plan &&
                !HasBlockingPlanIssues(plan);
            CancelSessionButton.IsEnabled = snapshot.HasPackage && !snapshot.IsBusy;
            BackButton.IsEnabled = !snapshot.IsBusy;
        }
        finally
        {
            rendering = false;
        }
    }

    private void RenderReplaceScopeEditor(ArchiveTransferSnapshot snapshot)
    {
        IReadOnlyList<ArchiveCatalogArchive> archives = snapshot.PackageArchives;
        ReplaceArchivePicker.ItemsSource = archives;
        ArchiveCatalogArchive? archive = archives.FirstOrDefault(
            item => item.ArchiveReference == replaceArchiveId) ?? archives.FirstOrDefault();
        replaceArchiveId = archive?.ArchiveReference;
        ReplaceArchivePicker.SelectedItem = archive;
        ReplaceKindPicker.SelectedIndex = replaceKindIndex;
        ArchiveCatalogAccount[] accounts = archive?.Accounts.ToArray() ?? [];
        ReplaceAccountPicker.ItemsSource = accounts
            .Select(account => new AccountOption(account))
            .ToArray();
        ArchiveCatalogAccount? account = accounts.FirstOrDefault(
            item => item.AccountReference == replaceAccountId) ?? accounts.FirstOrDefault();
        replaceAccountId = account?.AccountReference;
        ReplaceAccountPicker.SelectedItem = ReplaceAccountPicker.ItemsSource is
            IReadOnlyList<AccountOption> options
            ? options.FirstOrDefault(item => item.Account.AccountReference == replaceAccountId)
            : null;
        if (ReplaceDomainPicker.SelectedIndex < 0) ReplaceDomainPicker.SelectedIndex = 0;
        bool isAccountScope = replaceKindIndex is 1 or 2;
        bool isDomainScope = replaceKindIndex == 2;
        ReplaceAccountPicker.Visibility = isAccountScope ? Visibility.Visible : Visibility.Collapsed;
        ReplaceDomainPicker.Visibility = isDomainScope ? Visibility.Visible : Visibility.Collapsed;
        bool canAdd = !snapshot.IsBusy && snapshot.IsReplaceMode && archive is not null &&
            (!isAccountScope || account is not null) &&
            (!isDomainScope || ReplaceDomainPicker.SelectedIndex >= 0);
        ReplaceAddScopeButton.IsEnabled = canAdd;
        ReplaceRemoveScopeButton.IsEnabled = !snapshot.IsBusy &&
            ReplaceScopeList.SelectedItem is ReplaceScopeRow;
        ReplaceClearScopesButton.IsEnabled = !snapshot.IsBusy &&
            snapshot.ReplaceSelections.Count > 0;
    }

    private void OnReplaceScopeInputChanged(object sender, SelectionChangedEventArgs e)
    {
        if (rendering) return;
        if (ReplaceArchivePicker.SelectedItem is ArchiveCatalogArchive archive)
        {
            if (archive.ArchiveReference != replaceArchiveId)
            {
                replaceAccountId = null;
            }

            replaceArchiveId = archive.ArchiveReference;
        }

        if (ReplaceAccountPicker.SelectedItem is AccountOption account)
        {
            replaceAccountId = account.Account.AccountReference;
        }

        replaceKindIndex = Math.Max(0, ReplaceKindPicker.SelectedIndex);
        RenderReplaceScopeEditor(state.Snapshot);
    }

    private async void OnRecoveryModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (rendering || RecoveryModePicker.SelectedIndex < 0) return;
        ArchiveRestoreMode mode = RecoveryModePicker.SelectedIndex == 1
            ? ArchiveRestoreMode.Replace
            : ArchiveRestoreMode.Merge;
        ArchiveTransferActionResult result = await state.SetModeAsync(mode);
        Render();
        if (!result.Succeeded && state.Snapshot.HasPackage &&
            mode == ArchiveRestoreMode.Merge)
        {
            await ShowMessageAsync("模式未切换", result.Message);
        }
    }

    private async void OnAddReplaceScope(object sender, RoutedEventArgs e)
    {
        if (ReplaceArchivePicker.SelectedItem is not ArchiveCatalogArchive archive)
        {
            await ShowMessageAsync("未选择档案", "请先选择来源档案。");
            return;
        }

        ArchiveReplaceScopeKind kind = replaceKindIndex switch
        {
            1 => ArchiveReplaceScopeKind.Account,
            2 => ArchiveReplaceScopeKind.Domain,
            _ => ArchiveReplaceScopeKind.Archive
        };
        Guid? accountId = kind == ArchiveReplaceScopeKind.Archive
            ? null
            : ReplaceAccountPicker.SelectedItem is AccountOption account
                ? account.Account.AccountReference
                : null;
        ArchiveReplaceDomain? domain = kind == ArchiveReplaceScopeKind.Domain &&
            ReplaceDomainPicker.SelectedIndex >= 0
            ? Domains[ReplaceDomainPicker.SelectedIndex]
            : null;
        if (kind != ArchiveReplaceScopeKind.Archive && accountId is null)
        {
            await ShowMessageAsync("未选择账号", "账号/领域范围必须选择来源账号。");
            return;
        }

        var selection = new ArchiveReplaceSelection(
            archive.ArchiveReference,
            kind,
            kind == ArchiveReplaceScopeKind.Archive ? null : accountId,
            domain);
        ArchiveTransferActionResult result = await state.AddReplaceSelectionAsync(selection);
        Render();
        if (!result.Succeeded) await ShowMessageAsync("Replace 范围未加入", result.Message);
    }

    private async void OnRemoveReplaceScope(object sender, RoutedEventArgs e)
    {
        if (ReplaceScopeList.SelectedItem is not ReplaceScopeRow row) return;
        await state.RemoveReplaceSelectionAsync(row.Selection);
        Render();
    }

    private async void OnClearReplaceScopes(object sender, RoutedEventArgs e)
    {
        await state.ClearReplaceSelectionsAsync();
        Render();
    }

    private void OnReplaceScopeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (rendering) return;
        ReplaceRemoveScopeButton.IsEnabled = !state.Snapshot.IsBusy &&
            ReplaceScopeList.SelectedItem is ReplaceScopeRow;
    }

    private static bool HasUnresolvedMappings(ArchiveRestorePlan? plan) =>
        plan is not null &&
        (plan.Archives.Any(item =>
            item.Kind == ArchiveRestoreArchiveMappingKind.RequiresSelection) ||
         plan.Accounts.Any(item =>
            item.Kind == ArchiveRestoreAccountMappingKind.RequiresSelection));

    private static bool HasBlockingPlanIssues(ArchiveRestorePlan plan) =>
        plan.Conflicts.Count > 0 ||
        plan.Archives.Any(item =>
            item.Kind == ArchiveRestoreArchiveMappingKind.RequiresSelection) ||
        plan.Accounts.Any(item =>
            item.Kind is ArchiveRestoreAccountMappingKind.RequiresSelection or
                ArchiveRestoreAccountMappingKind.Collision);

    private void OnExportScopeChanged(object sender, RoutedEventArgs e)
    {
        if (rendering) return;
        state.SetExportAll(ExportAllRadio.IsChecked == true);
        Render();
    }

    private async void OnExportArchiveSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (rendering || ExportArchivePicker.SelectedItem is not PlayerArchive archive) return;
        await state.SelectArchiveAsync(archive.Id);
        Render();
    }

    private async void OnExport(object sender, RoutedEventArgs e)
    {
        StorageFile? destination = await filePicker.PickSaveFileAsync(
            $"FurinaChronicle_Archive_{DateTime.Now:yyyyMMdd_HHmmss}",
            "Furina Archive（完整逻辑档案）",
            ".fca");
        if (destination is null)
        {
            await ShowMessageAsync("已取消导出", "未选择保存位置，未写出文件。");
            return;
        }

        var generator = new ArchiveGeneratorInfo(
            "FurinaChronicle",
            version.Version,
            version.PhaseLabel,
            version.Commit,
            "WinUI");
        ArchiveTransferActionResult result =
            await state.ExportAsync(destination.Path, generator);
        Render();
        if (!result.Succeeded)
        {
            TryDeleteEmptyDestination(destination.Path);
            await ShowMessageAsync("导出未完成", result.Message);
        }
    }

    private static void TryDeleteEmptyDestination(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.Exists && info.Length == 0) info.Delete();
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private async void OnOpen(object sender, RoutedEventArgs e)
    {
        StorageFile? file = await filePicker.PickOpenFileAsync(".fca");
        if (file is null) return;
        ArchiveTransferActionResult result = await state.OpenPackageAsync(file.Path);
        Render();
        if (!result.Succeeded)
        {
            await ShowMessageAsync("无法读取 .fca", result.Message);
        }
        else if (HasUnresolvedMappings(state.Snapshot.Plan))
        {
            await ShowMessageAsync(
                "需要解析映射",
                "预览包含需要手动选择的档案或账号映射。请点击“解析映射”完成选择后再提交。");
        }
    }

    private async void OnReplan(object sender, RoutedEventArgs e)
    {
        ArchiveTransferActionResult result = await state.PlanAsync();
        Render();
        if (!result.Succeeded)
        {
            await ShowMessageAsync("预览未生成", result.Message);
        }
    }

    private async void OnApplySettingsChanged(object sender, RoutedEventArgs e)
    {
        if (rendering || !state.Snapshot.HasPackage) return;
        await state.SetApplyPortableSettingsAsync(ApplySettingsCheck.IsChecked == true);
        Render();
    }

    private async void OnResolveMappings(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!await ResolveArchiveMappingsAsync()) return;
            if (!await ResolveAccountMappingsAsync()) return;
            Render();
            if (state.Snapshot.Plan is not { } plan)
            {
                await ShowMessageAsync("没有预览", "请先生成恢复预览。");
                return;
            }

            if (HasBlockingPlanIssues(plan))
            {
                await ShowMessageAsync(
                    "仍有阻塞项",
                    state.Snapshot.DiagnosticSummary +
                    Environment.NewLine +
                    "请处理冲突/待确认项后重新生成预览；数据库未改变。");
            }
        }
        catch (Exception exception)
        {
            await ShowMessageAsync(
                "映射失败",
                ManagementTexts.DescribeOperationFailure(exception));
        }
    }

    private async Task<bool> ResolveArchiveMappingsAsync()
    {
        while (state.Snapshot.Plan is { } plan)
        {
            ArchiveRestoreArchiveMapping? mapping = plan.Archives.FirstOrDefault(item =>
                item.Kind == ArchiveRestoreArchiveMappingKind.RequiresSelection);
            if (mapping is null) return true;

            var choices = mapping.Candidates
                .Select(candidate => new MappingChoice(
                    $"{(candidate.IsExactMatch ? "同名" : "相似")}：{candidate.Name} " +
                    $"({candidate.ArchiveId.ToString("N")[..8]})",
                    candidate.ArchiveId))
                .Append(new MappingChoice("创建新的目标档案（自动唯一名，可在下方指定名称）", null))
                .ToArray();
            var picker = new ComboBox
            {
                ItemsSource = choices.Select(item => item.Label).ToArray(),
                SelectedIndex = 0,
                Header = "映射到"
            };
            var nameBox = new TextBox
            {
                Header = "新建档案名称（可选；留空使用自动唯一名）",
                MaxLength = 50
            };
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = $"档案映射：{mapping.SourceName}",
                PrimaryButtonText = "应用映射",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                Content = new StackPanel
                {
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "来源档案与目标档案同名/相似时需要明确选择；" +
                                "选择“创建新的目标档案”不会覆盖现有档案。",
                            TextWrapping = TextWrapping.Wrap
                        },
                        picker,
                        nameBox
                    }
                }
            };
            AutomationProperties.SetAutomationId(dialog, "Ui4ArchiveTransferMapArchiveDialog");
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return false;
            int selectedIndex = picker.SelectedIndex;
            if (selectedIndex < 0 || selectedIndex >= choices.Length) return false;
            MappingChoice choice = choices[selectedIndex];
            string? newName = choice.TargetArchiveId is null &&
                !string.IsNullOrWhiteSpace(nameBox.Text)
                ? nameBox.Text
                : null;
            ArchiveTransferActionResult result = await state.ResolveArchiveAsync(
                mapping.SourceArchiveReference,
                choice.TargetArchiveId,
                newName);
            Render();
            if (!result.Succeeded)
            {
                await ShowMessageAsync("映射未应用", result.Message);
                return false;
            }
        }

        return true;
    }

    private async Task<bool> ResolveAccountMappingsAsync()
    {
        while (state.Snapshot.Plan is { } plan)
        {
            ArchiveRestoreAccountMapping? mapping = plan.Accounts.FirstOrDefault(item =>
                item.Kind == ArchiveRestoreAccountMappingKind.RequiresSelection);
            if (mapping is null) return true;
            if (mapping.TargetArchiveId is not Guid targetArchiveId)
            {
                await ShowMessageAsync("账号映射未完成", "请先解析目标档案映射。");
                return false;
            }

            IReadOnlyList<GameAccount> targetAccounts =
                await state.GetTargetAccountsAsync(targetArchiveId);
            var choices = targetAccounts
                .Where(account => account.IdentityResolutionState ==
                    GameRoleIdentityResolutionState.Unresolved)
                .Select(account =>
                {
                    string label = string.IsNullOrWhiteSpace(account.DisplayName)
                        ? account.Uid
                        : $"{account.DisplayName} ({account.Uid})";
                    return new MappingChoice(
                        $"{label} ({account.Id.ToString("N")[..8]})",
                        account.Id);
                })
                .Append(new MappingChoice("创建新的 Unresolved 账号副本", null))
                .ToArray();
            var picker = new ComboBox
            {
                ItemsSource = choices.Select(item => item.Label).ToArray(),
                SelectedIndex = 0,
                Header = "映射到"
            };
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "Unresolved 账号映射",
                PrimaryButtonText = "应用账号映射",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                Content = new StackPanel
                {
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "来源账号身份尚未解析，必须明确映射到现有 Unresolved 副本或创建新副本；" +
                                "不会自动合并已解析身份。",
                            TextWrapping = TextWrapping.Wrap
                        },
                        picker
                    }
                }
            };
            AutomationProperties.SetAutomationId(dialog, "Ui4ArchiveTransferMapAccountDialog");
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return false;
            int selectedIndex = picker.SelectedIndex;
            if (selectedIndex < 0 || selectedIndex >= choices.Length) return false;
            ArchiveTransferActionResult result = await state.ResolveAccountAsync(
                mapping.SourceAccountReference,
                choices[selectedIndex].TargetArchiveId is Guid target ? target : null);
            Render();
            if (!result.Succeeded)
            {
                await ShowMessageAsync("账号映射未应用", result.Message);
                return false;
            }
        }

        return true;
    }

    private async void OnApply(object sender, RoutedEventArgs e)
    {
        if (state.Snapshot.Plan is not { } plan) return;
        if (HasBlockingPlanIssues(plan))
        {
            await ShowMessageAsync("无法提交", "预览仍存在未解决的映射、冲突或确认项。");
            return;
        }

        ArchiveRestoreConfirmationRequirement[] pending = plan.Confirmations
            .Where(item => !item.Satisfied)
            .ToArray();
        if (pending.Length > 0)
        {
            string warnings = string.Join(
                Environment.NewLine,
                pending.Select(item => $"• {item.Message}"));
            var confirm = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "确认 Merge 前置项",
                PrimaryButtonText = "接受并继续",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
                Content = new TextBlock
                {
                    Text = warnings + Environment.NewLine +
                        "确认只适用于当前预览与当前 marker/version；目标变化后必须重新预览。",
                    TextWrapping = TextWrapping.Wrap
                }
            };
            AutomationProperties.SetAutomationId(confirm, "Ui4ArchiveTransferConfirmDialog");
            if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
            await state.AcceptPendingConfirmationsAsync();
            Render();
            if (state.Snapshot.Plan is not { } next || !next.CanApply)
            {
                await ShowMessageAsync("仍需处理", state.Snapshot.DiagnosticSummary);
                return;
            }

            plan = next;
        }

        var finalConfirm = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = plan.Mode == ArchiveRestoreMode.Replace
                ? "确认破坏性 Scoped Replace"
                : "确认提交 Merge",
            PrimaryButtonText = plan.Mode == ArchiveRestoreMode.Replace
                ? "提交 Replace"
                : "提交 Merge",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            Content = new TextBlock
            {
                Text = state.BuildApplyConfirmationText(),
                TextWrapping = TextWrapping.Wrap
            }
        };
        AutomationProperties.SetAutomationId(finalConfirm, "Ui4ArchiveTransferApplyDialog");
        if (await finalConfirm.ShowAsync() != ContentDialogResult.Primary) return;

        ArchiveTransferApplyOutcome outcome = await state.ApplyAsync();
        if (outcome.Result?.Status == ChangeExecutionStatus.NeedsConfirmation &&
            outcome.Result.CleanupPlan is HistoryCleanupPlan cleanup)
        {
            var cleanupDialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "撤销材料空间不足",
                PrimaryButtonText = "允许清理并重试",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
                Content = new TextBlock
                {
                    Text = $"本次操作需要 {cleanup.RequiredBytes} 字节，当前可用 " +
                        $"{cleanup.AvailableBytes} 字节；清理计划可回收 " +
                        $"{cleanup.ReclaimableBytes} 字节。只清理计划列出的较早撤销材料，" +
                        "不删除当前业务事实。",
                    TextWrapping = TextWrapping.Wrap
                }
            };
            AutomationProperties.SetAutomationId(cleanupDialog, "Ui4ArchiveTransferCleanupDialog");
            if (await cleanupDialog.ShowAsync() == ContentDialogResult.Primary)
            {
                outcome = await state.ApplyAsync(cleanup.ConfirmationId);
            }
        }

        Render();
        await ShowMessageAsync(
            outcome.Succeeded ? "恢复结果" : "恢复未提交",
            outcome.Message);
        if (outcome.Succeeded) RestoreApplied?.Invoke(this, EventArgs.Empty);
    }

    private async void OnCancelSession(object sender, RoutedEventArgs e)
    {
        await state.ReleaseAsync();
        Render();
    }

    private async void OnBack(object sender, RoutedEventArgs e)
    {
        await state.ReleaseAsync();
        Render();
        BackRequested?.Invoke(this, EventArgs.Empty);
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            CloseButtonText = "确定",
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }
        };
        await dialog.ShowAsync();
    }

    public sealed record AccountOption(ArchiveCatalogAccount Account)
    {
        public string Label => string.IsNullOrWhiteSpace(Account.DisplayName)
            ? Account.LegacyUid
            : $"{Account.LegacyUid} · {Account.DisplayName}";
    }

    public sealed record ReplaceScopeRow(
        ArchiveReplaceSelection Selection,
        string Label);

    private sealed record MappingChoice(string Label, Guid? TargetArchiveId);
}
