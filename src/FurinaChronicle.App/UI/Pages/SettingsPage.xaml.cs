// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.App.UI.Gacha;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Presentation.Management;

namespace FurinaChronicle.App.UI.Pages;

// UI-4 settings groups for the MAUI host: appearance, isolated data/storage, undo
// retention and application information. All state comes from the shared SettingsState;
// this page only renders it and forwards user intents.
public partial class SettingsPage : ContentPage
{
    private readonly SettingsState state;
    private readonly GachaAppearanceSettings appearance;
    private bool rendering;

    public SettingsPage(
        SettingsState state,
        GachaAppearanceSettings appearance)
    {
        this.state = state ?? throw new ArgumentNullException(nameof(state));
        this.appearance = appearance ??
            throw new ArgumentNullException(nameof(appearance));
        InitializeComponent();
    }

    // UI-0/UI-1 technical previews only in the isolated validation build.
    public bool ShowValidationTools =>
#if PHASE8C_VALIDATION
        true;
#else
        false;
#endif

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        ValidationToolsSection.IsVisible = ShowValidationTools;
        ItemBackgroundPicker.SelectedIndex =
            appearance.UseClassicBackground ? 1 : 0;
        await state.EnterAsync();
        Render();
    }

    private void Render()
    {
        rendering = true;
        try
        {
            SettingsSnapshot snapshot = state.Snapshot;
            BusyIndicator.IsRunning = snapshot.IsBusy;
            BusyIndicator.IsVisible = snapshot.IsBusy;
            StatusLabel.Text = snapshot.Status;
            ErrorLabel.Text = snapshot.Error ?? string.Empty;
            ErrorLabel.IsVisible =
                !string.IsNullOrWhiteSpace(snapshot.Error);

            ThemePicker.SelectedIndex = (int)snapshot.Theme;

            ArchivePicker.ItemsSource = snapshot.Archives.ToList();
            ArchivePicker.SelectedIndex = snapshot.SelectedArchiveId is Guid id
                ? snapshot.Archives
                    .Select((archive, index) => (archive, index))
                    .Where(item => item.archive.Id == id)
                    .Select(item => item.index)
                    .DefaultIfEmpty(-1)
                    .First()
                : -1;
            UndoLimitEntry.Text =
                snapshot.SelectedArchiveId is null
                    ? string.Empty
                    : snapshot.UndoLimit.ToString();

            DataRootLabel.Text = snapshot.DataRootPath;
            DatabaseLabel.Text = snapshot.DatabasePath;
            DatabaseSizeLabel.Text = snapshot.DatabaseSizeText;
            CacheLabel.Text = snapshot.CacheDirectoryPath;
            CacheSizeLabel.Text = snapshot.CacheSizeText;

            VersionLabel.Text = snapshot.VersionText;
            VersionDiagnosticLabel.Text =
                $"Phase {snapshot.PhaseLabel} · {snapshot.Commit}";

            LicenseLabel.Text = state.LicenseText;
            ThirdPartyLabel.Text = state.ThirdPartyNoticesText;
        }
        finally
        {
            rendering = false;
        }
    }

    private async void OnThemeChanged(object? sender, EventArgs e)
    {
        if (rendering || ThemePicker.SelectedIndex is not (0 or 1 or 2))
        {
            return;
        }

        await state.SetThemeAsync(
            (SettingsTheme)ThemePicker.SelectedIndex);
        Render();
    }

    private async void OnHistoryArchiveChanged(object? sender, EventArgs e)
    {
        if (rendering || ArchivePicker.SelectedItem is not PlayerArchive archive)
        {
            return;
        }

        await state.SelectArchiveAsync(archive.Id);
        Render();
    }

    private async void OnSaveUndoLimitClicked(object? sender, EventArgs e)
    {
        if (!int.TryParse(UndoLimitEntry.Text, out int limit))
        {
            ErrorLabel.Text = "撤销保留次数必须为 0～1000 的整数。";
            ErrorLabel.IsVisible = true;
            return;
        }

        SettingsActionResult result = await state.SetUndoLimitAsync(limit);
        Render();
        await DisplayAlertAsync(
            result.Succeeded ? "已保存撤销保留次数" : "无法保存撤销保留次数",
            result.Message,
            "确定");
    }

    private async void OnClearIconCacheClicked(object? sender, EventArgs e)
    {
        SettingsActionResult result = await state.ClearIconCacheAsync();
        Render();
        await DisplayAlertAsync(
            result.Succeeded ? "图标缓存已清除" : "清除图标缓存失败",
            result.Message,
            "确定");
    }

    private async void OnExportDiagnosticsClicked(object? sender, EventArgs e)
    {
        SettingsActionResult result = await state.ExportDiagnosticsAsync();
        Render();
        await DisplayAlertAsync(
            result.Succeeded ? "诊断已导出" : "诊断导出失败",
            result.Message,
            "确定");
    }

    private void OnToggleLicenseClicked(object? sender, EventArgs e)
    {
        LicenseLabel.IsVisible = !LicenseLabel.IsVisible;
        if (LicenseLabel.IsVisible)
        {
            LicenseLabel.Text = state.LicenseText;
        }
    }

    private void OnToggleThirdPartyClicked(object? sender, EventArgs e)
    {
        ThirdPartyLabel.IsVisible = !ThirdPartyLabel.IsVisible;
        if (ThirdPartyLabel.IsVisible)
        {
            ThirdPartyLabel.Text = state.ThirdPartyNoticesText;
        }
    }

    private void OnItemBackgroundChanged(object? sender, EventArgs e)
    {
        if (rendering ||
            sender is not Picker { SelectedIndex: 0 or 1 } picker ||
            Application.Current is not Application app)
        {
            return;
        }

        bool useClassic = picker.SelectedIndex == 1;
        if (appearance.UseClassicBackground != useClassic)
        {
            appearance.SetClassicBackground(useClassic, app.Resources);
        }
    }

    private async void OnOpenUi0PreviewClicked(object? sender, EventArgs e)
    {
        if (Shell.Current is Shell shell)
        {
            await shell.GoToAsync("ui0-preview", animate: true);
        }
    }

    private async void OnOpenUi1PreviewClicked(object? sender, EventArgs e)
    {
        if (Shell.Current is Shell shell)
        {
            await shell.GoToAsync("ui1-preview", animate: true);
        }
    }
}
