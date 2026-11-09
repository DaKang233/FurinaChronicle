// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

namespace FurinaChronicle.App.UI.Gacha;

public partial class HistoryPoolDetailView : ContentView
{
    public static readonly BindableProperty IsIconModeProperty =
        BindableProperty.Create(
            nameof(IsIconMode),
            typeof(bool),
            typeof(HistoryPoolDetailView),
            true);

    public static readonly BindableProperty IsTextModeProperty =
        BindableProperty.Create(
            nameof(IsTextMode),
            typeof(bool),
            typeof(HistoryPoolDetailView),
            false);

    public bool IsIconMode
    {
        get => (bool)GetValue(IsIconModeProperty);
        set => SetValue(IsIconModeProperty, value);
    }

    public bool IsTextMode
    {
        get => (bool)GetValue(IsTextModeProperty);
        set => SetValue(IsTextModeProperty, value);
    }

    public HistoryPoolDetailView()
    {
        InitializeComponent();
    }
}
