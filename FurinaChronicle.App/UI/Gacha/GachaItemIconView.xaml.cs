// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

namespace FurinaChronicle.App.UI.Gacha;

public partial class GachaItemIconView : ContentView
{
    public static readonly BindableProperty IconSourceProperty =
        BindableProperty.Create(
            nameof(IconSource),
            typeof(ImageSource),
            typeof(GachaItemIconView));

    public static readonly BindableProperty RankTypeProperty =
        BindableProperty.Create(
            nameof(RankType),
            typeof(int?),
            typeof(GachaItemIconView),
            defaultValue: null,
            propertyChanged: OnRankTypeChanged);

    public static readonly BindableProperty ItemNameProperty =
        BindableProperty.Create(
            nameof(ItemName),
            typeof(string),
            typeof(GachaItemIconView),
            defaultValue: string.Empty,
            propertyChanged: OnTextChanged);

    public static readonly BindableProperty DetailTextProperty =
        BindableProperty.Create(
            nameof(DetailText),
            typeof(string),
            typeof(GachaItemIconView),
            defaultValue: string.Empty,
            propertyChanged: OnTextChanged);

    public static readonly BindableProperty CountTextProperty =
        BindableProperty.Create(
            nameof(CountText),
            typeof(string),
            typeof(GachaItemIconView),
            defaultValue: string.Empty,
            propertyChanged: OnTextChanged);

    public static readonly BindableProperty IconSizeProperty =
        BindableProperty.Create(
            nameof(IconSize),
            typeof(double),
            typeof(GachaItemIconView),
            defaultValue: 64d);

    public static readonly BindableProperty CardWidthProperty =
        BindableProperty.Create(
            nameof(CardWidth),
            typeof(double),
            typeof(GachaItemIconView),
            defaultValue: 84d);

    public static readonly BindableProperty ShowItemNameProperty =
        BindableProperty.Create(
            nameof(ShowItemName),
            typeof(bool),
            typeof(GachaItemIconView),
            defaultValue: true,
            propertyChanged: OnTextChanged);

    public static readonly BindableProperty ShowDetailTextProperty =
        BindableProperty.Create(
            nameof(ShowDetailText),
            typeof(bool),
            typeof(GachaItemIconView),
            defaultValue: true,
            propertyChanged: OnTextChanged);

    public GachaItemIconView()
    {
        InitializeComponent();
    }

    public ImageSource? IconSource
    {
        get => (ImageSource?)GetValue(IconSourceProperty);
        set => SetValue(IconSourceProperty, value);
    }

    public int? RankType
    {
        get => (int?)GetValue(RankTypeProperty);
        set => SetValue(RankTypeProperty, value);
    }

    public string ItemName
    {
        get => (string)GetValue(ItemNameProperty);
        set => SetValue(ItemNameProperty, value);
    }

    public string DetailText
    {
        get => (string)GetValue(DetailTextProperty);
        set => SetValue(DetailTextProperty, value);
    }

    public string CountText
    {
        get => (string)GetValue(CountTextProperty);
        set => SetValue(CountTextProperty, value);
    }

    public double IconSize
    {
        get => (double)GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    public double CardWidth
    {
        get => (double)GetValue(CardWidthProperty);
        set => SetValue(CardWidthProperty, value);
    }

    public bool ShowItemName
    {
        get => (bool)GetValue(ShowItemNameProperty);
        set => SetValue(ShowItemNameProperty, value);
    }

    public bool ShowDetailText
    {
        get => (bool)GetValue(ShowDetailTextProperty);
        set => SetValue(ShowDetailTextProperty, value);
    }

    public ImageSource RarityBackgroundSource => ImageSource.FromFile(
        RankType switch
        {
            5 => "gacha_quality_5.png",
            4 => "gacha_quality_4.png",
            _ => "gacha_quality_3.png"
        });

    public bool HasCount => !string.IsNullOrWhiteSpace(CountText);

    public bool HasItemName =>
        ShowItemName && !string.IsNullOrWhiteSpace(ItemName);

    public bool HasDetailText =>
        ShowDetailText && !string.IsNullOrWhiteSpace(DetailText);

    private static void OnRankTypeChanged(
        BindableObject bindable,
        object oldValue,
        object newValue)
    {
        ((GachaItemIconView)bindable).OnPropertyChanged(
            nameof(RarityBackgroundSource));
    }

    private static void OnTextChanged(
        BindableObject bindable,
        object oldValue,
        object newValue)
    {
        var view = (GachaItemIconView)bindable;
        view.OnPropertyChanged(nameof(HasCount));
        view.OnPropertyChanged(nameof(HasItemName));
        view.OnPropertyChanged(nameof(HasDetailText));
    }
}
