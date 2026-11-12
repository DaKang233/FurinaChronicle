// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.History;
using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Infrastructure.HistoryDomains.Collecting;
using FurinaChronicle.Services.Abstractions;
using FurinaChronicle.Services.HistoryDomains;
using FurinaChronicle.Services.HistoryDomains.Collecting;
using FurinaChronicle.Presentation.History;
using FurinaChronicle.Services.Passport;

namespace FurinaChronicle.App.UI.Pages;

public partial class CharacterHistoryPage : ContentPage
{
    private const int PageSize = 20;
    private readonly IPlayerArchiveRepository archives;
    private readonly IGameAccountRepository accounts;
    private readonly IArchiveDomainHistoryStore history;
    private readonly IPassportSelectionStore passportSelection;
    private readonly IPassportAccountStore passports;
    private readonly IMiHoYoAccountProfileClient profiles;
    private readonly IDomainMetadataService metadata;
    private readonly ValidateCharacterWeaponCollection validation;
    private readonly CollectCharacterWeapon business;
    private CancellationTokenSource? operation;
    private int offset;
    private int total;
    private IReadOnlyList<ItemStateObservation> loadedObservations =
        Array.Empty<ItemStateObservation>();
    private IReadOnlyList<CharacterObservationCard> loadedCards =
        Array.Empty<CharacterObservationCard>();

    public CharacterHistoryPage(
        IPlayerArchiveRepository archives,
        IGameAccountRepository accounts,
        IArchiveDomainHistoryStore history,
        IPassportSelectionStore passportSelection,
        IPassportAccountStore passports,
        IMiHoYoAccountProfileClient profiles,
        IDomainMetadataService metadata,
        ValidateCharacterWeaponCollection validation,
        CollectCharacterWeapon business)
    {
        InitializeComponent();
        this.archives = archives;
        this.accounts = accounts;
        this.history = history;
        this.passportSelection = passportSelection;
        this.passports = passports;
        this.profiles = profiles;
        this.metadata = metadata;
        this.validation = validation;
        this.business = business;

        ArchivePicker.ItemDisplayBinding = new Binding(nameof(PlayerArchive.Name));
        AccountPicker.ItemDisplayBinding = new Binding(nameof(GameAccount.Uid));
        KindPicker.ItemsSource = new[] { "全部观察", "角色", "武器" };
        KindPicker.SelectedIndex = 0;
        UpdatePaging();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await RunAsync(async ct =>
        {
            Guid? selectedArchiveId = (ArchivePicker.SelectedItem as PlayerArchive)?.Id;
            var items = (await archives.GetAllAsync(ct)).ToList();
            ArchivePicker.ItemsSource = items;
            ArchivePicker.SelectedItem = items.FirstOrDefault(
                archive => archive.Id == selectedArchiveId)
                ?? items.FirstOrDefault();
            await LoadAccountsAsync(ct);
            await AutoSelectPassportAccountAsync(ct);
        });
    }

    protected override void OnDisappearing()
    {
        operation?.Cancel();
        base.OnDisappearing();
    }

    private async void OnArchiveChanged(object? sender, EventArgs e) =>
        await RunAsync(async ct =>
        {
            await LoadAccountsAsync(ct);
            await AutoSelectPassportAccountAsync(ct);
        });

    private async void OnAccountChanged(object? sender, EventArgs e) =>
        await RunAsync(async ct =>
        {
            ClearHistory();
            await LoadHistoryAsync(0, ct);
        });

    private async void OnKindChanged(object? sender, EventArgs e) =>
        await RunAsync(ct => LoadHistoryAsync(0, ct));

    private async void OnPreviousClicked(object? sender, EventArgs e) =>
        await RunAsync(ct => LoadHistoryAsync(Math.Max(0, offset - PageSize), ct));

    private async void OnNextClicked(object? sender, EventArgs e) =>
        await RunAsync(ct => LoadHistoryAsync(offset + PageSize, ct));

