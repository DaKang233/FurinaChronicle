// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.History;
using FurinaChronicle.Core.Passport;
using FurinaChronicle.Services.Abstractions;
using FurinaChronicle.Services.History;
using FurinaChronicle.Services.HistoryDomains.Collecting;
using FurinaChronicle.Presentation.Notifications;
using FurinaChronicle.Presentation.Notifications;
using FurinaChronicle.Services.Passport;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FurinaChronicle.Windows.Composition;

public sealed class WindowsHistoryCollection(
    IGameAccountRepository accounts,
    DpapiPassportCredentialStore credentials,
    CollectCharacterWeapon characterWeapon,
    CollectSpiralAbyss abyss,
    CollectChallengeMode challenge,
    INotificationCenter notifications)
{
    public async Task<string?> RunAsync(
        XamlRoot root, Guid archiveId, Guid accountId, ChallengeMode? mode, CancellationToken token)
    {
        GameAccount? account = await accounts.GetByIdAsync(accountId, token);
        if (account?.PlayerArchiveId != archiveId || account.RoleIdentity is null)
            return "请选择身份已确认的档案账号。";

        string gameBiz = account.RoleIdentity.NaturalIdentity.GameBiz;
        PassportRealm realm = gameBiz == GenshinGameRoleIdentity.GlobalGameBiz
            ? PassportRealm.Oversea : PassportRealm.MainlandChina;
        PassportAccount[] available = (await credentials.GetAllAsync(token))
            .Where(item => item.Realm == realm && item.Credentials.LToken is not null)
            .ToArray();
        if (available.Length == 0)
            return "请在“登录账号”中登录与当前角色区服匹配的通行证。";

        var picker = new ComboBox { Header = "通行证", HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (PassportAccount item in available)
        {
            picker.Items.Add(item.DisplayName ?? $"通行证 {item.Aid}");
        }
        picker.SelectedIndex = 0;
        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(new TextBlock
        {
            Text = $"将查询官方服务，并保存到当前档案的角色 {account.Uid}。",
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(picker);
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = "采集最新记录",
            PrimaryButtonText = "开始采集",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            Content = content
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;
        token.ThrowIfCancellationRequested();
        PassportAccount selected = available[picker.SelectedIndex];
        if (mode is null)
        {
            CharacterWeaponCollectionResult result =
                await characterWeapon.ExecuteAsync(archiveId, accountId, selected, token);
            return Report(result.Commit, result.Status.ToString(), "角色与武器");
        }
        if (mode == ChallengeMode.SpiralAbyss)
        {
            SpiralAbyssCollectionResult result = await abyss.ExecuteAsync(archiveId, accountId, selected, token,
                verificationPrompt: new WindowsPassportVerification(root).VerifyGeetestAsync);
            return Report(result.Commit, result.Status.ToString(), "深境螺旋");
        }
        ChallengeModeCollectionResult other =
            await challenge.ExecuteAsync(archiveId, accountId, selected, mode.Value, token);
        return Report(
            other.Commit,
            other.Status.ToString(),
            mode == ChallengeMode.ImaginariumTheater ? "幻想真境剧诗" : "幽境危战");
    }

    private string Report(
        ArchiveOperationResult? commit,
        string status,
        string subject)
    {
        string message = Describe(commit, status);
        NotificationSeverity severity = commit?.Status switch
        {
            ChangeExecutionStatus.Applied => NotificationSeverity.Success,
            ChangeExecutionStatus.NoOp => NotificationSeverity.Information,
            _ => NotificationSeverity.Warning,
        };
        notifications.Publish(new(
            severity,
            NotificationCategory.Collection,
            $"{subject}采集",
            message));
        return message;
    }

    private static string Describe(ArchiveOperationResult? commit, string status) => commit?.Status switch
    {
        ChangeExecutionStatus.Applied => $"采集完成，写入 {commit.AffectedRecordCount} 项。",
        ChangeExecutionStatus.NoOp => "采集完成，当前观察没有变化。",
        ChangeExecutionStatus.NeedsConfirmation => "撤销材料空间不足，本次采集未保存。",
        ChangeExecutionStatus.Conflict => "保存期间档案已变化，请重新载入后重试。",
        _ => status switch
        {
            "AuthenticationRequired" => "登录已过期，请重新登录。",
            "RoleNotBound" or "ResponseRoleMismatch" => "所选通行证与当前档案角色不匹配。",
            "AccessDenied" => "官方服务限制了此角色的数据访问。",
            "VerificationRequired" => "官方服务要求额外安全验证，请完成验证后重试。",
            "RateLimited" => "请求过于频繁，请稍后重试。",
            "Empty" or "NotUnlocked" => "当前没有可采集的记录。",
            _ => "采集未完成，请检查登录和网络后重试。"
        }
    };
}
