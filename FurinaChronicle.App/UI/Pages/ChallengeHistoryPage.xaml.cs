// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.History;
using FurinaChronicle.Core.Metadata;
using FurinaChronicle.Core.Passport;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Infrastructure.HistoryDomains.Collecting;
using FurinaChronicle.Services.Abstractions;
using FurinaChronicle.Services.HistoryDomains;
using FurinaChronicle.Services.HistoryDomains.Collecting;
using FurinaChronicle.Presentation.History;
using FurinaChronicle.Services.Passport;

namespace FurinaChronicle.App.UI.Pages;

public partial class ChallengeHistoryPage : ContentPage
{
    private const int PageSize = 20;
    private readonly IPlayerArchiveRepository archives;
    private readonly IGameAccountRepository accounts;
    private readonly IArchiveDomainHistoryStore history;
    private readonly IPassportSelectionStore passportSelection;
    private readonly IPassportAccountStore passports;
    private readonly IMiHoYoAccountProfileClient profiles;
    private readonly IDomainMetadataService metadata;
    private readonly ValidateSpiralAbyssCollection validation;
    private readonly ValidateChallengeModeCollection modeValidation;
    private readonly CollectSpiralAbyss businessAbyss;
    private readonly CollectChallengeMode businessModes;
    private CancellationTokenSource? operation;
    private bool presentingVerification;
    private int offset;
    private int total;
    private IReadOnlyList<ChallengeObservation> loadedObservations =
        Array.Empty<ChallengeObservation>();

    public ChallengeHistoryPage(
        IPlayerArchiveRepository archives,
        IGameAccountRepository accounts,
        IArchiveDomainHistoryStore history,
        IPassportSelectionStore passportSelection,
        IPassportAccountStore passports,
        IMiHoYoAccountProfileClient profiles,
        IDomainMetadataService metadata,
        ValidateSpiralAbyssCollection validation,
        ValidateChallengeModeCollection modeValidation,
        CollectSpiralAbyss businessAbyss,
        CollectChallengeMode businessModes)
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
        this.modeValidation = modeValidation;
        this.businessAbyss = businessAbyss;
        this.businessModes = businessModes;

