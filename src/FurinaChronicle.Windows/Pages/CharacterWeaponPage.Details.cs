// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Globalization;
using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Presentation.History;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;

namespace FurinaChronicle.Windows.Pages;

public sealed partial class CharacterWeaponPage
{
    private sealed record CharacterChoice(InventoryStateEntry Entry, string Label);
    private ItemStateObservation? displayedCharacterObservation;
    private CancellationTokenSource? metadataRequest;

    private void RenderCharacterChoices(ItemStateObservation? observation)
    {
        if (observation?.Kind != InventoryItemKind.Character)
            observation = null;
        DetailBodyText.Visibility = observation is null ? Visibility.Visible : Visibility.Collapsed;
        CharacterList.Visibility = observation is null ? Visibility.Collapsed : Visibility.Visible;
        if (ReferenceEquals(observation, displayedCharacterObservation))
            return;

        string? selectedId = (CharacterList.SelectedItem as CharacterChoice)?.Entry.TypeId;
        displayedCharacterObservation = observation;
        var choices = observation is null ? [] : CharacterDetailPresentation.Characters(observation)
            .Select(entry => new CharacterChoice(entry, $"{CharacterDetailPresentation.Name(entry)}  {CharacterDetailPresentation.Level(entry.Level)}"))
            .ToArray();
        CharacterList.ItemsSource = choices;
        CharacterList.SelectedItem = choices.FirstOrDefault(choice => choice.Entry.TypeId == selectedId) ??
            choices.FirstOrDefault();
        if (choices.Length == 0) SelectCharacterProfile();
    }

    private void OnCharacterChanged(object sender, SelectionChangedEventArgs args) => SelectCharacterProfile();

    private void CancelCharacterMetadata() => metadataRequest?.Cancel();

    private async void SelectCharacterProfile()
    {
        CancelCharacterMetadata();
        RenderCharacterProfile();
        if (CharacterList.SelectedItem is not CharacterChoice choice || choice.Entry.CharacterDetail is null)
            return;
        using var request = new CancellationTokenSource();
        metadataRequest = request;
        try
        {
            var enriched = await CharacterDetailPresentation.WithMetadataAsync(choice.Entry, characterMetadata, request.Token);
            if (ReferenceEquals(metadataRequest, request) && !request.IsCancellationRequested)
                RenderCharacterProfile(enriched);
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception)
        {
            // The saved official detail is already rendered and remains available.
        }
        finally
        {
            if (ReferenceEquals(metadataRequest, request)) metadataRequest = null;
        }
    }

