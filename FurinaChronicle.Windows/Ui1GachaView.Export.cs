// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Infrastructure.Diagnostics;
using FurinaChronicle.Infrastructure.Gacha.Exporting;
using FurinaChronicle.Infrastructure.Gacha.Metadata;
using FurinaChronicle.Infrastructure.Gacha.Uigf.V4_2;
using FurinaChronicle.Presentation.Gacha;
using FurinaChronicle.Services.Gacha.Exporting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;

namespace FurinaChronicle.Windows;

public sealed partial class Ui1GachaView
{
    private static readonly (string Name, string Extension, GachaTableFormat? Table)[] ExportFormats =
    [
        ("UIGF 4.2（抽卡数据）", ".json", null),
        ("CSV（可读表格）", ".csv", GachaTableFormat.Csv),
        ("XLSX（可读表格）", ".xlsx", GachaTableFormat.Xlsx)
    ];
    private CancellationTokenSource? exportCancellation;
    private Task? exportTask;

    private void UpdateExportAvailability() => ExportButton.IsEnabled = exportCancellation is null &&
        importFlowTask is null && dataOperationTask is null && !importBusy &&
        state?.Snapshot is { IsBusy: false, ArchiveId: not null, Accounts.Count: > 0 };

    private async void OnExport(object sender, RoutedEventArgs e)
    {
        if (exportCancellation is not null || importFlowTask is not null ||
            dataOperationTask is not null || importBusy ||
            environment is null || filePicker is null ||
            state?.Snapshot is not { IsBusy: false, ArchiveId: Guid archiveId, Accounts.Count: > 0 } snapshot)
            return;

        using var cancellation = new CancellationTokenSource();
        exportCancellation = cancellation;
        UpdateExportAvailability();
        try
        {
            exportTask = ExportAsync(environment, snapshot, archiveId, cancellation.Token);
            await exportTask;
        }
        finally
        {
            exportTask = null;
            exportCancellation = null;
            UpdateExportAvailability();
        }
    }

    private async Task ExportAsync(Ui1PreviewEnvironment preview, GachaReadOnlySnapshot snapshot,
        Guid archiveId, CancellationToken token)
    {
        try
        {
            Guid[] current = snapshot.ArchiveAccountIds?.ToArray() ?? (snapshot.AccountId is Guid id ? [id] : []);
            Guid[] all = snapshot.Accounts.Select(account => account.Id).ToArray();
            var scopes = new List<(string Label, Guid[] Ids)>();
            if (current.Length > 0 && current.Length < all.Length)
            {
                string label = current.Length == 1
                    ? snapshot.Accounts.First(account => account.Id == current[0]).Label
                    : $"{current.Length} 个账号";
                scopes.Add(($"当前选择 · {label}", current));
            }
            scopes.Add(($"档案所有账号（{all.Length} 个）", all));

            var scope = new ComboBox { Header = "导出范围", ItemsSource = scopes.Select(item => item.Label).ToArray(), SelectedIndex = current.Length == 0 ? -1 : 0 };
            var format = new ComboBox { Header = "文件格式", ItemsSource = ExportFormats.Select(item => item.Name).ToArray(), SelectedIndex = 0 };
            string? archive = snapshot.Archives.FirstOrDefault(item => item.Id == archiveId)?.Label;
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "导出抽卡数据",
                PrimaryButtonText = "继续保存",
                IsPrimaryButtonEnabled = scope.SelectedIndex >= 0,
                CloseButtonText = "取消",
                Content = new StackPanel
                {
                    Spacing = 10,
                    Children =
                    {
                        new TextBlock { Text = $"隔离合成档案：{archive}\n导出所选账号的完整记录，当前筛选和分页仅用于查看。", TextWrapping = TextWrapping.Wrap },
                        scope, format
                    }
                }
            };
            scope.SelectionChanged += (_, _) => dialog.IsPrimaryButtonEnabled = scope.SelectedIndex >= 0;
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                ShowExportStatus("已取消导出。");
                return;
            }

            token.ThrowIfCancellationRequested();
            var chosen = ExportFormats[format.SelectedIndex];
            Guid[] accountIds = scopes[scope.SelectedIndex].Ids;
            ShowExportStatus("正在生成导出文件…");
            string path = Path.Combine(preview.TemporaryDirectory, $"gacha-{Guid.NewGuid():N}.tmp");
            await using var artifact = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
                65536, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
            var load = new LoadGachaExportData(preview.Archives, preview.Accounts, preview.Records, TimeProvider.System);
            var metadata = new EmptyGachaItemMetadataProvider();
            GachaExportResult result = await Task.Run(() => chosen.Table is GachaTableFormat table
                ? new ExportGachaTable(load, new GachaTableExportWriter(metadata)).ExecuteAsync(artifact, archiveId, accountIds,
                    new(table, GachaExportLanguages.SimplifiedChinese), token)
                : new ExportUigfV42GachaRecords(load, new UigfV42GachaWriter(metadata)).ExecuteAsync(artifact, archiveId, accountIds,
                    UigfV42ExportOptions.Compatible, token), token);

            token.ThrowIfCancellationRequested();
            ShowExportStatus("请选择保存位置…");
            StorageFile? destination = await filePicker!.PickSaveFileAsync(
                $"FurinaChronicle-Gacha-{DateTime.Today:yyyyMMdd}",
                chosen.Name,
                chosen.Extension);
            if (destination is null)
            {
                ShowExportStatus("已取消保存，未写出导出文件。");
                return;
            }

            ShowExportStatus("正在保存导出文件…");
            artifact.Position = 0;
            await SaveExportAsync(destination.Path, artifact, token);
            ShowExportStatus($"已保存 {destination.Name} · {result.AccountCount} 个账号 · {result.RecordCount} 条记录。");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception)
        {
            if (!token.IsCancellationRequested) ShowExportStatus("导出未完成。请检查导出范围和所选目录，再重试。");
        }
    }

    private void ShowExportStatus(string text)
    {
        ExportStatus.Text = text;
        ExportStatus.Visibility = Visibility.Visible;
    }

    private static async Task SaveExportAsync(string destination, Stream artifact, CancellationToken token)
    {
        if (!Path.IsPathFullyQualified(destination)) throw new IOException("A local destination is required.");
        string directory = Path.GetDirectoryName(destination) ?? throw new IOException("The destination directory is missing.");
        string sibling = Path.Combine(directory, $".furina-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var output = new FileStream(sibling, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                65536, FileOptions.Asynchronous))
            {
                await artifact.CopyToAsync(output, token);
                await output.FlushAsync(token);
            }
            token.ThrowIfCancellationRequested();
            File.Move(sibling, destination, overwrite: true);
        }
        finally
        {
            File.Delete(sibling);
        }
    }
}
