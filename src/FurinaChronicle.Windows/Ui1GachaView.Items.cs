// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Presentation.Gacha;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;

namespace FurinaChronicle.Windows;

public sealed partial class Ui1GachaView
{
    private bool itemListMode;
    private CollectionViewSource? itemGroups;

    private void RenderItems(GachaReadOnlySnapshot snapshot)
    {
        bool items = snapshot.View == GachaReadOnlyView.Items;
        bool archive = snapshot.ArchiveAccountIds is not null;
        if (archive) itemListMode = false;
        ItemPanel.Visibility = items ? Visibility.Visible : Visibility.Collapsed;
        ItemLayoutButton.Visibility = !archive ? Visibility.Visible : Visibility.Collapsed;
        ItemLayoutButton.Content = itemListMode ? "图标网格" : "列表与时间";
        ItemLayoutButton.IsEnabled = !snapshot.IsBusy;
        ItemScopeText.Text = archive ? "档案物品统计 · 仅统计抽卡所得" : "账号物品 · 仅统计抽卡所得";
        ItemGrid.ItemsSource = null;
        ItemList.ItemsSource = null;
        itemGroups = null;
        if (!items) return;
        itemGroups = new CollectionViewSource
        {
            IsSourceGrouped = true,
            ItemsPath = new PropertyPath(nameof(GachaItemGroup.Items)),
            Source = GachaItemGroup.Build(snapshot.Rows),
        };
        ItemGrid.Visibility = itemListMode ? Visibility.Collapsed : Visibility.Visible;
        ItemList.Visibility = itemListMode ? Visibility.Visible : Visibility.Collapsed;
        if (itemListMode) ItemList.ItemsSource = itemGroups.View;
        else ItemGrid.ItemsSource = itemGroups.View;
    }

    private void OnItemLayoutChanged(object sender, RoutedEventArgs args)
    {
        if (state is null || state.Snapshot.IsBusy || state.Snapshot.ArchiveAccountIds is not null) return;
        itemListMode = !itemListMode;
        RenderItems(state.Snapshot);
        UpdateLayoutMode();
    }

    private void OnToggleItemTimes(object sender, RoutedEventArgs args)
    {
        if (sender is FrameworkElement { DataContext: GachaReadOnlyRow { Item: { } item } }) item.ToggleTimes();
    }

    private void OnMoreItemTimes(object sender, RoutedEventArgs args)
    {
        if (sender is FrameworkElement { DataContext: GachaReadOnlyRow { Item: { } item } }) item.LoadMoreTimes();
    }
}
