using FurinaChronicle.Core.Archives;
using FurinaChronicle.Services.Gacha.Exporting;

namespace FurinaChronicle.App.Exporting;

public sealed class TableExportOptionsPage : ContentPage
{
    private readonly TaskCompletionSource<TableExportDialogResult?> completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool isClosing;
    private readonly Dictionary<Guid, CheckBox> accountChecks = [];
    private readonly Picker languagePicker;
    private readonly Picker formatPicker;

    public TableExportOptionsPage(
        IReadOnlyList<GameAccount> accounts,
        Guid? lockedAccountId)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        Title = "导出表格";

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

        languagePicker = new Picker
        {
            Title = "表格语言",
            ItemsSource = ExportLanguageOption.All,
            SelectedIndex = 0
        };
        formatPicker = new Picker
        {
            Title = "表格格式",
            ItemsSource = TableFormatOption.All,
            SelectedIndex = 0
        };

        var cancelButton = new Button { Text = "取消" };
        cancelButton.Clicked += async (_, _) =>
            await CloseAsync(null);
        var exportButton = new Button { Text = "导出" };
        exportButton.Clicked += async (_, _) =>
            await ConfirmAsync();

        var actions = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            }
        };
        actions.Add(cancelButton);
        actions.Add(exportButton, 1);

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
                        Text = "导出祈愿表格",
                        FontSize = 24,
                        FontAttributes = FontAttributes.Bold
                    },
                    new Label
                    {
                        Text = "同一账号的记录会连续写出，完成后才进入下一个账号。"
                    },
                    new Border
                    {
                        Padding = 12,
                        Content = accountLayout
                    },
                    languagePicker,
                    formatPicker,
                    new Label
                    {
                        FontSize = 12,
                        Text = "列：UID、ExternalID、时间、时区偏移、物品名称、" +
                            "物品类型、卡池、序号、保底内计数。"
                    },
                    actions
                }
            }
        };
    }

    public Task<TableExportDialogResult?> WaitForResultAsync() =>
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
        var format =
            (TableFormatOption)formatPicker.SelectedItem;
        await CloseAsync(new TableExportDialogResult(
            accountIds,
            new GachaTableExportOptions(
                format.Format,
                language.Code)));
    }

    private async Task CloseAsync(TableExportDialogResult? result)
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