        ModePicker.ItemsSource = new[]
        {
            "深境螺旋",
            "幻想真境剧诗",
            "幽境危战",
        };
        ModePicker.SelectedIndex = 0;
        ArchivePicker.ItemDisplayBinding = new Binding(nameof(PlayerArchive.Name));
        AccountPicker.ItemDisplayBinding = new Binding(nameof(GameAccount.Uid));
        PeriodPicker.ItemsSource = new[] { "当前期", "前一期" };
        PeriodPicker.SelectedIndex = 0;
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
            ArchivePicker.SelectedItem = items.FirstOrDefault(archive => archive.Id == selectedArchiveId)
                ?? items.FirstOrDefault();
            await LoadAccountsAsync(ct);
            await AutoSelectPassportAccountAsync(ct);
        });
    }

    protected override void OnDisappearing()
    {
        if (!presentingVerification)
        {
            operation?.Cancel();
        }
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

    private async void OnPreviousClicked(object? sender, EventArgs e) =>
        await RunAsync(ct => LoadHistoryAsync(Math.Max(0, offset - PageSize), ct));

    private async void OnNextClicked(object? sender, EventArgs e) =>
        await RunAsync(ct => LoadHistoryAsync(offset + PageSize, ct));

    private async void OnValidateClicked(object? sender, EventArgs e) =>
        await RunAsync(async ct =>
        {
            if (AccountPicker.SelectedItem is not GameAccount { RoleIdentity: not null } target)
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

            ChallengeMode mode = SelectedMode();
            if (mode != ChallengeMode.SpiralAbyss)
            {
                await ValidateModeAsync(target, passport, mode, ct);
                return;
            }

            var period = PeriodPicker.SelectedIndex == 1
                ? SpiralAbyssPeriod.Previous : SpiralAbyssPeriod.Current;
            StatusLabel.Text = "正在请求战绩并验证临时数据库写入…";
            SpiralAbyssValidationResult result = await validation.ExecuteAsync(target, passport, period, ct, PresentVerificationAsync);
            if (result.First.Commit?.Status != ChangeExecutionStatus.Applied)
            {
                StatusLabel.Text = DescribeFailure(result.First);
                return;
            }

            string repeat = result.Repeated switch
            {
                { Commit.Status: ChangeExecutionStatus.NoOp } => "后续刷新无变化",
                { Status: SpiralAbyssCollectionStatus.VerificationRequired } =>
                    "后续刷新仍要求验证，首次采集已完成",
                { } repeated => $"后续刷新 · {DescribeFailure(repeated)}",
                null => "已完成首次采集",
            };
            var summaries = result.ReopenedObservations.Select(observation =>
                $"第 {observation.SeasonId} 期 · {observation.SpiralAbyssFloors.Sum(floor => floor.Stars)} 星");
            StatusLabel.Text = $"隔离采集已写入 · {repeat} · 重开读回 {result.ReopenedObservations.Count} 条\n" +
                string.Join("、", summaries);
        });

    private async void OnModeChanged(object? sender, EventArgs e)
    {
        PeriodPicker.IsEnabled = SelectedMode() == ChallengeMode.SpiralAbyss;
        await RunAsync(ct => LoadHistoryAsync(0, ct));
    }

    private ChallengeMode SelectedMode() => ModePicker.SelectedIndex switch
    {
        1 => ChallengeMode.ImaginariumTheater,
        2 => ChallengeMode.StygianOnslaught,
        _ => ChallengeMode.SpiralAbyss,
    };

    private async Task ValidateModeAsync(
        GameAccount target,
        PassportAccount passport,
        ChallengeMode mode,
        CancellationToken ct)
    {
        StatusLabel.Text = "正在请求挑战数据并验证临时数据库写入…";
        ChallengeModeValidationResult result =
            await modeValidation.ExecuteAsync(target, passport, mode, ct);
        if (result.First.Commit?.Status != ChangeExecutionStatus.Applied)
        {
            StatusLabel.Text = DescribeModeFailure(result.First);
            return;
        }

        string repeat = result.Repeated switch
        {
            { Commit.Status: ChangeExecutionStatus.NoOp } => "后续刷新无变化",
            { } repeated => $"后续刷新 · {DescribeModeFailure(repeated)}",
            null => "已完成首次采集",
        };
        string summaries = string.Join(
            "、",
            result.ReopenedObservations.Select(observation =>
                $"{ChallengeObservationCard.ModeName(observation.Mode)} 第 {observation.SeasonId} 期 · {observation.Stages.Count} 条"));
        StatusLabel.Text =
            $"隔离采集已写入 · {repeat} · 重开读回 " +
            $"{result.ReopenedObservations.Count} 条\n{summaries}";
    }

    private static string DescribeModeFailure(ChallengeModeCollectionResult result)
    {
        string message = result.Status switch
        {
            SpiralAbyssCollectionStatus.AccessDenied => "挑战数据访问受限",
            SpiralAbyssCollectionStatus.AuthenticationRequired => "通行证登录状态无效",
            SpiralAbyssCollectionStatus.RoleNotBound => "所选通行证未绑定此角色",
            SpiralAbyssCollectionStatus.UnsupportedRealm => "所选通行证与角色的区服不匹配",
            SpiralAbyssCollectionStatus.Empty => "该模式暂无挑战数据",
            SpiralAbyssCollectionStatus.RateLimited => "请求过于频繁",
            SpiralAbyssCollectionStatus.Unavailable => "挑战服务暂不可用",
            SpiralAbyssCollectionStatus.Received =>
                $"数据库提交结果 {result.Commit?.Status}",
            _ => $"采集失败 · {result.Status}",
        };
        string detail = result.ProviderCode is int code
            ? $"（业务码 {code}）"
            : result.HttpStatus is int http ? $"（HTTP {http}）" : string.Empty;
        return message + detail;
    }

    private Task<PassportGeetestResult?> PresentVerificationAsync(
        PassportGeetestChallenge challenge, CancellationToken cancellationToken) =>
        MainThread.InvokeOnMainThreadAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            presentingVerification = true;
            StatusLabel.Text = "请完成米游社安全验证。";
            try
            {
                var page = new GeetestVerificationPage(challenge);
                await Navigation.PushModalAsync(page);
                var answer = await page.WaitForResultAsync(cancellationToken);
                if (answer is not null)
                {
                    StatusLabel.Text = "正在提交验证结果并刷新战绩…";
                }
                return answer;
            }
            finally
            {
                presentingVerification = false;
            }
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
    private async Task LoadAccountsAsync(CancellationToken ct)
    {
        ClearHistory();
        Guid? selectedId = (AccountPicker.SelectedItem as GameAccount)?.Id;
        AccountPicker.ItemsSource = null;
        var items = ArchivePicker.SelectedItem is PlayerArchive archive
            ? (await accounts.GetByArchiveIdAsync(archive.Id, ct)).ToList()
            : new List<GameAccount>();
        AccountPicker.ItemsSource = items;
        AccountPicker.SelectedItem = items.FirstOrDefault(account => account.Id == selectedId)
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

        var page = await history.GetChallengeObservationPageAsync(
            new(account.Id, SelectedMode(), Offset: requestedOffset, Limit: PageSize), ct);
        if (requestedOffset > 0 && page.Items.Count == 0)
        {
            page = await history.GetChallengeObservationPageAsync(
                new(account.Id, SelectedMode(), Limit: PageSize), ct);
        }
        offset = page.Offset;
        total = page.TotalCount;
        loadedObservations = page.Items;
        ObservationPicker.ItemsSource = page.Items
            .Select(DescribeObservationChoice)
            .ToList();
        ObservationPicker.SelectedIndex = page.Items.Count > 0 ? 0 : -1;
        var cards = new List<ChallengeObservationCard>();
        foreach (ChallengeObservation observation in page.Items)
        {
            ChallengeScheduleMetadata? schedule = await metadata.ResolveScheduleAsync(
                observation.Mode, observation.SeasonId, ct);
            cards.Add(ChallengeObservationCard.FromObservation(
                observation, schedule?.Name));
        }
        HistoryList.ItemsSource = cards;
        RenderObservationDetail();
    }

    private void ClearHistory()
    {
        HistoryList.ItemsSource = Array.Empty<ChallengeObservationCard>();
        loadedObservations = Array.Empty<ChallengeObservation>();
        ObservationPicker.ItemsSource = null;
        ObservationDetailLabel.Text = string.Empty;
        offset = total = 0;
    }

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

            ChallengeMode mode = SelectedMode();
            StatusLabel.Text = "正在刷新到当前档案…";
            if (mode == ChallengeMode.SpiralAbyss)
            {
                var period = PeriodPicker.SelectedIndex == 1
                    ? SpiralAbyssPeriod.Previous : SpiralAbyssPeriod.Current;
                SpiralAbyssCollectionResult result =
                    await businessAbyss.ExecuteAsync(
                        target.PlayerArchiveId, target.Id, passport, ct,
                        period, PresentVerificationAsync);
                if (result.Commit?.Status is not (
                    ChangeExecutionStatus.Applied or
                    ChangeExecutionStatus.NoOp))
                {
                    StatusLabel.Text = DescribeFailure(result);
                    return;
                }
                StatusLabel.Text =
                    $"刷新完成 · 提交结果 {result.Commit.Status}";
            }
            else
            {
                ChallengeModeCollectionResult result =
                    await businessModes.ExecuteAsync(
                        target.PlayerArchiveId, target.Id, passport, mode, ct);
                if (result.Commit?.Status is not (
                    ChangeExecutionStatus.Applied or
                    ChangeExecutionStatus.NoOp))
                {
                    StatusLabel.Text = DescribeModeFailure(result);
                    return;
                }
                StatusLabel.Text =
                    $"刷新完成 · 提交结果 {result.Commit.Status}";
            }
            await LoadHistoryAsync(0, ct);
        });
    private void OnObservationChanged(object? sender, EventArgs e) =>
        RenderObservationDetail();

    private void RenderObservationDetail()
    {
        if (loadedObservations.Count == 0)
        {
            ObservationDetailLabel.Text = string.Empty;
            return;
        }

        int index = ObservationPicker.SelectedIndex >= 0 &&
            ObservationPicker.SelectedIndex < loadedObservations.Count
            ? ObservationPicker.SelectedIndex
            : 0;
        ChallengeObservation observation = loadedObservations[index];
        ObservationDetailLabel.Text = string.Join(
            "\n",
            observation.Stages.Select(stage => ChallengeObservationCard.DescribeStage(stage, observation.Mode)));
    }


    private static string DescribeObservationChoice(
        ChallengeObservation observation)
    {
        string time = observation.Provenance.Timestamps.ObservedAt is { } observed
            ? observed.ToLocalTime().ToString("g")
            : "未知时间";
        return $"{ChallengeObservationCard.ModeName(observation.Mode)} · 第 {observation.SeasonId} 期 · {time}";
    }
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
            StatusLabel.Text = "读取或验证失败，请检查登录状态与网络后重试。";
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

    private static string DescribeFailure(SpiralAbyssCollectionResult result)
    {
        string message = result.Status switch
        {
            SpiralAbyssCollectionStatus.AccessDenied => "战绩访问受限",
            SpiralAbyssCollectionStatus.VerificationRequired => "战绩服务要求安全验证",
            SpiralAbyssCollectionStatus.VerificationCanceled => "已取消米游社安全验证",
            SpiralAbyssCollectionStatus.VerificationFailed => "米游社安全验证未通过，请重试",
            SpiralAbyssCollectionStatus.AuthenticationRequired => "通行证登录状态无效",
            SpiralAbyssCollectionStatus.RoleNotBound => "所选通行证未绑定此角色",
            SpiralAbyssCollectionStatus.UnsupportedRealm => "所选通行证与角色的区服不匹配",
            SpiralAbyssCollectionStatus.NotUnlocked => "本期深境螺旋未解锁",
            SpiralAbyssCollectionStatus.Empty => "该期暂无挑战结果",
            SpiralAbyssCollectionStatus.RateLimited => "请求过于频繁",
            SpiralAbyssCollectionStatus.Unavailable => "战绩服务暂不可用",
            SpiralAbyssCollectionStatus.Received => $"数据库提交结果 {result.Commit?.Status}",
            _ => $"采集失败 · {result.Status}",
        };
        string? stage = result.VerificationStage switch
        {
            GameRecordVerificationStage.Registration => "申请挑战",
            GameRecordVerificationStage.Submission => "提交验证",
            GameRecordVerificationStage.Retry => "验证后刷新",
            _ => null,
        };
        string detail = result.ProviderCode is int code ? $"（业务码 {code}）" :
            result.HttpStatus is int http ? $"（HTTP {http}）" : string.Empty;
        if (result.ProviderCode == 10306 && result.VerificationSessionCookieSent is bool sent)
        {
            detail += sent ? " · 已携带验证会话" : " · 服务端未下发可用的验证会话 Cookie";
        }
        return stage is null ? message + detail : $"{stage} · {message}{detail}";
    }
}
