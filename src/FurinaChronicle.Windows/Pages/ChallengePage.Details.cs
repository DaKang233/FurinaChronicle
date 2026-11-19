// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Globalization;
using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Presentation.History;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace FurinaChronicle.Windows.Pages;

public sealed partial class ChallengePage
{
    private ChallengeObservationView? displayedObservation;
    private int? selectedFloor;
    private bool statisticsOpen;

    private void RenderSelectedDetails()
    {
        DetailContent.Children.Clear();
        if (displayedObservation is not { } view) return;
        if (state.Snapshot.Mode == ChallengeMode.SpiralAbyss) RenderAbyss(view);
        else if (state.Snapshot.Mode == ChallengeMode.ImaginariumTheater) RenderTheater(view);
        else RenderStygian(view);
    }

    private void RenderAbyss(ChallengeObservationView view)
    {
        foreach (var blessing in view.DetailSections.Where(section => section.Kind == ChallengeSectionKind.Blessing))
        {
            var content = Stack(); content.Children.Add(Text(blessing.Title, 18, true));
            AddLines(content, blessing.DisplayLines); DetailContent.Children.Add(Card(content));
        }
        if (FloorTabs.SelectedItem is not ChallengeDetailSection floor)
        {
            DetailContent.Children.Add(Text("本次快照没有已保存的楼层记录。")); return;
        }
        var floorContent = Stack(); floorContent.Children.Add(Text(floor.Title, 17, true));
        // Shared effects and configuration provenance remain visible; per-half effects belong in their columns.
        AddLines(floorContent, floor.DisplayLines);
        var upper = Stack(); upper.Children.Add(Text("上半", 13, true)); AddLines(upper, floor.UpperEffects);
        var lower = Stack(); lower.Children.Add(Text("下半", 13, true)); AddLines(lower, floor.LowerEffects);
        floorContent.Children.Add(Columns(upper, lower));
        foreach (var chamber in floor.Chambers)
        {
            var body = Stack();
            var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
            title.Children.Add(Asset("Abyss/UI_Icon_Tower_Star.png", 18)); title.Children.Add(Text(chamber.Title, 13, true));
            body.Children.Add(title);
            body.Children.Add(Columns(Half(chamber.Upper, "上半"), Half(chamber.Lower, "下半")));
            floorContent.Children.Add(Card(body));
        }
        DetailContent.Children.Add(Card(floorContent));
    }

    private StackPanel Half(IReadOnlyList<ChallengeCombatDetail> battles, string name)
    {
        var body = Stack();
        if (battles.Count == 0) { body.Children.Add(Text(name + "：未保存战斗记录／敌方配置。", 11)); return body; }
        foreach (var battle in battles)
        {
            body.Children.Add(Text(battle.Title, 11, true));
            foreach (var group in battle.Groups) body.Children.Add(PortraitGroup(group, false));
            AddLines(body, battle.DisplayLines);
        }
        return body;
    }