    private void RenderCharacterProfile(CharacterDetail? enriched = null)
    {
        for (int i = CharacterDetailStack.Children.Count - 1; i > 0; i--)
            CharacterDetailStack.Children.RemoveAt(i);
        if (displayedCharacterObservation is not { } observation ||
            CharacterList.SelectedItem is not CharacterChoice choice)
            return;

        var entry = choice.Entry;
        var detail = enriched ?? entry.CharacterDetail;
        var title = new Grid { ColumnSpacing = 8 };
        title.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        title.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var name = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        if (CharacterDetailPresentation.ElementIcon(detail?.Element) is { } element)
            name.Children.Add(CharacterAsset($"ui_gcg_buff_common_element_{element}.png", 24));
        name.Children.Add(CharacterText(detail?.Name ?? CharacterDetailPresentation.Name(entry), 26, true));
        name.Children.Add(CharacterText(CharacterDetailPresentation.Level(entry.Level), 16));
        title.Children.Add(name);
        var rarity = CharacterText(CharacterDetailPresentation.Rarity(detail?.Rarity), 14);
        rarity.Foreground = CharacterColor(0xE7, 0xAA, 0x36);
        Grid.SetColumn(rarity, 1);
        title.Children.Add(rarity);
        CharacterDetailStack.Children.Add(title);

        var progression = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        progression.Children.Add(CharacterText(CharacterDetailPresentation.Ascension(detail?.Ascension), 16));
        progression.Children.Add(CharacterAsset("ui_icon_fetter.png", 18));
        progression.Children.Add(CharacterText(detail?.Friendship is int value ? $"好感度 {value}" : "好感度未知"));
        CharacterDetailStack.Children.Add(progression);

        var actions = new ChallengeWrapPanel { Spacing = 6 };
        actions.Children.Add(ClipboardAction("复制当前角色", () => CharacterDetailPresentation.Export(observation, entry)));
        actions.Children.Add(ClipboardAction("复制此快照全部角色", () => CharacterDetailPresentation.Export(observation)));
        CharacterDetailStack.Children.Add(actions);
        if (detail is null)
        {
            CharacterDetailStack.Children.Add(CharacterText(entry.Progression is int count
                ? $"命座 {count} · 此快照仅保存角色概况" : "此快照仅保存角色概况"));
            return;
        }

        var constellations = new ChallengeWrapPanel();
        foreach (var item in (detail.Constellations ?? []).OrderBy(item => item.Position))
            constellations.Children.Add(SkillButton(item.Name ?? $"命座 {item.Position}", item.Icon,
                item.Unlocked, item.Description, null, item.Position is int pos ? $"C{pos}" : null));
        if (constellations.Children.Count > 0)
            CharacterDetailStack.Children.Add(CharacterCard("命座", constellations));

        var talents = new ChallengeWrapPanel();
        foreach (var item in detail.Talents ?? [])
            talents.Children.Add(SkillButton(item.Name ?? $"天赋 {item.Id}", item.Icon, item.Unlocked,
                item.Description, item.Attributes, item.Level is int level ? $"Lv.{level}" : null));
        if (talents.Children.Count > 0)
            CharacterDetailStack.Children.Add(CharacterCard("天赋", talents));

        CharacterDetailStack.Children.Add(BuildAttributeCard(detail, observation.CharacterMetadata));
        if (detail.Weapon is { } weapon)
        {
            var body = new StackPanel { Spacing = 4 };
            body.Children.Add(CharacterText(weapon.Name ?? $"武器 {weapon.Id}", 16, true));
            body.Children.Add(CharacterText($"{CharacterDetailPresentation.Level(weapon.Level)}  ·  精炼 {weapon.Refinement?.ToString() ?? "未知"}"));
            body.Children.Add(CharacterText(CharacterDetailPresentation.Ascension(weapon.Ascension)));
            foreach (var stat in new[] { weapon.MainStat, weapon.SecondaryStat }.OfType<CharacterStat>())
                body.Children.Add(StatRow(CharacterDetailPresentation.Project(stat, observation.CharacterMetadata)));
            CharacterDetailStack.Children.Add(CharacterCard("武器", WithIcon(CharacterIcon(weapon.Icon, weapon.Rarity, 74), body)));
        }
        if (detail.Artifacts is { Count: > 0 } artifacts)
        {
            var artifactGrid = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
            for (int i = 0; i < 2; i++) artifactGrid.ColumnDefinitions.Add(new());
            foreach (var (artifact, index) in artifacts.OrderBy(item => item.Slot).Select((item, index) => (item, index)))
            {
                if (index % 2 == 0) artifactGrid.RowDefinitions.Add(new() { Height = GridLength.Auto });
                var body = new StackPanel { Spacing = 3 };
                string subheading = string.Join(" · ", new[] { artifact.Set?.Name, artifact.SlotName, artifact.Level is int level ? $"＋{level}" : null }.OfType<string>());
                body.Children.Add(CharacterText(subheading, 12));
                if (artifact.MainStat is { } main)
                    body.Children.Add(ArtifactStat(main, observation.CharacterMetadata, bold: true));
                foreach (var sub in artifact.SubStats ?? [])
                    body.Children.Add(ArtifactStat(sub, observation.CharacterMetadata, bold: false));
                var card = CharacterCard(artifact.Name ?? $"圣遗物 {artifact.Id}", WithIcon(CharacterIcon(artifact.Icon, artifact.Rarity, 52), body));
                Grid.SetColumn(card, index % 2);
                Grid.SetRow(card, index / 2);
                artifactGrid.Children.Add(card);
            }
            CharacterDetailStack.Children.Add(CharacterCard("圣遗物", artifactGrid));
        }
        CharacterDetailScroll.ChangeView(null, 0, null, true);
    }

