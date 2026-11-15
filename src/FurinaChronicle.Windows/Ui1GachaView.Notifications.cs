// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Presentation.Diagnostics;
using FurinaChronicle.Presentation.Notifications;
using FurinaChronicle.Services.Gacha;
using FurinaChronicle.Services.Gacha.Refreshing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;

namespace FurinaChronicle.Windows;

// Page-local notification helpers. All user-initiated Gacha operations report through the
// shared notification center; failures go through the diagnostic service exactly once.
public sealed partial class Ui1GachaView
{
    private INotificationCenter? notifications;
    private IDiagnosticService? diagnostics;
    private string? lastReadFailure;

    private void ReportReadFailureOnce(string status)
    {
        if (!status.Contains("失败", StringComparison.Ordinal))
        {
            lastReadFailure = null;
            return;
        }

        if (string.Equals(lastReadFailure, status, StringComparison.Ordinal)) return;
        lastReadFailure = status;
        ReportFailure(
            "gacha.read",
            "gacha-page-read",
            "抽卡数据读取失败",
            status);
    }

    private void ShowInlineValidation(string message)
    {
        InlineValidation.Text = message;
        InlineValidation.Visibility = Visibility.Visible;
        AutomationProperties.SetName(InlineValidation, $"输入校验：{message}");
    }

    private void ClearInlineValidation()
    {
        InlineValidation.Text = string.Empty;
        InlineValidation.Visibility = Visibility.Collapsed;
    }

    private void PublishProgress(string taskKey, string title, string message)
    {
        notifications?.Publish(new NotificationRequest(
            NotificationSeverity.Information,
            NotificationCategory.Gacha,
            title,
            message)
        {
            TaskKey = taskKey,
            IsInProgress = true
        });
    }

    private void PublishSuccess(
        string taskKey,
        string title,
        string message,
        string? actionLabel = null,
        Action? action = null)
    {
        notifications?.Publish(new NotificationRequest(
            NotificationSeverity.Success,
            NotificationCategory.Gacha,
            title,
            message)
        {
            TaskKey = taskKey,
            IsInProgress = false,
            ActionLabel = actionLabel,
            Action = action
        });
    }

    private void PublishWarning(
        string taskKey,
        string title,
        string message,
        string? actionLabel = null,
        Action? action = null)
    {
        notifications?.Publish(new NotificationRequest(
            NotificationSeverity.Warning,
            NotificationCategory.Gacha,
            title,
            message)
        {
            TaskKey = taskKey,
            IsInProgress = false,
            ActionLabel = actionLabel,
            Action = action
        });
    }

    private void ReportFailure(
        string taskKey,
        string operation,
        string title,
        string message,
        Exception? exception = null)
    {
        if (diagnostics is not null)
        {
            diagnostics.Report(new DiagnosticEvent(
                NotificationCategory.Gacha,
                operation,
                title,
                message,
                DiagnosticSeverity.Error,
                exception,
                taskKey));
            return;
        }

        notifications?.Publish(new NotificationRequest(
            NotificationSeverity.Error,
            NotificationCategory.Gacha,
            title,
            message)
        {
            TaskKey = taskKey,
            IsInProgress = false
        });
    }

    private void ClearProgress(string taskKey)
    {
        AppNotification? running = notifications?.FindByTask(taskKey);
        if (running is { IsInProgress: true })
        {
            notifications?.Remove(running.Id);
        }
    }

    private async Task<(bool Active, bool Success, string Detail)> ReloadAfterCommitAsync()
    {
        if (state is null) return (false, false, "抽卡页状态不可用");
        if (!entered) return (false, false, "抽卡页已离开");
        try
        {
            // Read-only refresh of the same scope; never repeats the committed write.
            await state.EnterAsync();
            bool failed = state.Snapshot.Status.Contains("失败", StringComparison.Ordinal);
            return (true, !failed, failed ? "列表更新失败" : "列表已更新");
        }
        catch (Exception)
        {
            return (true, false, "列表更新失败");
        }
    }

    private async Task PublishCommittedResultAsync(
        string taskKey,
        string title,
        string message)
    {
        (bool active, bool success, string detail) = await ReloadAfterCommitAsync();
        if (!active)
        {
            PublishSuccess(
                taskKey,
                title,
                message + Environment.NewLine + "抽卡页已离开；返回后可重新载入查看。");
            return;
        }

        if (success)
        {
            PublishSuccess(taskKey, title, message + Environment.NewLine + detail + "。");
            return;
        }

        PublishWarning(
            taskKey,
            title,
            message + Environment.NewLine + "列表更新失败；数据已提交，只需重新加载显示。",
            "重新加载",
            CreateReloadAction());
    }

    private Action CreateReloadAction() =>
        () => _ = ReloadFromNotificationAsync();

    private async Task ReloadFromNotificationAsync()
    {
        if (state is null || !entered) return;
        try
        {
            await state.EnterAsync();
            Render();
        }
        catch (Exception exception)
        {
            diagnostics?.Report(new DiagnosticEvent(
                NotificationCategory.Gacha,
                "gacha.reload",
                "重新加载失败",
                "数据已保存；当前列表仍无法重新加载，可稍后重试。",
                DiagnosticSeverity.Warning,
                exception));
        }
    }

    private Action? CreateOpenFolderAction(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) ||
            path.Contains("content://", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(path))
        {
            return null;
        }

        return () =>
        {
            try
            {
                if (!File.Exists(path))
                {
                    diagnostics?.Report(new DiagnosticEvent(
                        NotificationCategory.Gacha,
                        "gacha.export.open-folder",
                        "文件已不存在",
                        "导出文件已被移动或删除，无法打开所在文件夹。",
                        DiagnosticSeverity.Warning));
                    return;
                }

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{path}\"",
                    UseShellExecute = true
                });
            }
            catch (Exception exception)
            {
                diagnostics?.Report(new DiagnosticEvent(
                    NotificationCategory.Gacha,
                    "gacha.export.open-folder",
                    "无法打开文件夹",
                    "导出文件仍在，但无法打开所在文件夹；可从保存位置手动打开。",
                    DiagnosticSeverity.Warning,
                    exception));
            }
        };
    }

    private static string DescribeRefresh(GachaRefreshResult? result) =>
        result is null
            ? string.Empty
            : $"新增 {result.InsertedCount} 条、更新 {result.UpdatedCount} 条、" +
                $"跳过重复 {result.DuplicateCount} 条（共获取 {result.FetchedCount} 条）。";

    private static string DescribeSavedLocation(string? path, string fileName)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            path.Contains("content://", StringComparison.OrdinalIgnoreCase) ||
            !Path.IsPathFullyQualified(path))
        {
            return $"保存对话框所选位置 / {fileName}";
        }

        string? folder = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(folder)) return fileName;
        string downloads = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) +
            Path.DirectorySeparatorChar + "Downloads";
        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        string label =
            string.Equals(folder, downloads, StringComparison.OrdinalIgnoreCase) ? "下载目录" :
            string.Equals(folder, documents, StringComparison.OrdinalIgnoreCase) ? "文档" :
            string.Equals(folder, desktop, StringComparison.OrdinalIgnoreCase) ? "桌面" :
            "所选文件夹";
        return $"{label} / {fileName}";
    }
}
