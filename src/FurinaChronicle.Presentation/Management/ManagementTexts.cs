// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Services.Archives;

namespace FurinaChronicle.Presentation.Management;

// Shared wording for the archive/account management page. The destructive confirmations mirror
// the MAUI host so both front ends communicate the same cascade scope and irreversibility.
public static class ManagementTexts
{
    public const string DeleteArchiveConfirmation =
        "确定要永久删除所选档案吗？档案内的全部账号和本地业务数据将被清除且无法撤销；" +
        "已经导出的文件、其他设备和云端备份不受影响。";

    public const string DeleteAccountConfirmation =
        "确定要永久删除所选游戏账号吗？该账号在当前档案中的本地业务数据将被清除且无法撤销；" +
        "已经导出的文件、其他档案、其他设备和云端备份不受影响。";

    public static string RegionLabel(GameServerRegion region) => region switch
    {
        GameServerRegion.ChinaOfficial => "官服（天空岛）",
        GameServerRegion.ChinaBilibili => "B 服（世界树）",
        GameServerRegion.America => "美服",
        GameServerRegion.Europe => "欧服",
        GameServerRegion.Asia => "亚服",
        GameServerRegion.TaiwanHongKongMacao => "港澳台服",
        _ => "区服未知"
    };

    public static string AccountLabel(GameAccount account) =>
        $"{account.DisplayName ?? "游戏账号"} · UID {account.Uid} · " +
        RegionLabel(account.ServerRegion);

    public static string IdentityStatus(
        GameAccount account,
        UnresolvedGameAccountDiagnostic? diagnostic)
    {
        if (diagnostic is not null)
        {
            return $"身份待确认：{UnresolvedReasonLabel(diagnostic.Reason)}";
        }

        return account.RoleIdentity is not null
            ? "身份已确认"
            : "身份未解析（缺少角色身份）";
    }

    public static string UnresolvedReasonLabel(UnresolvedGameAccountReason reason) =>
        reason switch
        {
            UnresolvedGameAccountReason.PlaceholderAccount => "占位账号，尚未完成身份确认",
            UnresolvedGameAccountReason.InvalidUid => "UID 结构无效",
            UnresolvedGameAccountReason.UidRegionMismatch => "UID 推断区服与记录不一致",
            UnresolvedGameAccountReason.UnknownServerRegion => "区服未知",
            _ => "缺少已解析角色身份"
        };

    public static string ArchiveCascadeWarning(int accountCount) =>
        $"级联范围：该档案内的 {accountCount} 个账号副本，以及它们的抽卡记录、修订和撤销材料" +
        "将一并清除；此操作不可撤销，也不影响已导出的文件、其他设备和云端备份。";

    public static string AccountCascadeWarning() =>
        "级联范围：该账号副本在当前档案中的抽卡记录、修订和撤销材料将一并清除；" +
        "此操作不可撤销，也不影响其他档案、已导出的文件、其他设备和云端备份。";

    // Only surface messages that business use cases deliberately write in Chinese. Unknown
    // exceptions must not leak paths or SQLite details into the UI.
    public static string DescribeOperationFailure(Exception exception) => exception switch
    {
        ArgumentException or InvalidOperationException or KeyNotFoundException
            when IsSafeMessage(exception.Message) => exception.Message,
        _ => "操作未完成；本地数据未被修改。请重新载入后重试。"
    };

    private static bool IsSafeMessage(string? message) =>
        !string.IsNullOrWhiteSpace(message) &&
        message.Length <= 160 &&
        !message.Contains('\\', StringComparison.Ordinal) &&
        !message.Contains('/', StringComparison.Ordinal) &&
        !message.Contains("SQLite", StringComparison.OrdinalIgnoreCase);
}