    private static FrameworkElement BuildAttributeCard(CharacterDetail detail, CharacterResponseMetadata? metadata)
    {
        var body = new StackPanel { Spacing = 8 };
        var overview = CharacterDetailPresentation.Overview(detail, metadata);
        var grid = new Grid { ColumnSpacing = 14 };
        for (int i = 0; i < 2; i++) grid.ColumnDefinitions.Add(new());
        for (int i = 0; i < 5; i++) grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        for (int i = 0; i < overview.Count; i++)
        {
            var row = StatRow(overview[i]);
            Grid.SetColumn(row, i / 5);
            Grid.SetRow(row, i % 5);
            grid.Children.Add(row);
        }
        body.Children.Add(grid);
        var sections = new StackPanel { Spacing = 6 };
        foreach (var (name, stats) in CharacterDetailPresentation.AllGroups(detail))
        {
            sections.Children.Add(CharacterText(name, 14, true));
            foreach (var stat in stats)
                sections.Children.Add(StatRow(CharacterDetailPresentation.Project(stat, metadata)));
        }
        body.Children.Add(new Expander { Header = "全部属性", Content = sections, HorizontalAlignment = HorizontalAlignment.Stretch });
        return CharacterCard("角色属性", body);
    }

    private static FrameworkElement StatRow(CharacterStatView stat)
    {
        var grid = new Grid { ColumnSpacing = 6, MinHeight = 30 };
        grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        if (OfficialCharacterImage(stat.Icon, 15) is { } picture) grid.Children.Add(picture);
        var label = CharacterText(stat.Name, 13);
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);
        var value = CharacterText("", 13);
        if (stat.Base is { } basic && stat.SignedBonus is { } bonus)
        {
            value.Inlines.Add(new Run { Text = basic });
            value.Inlines.Add(new Run { Text = bonus, Foreground = CharacterColor(0x76, 0xB8, 0x08) });
            value.Inlines.Add(new Run { Text = "　" });
        }
        value.Inlines.Add(new Run { Text = stat.Total, FontWeight = FontWeights.SemiBold });
        Grid.SetColumn(value, 2);
        grid.Children.Add(value);
        return grid;
    }

    private static FrameworkElement ArtifactStat(CharacterArtifactStat stat, CharacterResponseMetadata? metadata, bool bold)
        => CharacterText($"{CharacterDetailPresentation.StatName(stat.PropertyId, metadata)}  {stat.Value ?? "—"}" +
            (stat.Hits is int hits && !bold ? $"  ×{hits}" : ""), 13, bold);

    private static Button SkillButton(string name, string? icon, bool? unlocked, string? description,
        IReadOnlyList<CharacterTalentValue>? attributes, string? badge)
    {
        var picture = new Grid { Width = 48, Height = 48 };
        if (OfficialCharacterImage(icon, 40) is { } image)
            picture.Children.Add(image);
        else
            picture.Children.Add(CharacterText(badge ?? "详", 14, true));
        if (unlocked == false)
        {
            var locked = CharacterAsset("ui_icon_locked.png", 16);
            locked.HorizontalAlignment = HorizontalAlignment.Right;
            locked.VerticalAlignment = VerticalAlignment.Bottom;
            picture.Children.Add(locked);
        }
        var button = new Button { Content = picture, Padding = new(2) };
        AutomationProperties.SetName(button, unlocked == false ? name + "（未解锁）" : name);
        ToolTipService.SetToolTip(button, name);
        var mechanics = new StackPanel { Spacing = 6, MaxWidth = 380 };
        mechanics.Children.Add(CharacterText(name + (badge is null ? "" : " · " + badge), 16, true));
        mechanics.Children.Add(CharacterRichText(description ?? "暂无机制说明"));
        if (attributes is { Count: > 0 })
        {
            var stats = new StackPanel { Spacing = 4 };
            foreach (var stat in attributes)
                stats.Children.Add(CharacterRichText($"{stat.Name}  {stat.Value}"));
            mechanics.Children.Add(new Expander { Header = "详细属性", Content = stats, HorizontalAlignment = HorizontalAlignment.Stretch });
        }
        button.Flyout = new Flyout { Content = new ScrollViewer
        {
            MaxHeight = 480,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = mechanics,
        } };
        return button;
    }

    private Button ClipboardAction(string label, Func<string> content)
    {
        var button = new Button { Content = label };
        button.Click += (_, _) =>
        {
            try
            {
                var package = new DataPackage();
                package.SetText(content());
                Clipboard.SetContent(package);
                button.Content = "已复制";
            }
            catch (Exception)
            {
                button.Content = "复制失败，请重试";
            }
        };
        return button;
    }

    private static Grid WithIcon(FrameworkElement icon, FrameworkElement body)
    {
        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        grid.Children.Add(body);
        Grid.SetColumn(icon, 1);
        grid.Children.Add(icon);
        return grid;
    }

    private static FrameworkElement CharacterIcon(string? icon, int? rarity, double size)
    {
        var tile = new Grid { Width = size, Height = size, CornerRadius = new(6), VerticalAlignment = VerticalAlignment.Top };
        if (rarity is int quality &&
            new GachaRarityBackgroundConverter().Convert(quality, typeof(ImageSource), null!, "") is ImageSource background)
        {
            tile.Children.Add(new Image { Source = background, Stretch = Stretch.UniformToFill });
            tile.Children.Add(new Image { Source = new BitmapImage(new Uri("ms-appx:///Assets/ItemRarity/ui_imgsign_itemicon.png")) });
        }
        if (OfficialCharacterImage(icon, size) is { } image) tile.Children.Add(image);
        return tile;
    }

    private static Image? OfficialCharacterImage(string? url, double size) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
        uri.UserInfo.Length == 0 && uri.IsDefaultPort &&
        (uri.Host == "enka.network" || uri.Host.EndsWith(".mihoyo.com", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith(".hoyolab.com", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith(".hoyoverse.com", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith(".yuanshen.com", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith(".miyoushe.com", StringComparison.OrdinalIgnoreCase))
            ? new Image { Source = new BitmapImage(uri), Width = size, Height = size, Stretch = Stretch.Uniform }
            : null;

    private static Image CharacterAsset(string asset, double size) => new()
    {
        Source = new BitmapImage(new Uri("ms-appx:///Assets/Character/" + asset)),
        Width = size, Height = size, Stretch = Stretch.Uniform,
    };

    private static Border CharacterCard(string title, FrameworkElement content)
    {
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(CharacterText(title, 16, true));
        body.Children.Add(content);
        return new Border
        {
            Child = body, Padding = new(10), CornerRadius = new(8), BorderThickness = new(1),
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
        };
    }

    private static TextBlock CharacterText(string text, double size = 14, bool bold = false) => new()
    {
        Text = text, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap,
    };

    private static TextBlock CharacterRichText(string value)
    {
        var block = CharacterText("");
        foreach (var token in HoYoRichText.Parse(value))
        {
            var run = new Run { Text = token.Text, FontWeight = token.Bold ? FontWeights.Bold : FontWeights.Normal };
            if (token.Italic) run.FontStyle = global::Windows.UI.Text.FontStyle.Italic;
            if (token.Underline) run.TextDecorations = global::Windows.UI.Text.TextDecorations.Underline;
            if (token.Size is int size) run.FontSize = size;
            if (token.Color is { Length: >= 7 } color &&
                uint.TryParse(color.AsSpan(1, 6), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb))
                run.Foreground = CharacterColor((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
            block.Inlines.Add(run);
        }
        return block;
    }

    private static SolidColorBrush CharacterColor(byte red, byte green, byte blue) =>
        new(global::Windows.UI.Color.FromArgb(255, red, green, blue));
}
