// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.App.UI.Gacha;
using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Presentation.History;
using Microsoft.Maui.Controls.Shapes;

namespace FurinaChronicle.App.UI.Pages;

public partial class CharacterHistoryPage
{
    private ItemStateObservation? profileObservation;
    private IReadOnlyList<InventoryStateEntry> profileEntries = [];
    private CancellationTokenSource? profileMetadataRequest;

    private void RenderProfilePicker(ItemStateObservation? observation)
    {
        if (observation?.Kind != InventoryItemKind.Character)
            observation = null;
        ObservationDetailLabel.IsVisible = observation is null;
        HistoryList.IsVisible = observation is null;
        ProfileScroll.IsVisible = ProfilePicker.IsVisible = observation is not null;
        if (ReferenceEquals(profileObservation, observation)) return;

        CloseCharacterSheet();
        string? previous = SelectedProfile()?.TypeId;
        profileObservation = observation;
        profileEntries = observation is null ? [] : CharacterDetailPresentation.Characters(observation);
        ProfilePicker.ItemsSource = profileEntries.Select(entry =>
            $"{CharacterDetailPresentation.Name(entry)} · {CharacterDetailPresentation.Level(entry.Level)}").ToArray();
        int index = Array.FindIndex(profileEntries.ToArray(), entry => entry.TypeId == previous);
        ProfilePicker.SelectedIndex = index >= 0 ? index : profileEntries.Count > 0 ? 0 : -1;
        SelectProfile();
    }

    private InventoryStateEntry? SelectedProfile() => ProfilePicker.SelectedIndex is >= 0 &&
        ProfilePicker.SelectedIndex < profileEntries.Count ? profileEntries[ProfilePicker.SelectedIndex] : null;

    private void OnProfileChanged(object? sender, EventArgs args) => SelectProfile();

    private async void SelectProfile()
    {
        profileMetadataRequest?.Cancel();
        RenderProfile();
        if (SelectedProfile() is not { CharacterDetail: not null } entry) return;
        using var request = new CancellationTokenSource();
        profileMetadataRequest = request;
        try
        {
            var enriched = await CharacterDetailPresentation.WithMetadataAsync(entry, metadata, request.Token);
            if (ReferenceEquals(profileMetadataRequest, request) && !request.IsCancellationRequested)
                RenderProfile(enriched);
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception)
        {
            // Metadata is optional and the official fields are already shown.
        }
        finally
        {
            if (ReferenceEquals(profileMetadataRequest, request)) profileMetadataRequest = null;
        }
    }

