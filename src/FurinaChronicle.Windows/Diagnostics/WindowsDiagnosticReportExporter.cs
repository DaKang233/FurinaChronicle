// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Presentation.Diagnostics;
using FurinaChronicle.Windows.Composition;
using Windows.Storage;

namespace FurinaChronicle.Windows.Diagnostics;

/// <summary>
/// Saves the local diagnostic report through the window-associated file picker.
/// Upload to a server is not implemented on this branch; the local report is the
/// fallback required by the notification plan.
/// </summary>
public sealed class WindowsDiagnosticReportExporter(
    IDiagnosticService diagnostics,
    WindowsPaths paths,
    IFilePickerService filePicker) : IDiagnosticReportExporter
{
    public async Task<string?> ExportAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StorageFile? file = await filePicker.PickSaveFileAsync(
            $"furina-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
            "诊断报告",
            ".txt");
        if (file is null)
        {
            return null;
        }

        string text = DiagnosticReportFormatter.Build(
            diagnostics,
            Path.Combine(paths.DataRoot, "Logs"));
        await FileIO.WriteTextAsync(file, text);
        return string.IsNullOrWhiteSpace(file.Path) ? file.Name : file.Path;
    }
}
