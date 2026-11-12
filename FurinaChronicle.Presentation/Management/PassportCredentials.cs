// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Passport;

namespace FurinaChronicle.Presentation.Management;

// Safe, non-secret summary of a locally stored credential. SToken/MID never leave the store
// boundary; MID is already masked by the host adapter.
public sealed record IsolatedCredentialSummary(
    Guid Id,
    string? Label,
    string MaskedMid,
    string BoundRoleUid,
    DateTimeOffset UpdatedAt,
    bool IsUsable,
    string? FailureReason);

public interface IIsolatedCredentialStore
{
    // Non-null when the encrypted file could not be loaded and the store degraded to empty.
    string? LoadWarning { get; }

    Task<IReadOnlyList<IsolatedCredentialSummary>> ListAsync(
        CancellationToken cancellationToken = default);

    Task<Guid> SaveAsync(
        string sToken,
        string mid,
        string boundRoleUid,
        string? label,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid accountId, CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);

    Task MarkUnusableAsync(
        Guid accountId,
        string safeReason,
        CancellationToken cancellationToken = default);

    Task MarkUsableAsync(Guid accountId, CancellationToken cancellationToken = default);
}

public static class CredentialValidation
{
    public const int MaximumSTokenLength = 2048;
    public const int MaximumMidLength = 20;
    public const int MinimumUidLength = 9;
    public const int MaximumUidLength = 10;

    public static string? ValidateSToken(string? value)
    {
        string token = value?.Trim() ?? string.Empty;
        if (token.Length == 0) return "SToken 不能为空。";
        if (token.Length > MaximumSTokenLength)
        {
            return $"SToken 长度不能超过 {MaximumSTokenLength} 个字符。";
        }

        if (token.Any(char.IsWhiteSpace) || token.Any(char.IsControl))
        {
            return "SToken 不能包含空白或控制字符。";
        }

        return null;
    }

    public static string? ValidateMid(string? value)
    {
        string mid = value?.Trim() ?? string.Empty;
        if (mid.Length == 0) return "MID 不能为空。";
        if (mid.Length > MaximumMidLength || !mid.All(char.IsAsciiDigit))
        {
            return "MID 必须是纯数字。";
        }

        return null;
    }

    public static string? ValidateBoundUid(string? value)
    {
        string uid = value?.Trim() ?? string.Empty;
        if (uid.Length is < MinimumUidLength or > MaximumUidLength ||
            !uid.All(char.IsAsciiDigit))
        {
            return $"绑定角色 UID 必须是 {MinimumUidLength}～{MaximumUidLength} 位数字。";
        }

        return null;
    }

    public static string MaskMid(string? mid)
    {
        string value = mid?.Trim() ?? string.Empty;
        if (value.Length == 0) return "****";
        if (value.Length <= 4) return "****";
        return value[..2] + "****" + value[^2..];
    }

    public static string MaskUid(string? uid)
    {
        string value = uid?.Trim() ?? string.Empty;
        return value.Length <= 4 ? "****" : value[..3] + "****" + value[^2..];
    }

    public static PassportCredentials CreateCredentials(string sToken, DateTimeOffset now) =>
        new(sToken, null, null, now, null, null, null);
}

public sealed record PassportCredentialSnapshot
{
    public IReadOnlyList<IsolatedCredentialSummary> Credentials { get; init; } = [];
    public Guid? SelectedCredentialId { get; init; }
    public bool IsBusy { get; init; }
    public string Status { get; init; } = "尚未载入本机隔离宿主凭据。";
    public string? Error { get; init; }
    public string? LoadWarning { get; init; }

    public IsolatedCredentialSummary? SelectedCredential =>
        Credentials.FirstOrDefault(item => item.Id == SelectedCredentialId);

    public bool HasCredentials => Credentials.Count > 0;
}

public sealed record PassportCredentialActionResult(bool Succeeded, string Message);

// Page-local credential management. This state never receives masked values back after save and
// never places SToken/MID into the snapshot, status or error text.
public sealed class PassportCredentialState(IIsolatedCredentialStore store)
{
    private Guid? preferredId;

    public PassportCredentialSnapshot Snapshot { get; private set; } = new();

    public Task EnterAsync(CancellationToken cancellationToken = default) =>
        ReloadAsync("已载入本机隔离宿主凭据。", cancellationToken);