    private void RenderTheater(ChallengeObservationView view)
    {
        var setup = Stack(); setup.Children.Add(Text("本期限制元素", 14, true));
        var restrictions = view.DetailSections.Where(section => section.Kind == ChallengeSectionKind.TheaterSetup).ToArray();
        if (restrictions.Length == 0) setup.Children.Add(Text("本期配置未缓存或来源未提供。", 11));
        else foreach (var section in restrictions) AddLines(setup, section.DisplayLines);
        var rosters = view.DetailSections.Where(section => section.Kind == ChallengeSectionKind.TheaterRoster).ToArray();
        var initial = rosters.FirstOrDefault(section => section.Title.StartsWith("初始角色", StringComparison.Ordinal));
        var guests = rosters.FirstOrDefault(section => section.Title.StartsWith("特邀角色", StringComparison.Ordinal));
        setup.Children.Add(Columns(Roster(initial, "开幕角色"), Roster(guests, "特邀角色")));
        setup.Children.Add(Text("开幕／特邀角色来自期次配置；不表示该账号实际拥有这些角色。", 11));
        DetailContent.Children.Add(Card(setup));

        var summary = Stack();
        var summaryHeader = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        if (view.TheaterHeraldry is >= 0 and <= 5) summaryHeader.Children.Add(Asset($"RoleCombat/UI_RoleCombat_Medal_S_{view.TheaterHeraldry}.png", 40));
        summaryHeader.Children.Add(Text(view.DifficultyText + " · " + view.MedalText, 17, true)); summary.Children.Add(summaryHeader);
        foreach (var section in view.DetailSections.Where(section => section.Kind == ChallengeSectionKind.TheaterSummary)) AddLines(summary, section.DisplayLines);
        foreach (var metric in view.DetailSections.Where(section => section.Kind == ChallengeSectionKind.Metric).SelectMany(section => section.Metrics)) summary.Children.Add(MetricRow(metric));
        foreach (var roster in rosters.Where(section => section.Title == "候补角色")) summary.Children.Add(Roster(roster, "待命角色"));
        DetailContent.Children.Add(Card(summary));

        foreach (var stage in view.DetailSections.Where(section => section.Kind == ChallengeSectionKind.TheaterRound))
        {
            var round = Stack();
            var header = new Grid(); header.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
            if (stage.IsTarot && stage.Award is int tarotAward) label.Children.Add(Asset($"RoleCombat/UI_Icon_RoleCombat_Reward_Tarot_{(tarotAward > 0 ? "04" : "05")}.png", 18));
            else if (stage.Award is int award) label.Children.Add(Asset($"RoleCombat/UI_Icon_Tower_Star{(award > 0 ? 1 : 2)}.png", 18));
            label.Children.Add(Text(stage.Title, 15, true));
            header.Children.Add(label); var at = Text(stage.TimeText ?? "完成时间未知", 11); Grid.SetColumn(at, 1); header.Children.Add(at); round.Children.Add(header);
            var teams = stage.Battles.SelectMany(battle => battle.Groups).ToArray();
            round.Children.Add(Columns(GroupColumn(teams.Where(group => group.Title == "敌人")), GroupColumn(teams.Where(group => group.Title != "敌人"))));
            var blessing = Stack(); blessing.Children.Add(Text("辉彩祝福", 13, true));
            if (!string.IsNullOrWhiteSpace(stage.BlessingText)) blessing.Children.Add(Rich(stage.BlessingText));
            else if (stage.Buffs.Count == 0) AddLines(blessing, stage.DisplayLines);
            blessing.Children.Add(BuffIcons(stage.Buffs.Where(buff => buff.IsSplendour)));
            var choices = Stack(); choices.Children.Add(Text("神秘收获", 13, true)); choices.Children.Add(BuffIcons(stage.Buffs.Where(buff => !buff.IsSplendour)));
            round.Children.Add(Columns(blessing, choices)); DetailContent.Children.Add(Card(round));
        }
    }

    private StackPanel GroupColumn(IEnumerable<ChallengePortraitGroup> groups)
    {
        var body = Stack(); foreach (var group in groups) body.Children.Add(PortraitGroup(group, false)); return body;
    }

    private StackPanel Roster(ChallengeDetailSection? roster, string title)
    {
        var body = Stack(); body.Children.Add(Text(title, 13, true));
        if (roster is null) body.Children.Add(Text("未提供", 11));
        else foreach (var group in roster.Battles.SelectMany(battle => battle.Groups)) body.Children.Add(PortraitGroup(group, false));
        return body;
    }

