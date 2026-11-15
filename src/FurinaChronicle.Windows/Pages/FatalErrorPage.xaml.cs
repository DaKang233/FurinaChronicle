// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Presentation.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace FurinaChronicle.Windows.Pages;

/// <summary>
/// Independent fatal error surface. It does not use the business database, the
/// navigation shell or page services; it only renders the sanitized diagnostic
/// report and offers copy/export/exit actions.
/// </summary>
public sealed partial class FatalErrorPage : UserControl
{
    private readonly DiagnosticReport report;
    private readonly IDiagnosticReportExporter? exporter;

    public FatalErrorPage(DiagnosticReport report, IDiagnosticReportExporter? exporter)
    {
        this.report = report;
        this.exporter = exporter;
        InitializeComponent();
        ExportButton.Visibility = exporter is null ? Visibility.Collapsed : Visibility.Visible;
    }

    public string MessageText =>
        $"{report.Title}。应用无法继续运行；本地数据未被修改。";

    public string DiagnosticIdText => $"诊断编号：{report.DiagnosticId}";

    public string DetailsText
    {
        get
        {
            string details = $"时间：{report.OccurredAt:O}{Environment.NewLine}" +
                $"操作：{report.Operation}{Environment.NewLine}" +
                $"级别：{report.Severity}{Environment.NewLine}" +
                $"异常类型：{report.ExceptionType ?? "(none)"}{Environment.NewLine}" +
                $"{report.StackTrace ?? string.Empty}";
            return details;
        }
    }

    private void OnCopyClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            var package = new DataPackage();
            package.SetText($"{DiagnosticIdText}{Environment.NewLine}{DetailsText}");
            Clipboard.SetContent(package);
            StatusText.Text = "详情已复制。";
        }
        catch (Exception)
        {
            StatusText.Text = "复制失败。";
        }
    }

    private async void OnExportClicked(object sender, RoutedEventArgs e)
    {
        if (exporter is null)
        {
            return;
        }
        ExportButton.IsEnabled = false;
        try
        {
            string? path = await exporter.ExportAsync();
            StatusText.Text = path is null ? string.Empty : $"诊断报告已保存：{path}";
        }
        catch (Exception)
        {
            StatusText.Text = "诊断报告保存失败；可重试。";
        }
        finally
        {
            ExportButton.IsEnabled = true;
        }
    }

    private void OnExitClicked(object sender, RoutedEventArgs e) =>
        Application.Current.Exit();
}