    public Task SelectAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        preferredId = accountId;
        Set(Snapshot with { SelectedCredentialId = accountId });
        return Task.CompletedTask;
    }

    public async Task<PassportCredentialActionResult> SaveAsync(
        string? sToken,
        string? mid,
        string? boundRoleUid,
        string? label,
        CancellationToken cancellationToken = default)
    {
        string? tokenError = CredentialValidation.ValidateSToken(sToken);
        string? midError = CredentialValidation.ValidateMid(mid);
        string? uidError = CredentialValidation.ValidateBoundUid(boundRoleUid);
        string? error = tokenError ?? midError ?? uidError;
        if (error is not null)
        {
            Set(Snapshot with { Error = error });
            return new(false, error);
        }

        if (label is { Length: > 50 })
        {
            const string labelError = "凭据标签不能超过 50 个字符。";
            Set(Snapshot with { Error = labelError });
            return new(false, labelError);
        }

        try
        {
            Guid id = await store.SaveAsync(
                sToken!,
                mid!,
                boundRoleUid!,
                label,
                cancellationToken);
            preferredId = id;
            await ReloadAsync("凭据已保存到本机隔离宿主；不会显示或记录 SToken/MID。", cancellationToken);
            return new(true, "凭据已保存到本机隔离宿主；档案与账号事实未改变。");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            string message = Describe(exception);
            Set(Snapshot with { Error = message });
            return new(false, message);
        }
    }

    public async Task<PassportCredentialActionResult> DeleteSelectedAsync(
        CancellationToken cancellationToken = default)
    {
        if (Snapshot.SelectedCredentialId is not Guid id)
        {
            return new(false, "请先选择一条凭据。");
        }

        try
        {
            await store.DeleteAsync(id, cancellationToken);
            preferredId = null;
            await ReloadAsync("已删除本机隔离宿主凭据；档案与账号事实未改变。", cancellationToken);
            return new(true, "已删除本机隔离宿主凭据；未删除档案或游戏账号事实。");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            string message = Describe(exception);
            Set(Snapshot with { Error = message });
            return new(false, message);
        }
    }

    public async Task<PassportCredentialActionResult> ClearAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            await store.ClearAsync(cancellationToken);
            preferredId = null;
            await ReloadAsync("已清除全部本机隔离宿主凭据；档案与账号事实未改变。", cancellationToken);
            return new(true, "已清除全部本机隔离宿主凭据；未删除档案或游戏账号事实。");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            string message = Describe(exception);
            Set(Snapshot with { Error = message });
            return new(false, message);
        }
    }

    public async Task<PassportCredentialActionResult> RefreshAsync(
        CancellationToken cancellationToken = default) =>
        await ReloadActionAsync(cancellationToken);

    private async Task<PassportCredentialActionResult> ReloadActionAsync(
        CancellationToken cancellationToken)
    {
        await ReloadAsync(null, cancellationToken);
        return Snapshot.Error is null
            ? new(true, Snapshot.Status)
            : new(false, Snapshot.Error);
    }

    private async Task ReloadAsync(
        string? operationStatus,
        CancellationToken cancellationToken)
    {
        Set(Snapshot with { IsBusy = true, Error = null, Status = "正在读取本机隔离宿主凭据…" });
        try
        {
            IReadOnlyList<IsolatedCredentialSummary> credentials =
                await store.ListAsync(cancellationToken);
            Guid? selected = preferredId is Guid preferred &&
                credentials.Any(item => item.Id == preferred)
                ? preferred
                : credentials.FirstOrDefault()?.Id;
            preferredId = selected;
            Set(new PassportCredentialSnapshot
            {
                Credentials = credentials,
                SelectedCredentialId = selected,
                LoadWarning = store.LoadWarning,
                Status = operationStatus ??
                    (credentials.Count == 0
                        ? "本机隔离宿主暂无登录凭据；添加后仅用于此处与抽卡页刷新。"
                        : $"共 {credentials.Count} 条本机隔离宿主凭据；仅隔离宿主，生产凭据迁移待 UI-6 评审。")
            });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Set(Snapshot with
            {
                IsBusy = false,
                Status = "凭据读取失败；档案与账号事实未改变。",
                LoadWarning = store.LoadWarning,
                Error = Describe(exception)
            });
        }
    }

    private static string Describe(Exception exception) => exception switch
    {
        FormatException or InvalidDataException =>
            "本机凭据文件损坏或格式无效；已按空列表退化，未删除档案。",
        UnauthorizedAccessException or IOException =>
            "无法读取或写入本机隔离宿主凭据文件；未删除档案。",
        _ => "凭据操作未完成；档案与账号事实未改变。"
    };

    private void Set(PassportCredentialSnapshot snapshot) => Snapshot = snapshot;
}
