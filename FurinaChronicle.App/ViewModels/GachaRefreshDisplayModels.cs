// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using CommunityToolkit.Mvvm.ComponentModel;

namespace FurinaChronicle.App.ViewModels;

public sealed partial class GachaRefreshItemDisplayItem(
    string key,
    string itemName,
    int? rankType,
    int count) : ObservableObject
{
    public string Key { get; } = key;

    public string ItemName { get; } = itemName;

    public int? RankType { get; } = rankType;

    [ObservableProperty]
    public partial ImageSource? IconSource { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText))]
    public partial int Count { get; set; } = count;

    public string CountText => $"× {Count}";
}
