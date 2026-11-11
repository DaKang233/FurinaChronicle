// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.History;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Infrastructure.HistoryDomains.Collecting;
using FurinaChronicle.Services.Abstractions;
using FurinaChronicle.Services.HistoryDomains;
using FurinaChronicle.Services.HistoryDomains.Collecting;
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
    private readonly ValidateSpiralAbyssCollection validation;
    private CancellationTokenSource? operation;
    private bool presentingVerification;
    private int offset;
    private int total;

    public ChallengeHistoryPage(
        IPlayerArchiveRepository archives,
        IGameAccountRepository accounts,
        IArchiveDomainHistoryStore history,
        IPassportSelectionStore passportSelection,
        IPassportAccountStore passports,
        ValidateSpiralAbyssCollection validation)
    {
        InitializeComponent();
        this.archives = archives;
        this.accounts = accounts;
        this.history = history;
        this.passportSelection = passportSelection;
        this.passports = passports;
        this.validation = validation;

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
        await RunAsync(LoadAccountsAsync);

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
            new(account.Id, ChallengeMode.SpiralAbyss, Offset: requestedOffset, Limit: PageSize), ct);
        if (requestedOffset > 0 && page.Items.Count == 0)
        {
            page = await history.GetChallengeObservationPageAsync(
                new(account.Id, ChallengeMode.SpiralAbyss, Limit: PageSize), ct);
        }
        offset = page.Offset;
        total = page.TotalCount;
        HistoryList.ItemsSource = page.Items.Select(ChallengeObservationCard.FromObservation).ToList();
    }

    private void ClearHistory()
    {
        HistoryList.ItemsSource = Array.Empty<ChallengeObservationCard>();
        offset = total = 0;
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

public sealed record ChallengeObservationCard(string Title, string Evidence, string Floors)
{
    public static ChallengeObservationCard FromObservation(ChallengeObservation observation)
    {
        int stars = observation.SpiralAbyssFloors.Sum(floor => floor.Stars);
        string coverage = observation.Completeness switch
        {
            DataCompleteness.Complete => "完整观察",
            DataCompleteness.Partial => "部分观察",
            _ => "覆盖未知",
        };
        string observed = observation.Provenance.Timestamps.ObservedAt is { } time
            ? time.ToLocalTime().ToString("g") : "未知";
        string floors = string.Join("\n", observation.SpiralAbyssFloors.Select(floor =>
            $"第 {floor.Floor} 层 · {floor.Stars} 星  " +
            string.Join("  ", floor.Chambers.Select(chamber => $"{chamber.Chamber} 间 {chamber.Stars} 星"))));
        return new($"第 {observation.SeasonId} 期 · {stars} 星",
            $"{coverage} · 观察于 {observed}", floors);
    }
}