    private async void OnRefreshClicked(object? sender, EventArgs e) =>
        await RunAsync(async ct =>
        {
            if (AccountPicker.SelectedItem is not GameAccount
                { RoleIdentity: not null } target)
            {
                StatusLabel.Text = "请选择已确认角色身份的账号。";
                return;
            }
            PassportSelection? selection = await passportSelection.LoadAsync(ct);
            var passport = selection is null ? null :
                await passports.GetByIdAsync(selection.PassportAccountId, ct);
            if (passport is null)
            {
                StatusLabel.Text = "请先在用户页登录并选择该角色所属的米游社或 HoYoLAB 账号。";
                return;
            }

            StatusLabel.Text = "正在刷新角色与武器到当前档案…";
            CharacterWeaponCollectionResult result = await business.ExecuteAsync(
                target.PlayerArchiveId, target.Id, passport, ct);
            if (result.Commit?.Status is not (
                ChangeExecutionStatus.Applied or ChangeExecutionStatus.NoOp))
            {
                StatusLabel.Text = DescribeFailure(result);
                return;
            }
            StatusLabel.Text =
                $"刷新完成 · 提交结果 {result.Commit.Status} · " +
                DescribeSnapshot(result.Snapshot);
            await LoadHistoryAsync(0, ct);
        });
    private async void OnCompareClicked(object? sender, EventArgs e) =>
        await RunAsync(async ct =>
        {
            if (AccountPicker.SelectedItem is not GameAccount account)
            {
                StatusLabel.Text = "请选择账号副本。";
                return;
            }

            InventoryItemKind? kind = SelectedKind();
            InventoryItemKind[] kinds = kind is null
                ? [InventoryItemKind.Character, InventoryItemKind.Weapon]
                : [kind.Value];
            var summaries = new List<string>();
            foreach (InventoryItemKind current in kinds)
            {
                var observations = (await history.GetItemObservationsAsync(
                        account.Id, current, ct))
                    .OrderBy(observation =>
                        observation.Provenance.Timestamps.ObservedAt)
                    .ToArray();
                if (observations.Length < 2)
                {
                    summaries.Add(
                        $"{KindName(current)}：不足两次观察，无法比较");
                    continue;
                }

                IReadOnlyList<InventoryStateChange> changes =
                    ItemStateHistoryComparer.Compare(
                        observations[^2], observations[^1]);
                if (changes.Count == 0)
                {
                    summaries.Add($"{KindName(current)}：最近两次观察无变化");
                    continue;
                }

                string details = string.Join(
                    "、",
                    changes.Take(6).Select(DescribeChange));
                summaries.Add(
                    $"{KindName(current)}：{changes.Count} 处变化（{details}" +
                    (changes.Count > 6 ? "…）" : "）"));
            }
            StatusLabel.Text = string.Join("\n", summaries);
        });

    private async void OnValidateClicked(object? sender, EventArgs e) =>
        await RunAsync(async ct =>
        {
            if (AccountPicker.SelectedItem is not GameAccount
                { RoleIdentity: not null } target)
            {
                StatusLabel.Text = "请选择已确认角色身份的账号。";
                return;
            }

            PassportSelection? selection =
                await passportSelection.LoadAsync(ct);
            var passport = selection is null ? null :
                await passports.GetByIdAsync(selection.PassportAccountId, ct);
            if (passport is null)
            {
                StatusLabel.Text =
                    "请先在用户页登录并选择该角色所属的米游社或 HoYoLAB 账号。";
                return;
            }

            StatusLabel.Text = "正在请求角色与武器数据并验证临时数据库写入…";
            CharacterWeaponValidationResult result =
                await validation.ExecuteAsync(target, passport, ct);
            if (result.First.Commit?.Status != ChangeExecutionStatus.Applied)
            {
                StatusLabel.Text = DescribeFailure(result.First);
                return;
            }

            string repeat = result.Repeated switch
            {
                { Commit.Status: ChangeExecutionStatus.NoOp } =>
                    "后续刷新无变化",
                { } repeated => $"后续刷新 · {DescribeFailure(repeated)}",
                null => "已完成首次采集",
            };
            int characterObservations = result.ReopenedObservations.Count(
                observation => observation.Kind == InventoryItemKind.Character);
            int weaponObservations = result.ReopenedObservations.Count(
                observation => observation.Kind == InventoryItemKind.Weapon);
            StatusLabel.Text =
                $"隔离采集已写入 · {repeat} · 重开读回 " +
                $"{result.ReopenedObservations.Count} 条" +
                $"（角色 {characterObservations} / 武器 {weaponObservations}）\n" +
                DescribeSnapshot(result.First.Snapshot);
        });

