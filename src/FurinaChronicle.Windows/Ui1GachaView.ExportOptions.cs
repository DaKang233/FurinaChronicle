// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Presentation.Gacha;
using FurinaChronicle.Services.Gacha.Abstractions;
using FurinaChronicle.Services.Gacha.Exporting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace FurinaChronicle.Windows;

public sealed partial class Ui1GachaView
{
    private IGachaItemMetadataProvider? exportMetadata;
    private sealed record GachaExportSelection(int FormatIndex, Guid[] AccountIds, UigfV42ExportOptions Uigf);

    private async Task<GachaExportSelection?> ChooseExportAsync(GachaReadOnlySnapshot snapshot, Guid archiveId)
    {
        Guid[] current = snapshot.ArchiveAccountIds?.ToArray() ??
            (snapshot.AccountId is Guid id ? [id] : []);
        var accounts = new StackPanel { Spacing = 4 };
        var checks = new Dictionary<Guid, CheckBox>();
        foreach (var account in snapshot.Accounts)
        {
            var check = new CheckBox { Content = account.Label, IsChecked = current.Contains(account.Id) };
            AutomationProperties.SetAutomationId(check, $"GachaExportAccount{account.Id:N}");
            checks.Add(account.Id, check);
            accounts.Children.Add(check);
        }

        var language = new ComboBox
        {
            Header = "导出语言", ItemsSource = new[] { "简体中文", "繁體中文", "English" }, SelectedIndex = 0,
        };
        AutomationProperties.SetAutomationId(language, "GachaExportLanguage");
        var format = new ComboBox
        {
            Header = "文件格式", ItemsSource = ExportFormats.Select(item => item.Name).ToArray(), SelectedIndex = 0,
        };
        AutomationProperties.SetAutomationId(format, "GachaExportFormat");
        string[] labels = ["info.lang", "账号 lang", "count", "name", "item_type", "rank_type", "空 hk4e_ugc 兼容节点"];
        CheckBox[] flags = labels.Select(label => new CheckBox { Content = label }).ToArray();
        for (int i = 0; i < flags.Length; i++)
            AutomationProperties.SetAutomationId(flags[i], $"GachaExportField{i}");
        void ApplyPreset(UigfV42ExportOptions value)
        {
            bool[] values = [value.IncludeInfoLanguage, value.IncludeAccountLanguage, value.IncludeCount,
                value.IncludeName, value.IncludeItemType, value.IncludeRankType, value.IncludeEmptyHk4eUgc];
            for (int i = 0; i < flags.Length; i++) flags[i].IsChecked = values[i];
        }
        ApplyPreset(UigfV42ExportOptions.Compatible);
        var advanced = new StackPanel { Spacing = 4 };
        advanced.Children.Add(new TextBlock
        {
            Text = "必要字段始终导出：uid、timezone、list、gacha_type、item_id、time、id、uigf_gacha_type。",
            TextWrapping = TextWrapping.Wrap,
        });
        foreach (var flag in flags) advanced.Children.Add(flag);
        var presets = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var compatible = new Button { Content = "兼容预设" };
        var minimal = new Button { Content = "最小预设" };
        var reset = new Button { Content = "恢复默认" };
        compatible.Click += (_, _) => ApplyPreset(UigfV42ExportOptions.Compatible);
        minimal.Click += (_, _) => ApplyPreset(UigfV42ExportOptions.Minimal);
        reset.Click += (_, _) => { language.SelectedIndex = 0; ApplyPreset(UigfV42ExportOptions.Compatible); };
        presets.Children.Add(compatible);
        presets.Children.Add(minimal);
        presets.Children.Add(reset);
        var uigf = new StackPanel { Spacing = 8 };
        uigf.Children.Add(presets);
        uigf.Children.Add(new Expander { Header = "高级字段选项", Content = advanced, HorizontalAlignment = HorizontalAlignment.Stretch });
        var tableHint = new TextBlock
        {
            Text = "表格包含抽卡字段、档案与账号身份、来源、采集时间及导出说明；使用固定列结构。",
            TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed,
        };
        format.SelectionChanged += (_, _) =>
        {
            bool isUigf = format.SelectedIndex == 0;
            uigf.Visibility = isUigf ? Visibility.Visible : Visibility.Collapsed;
            tableHint.Visibility = isUigf ? Visibility.Collapsed : Visibility.Visible;
        };
        var quick = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (string label in new[] { "当前选择", "全选", "清空" })
        {
            var button = new Button { Content = label };
            button.Click += (_, _) =>
            {
                foreach (var (id, check) in checks)
                    check.IsChecked = label == "全选" || (label == "当前选择" && current.Contains(id));
            };
            quick.Children.Add(button);
        }
        string? archive = snapshot.Archives.FirstOrDefault(item => item.Id == archiveId)?.Label;
        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(new TextBlock
        {
            Text = $"档案：{archive}\n导出勾选账号的完整记录；页面筛选和分页不限制导出范围。未缓存的翻译保留已有名称，不联网获取。",
            TextWrapping = TextWrapping.Wrap,
        });
        content.Children.Add(quick);
        content.Children.Add(new ScrollViewer { Content = accounts, MaxHeight = 150, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        content.Children.Add(format);
        content.Children.Add(language);
        content.Children.Add(uigf);
        content.Children.Add(tableHint);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = "导出抽卡数据", PrimaryButtonText = "继续保存", CloseButtonText = "取消",
            IsPrimaryButtonEnabled = checks.Values.Any(check => check.IsChecked == true),
            Content = new ScrollViewer { Content = content, MaxHeight = 450, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
        };
        foreach (var check in checks.Values)
        {
            check.Checked += (_, _) => dialog.IsPrimaryButtonEnabled = true;
            check.Unchecked += (_, _) => dialog.IsPrimaryButtonEnabled = checks.Values.Any(item => item.IsChecked == true);
        }
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;
        Guid[] ids = checks.Where(pair => pair.Value.IsChecked == true).Select(pair => pair.Key).ToArray();
        if (ids.Length == 0) return null;
        var options = new UigfV42ExportOptions
        {
            Language = GachaExportLanguages.Supported[language.SelectedIndex],
            IncludeInfoLanguage = flags[0].IsChecked == true,
            IncludeAccountLanguage = flags[1].IsChecked == true,
            IncludeCount = flags[2].IsChecked == true,
            IncludeName = flags[3].IsChecked == true,
            IncludeItemType = flags[4].IsChecked == true,
            IncludeRankType = flags[5].IsChecked == true,
            IncludeEmptyHk4eUgc = flags[6].IsChecked == true,
        };
        options.Validate();
        return new(format.SelectedIndex, ids, options);
    }
}
