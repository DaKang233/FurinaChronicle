// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Services.Archives.Portable;

namespace FurinaChronicle.Presentation.Management;

// UI-side mirror of the service's Replace scope shape/overlap rules. It is advisory: the formal
// planner validates again before planning, so a stale or hand-edited selection can never bypass
// the service boundary.
public static class ArchiveReplaceScopeValidator
{
    private static readonly IReadOnlySet<ArchiveReplaceDomain> AllDomains =
        Enum.GetValues<ArchiveReplaceDomain>().ToHashSet();

    public static string? Validate(
        ArchiveCatalog catalog,
        IReadOnlyList<ArchiveReplaceSelection> selections)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(selections);
        if (selections.Count == 0)
        {
            return "Replace 需要至少选择一个范围（Archive / Account / Domain）。";
        }

        if (selections.Distinct().Count() != selections.Count)
        {
            return "Replace 范围不能重复；请调整后重试，未写入任何数据。";
        }

        IReadOnlyDictionary<Guid, ArchiveCatalogArchive> archives =
            catalog.Archives.ToDictionary(item => item.ArchiveReference);
        var covered = new HashSet<(Guid Archive, Guid Account, ArchiveReplaceDomain Domain)>();
        foreach (ArchiveReplaceSelection selection in selections)
        {
            if (selection.SourceArchiveReference == Guid.Empty ||
                !archives.TryGetValue(
                    selection.SourceArchiveReference,
                    out ArchiveCatalogArchive? archive))
            {
                return "Replace 范围引用了不存在的来源档案；未写入任何数据。";
            }

            Guid[] accountIds;
            IReadOnlySet<ArchiveReplaceDomain> domains;
            switch (selection.Kind)
            {
                case ArchiveReplaceScopeKind.Archive
                    when selection.SourceAccountReference is null &&
                         selection.Domain is null:
                    accountIds = archive.Accounts
                        .Select(item => item.AccountReference)
                        .ToArray();
                    domains = AllDomains;
                    break;
                case ArchiveReplaceScopeKind.Account
                    when selection.SourceAccountReference is Guid accountId &&
                         selection.Domain is null:
                    if (archive.Accounts.All(item => item.AccountReference != accountId))
                    {
                        return "Replace 范围引用了该档案中不存在的账号；未写入任何数据。";
                    }

                    accountIds = [accountId];
                    domains = AllDomains;
                    break;
                case ArchiveReplaceScopeKind.Domain
                    when selection.SourceAccountReference is Guid accountId &&
                         selection.Domain is ArchiveReplaceDomain domain &&
                         Enum.IsDefined(domain):
                    if (archive.Accounts.All(item => item.AccountReference != accountId))
                    {
                        return "Replace 范围引用了该档案中不存在的账号；未写入任何数据。";
                    }

                    accountIds = [accountId];
                    domains = new HashSet<ArchiveReplaceDomain> { domain };
                    break;
                default:
                    return "Replace 范围形状无效；未写入任何数据。";
            }

            foreach (Guid accountId in accountIds)
            foreach (ArchiveReplaceDomain domain in domains)
            {
                if (!covered.Add((selection.SourceArchiveReference, accountId, domain)))
                {
                    return "Replace 范围重叠（同一档案/账号/领域被多个范围覆盖）；" +
                        "请调整范围后重试，未写入任何数据。";
                }
            }
        }

        return null;
    }

    public static string Describe(
        ArchiveCatalog? catalog,
        ArchiveReplaceSelection selection)
    {
        string archiveName = catalog?.Archives
            .FirstOrDefault(item => item.ArchiveReference == selection.SourceArchiveReference)
            ?.Name ?? selection.SourceArchiveReference.ToString("N")[..8];
        if (selection.Kind == ArchiveReplaceScopeKind.Archive)
        {
            return $"整个档案：{archiveName}";
        }

        string account = selection.SourceAccountReference is Guid accountId
            ? catalog?.Archives
                .SelectMany(item => item.Accounts)
                .FirstOrDefault(item => item.AccountReference == accountId)
                ?.LegacyUid ?? accountId.ToString("N")[..8]
            : "未指定账号";
        return selection.Kind == ArchiveReplaceScopeKind.Account
            ? $"档案 {archiveName} / 账号 {account}（全部领域）"
            : $"档案 {archiveName} / 账号 {account} / 领域 {DomainLabel(selection.Domain)}";
    }

    public static string DomainLabel(ArchiveReplaceDomain? domain) => domain switch
    {
        ArchiveReplaceDomain.Gacha => "抽卡",
        ArchiveReplaceDomain.ItemObservation => "角色/武器观察",
        ArchiveReplaceDomain.ManualAcquisition => "手工获取",
        ArchiveReplaceDomain.ChallengeObservation => "挑战观察",
        _ => "未指定"
    };
}