    private async Task AutoSelectPassportAccountAsync(CancellationToken ct)
    {
        if (ArchivePicker.SelectedItem is not PlayerArchive)
        {
            return;
        }
        PassportSelection? selection = await passportSelection.LoadAsync(ct);
        if (selection is null)
        {
            return;
        }
        var passport = await passports.GetByIdAsync(
            selection.PassportAccountId, ct);
        if (passport is null)
        {
            return;
        }
        PassportAccountProfile profile = await profiles.GetAsync(passport, ct);
        var items = (AccountPicker.ItemsSource as IEnumerable<GameAccount>)
            ?.ToList() ?? [];
        var matches = items.Where(account =>
            account.RoleIdentity is { } identity &&
            profile.Roles.Any(role =>
                string.Equals(
                    role.GameBiz.Trim(),
                    identity.NaturalIdentity.GameBiz,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    role.Region.Trim(),
                    identity.NaturalIdentity.Server,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    role.Uid.Trim(),
                    identity.NaturalIdentity.Uid,
                    StringComparison.Ordinal))).ToList();
        if (matches.Count == 1)
        {
            AccountPicker.SelectedItem = matches[0];
        }
    }
    private void OnObservationChanged(object? sender, EventArgs e) =>
        RenderObservationDetail();

    private void RenderObservationDetail()
    {
        if (loadedCards.Count == 0)
        {
            ObservationDetailLabel.Text = string.Empty;
            return;
        }
        int index = ObservationPicker.SelectedIndex >= 0 &&
            ObservationPicker.SelectedIndex < loadedCards.Count
            ? ObservationPicker.SelectedIndex
            : 0;
        CharacterObservationCard card = loadedCards[index];
        ObservationDetailLabel.Text = $"{card.Evidence}\n{card.Detail}";
    }

    private static string DescribeObservationChoice(
        ItemStateObservation observation)
    {
        string kind = observation.Kind == InventoryItemKind.Character
            ? "角色"
            : "武器";
        string time = observation.Provenance.Timestamps.ObservedAt is { } observed
            ? observed.ToLocalTime().ToString("g")
            : "未知时间";
        return $"{kind} · {observation.Entries.Count} 条 · {time}";
    }
    private async Task LoadAccountsAsync(CancellationToken ct)
    {
        ClearHistory();
        Guid? selectedId = (AccountPicker.SelectedItem as GameAccount)?.Id;
        AccountPicker.ItemsSource = null;
        var items = ArchivePicker.SelectedItem is PlayerArchive archive
            ? (await accounts.GetByArchiveIdAsync(archive.Id, ct)).ToList()
            : [];
        AccountPicker.ItemsSource = items;
        AccountPicker.SelectedItem = items.FirstOrDefault(
            account => account.Id == selectedId)
            ?? items.FirstOrDefault();
        await LoadHistoryAsync(0, ct);
    }

    private async Task LoadHistoryAsync(int requestedOffset, CancellationToken ct)
    {
        if (AccountPicker.SelectedItem is not GameAccount account)
        {
            ClearHistory();
            return;
        }

        var page = await history.GetItemObservationPageAsync(
            new ItemObservationPageQuery(
                account.Id,
                SelectedKind(),
                Offset: requestedOffset,
                Limit: PageSize),
            ct);
        if (requestedOffset > 0 && page.Items.Count == 0)
        {
            page = await history.GetItemObservationPageAsync(
                new ItemObservationPageQuery(
                    account.Id,
                    SelectedKind(),
                    Limit: PageSize),
                ct);
        }
        offset = page.Offset;
        total = page.TotalCount;
        var cards = new List<CharacterObservationCard>(page.Items.Count);
        foreach (ItemStateObservation observation in page.Items)
        {
            cards.Add(await CharacterObservationCard.FromObservationAsync(
                observation, metadata, ct));
        }
        loadedObservations = page.Items;
        loadedCards = cards;
        ObservationPicker.ItemsSource = page.Items
            .Select(DescribeObservationChoice)
            .ToList();
        ObservationPicker.SelectedIndex = page.Items.Count > 0 ? 0 : -1;
        HistoryList.ItemsSource = cards;
        RenderObservationDetail();
    }

