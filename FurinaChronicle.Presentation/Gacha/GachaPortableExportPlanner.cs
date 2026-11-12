// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;

namespace FurinaChronicle.Presentation.Gacha;

// Resolves and describes the Portable export scope. The actual ZIP is produced by the production
// export file service after the host has a private temporary directory.
public sealed class GachaPortableExportPlanner(IGachaImportPlanningSource planningSource)
{
    public async Task<GachaPortableExportPlanResult> PlanAsync(
        Guid archiveId,
        IReadOnlyCollection<Guid> accountIds,
        CancellationToken cancellationToken = default)
    {
        if (archiveId == Guid.Empty)
        {
            return new(null, "请先选择要导出的档案。");
        }

        Guid[] requested = accountIds.Distinct().ToArray();
        if (requested.Length == 0)
        {
            return new(null, "请选择要导出的账号范围。");
        }

        IReadOnlyList<PlayerArchive> archives;
        IReadOnlyList<GameAccount> allAccounts;
        try
        {
            archives = await planningSource.GetArchivesAsync(cancellationToken).ConfigureAwait(false);
            PlayerArchive? archive = archives.FirstOrDefault(item => item.Id == archiveId);
            if (archive is null)
            {
                return new(null, "目标档案已不存在，请重新选择。");
            }

            allAccounts = await planningSource.GetAccountsAsync(archiveId, cancellationToken)
                .ConfigureAwait(false);
            GameAccount[] selected = allAccounts
                .Where(account => requested.Contains(account.Id))
                .OrderBy(account => account.DisplayName, StringComparer.Ordinal)
                .ToArray();
            if (selected.Length != requested.Length)
            {
                return new(null, "所选账号中有已失效的账号，请重新选择导出范围。");
            }

            GachaScopeOption[] options = selected.Select(account =>
                new GachaScopeOption(account.Id, AccountLabel(account)) { Region = account.ServerRegion })
                .ToArray();
            string scope = selected.Length == allAccounts.Count
                ? $"档案全部账号（{selected.Length} 个）"
                : selected.Length == 1
                    ? $"当前所选账号：{options[0].Label}"
                    : $"已选 {selected.Length} 个账号";
            var text = new List<string>
            {
                $"档案：{archive.Name}",
                $"范围：{scope}",
                "账号："
            };
            text.AddRange(options.Select(option => $"· {option.Label}"));
            text.Add("格式：Furina Gacha Portable v1.0（ZIP 抽卡数据集）");
            text.Add("内容：所选账号的抽卡事实与来源 provenance；本机接收信息写入独立 Receipt，不进入包内容。");
            text.Add("边界：这是抽卡数据集，不是完整应用备份；不包含档案设置、凭据或其他领域数据。");
            text.Add("导出说明：先在私有临时目录生成并校验，再打开系统保存对话框；取消保存会删除临时文件，不报告导出成功。");
            return new(
                new GachaPortableExportPlan(archive.Id, archive.Name, options, scope, string.Join(Environment.NewLine, text)),
                null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return new(null, "无法读取本地档案与账号范围，未生成导出文件。请稍后重试。");
        }
    }

    private static string AccountLabel(GameAccount account) =>
        $"{account.DisplayName ?? "游戏账号"} · UID {account.Uid} · " +
        GachaImportText.RegionLabel(account.ServerRegion);
}

internal static class GachaPortableExportText
{
    public static string FormatSaved(string fileName, int accountCount, int recordCount) =>
        $"已保存 {fileName}：{accountCount} 个账号、{recordCount} 条记录。" +
        "这是 Furina 抽卡数据集（Portable v1），不是完整应用备份。";
}
