// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

namespace FurinaChronicle.App.UI.Pages;

public partial class GachaPage
{
    private bool dataActionsOpen;

    private async void OnDataActionsClicked(object? sender, EventArgs e)
    {
        if (dataActionsOpen || ViewModel.IsBusy) return;
        dataActionsOpen = true;
        try
        {
            string? group = await DisplayActionSheetAsync("抽卡数据操作", "取消", null,
                "刷新记录", "导入数据", "导出数据", "更多操作");
            switch (group)
            {
                case "刷新记录":
                    var refresh = new List<(string, Func<Task>)>
                    {
                        ("通过登录账号刷新", RefreshFromPassportAsync),
                        ("输入手动链接刷新", RefreshFromLinkAsync)
                    };
                    if (OperatingSystem.IsWindows()) refresh.Insert(1, ("通过网页缓存刷新", RefreshFromWebCacheAsync));
                    await RunDataActionAsync(group, refresh);
                    break;
                case "导入数据":
                    await RunDataActionAsync(group,
                    [
                        ("导入 UIGF 到档案", ImportIntoArchiveAsync),
                        ("导入 UIGF 到所选账号", ImportIntoSelectedAccountAsync),
                        ("从提瓦特小助手导入", ImportFromTeyvatHelperAsync),
                        ("导入 Furina 抽卡数据集", ImportPortableAsync)
                    ]);
                    break;
                case "导出数据":
                    await RunDataActionAsync(group,
                    [
                        ("导出档案 UIGF", ExportArchiveUigfAsync),
                        ("导出所选账号 UIGF", ExportAccountUigfAsync),
                        ("导出档案表格", ExportArchiveTableAsync),
                        ("导出所选账号表格", ExportAccountTableAsync),
                        ("导出档案 Furina 抽卡数据集", ExportPortableAsync)
                    ]);
                    break;
                case "更多操作":
                    await RunDataActionAsync(group, [("清除图标缓存", ClearGachaIconCacheAsync)]);
                    break;
            }
        }
        finally
        {
            dataActionsOpen = false;
        }
    }

    private async Task RunDataActionAsync(string title, IReadOnlyList<(string Label, Func<Task> Execute)> actions)
    {
        string? selected = await DisplayActionSheetAsync(title, "取消", null, actions.Select(action => action.Label).ToArray());
        if (ViewModel.IsBusy) return;
        foreach ((string label, Func<Task> execute) in actions)
        {
            if (label != selected) continue;
            await execute();
            return;
        }
    }

    private async Task ClearGachaIconCacheAsync()
    {
        var command = ViewModel.Analysis.ClearIconCacheCommand;
        if (command.CanExecute(null)) await command.ExecuteAsync(null);
    }
}
