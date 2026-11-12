// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Presentation.Management;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace FurinaChronicle.Windows.Pages;

public sealed partial class MigrationPage : UserControl
{
    private readonly ProductionMigrationState state;
    private bool rendering;

    public MigrationPage(ProductionMigrationState state)
    {
        this.state = state;
        InitializeComponent();
    }

    public event EventHandler? CloseRequested;

    public bool ShouldPrompt => state.Snapshot.ShouldPrompt;

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
            ProductionMigrationSnapshot snapshot = state.Snapshot;
            StatusText.Text = snapshot.Status;
            ErrorText.Text = snapshot.Error ?? string.Empty;
            ErrorText.Visibility = string.IsNullOrWhiteSpace(snapshot.Error)
                ? Visibility.Collapsed
                : Visibility.Visible;
            Progress.IsActive = snapshot.IsBusy;
            if (snapshot.Source is { } source)
            {
                SourceText.Text =
                    $"源库：{source.DatabasePath}\n" +
                    $"存在：{(source.Exists ? "是" : "否")}；" +
                    $"大小：{(source.Exists ? $"{source.SizeBytes} B" : "无")}；" +
                    $"最后写入：{(source.LastWriteUtc is { } time ? time.ToString("yyyy-MM-dd HH:mm:ss zzz") : "无")}\n" +
                    $"探测类型：{(source.IsSyntheticFixture ? "合成夹具（仅验收）" : "生产路径（仅目录元数据）")}";
            }
            else
            {
                SourceText.Text = "源库：未探测。";
            }

            TargetText.Text = $"目标副本：{snapshot.Paths.TargetDatabasePath}";
            BackupText.Text = $"强制备份目录：{snapshot.Paths.BackupDirectory}";
            MarkerText.Text = snapshot.Marker is { } marker
                ? $"迁移标记：{marker.Status}；完成时间 {marker.CompletedAtUtc:yyyy-MM-dd HH:mm:ss zzz}；" +
                    $"源文件 {marker.SourceFileName}（路径仅哈希）；备份 {marker.BackupFileName}；" +
                    $"目标 SHA-256 {marker.TargetSha256[..Math.Min(16, marker.TargetSha256.Length)]}…"
                : "迁移标记：尚未写入。";
            LegacyText.Text = snapshot.LegacyHostRunning
                ? "检测到旧宿主或其他进程可能正在使用源数据；请先关闭旧宿主。未写入任何数据。"
                : string.Empty;
            LegacyText.Visibility = snapshot.LegacyHostRunning
                ? Visibility.Visible
                : Visibility.Collapsed;
            ForceRemigrateCheck.Visibility = snapshot.IsCompleted
                ? Visibility.Visible
                : Visibility.Collapsed;
            StartButton.IsEnabled = !snapshot.IsBusy &&
                snapshot.Source?.Exists == true &&
                !snapshot.LegacyHostRunning &&
                (!snapshot.IsCompleted || ForceRemigrateCheck.IsChecked == true);
            SkipButton.IsEnabled = !snapshot.IsBusy;
            ContinueButton.Visibility = snapshot.IsCompleted ||
                snapshot.TargetValid
                ? Visibility.Visible
                : Visibility.Collapsed;
            ContinueButton.IsEnabled = !snapshot.IsBusy;
            RollbackText.Text = state.BuildRollbackText() + Environment.NewLine +
                state.CredentialsText;
        }
        finally
        {
            rendering = false;
        }
    }

    private async void OnStart(object sender, RoutedEventArgs e)
    {
        ProductionMigrationSnapshot snapshot = state.Snapshot;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "开始复制迁移",
            PrimaryButtonText = "开始迁移",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            Content = new ScrollViewer
            {
                MaxHeight = 460,
                Content = new TextBlock
                {
                    Text = $"源库（只读，绝不修改/移动）：{snapshot.Source?.DatabasePath}\n" +
                        $"目标副本：{snapshot.Paths.TargetDatabasePath}\n" +
                        $"强制备份目录：{snapshot.Paths.BackupDirectory}\n\n" +
                        "迁移会先做一致性备份，再复制到新宿主目录并做只读校验；任一步失败不切换，" +
                        "源库与已有目标不变。回退=回到迁移点；凭据不迁移，需重新授权。" +
                        (ForceRemigrateCheck.IsChecked == true
                            ? "\n\n已选择重新迁移；仅在校验通过后替换目标副本。"
                            : string.Empty),
                    TextWrapping = TextWrapping.Wrap
                }
            }
        };
        AutomationProperties.SetAutomationId(dialog, "Ui6MigrationConfirmDialog");
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        ProductionMigrationResult result = await state.StartAsync(
            ForceRemigrateCheck.IsChecked == true);
        Render();
        var resultDialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = result.Succeeded ? "迁移结果" : "迁移未完成",
            CloseButtonText = "确定",
            Content = new TextBlock { Text = result.Message, TextWrapping = TextWrapping.Wrap }
        };
        AutomationProperties.SetAutomationId(resultDialog, "Ui6MigrationResultDialog");
        await resultDialog.ShowAsync();
        if (result.Succeeded)
        {
            ContinueButton.Visibility = Visibility.Visible;
            Render();
        }
    }

    private void OnContinue(object sender, RoutedEventArgs e) =>
        CloseRequested?.Invoke(this, EventArgs.Empty);

    private void OnSkip(object sender, RoutedEventArgs e) =>
        CloseRequested?.Invoke(this, EventArgs.Empty);

    private void OnForceChanged(object sender, RoutedEventArgs e)
    {
        if (!rendering) Render();
    }
}
