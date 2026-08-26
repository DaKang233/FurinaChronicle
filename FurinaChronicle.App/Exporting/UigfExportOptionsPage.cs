using FurinaChronicle.Core.Archives;
using FurinaChronicle.Services.Gacha.Exporting;

namespace FurinaChronicle.App.Exporting;

public sealed class UigfExportOptionsPage : ContentPage
{
    private readonly TaskCompletionSource<UigfExportDialogResult?> completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool isClosing;
    private readonly Dictionary<Guid, CheckBox> accountChecks = [];
    private readonly Picker languagePicker;
    private readonly VerticalStackLayout advancedOptions;
    private readonly CheckBox infoLanguageCheck = new();
    private readonly CheckBox accountLanguageCheck = new();
    private readonly CheckBox countCheck = new();
    private readonly CheckBox nameCheck = new();
    private readonly CheckBox itemTypeCheck = new();
    private readonly CheckBox rankTypeCheck = new();
    private readonly CheckBox ugcCheck = new();

    public UigfExportOptionsPage(
        IReadOnlyList<GameAccount> accounts,
        Guid? lockedAccountId)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        Title = "导出 UIGF 4.2";

        languagePicker = new Picker
        {
            Title = "导出语言",
            ItemsSource = ExportLanguageOption.All,
            SelectedIndex = 0
        };

        advancedOptions = new VerticalStackLayout
        {
            Spacing = 8,
            IsVisible = false,
            Children =
            {
                new Label
                {
                    Text = "必要字段始终导出：uid、timezone、list、" +
                        "gacha_type、item_id、time、id、uigf_gacha_type。",
                    FontSize = 12
                },
                CreateOptionRow("info.lang", infoLanguageCheck),
                CreateOptionRow("账号 lang", accountLanguageCheck),
                CreateOptionRow("count", countCheck),
                CreateOptionRow("name", nameCheck),
                CreateOptionRow("item_type", itemTypeCheck),
                CreateOptionRow("rank_type", rankTypeCheck),
                CreateOptionRow("空 hk4e_ugc 兼容节点", ugcCheck)
            }
        };

        var accountLayout = new VerticalStackLayout { Spacing = 4 };
        foreach (GameAccount account in accounts)
        {
            bool isLocked =
                lockedAccountId is Guid locked &&
                account.Id == locked;
            var check = new CheckBox
            {
                IsChecked = lockedAccountId is null || isLocked,
                IsEnabled = lockedAccountId is null
            };
            accountChecks.Add(account.Id, check);
            accountLayout.Children.Add(
                CreateOptionRow(
                    account.DisplayName ?? account.Uid,
                    check));
        }

        var advancedButton = new Button
        {
            Text = "展开高级字段选项"
        };
        advancedButton.Clicked += (_, _) =>
        {
            advancedOptions.IsVisible = !advancedOptions.IsVisible;
            advancedButton.Text = advancedOptions.IsVisible
                ? "折叠高级字段选项"
                : "展开高级字段选项";
        };

        var compatibleButton = new Button
        {
            Text = "兼容预设"
        };
        compatibleButton.Clicked += (_, _) =>
            ApplyPreset(UigfV42ExportOptions.Compatible);

        var minimalButton = new Button
        {
            Text = "最小预设"
        };
        minimalButton.Clicked += (_, _) =>
            ApplyPreset(UigfV42ExportOptions.Minimal);

        var resetButton = new Button
        {
            Text = "恢复默认"
        };
        resetButton.Clicked += (_, _) =>
        {
            languagePicker.SelectedIndex = 0;
            ApplyPreset(UigfV42ExportOptions.Compatible);
        };

        var cancelButton = new Button { Text = "取消" };
        cancelButton.Clicked += async (_, _) =>
            await CloseAsync(null);

        var exportButton = new Button { Text = "导出" };
        exportButton.Clicked += async (_, _) =>
            await ConfirmAsync();

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = 20,
                Spacing = 12,
                Children =
                {
                    new Label
                    {
                        Text = "导出 UIGF 4.2",
                        FontSize = 24,
                        FontAttributes = FontAttributes.Bold
                    },
                    new Label
                    {
                        Text = "选择要导出的账号。档案层可以选择一个或多个账号；" +
                            "账号层固定为当前账号。"
                    },
                    new Border
                    {
                        Padding = 12,
                        Content = accountLayout
                    },
                    languagePicker,
                    advancedButton,
                    advancedOptions,
                    new Grid
                    {
                        ColumnDefinitions =
                        {
                            new ColumnDefinition(GridLength.Star),
                            new ColumnDefinition(GridLength.Star),
                            new ColumnDefinition(GridLength.Star)
                        },
                        Children =
                        {
                            compatibleButton,
                            minimalButton,
                            resetButton
                        }
                    },
                    new Grid
                    {
                        ColumnDefinitions =
                        {
                            new ColumnDefinition(GridLength.Star),
                            new ColumnDefinition(GridLength.Star)
                        },
                        Children =
                        {
                            cancelButton,
                            exportButton
                        }
                    }
                }
            }
        };

        Grid.SetColumn(minimalButton, 1);
        Grid.SetColumn(resetButton, 2);
        Grid.SetColumn(exportButton, 1);
        ApplyPreset(UigfV42ExportOptions.Compatible);
    }

    public Task<UigfExportDialogResult?> WaitForResultAsync() =>
        completion.Task;

    protected override bool OnBackButtonPressed()
    {
        _ = CloseAsync(null);
        return true;
    }

    private async Task ConfirmAsync()
    {
        Guid[] accountIds = accountChecks
            .Where(pair => pair.Value.IsChecked)
            .Select(pair => pair.Key)
            .ToArray();
        if (accountIds.Length == 0)
        {
            await DisplayAlertAsync(
                "无法导出",
                "请至少选择一个账号。",
                "确定");
            return;
        }

        var language =
            (ExportLanguageOption)languagePicker.SelectedItem;
        var options = new UigfV42ExportOptions
        {
            Language = language.Code,
            IncludeInfoLanguage = infoLanguageCheck.IsChecked,
            IncludeAccountLanguage = accountLanguageCheck.IsChecked,
            IncludeCount = countCheck.IsChecked,
            IncludeName = nameCheck.IsChecked,
            IncludeItemType = itemTypeCheck.IsChecked,
            IncludeRankType = rankTypeCheck.IsChecked,
            IncludeEmptyHk4eUgc = ugcCheck.IsChecked
        };
        await CloseAsync(new UigfExportDialogResult(accountIds, options));
    }

    private void ApplyPreset(UigfV42ExportOptions preset)
    {
        infoLanguageCheck.IsChecked = preset.IncludeInfoLanguage;
        accountLanguageCheck.IsChecked = preset.IncludeAccountLanguage;
        countCheck.IsChecked = preset.IncludeCount;
        nameCheck.IsChecked = preset.IncludeName;
        itemTypeCheck.IsChecked = preset.IncludeItemType;
        rankTypeCheck.IsChecked = preset.IncludeRankType;
        ugcCheck.IsChecked = preset.IncludeEmptyHk4eUgc;
    }

    private async Task CloseAsync(UigfExportDialogResult? result)
    {
        if (isClosing)
        {
            return;
        }
        isClosing = true;
        await Navigation.PopModalAsync();
        completion.TrySetResult(result);
    }

    private static Grid CreateOptionRow(
        string label,
        CheckBox checkBox)
    {
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };
        grid.Add(
            new Label
            {
                Text = label,
                VerticalTextAlignment = TextAlignment.Center
            });
        grid.Add(checkBox, 1);
        return grid;
    }
}