    private void ClearHistory()
    {
        HistoryList.ItemsSource = Array.Empty<CharacterObservationCard>();
        loadedObservations = Array.Empty<ItemStateObservation>();
        loadedCards = Array.Empty<CharacterObservationCard>();
        ObservationPicker.ItemsSource = null;
        ObservationDetailLabel.Text = string.Empty;
        offset = total = 0;
    }

    private InventoryItemKind? SelectedKind() => KindPicker.SelectedIndex switch
    {
        1 => InventoryItemKind.Character,
        2 => InventoryItemKind.Weapon,
        _ => null,
    };

    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        if (operation is not null)
        {
            return;
        }
        using var cancellation = new CancellationTokenSource();
        operation = cancellation;
        Filters.IsEnabled = Paging.IsEnabled = false;
        StatusLabel.Text = string.Empty;
        try
        {
            await action(cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            StatusLabel.Text = "读取或采集失败，请检查登录状态与网络后重试。";
        }
        finally
        {
            operation = null;
            Filters.IsEnabled = Paging.IsEnabled = true;
            UpdatePaging();
        }
    }

    private void UpdatePaging()
    {
        PreviousButton.IsEnabled = offset > 0;
        NextButton.IsEnabled = offset + PageSize < total;
        PageLabel.Text = total == 0 ? "0 条观察" :
            $"{offset + 1}–{Math.Min(offset + PageSize, total)} / {total} 条";
    }

    private static string DescribeSnapshot(CharacterWeaponSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return string.Empty;
        }
        return $"角色 {snapshot.Characters.Count} 条 · 武器 {snapshot.Weapons.Count} 条";
    }

    private static string DescribeFailure(CharacterWeaponCollectionResult result)
    {
        string message = result.Status switch
        {
            CharacterWeaponCollectionStatus.AccessDenied => "角色数据访问受限",
            CharacterWeaponCollectionStatus.VerificationRequired =>
                "角色服务要求安全验证",
            CharacterWeaponCollectionStatus.AuthenticationRequired =>
                "通行证登录状态无效",
            CharacterWeaponCollectionStatus.RoleNotBound =>
                "所选通行证未绑定此角色",
            CharacterWeaponCollectionStatus.UnsupportedRealm =>
                "所选通行证与角色的区服不匹配",
            CharacterWeaponCollectionStatus.Empty => "该账号暂无角色数据",
            CharacterWeaponCollectionStatus.RateLimited => "请求过于频繁",
            CharacterWeaponCollectionStatus.Unavailable => "角色服务暂不可用",
            CharacterWeaponCollectionStatus.Received =>
                $"数据库提交结果 {result.Commit?.Status}",
            _ => $"采集失败 · {result.Status}",
        };
        string detail = result.ProviderCode is int code
            ? $"（业务码 {code}）"
            : result.HttpStatus is int http ? $"（HTTP {http}）" : string.Empty;
        return message + detail;
    }

    private static string KindName(InventoryItemKind kind) =>
        kind == InventoryItemKind.Character ? "角色" : "武器";

    private static string DescribeChange(InventoryStateChange change)
    {
        string kind = change.ChangeKind switch
        {
            InventoryStateChangeKind.Added => "新增",
            InventoryStateChangeKind.Removed => "移除",
            _ => "变化",
        };
        string before = change.Before is { } earlier
            ? DescribeEntry(earlier)
            : "无";
        string after = change.After is { } later
            ? DescribeEntry(later)
            : "无";
        return $"{kind} {change.StableItemKey} {before}→{after}";
    }

    private static string DescribeEntry(InventoryStateEntry entry)
    {
        string level = entry.Level is int value ? $"Lv.{value}" : "等级未知";
        string progression = entry.Kind == InventoryItemKind.Character
            ? entry.Progression is int constellation
                ? $" {constellation} 命"
                : string.Empty
            : entry.Progression is int refinement
                ? $" R{refinement}"
                : string.Empty;
        string quantity = entry.Quantity is int count && count > 1
            ? $" ×{count}"
            : string.Empty;
        return $"{level}{progression}{quantity}";
    }
}

