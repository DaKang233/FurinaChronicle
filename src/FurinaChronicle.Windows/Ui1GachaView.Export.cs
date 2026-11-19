// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Windows.Composition;
using FurinaChronicle.Infrastructure.Gacha.Exporting;
using FurinaChronicle.Infrastructure.Gacha.Metadata;
using FurinaChronicle.Infrastructure.Gacha.Uigf.V4_2;
using FurinaChronicle.Presentation.Gacha;
using FurinaChronicle.Presentation.Notifications;
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

    private async Task ExportAsync(WindowsHostEnvironment preview, GachaReadOnlySnapshot snapshot,
        Guid archiveId, CancellationToken token)
    {
        string taskKey = NotificationTaskKeys.GachaExportArchive;
        try
        {
            GachaExportSelection? selection = await ChooseExportAsync(snapshot, archiveId);
            if (selection is null) return;
            token.ThrowIfCancellationRequested();
            var chosen = ExportFormats[selection.FormatIndex];
            taskKey = chosen.Table is null
                ? NotificationTaskKeys.GachaExportArchive
                : NotificationTaskKeys.GachaExportTable;
            Guid[] accountIds = selection.AccountIds;
            PublishProgress(taskKey, "导出抽卡数据", "正在生成导出文件…");
            string path = Path.Combine(preview.TemporaryDirectory, $"gacha-{Guid.NewGuid():N}.tmp");
            await using var artifact = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
                65536, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
            var load = new LoadGachaExportData(preview.Archives, preview.Accounts, preview.Records, TimeProvider.System);
            var metadata = exportMetadata ?? new EmptyGachaItemMetadataProvider();
            GachaExportResult result = await Task.Run(() => chosen.Table is GachaTableFormat table
                ? new ExportGachaTable(load, new GachaTableExportWriter(metadata)).ExecuteAsync(artifact, archiveId, accountIds,
                    new(table, selection.Uigf.Language), token)
                : new ExportUigfV42GachaRecords(load, new UigfV42GachaWriter(metadata)).ExecuteAsync(artifact, archiveId, accountIds,
                    selection.Uigf, token), token);

            token.ThrowIfCancellationRequested();
            PublishProgress(taskKey, "导出抽卡数据", "请选择保存位置…");
            StorageFile? destination = await filePicker!.PickSaveFileAsync(
                $"FurinaChronicle-Gacha-{DateTime.Today:yyyyMMdd}",
                chosen.Name,
                chosen.Extension);
            if (destination is null)
            {
                ClearProgress(taskKey);
                return;
            }

            PublishProgress(taskKey, "导出抽卡数据", "正在保存导出文件…");
            artifact.Position = 0;
            await SaveExportAsync(destination.Path, artifact, token);
            Action? openFolder = CreateOpenFolderAction(destination.Path);
            PublishSuccess(
                taskKey,
                "导出完成",
                $"已保存到 {DescribeSavedLocation(destination.Path, destination.Name)}：" +
                $"{result.AccountCount} 个账号、{result.RecordCount} 条记录。",
                openFolder is null ? null : "打开文件夹",
                openFolder);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            ClearProgress(NotificationTaskKeys.GachaExportArchive);
            ClearProgress(NotificationTaskKeys.GachaExportTable);
        }
        catch (Exception exception)
        {
            if (!token.IsCancellationRequested)
            {
                ReportFailure(
                    taskKey,
                    "gacha.export",
                    "导出失败",
                    "导出未完成。请检查导出范围和所选目录，再重试。",
                    exception);
            }
        }
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
