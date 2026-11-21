// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Presentation.Ai;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace FurinaChronicle.Windows.Pages;

internal static class AiSettingsDialog
{
    public static async Task ShowAsync(XamlRoot root, AiSettingsState state)
    {
        var dialog = new ContentDialog { XamlRoot = root, Title = "AI 设置", CloseButtonText = "关闭" };
        AutomationProperties.SetAutomationId(dialog, "AiSettingsDialog");
        ContentDialog? pendingChild = null;
        TaskCompletionSource<ContentDialogResult>? pendingResult = null;
        var panel = new AiSettingsPanel(state, child =>
        {
            pendingChild = child;
            pendingResult = new(TaskCreationOptions.RunContinuationsAsynchronously);
            dialog.Hide();
            return pendingResult.Task;
        });
        dialog.Content = new ScrollViewer
        {
            Content = panel, MinWidth = 420,
            MaxHeight = Math.Clamp(root.Size.Height - 240, 180, 660),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        dialog.Closing += (_, args) =>
        {
            if (panel.IsBusy && pendingChild is null) args.Cancel = true;
        };
        await panel.LoadAsync();
        try
        {
            while (true)
            {
                await dialog.ShowAsync();
                if (pendingChild is not { } child) break;
                // Await the parent's complete close before presenting a nested confirmation.
                // Reuse the same editor afterward so unsaved fields and scroll position survive.
                var completion = pendingResult!;
                try { var result = await child.ShowAsync(); pendingChild = null; pendingResult = null; completion.TrySetResult(result); }
                catch (Exception error) { pendingChild = null; pendingResult = null; completion.TrySetException(error); throw; }
            }
        }
        finally { pendingResult?.TrySetCanceled(); dialog.Content = null; }
    }
}