    private void RenderProfile(CharacterDetail? enriched = null)
    {
        ProfileContent.Clear();
        if (profileObservation is not { } observation || SelectedProfile() is not { } entry) return;
        var detail = enriched ?? entry.CharacterDetail;

        var head = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) } };
        var name = new HorizontalStackLayout { Spacing = 5 };
        if (CharacterDetailPresentation.ElementIcon(detail?.Element) is { } element)
            name.Add(LocalIcon($"ui_gcg_buff_common_element_{element}.png", 22));
        name.Add(ProfileLabel(detail?.Name ?? CharacterDetailPresentation.Name(entry), 21, true));
        name.Add(ProfileLabel(CharacterDetailPresentation.Level(entry.Level), 14));
        head.Add(name);
        var rarity = ProfileLabel(CharacterDetailPresentation.Rarity(detail?.Rarity), 14);
        rarity.TextColor = Color.FromArgb("#E7AA36");
        head.Add(rarity, 1, 0);
        ProfileContent.Add(head);

        var progress = new HorizontalStackLayout { Spacing = 8 };
        progress.Add(ProfileLabel(CharacterDetailPresentation.Ascension(detail?.Ascension)));
        progress.Add(LocalIcon("ui_icon_fetter.png", 17));
        progress.Add(ProfileLabel(detail?.Friendship is int friendship ? $"好感度 {friendship}" : "好感度未知"));
        ProfileContent.Add(progress);

        var copy = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star) }, ColumnSpacing = 6 };
        copy.Add(ProfileButton("复制角色", () => CopyProfileAsync(CharacterDetailPresentation.Export(observation, entry))));
        copy.Add(ProfileButton("复制此快照全部", () => CopyProfileAsync(CharacterDetailPresentation.Export(observation))), 1, 0);
        ProfileContent.Add(copy);
        if (detail is null)
        {
            ProfileContent.Add(ProfileLabel("此快照仅保存角色概况。"));
            return;
        }

        var constellations = ProfileWrap();
        foreach (var item in (detail.Constellations ?? []).OrderBy(item => item.Position))
            constellations.Add(ProfileSkill(item.Name ?? $"命座 {item.Position}", item.Icon, item.Unlocked,
                item.Position is int pos ? $"C{pos}" : "命座", item.Description, null));
        if (constellations.Children.Count != 0) ProfileContent.Add(ProfileCard("命座", constellations));

        var talents = ProfileWrap();
        foreach (var item in detail.Talents ?? [])
            talents.Add(ProfileSkill(item.Name ?? $"天赋 {item.Id}", item.Icon, item.Unlocked,
                item.Level is int level ? $"Lv.{level}" : "天赋", item.Description, item.Attributes));
        if (talents.Children.Count != 0) ProfileContent.Add(ProfileCard("天赋", talents));

        var attributes = new VerticalStackLayout { Spacing = 3 };
        foreach (var stat in CharacterDetailPresentation.Overview(detail, observation.CharacterMetadata))
            attributes.Add(ProfileStat(stat));
        attributes.Add(ProfileButton("全部属性", async () =>
        {
            var groups = new VerticalStackLayout { Spacing = 6 };
            foreach (var (name, stats) in CharacterDetailPresentation.AllGroups(detail))
            {
                groups.Add(ProfileLabel(name, 16, true));
                foreach (var stat in stats)
                    groups.Add(ProfileStat(CharacterDetailPresentation.Project(stat, observation.CharacterMetadata)));
            }
            await ShowCharacterSheetAsync("全部属性", groups);
        }));
        ProfileContent.Add(ProfileCard("角色属性", attributes));

        if (detail.Weapon is { } weapon)
        {
            var info = new VerticalStackLayout { Spacing = 3 };
            info.Add(ProfileLabel($"{CharacterDetailPresentation.Level(weapon.Level)} · 精炼 {weapon.Refinement?.ToString() ?? "未知"}"));
            info.Add(ProfileLabel(CharacterDetailPresentation.Ascension(weapon.Ascension)));
            foreach (var stat in new[] { weapon.MainStat, weapon.SecondaryStat }.OfType<CharacterStat>())
                info.Add(ProfileStat(CharacterDetailPresentation.Project(stat, observation.CharacterMetadata)));
            ProfileContent.Add(ProfileCard(weapon.Name ?? $"武器 {weapon.Id}", WithProfileIcon(info, weapon.Icon, weapon.Rarity)));
        }
        foreach (var artifact in (detail.Artifacts ?? []).OrderBy(item => item.Slot))
        {
            var info = new VerticalStackLayout { Spacing = 3 };
            var subheading = string.Join(" · ", new[] { artifact.Set?.Name, artifact.SlotName,
                artifact.Level is int level ? $"＋{level}" : null }.OfType<string>());
            info.Add(ProfileLabel(subheading, 12));
            if (artifact.MainStat is { } main) info.Add(ArtifactLine(main, observation.CharacterMetadata, true));
            foreach (var sub in artifact.SubStats ?? []) info.Add(ArtifactLine(sub, observation.CharacterMetadata, false));
            ProfileContent.Add(ProfileCard(artifact.Name ?? $"圣遗物 {artifact.Id}", WithProfileIcon(info, artifact.Icon, artifact.Rarity)));
        }
        _ = ProfileScroll.ScrollToAsync(0, 0, false);
    }

    private async Task CopyProfileAsync(string value)
    {
        try
        {
            await Clipboard.Default.SetTextAsync(value);
            StatusLabel.Text = "已复制角色数据";
        }
        catch (Exception)
        {
            StatusLabel.Text = "复制失败，请重试";
        }
    }

    private View ProfileSkill(string name, string? icon, bool? unlocked, string badge, string? description,
        IReadOnlyList<CharacterTalentValue>? attributes)
    {
        var tile = new Grid { WidthRequest = 47, HeightRequest = 47 };
        if (ProfileArtwork(icon, 38) is { } artwork)
        {
            tile.Add(new Border { Padding = 2, StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 7 }, Content = artwork,
                BackgroundColor = Color.FromArgb("#4B505E"), Margin = 3 });
        }
        else tile.Add(ProfileLabel(badge));
        if (unlocked == false)
        {
            var lockIcon = LocalIcon("ui_icon_locked.png", 16);
            if (icon is null)
                lockIcon.SetAppTheme(Image.SourceProperty,
                    ImageSource.FromFile("ui_icon_locked_dark.png"), ImageSource.FromFile("ui_icon_locked.png"));
            lockIcon.HorizontalOptions = LayoutOptions.End;
            lockIcon.VerticalOptions = LayoutOptions.End;
            tile.Add(lockIcon);
        }
        SemanticProperties.SetDescription(tile, unlocked == false ? name + "（未解锁）" : name);
        var gesture = new TapGestureRecognizer();
        gesture.Tapped += async (_, _) =>
        {
            var panel = new VerticalStackLayout { Spacing = 8 };
            panel.Add(ProfileArtwork(icon, 56) ?? ProfileLabel(badge));
            panel.Add(ProfileLabel(name + " · " + badge, 18, true));
            var intro = ProfileRichLabel(description ?? "暂无机制说明");
            panel.Add(intro);
            if (attributes is { Count: > 0 })
            {
                var values = new VerticalStackLayout { Spacing = 5, IsVisible = false };
                foreach (var value in attributes) values.Add(ProfileRichLabel($"{value.Name}  {value.Value}"));
                var tabs = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star) }, ColumnSpacing = 8 };
                tabs.Add(ProfileButton("技能介绍", () =>
                {
                    intro.IsVisible = true;
                    values.IsVisible = false;
                    return Task.CompletedTask;
                }));
                tabs.Add(ProfileButton("详细属性", () =>
                {
                    intro.IsVisible = false;
                    values.IsVisible = true;
                    return Task.CompletedTask;
                }), 1, 0);
                panel.Children.Insert(2, tabs);
                panel.Add(values);
            }
            await ShowCharacterSheetAsync(name, panel);
        };
        tile.GestureRecognizers.Add(gesture);
        return tile;
    }

    private async Task ShowCharacterSheetAsync(string title, View content)
    {
        CharacterSheetTitle.Text = title;
        CharacterSheetContent.Clear();
        CharacterSheetContent.Add(content);
        CharacterSheet.TranslationY = Height > 0 ? Height : 600;
        CharacterOverlay.IsVisible = true;
        await CharacterSheetScroll.ScrollToAsync(0, 0, false);
        await CharacterSheet.TranslateToAsync(0, 0, 180, Easing.CubicOut);
    }

    private void OnCloseCharacterSheet(object? sender, EventArgs args) => CloseCharacterSheet();

    private void CloseCharacterSheet()
    {
        if (CharacterOverlay is null || !CharacterOverlay.IsVisible) return;
        CharacterOverlay.IsVisible = false;
        CharacterSheetContent.Clear();
    }

    protected override bool OnBackButtonPressed()
    {
        if (!CharacterOverlay.IsVisible) return base.OnBackButtonPressed();
        CloseCharacterSheet();
        return true;
    }

    private static View ProfileStat(CharacterStatView stat)
    {
        var row = new Grid { ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) },
            ColumnSpacing = 7, MinimumHeightRequest = 28 };
        if (ProfileArtwork(stat.Icon, 15) is { } icon) row.Add(icon);
        row.Add(ProfileLabel(stat.Name, 13), 1, 0);
        var number = ProfileLabel("", 13);
        var formatted = new FormattedString();
        if (stat.Base is { } basic && stat.SignedBonus is { } bonus)
        {
            formatted.Spans.Add(new Span { Text = basic });
            formatted.Spans.Add(new Span { Text = bonus, TextColor = Color.FromArgb("#76B808") });
            formatted.Spans.Add(new Span { Text = "　" });
        }
        formatted.Spans.Add(new Span { Text = stat.Total, FontAttributes = FontAttributes.Bold });
        number.FormattedText = formatted;
        row.Add(number, 2, 0);
        return row;
    }

    private static View ArtifactLine(CharacterArtifactStat stat, CharacterResponseMetadata? metadata, bool main) =>
        ProfileLabel($"{CharacterDetailPresentation.StatName(stat.PropertyId, metadata)}  {stat.Value ?? "—"}" +
            (stat.Hits is int hits && !main ? $"  ×{hits}" : ""), 13, main);

    private static Grid WithProfileIcon(View body, string? icon, int? rarity)
    {
        var grid = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 8 };
        grid.Add(body);
        grid.Add(new GachaItemIconView { IconSource = ArtworkSource(icon), IconSize = 57, CardWidth = 60, RankType = rarity,
            ShowItemName = false, ShowDetailText = false, VerticalOptions = LayoutOptions.Start }, 1, 0);
        return grid;
    }

    private static Border ProfileCard(string title, View body)
    {
        var content = new VerticalStackLayout { Spacing = 6 };
        content.Add(ProfileLabel(title, 16, true));
        content.Add(body);
        var card = new Border { Content = content, Padding = 10, StrokeShape = new RoundRectangle { CornerRadius = 9 } };
        card.SetAppThemeColor(BackgroundColorProperty, Color.FromArgb("#F5F5F8"), Color.FromArgb("#262831"));
        return card;
    }

    private static FlexLayout ProfileWrap() => new() { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };

    private static Label ProfileLabel(string text, double size = 14, bool bold = false) =>
        new() { Text = text, FontSize = size, FontAttributes = bold ? FontAttributes.Bold : FontAttributes.None,
            VerticalOptions = LayoutOptions.Center };

    private static Button ProfileButton(string text, Func<Task> action)
    {
        var button = new Button { Text = text };
        button.Clicked += async (_, _) => await action();
        return button;
    }

    private static Label ProfileRichLabel(string source)
    {
        var formatted = new FormattedString();
        foreach (var run in HoYoRichText.Parse(source))
        {
            var span = new Span { Text = run.Text,
                FontAttributes = (run.Bold ? FontAttributes.Bold : FontAttributes.None) |
                    (run.Italic ? FontAttributes.Italic : FontAttributes.None),
                TextDecorations = run.Underline ? TextDecorations.Underline : TextDecorations.None };
            if (run.Size is int size) span.FontSize = size;
            if (run.Color is { Length: >= 7 } color) span.TextColor = Color.FromArgb(color[..7]);
            formatted.Spans.Add(span);
        }
        return new Label { FormattedText = formatted };
    }

    private static View? ProfileArtwork(string? url, double size) => ArtworkSource(url) is { } source
        ? new Image { Source = source, WidthRequest = size, HeightRequest = size, Aspect = Aspect.AspectFit } : null;

    private static ImageSource? ArtworkSource(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps && uri.UserInfo.Length == 0 && uri.IsDefaultPort &&
        (uri.Host == "enka.network" || uri.Host.EndsWith(".mihoyo.com", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.EndsWith(".hoyolab.com", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.EndsWith(".hoyoverse.com", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.EndsWith(".yuanshen.com", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.EndsWith(".miyoushe.com", StringComparison.OrdinalIgnoreCase))
        ? ImageSource.FromUri(uri) : null;

    private static Image LocalIcon(string filename, double size) =>
        new() { Source = filename, WidthRequest = size, HeightRequest = size, Aspect = Aspect.AspectFit };
}