    private void RenderStygian(ChallengeObservationView view)
    {
        string run = RunPicker.SelectedIndex == 1 ? "联机挑战" : "单人挑战";
        var summary = view.DetailSections.FirstOrDefault(section => section.Kind == ChallengeSectionKind.StygianSummary && section.Run == run);
        var stages = view.DetailSections.Where(section => section.Kind == ChallengeSectionKind.StygianStage && section.Run == run).ToArray();
        if (summary is not null)
        {
            var body = Stack(); var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            if (summary.HeaderArtwork is { } icon) title.Children.Add(Portrait(icon, 38, false));
            title.Children.Add(Text(run, 17, true)); body.Children.Add(title);
            var damage = stages.SelectMany(stage => stage.Metrics).Where(metric => metric.Name == "最高总伤害")
                .Select(metric => long.TryParse(metric.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value) ? (long?)value : null)
                .OfType<long>().ToArray();
            if (summary.Metrics.Count > 0)
            {
                var metrics = summary.Metrics.Concat([new ChallengeMetric("最高总伤害（本次记录）", damage.Length > 0 ? damage.Max().ToString("N0") : "未知")]);
                body.Children.Add(Columns(metrics.Select(metric =>
                {
                    var tile = Stack(); tile.Children.Add(Text(metric.Name, 11)); tile.Children.Add(Text(metric.Value, 18, true));
                    return (FrameworkElement)Card(tile);
                }).ToArray()));
                body.Children.Add(Text("发生时间未知；按采集快照查看。", 11));
            }
            else AddLines(body, summary.DisplayLines);
            DetailContent.Children.Add(Card(body));
        }
        if (stages.Length == 0) DetailContent.Children.Add(Text("暂无" + run + "关卡记录。"));
        foreach (var stage in stages)
        {
            var body = Stack();
            var enemyGroups = stage.Battles.SelectMany(battle => battle.Groups).Where(group => group.Title == "敌人").ToArray();
            var title = Stack(); title.Children.Add(Text(stage.Heading ?? stage.Title, 17, true)); title.Children.Add(Text("战斗用时 · " + stage.TimeText, 12));
            var heading = new Grid { ColumnSpacing = 10 };
            heading.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); heading.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            if (enemyGroups.SelectMany(group => group.Portraits).FirstOrDefault() is { } enemy) heading.Children.Add(Portrait(enemy, 48, false));
            Grid.SetColumn(title, 1); heading.Children.Add(title); body.Children.Add(heading);
            AddLines(body, stage.DisplayLines.Where(line => line != stage.TimeText));
            foreach (var group in stage.Battles.SelectMany(battle => battle.Groups).Where(group => group.Title == "阵容")) body.Children.Add(PortraitGroup(group, true));
            if (stage.Metrics.Count > 0)
            {
                body.Children.Add(Text("最佳角色", 12, true));
                body.Children.Add(Columns(stage.Metrics.Select(metric =>
                {
                    var performance = new Grid { ColumnSpacing = 8 };
                    performance.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); performance.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
                    if (metric.Portrait is { } side) performance.Children.Add(Portrait(side with { IsSidePortrait = true }, 40, false));
                    var value = Stack(); value.Children.Add(Text(metric.Name, 11)); value.Children.Add(Text(metric.Value, 13, true)); Grid.SetColumn(value, 1); performance.Children.Add(value);
                    return (FrameworkElement)performance;
                }).ToArray()));
            }
            DetailContent.Children.Add(Card(body));
        }
        foreach (var section in view.DetailSections.Where(section => section.Kind == ChallengeSectionKind.Other))
        {
            var body = Stack(); body.Children.Add(Text(section.Title, 14, true)); AddLines(body, section.DisplayLines);
            foreach (var group in section.Battles.SelectMany(battle => battle.Groups)) body.Children.Add(PortraitGroup(group, true));
            DetailContent.Children.Add(Card(body));
        }
    }

    private async void OnStatistics(object sender, RoutedEventArgs args)
    {
        if (statisticsOpen || displayedObservation is not { } view) return;
        statisticsOpen = true;
        try
        {
            var left = Stack(); var right = Stack();
            foreach (var section in view.DetailSections.Where(section => section.Kind == ChallengeSectionKind.Metric))
            {
                if (section.Title == "出战次数")
                {
                    right.Children.Add(Text(section.Title, 13, true));
                    var avatars = new ChallengeWrapPanel { Spacing = 5 };
                    foreach (var metric in section.Metrics)
                    {
                        var child = Stack(); if (metric.Portrait is { } portrait) child.Children.Add(Portrait(portrait, 38, false)); child.Children.Add(Text(metric.Value, 12)); avatars.Children.Add(child);
                    }
                    right.Children.Add(avatars);
                }
                else if (section.Metrics.FirstOrDefault() is { } first) left.Children.Add(MetricRow(first));
            }
            foreach (var section in view.DetailSections.Where(section => section.Kind == ChallengeSectionKind.Summary))
                foreach (string line in section.DisplayLines) right.Children.Insert(0, Card(Text(line, 13)));
            var layout = Columns(left, right); layout.MinWidth = 440;
            var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "本期统计数据", CloseButtonText = "关闭",
                Content = new ScrollViewer { Content = layout, MaxHeight = 600, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
            AutomationProperties.SetAutomationId(dialog, "ChallengeStatisticsDialog");
            await dialog.ShowAsync();
        }
        finally { statisticsOpen = false; }
    }

    private void OnFloorChanged(object sender, SelectionChangedEventArgs args)
    {
        if (rendering) return;
        selectedFloor = (FloorTabs.SelectedItem as ChallengeDetailSection)?.Number;
        artwork.Reset(); RenderSelectedDetails(); DetailScroll.ChangeView(null, 0, null, true);
    }

    private void OnRunChanged(object sender, SelectionChangedEventArgs args)
    {
        if (rendering || DetailContent is null) return;
        artwork.Reset(); RenderSelectedDetails(); DetailScroll.ChangeView(null, 0, null, true);
    }

    private FrameworkElement BuffIcons(IEnumerable<ChallengeBuffDetail> buffs)
    {
        var panel = new ChallengeWrapPanel { Spacing = 5 };
        foreach (var buff in buffs)
        {
            var picture = new Grid { Width = 46, Height = 46, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
            picture.Children.Add(Portrait(buff.Artwork, 44, false, showTooltip: false));
            var icon = new Button { Width = 48, Height = 48, Padding = new(0), BorderThickness = new(0),
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), Content = picture };
            if (buff.Level is int level)
                picture.Children.Add(new Border { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
                    Background = new SolidColorBrush(Microsoft.UI.Colors.DeepSkyBlue), CornerRadius = new(9), Padding = new(4, 0, 4, 0), Child = Text(level.ToString(), 11, true) });
            StackPanel Description()
            {
                var content = Stack(); content.MaxWidth = 380;
                content.Children.Add(Text(buff.Title, 13, true)); content.Children.Add(Rich(buff.RichDetail ?? buff.Detail));
                return content;
            }
            ToolTipService.SetToolTip(icon, new ToolTip { Content = new ScrollViewer { Content = Description(), MaxHeight = 480, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
            // Clicking/keyboard activation also pins the same explanation for touch and accessibility.
            icon.Flyout = new Flyout { Content = new ScrollViewer { Content = Description(), MaxHeight = 480, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
            AutomationProperties.SetName(icon, buff.Title); panel.Children.Add(icon);
        }
        return panel;
    }

    private FrameworkElement PortraitGroup(ChallengePortraitGroup group, bool labels)
    {
        var body = Stack(); if (group.HasTitle) body.Children.Add(Text(group.Title, 12, true));
        var portraits = new ChallengeWrapPanel { Spacing = 5 };
        foreach (var portrait in group.Portraits) portraits.Children.Add(Portrait(portrait, labels ? 60 : 44, labels));
        body.Children.Add(portraits); return body;
    }

    private FrameworkElement Portrait(ChallengePortrait portrait, double size, bool label, bool showTooltip = true)
    {
        var body = new StackPanel { Width = size + (label ? 8 : 0), Spacing = 3 };
        var imageBody = new Grid { Width = size, Height = size, CornerRadius = new(5) };
        if (portrait.UseRarityBackground)
        {
            if (new GachaRarityBackgroundConverter().Convert(portrait.Rarity!, typeof(ImageSource), null!, "") is ImageSource background)
                imageBody.Children.Add(new Image { Source = background, Stretch = Stretch.UniformToFill });
            imageBody.Children.Add(new Image { Source = new BitmapImage(new Uri("ms-appx:///Assets/ItemRarity/ui_imgsign_itemicon.png")), Stretch = Stretch.UniformToFill });
        }
        var view = artwork.Get(portrait);
        var fallback = Text(portrait.ShortName ?? "暂无图", 9); fallback.TextAlignment = TextAlignment.Center; fallback.MaxLines = 2;
        fallback.SetBinding(UIElement.VisibilityProperty, new Binding { Source = view, Path = new PropertyPath(nameof(ChallengeArtworkView.MissingVisibility)) });
        imageBody.Children.Add(fallback);
        var image = new Image { Stretch = Stretch.Uniform };
        image.SetBinding(Image.SourceProperty, new Binding { Source = view, Path = new PropertyPath(nameof(ChallengeArtworkView.Source)) });
        AutomationProperties.SetName(image, "挑战图片：" + portrait.Label); imageBody.Children.Add(image);
        if (portrait.UseRarityBackground && portrait.LevelText is { } level)
            imageBody.Children.Add(new TextBlock { Text = level, FontSize = 10, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White) });
        if (label && portrait.UseRarityBackground && portrait.Rarity is int rarity)
            imageBody.Children.Add(new Border { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                Background = new SolidColorBrush(Microsoft.UI.Colors.Black), CornerRadius = new(4), Padding = new(2, 0, 2, 0), Child = Text("★" + rarity, 10) });
        if (portrait.Badge is { } badge)
            imageBody.Children.Add(new Border { Background = new SolidColorBrush(Microsoft.UI.Colors.DimGray), Padding = new(2, 0, 2, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Child = Text(badge, 10) });
        body.Children.Add(imageBody);
        if (label) { var name = Text(portrait.ShortName ?? portrait.Label, 10); name.TextAlignment = TextAlignment.Center; body.Children.Add(name); }
        if (showTooltip) ToolTipService.SetToolTip(body, portrait.Label);
        AutomationProperties.SetName(body, portrait.Label);
        return body;
    }

    private FrameworkElement MetricRow(ChallengeMetric metric)
    {
        var row = new Grid { ColumnSpacing = 7, MinHeight = 38 };
        row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.Children.Add(Text(metric.Name, 12, true)); var value = Text(metric.Value, 13); value.TextAlignment = TextAlignment.Right; Grid.SetColumn(value, 1); row.Children.Add(value);
        if (metric.Portrait is { } portrait) { var picture = Portrait(portrait with { IsSidePortrait = true }, 32, false); Grid.SetColumn(picture, 2); row.Children.Add(picture); }
        return Card(row, 8);
    }

    private static StackPanel Stack() => new() { Spacing = 7 };
    private static TextBlock Text(string value, double size = 12, bool bold = false) => new()
    {
        Text = value, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center,
    };
    private static TextBlock Rich(string value)
    {
        var text = Text("", 12); text.IsTextSelectionEnabled = true;
        foreach (var part in HoYoRichText.Parse(value))
        {
            var run = new Run { Text = part.Text, FontWeight = part.Bold ? FontWeights.Bold : FontWeights.Normal,
                FontStyle = part.Italic ? global::Windows.UI.Text.FontStyle.Italic : global::Windows.UI.Text.FontStyle.Normal };
            if (part.Size is int size) run.FontSize = size;
            if (part.Underline) run.TextDecorations = global::Windows.UI.Text.TextDecorations.Underline;
            if (part.Color is { } color)
            {
                byte R(int offset) => byte.Parse(color.AsSpan(offset, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                run.Foreground = new SolidColorBrush(global::Windows.UI.Color.FromArgb(color.Length == 9 ? R(7) : (byte)255, R(1), R(3), R(5)));
            }
            text.Inlines.Add(run);
        }
        return text;
    }
    private static void AddLines(StackPanel panel, IEnumerable<string> lines) { foreach (string line in lines) panel.Children.Add(Rich(line)); }
    private Border Card(UIElement body, double padding = 10) => new()
    {
        Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
        BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"], BorderThickness = new(1), CornerRadius = new(8), Padding = new(padding), Child = body,
    };
    private static Grid Columns(params FrameworkElement[] children)
    {
        var grid = new Grid { ColumnSpacing = 12 };
        for (int i = 0; i < children.Length; i++) { grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); Grid.SetColumn(children[i], i); grid.Children.Add(children[i]); }
        return grid;
    }
    private static Image Asset(string path, double size) => new() { Source = new BitmapImage(new Uri("ms-appx:///Assets/Challenge/" + path)), Width = size, Height = size, Stretch = Stretch.Uniform };
}

internal sealed class ChallengeWrapPanel : Panel
{
    public double Spacing { get; init; } = 5;
    protected override global::Windows.Foundation.Size MeasureOverride(global::Windows.Foundation.Size availableSize)
    {
        foreach (UIElement child in Children) child.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
        return Layout(availableSize.Width, false);
    }
    protected override global::Windows.Foundation.Size ArrangeOverride(global::Windows.Foundation.Size finalSize)
    { Layout(finalSize.Width, true); return finalSize; }
    private global::Windows.Foundation.Size Layout(double limit, bool arrange)
    {
        double x = 0, y = 0, rowHeight = 0, width = 0;
        foreach (UIElement child in Children)
        {
            var size = child.DesiredSize;
            if (x > 0 && x + size.Width > limit) { y += rowHeight + Spacing; x = 0; rowHeight = 0; }
            if (arrange) child.Arrange(new(x, y, size.Width, size.Height));
            x += size.Width + Spacing; width = Math.Max(width, x - Spacing); rowHeight = Math.Max(rowHeight, size.Height);
        }
        return new(width, y + rowHeight);
    }
}
