// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Presentation.Gacha;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;

namespace FurinaChronicle.Windows;

public sealed partial class Ui1GachaView
{
    private CollectionViewSource? itemGroups;

    private void RenderItems(GachaReadOnlySnapshot snapshot)
    {
        bool items = snapshot.View == GachaReadOnlyView.Items;
        ItemPanel.Visibility = items ? Visibility.Visible : Visibility.Collapsed;
        ItemScopeText.Text = snapshot.ArchiveAccountIds is not null
            ? "档案物品统计 · 点击物品查看获取信息" : "账号物品 · 点击物品查看获取信息";
        ItemGrid.ItemsSource = null;
        ItemDetails.Content = null;
        ItemDetails.Visibility = Visibility.Collapsed;
        itemGroups = null;
        if (!items) return;
        itemGroups = new CollectionViewSource
        {
            IsSourceGrouped = true,
            ItemsPath = new PropertyPath(nameof(GachaItemGroup.Items)),
            Source = GachaItemGroup.Build(snapshot.Rows),
        };
        ItemGrid.ItemsSource = itemGroups.View;
    }

    private void OpenItemDetails(GachaReadOnlyRow row)
    {
        row.Item?.OpenDetails();
        selectedRow = row;
        DetailTitle.Text = row.Name + " · " + (row.CountText ?? "未知") + " 次";
        DetailBody.Visibility = Visibility.Collapsed;
        ItemDetails.Content = row;
        ItemDetails.Visibility = Visibility.Visible;
        UpdateCommandAvailability();
        UpdateLayoutMode();
    }

}
