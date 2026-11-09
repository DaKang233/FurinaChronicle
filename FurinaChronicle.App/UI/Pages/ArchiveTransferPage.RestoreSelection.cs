// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Services.Archives.Portable;

namespace FurinaChronicle.App.UI.Pages;

public partial class ArchiveTransferPage
{
    private async Task<ArchiveReplaceSelection?> SelectReplaceScopeAsync()
    {
        ArchiveCatalog catalog = ViewModel.StagedCatalog ??
            throw new InvalidOperationException("Archive catalog 不存在。");
        ArchiveCatalogArchive? sourceArchive = await ChooseSourceArchiveAsync(
            catalog.Archives);
        if (sourceArchive is null)
        {
            return null;
        }

        const string archiveScope = "整个来源档案";
        const string accountScope = "一个来源账号";
        const string domainScope = "一个账号的单一领域";
        string? scope = await DisplayActionSheetAsync(
            "选择 Replace 范围",
            CancelOption,
            destruction: null,
            archiveScope,
            accountScope,
            domainScope);
        if (scope is null or CancelOption)
        {
            return null;
        }
        if (scope == archiveScope)
        {
            return new ArchiveReplaceSelection(
                sourceArchive.ArchiveReference,
                ArchiveReplaceScopeKind.Archive);
        }

        ArchiveCatalogAccount? sourceAccount =
            await ChooseSourceAccountAsync(sourceArchive.Accounts);
        if (sourceAccount is null)
        {
            return null;
        }
        if (scope == accountScope)
        {
            return new ArchiveReplaceSelection(
                sourceArchive.ArchiveReference,
                ArchiveReplaceScopeKind.Account,
                sourceAccount.AccountReference);
        }

        var domains = new Dictionary<string, ArchiveReplaceDomain>(
            StringComparer.Ordinal)
        {
            ["抽卡当前事实与修订"] = ArchiveReplaceDomain.Gacha,
            ["角色／武器物品观察"] = ArchiveReplaceDomain.ItemObservation,
            ["手工获取与修订"] = ArchiveReplaceDomain.ManualAcquisition,
            ["挑战观察"] = ArchiveReplaceDomain.ChallengeObservation
        };
        string? selected = await DisplayActionSheetAsync(
            "选择 Replace 领域",
            CancelOption,
            destruction: null,
            domains.Keys.ToArray());
        if (selected is null or CancelOption ||
            !domains.TryGetValue(selected, out ArchiveReplaceDomain domain))
        {
            return null;
        }
        return new ArchiveReplaceSelection(
            sourceArchive.ArchiveReference,
            ArchiveReplaceScopeKind.Domain,
            sourceAccount.AccountReference,
            domain);
    }

    private async Task<bool> ResolveMappingsAsync()
    {
        while (true)
        {
            ArchiveRestoreArchiveMapping? unresolvedArchive =
                ViewModel.CurrentPlan?.Archives.FirstOrDefault(item =>
                    item.Kind ==
                    ArchiveRestoreArchiveMappingKind.RequiresSelection);
            if (unresolvedArchive is null)
            {
                break;
            }

            const string createNew = "创建新的目标档案";
            var options = new Dictionary<string, Guid>(StringComparer.Ordinal);
            foreach (ArchiveRestoreArchiveCandidate candidate in
                unresolvedArchive.Candidates)
            {
                string label =
                    $"{(candidate.IsExactMatch ? "同名" : "相似")}：{candidate.Name} " +
                    $"({candidate.ArchiveId.ToString("N")[..8]})";
                options[label] = candidate.ArchiveId;
            }
            string? selected = await DisplayActionSheetAsync(
                $"来源档案“{unresolvedArchive.SourceName}”映射到",
                CancelOption,
                destruction: null,
                options.Keys.Append(createNew).ToArray());
            if (selected is null or CancelOption)
            {
                return false;
            }
            await ViewModel.ResolveArchiveAsync(
                unresolvedArchive.SourceArchiveReference,
                options.TryGetValue(selected, out Guid archiveId)
                    ? archiveId
                    : null);
        }

        while (true)
        {
            ArchiveRestoreAccountMapping? unresolvedAccount =
                ViewModel.CurrentPlan?.Accounts.FirstOrDefault(item =>
                    item.Kind ==
                    ArchiveRestoreAccountMappingKind.RequiresSelection);
            if (unresolvedAccount is null)
            {
                return true;
            }
            if (unresolvedAccount.TargetArchiveId is not Guid targetArchiveId)
            {
                return false;
            }

            IReadOnlyList<GameAccount> targetAccounts =
                await ViewModel.GetTargetAccountsAsync(targetArchiveId);
            var options = new Dictionary<string, Guid>(StringComparer.Ordinal);
            foreach (GameAccount account in targetAccounts.Where(item =>
                item.IdentityResolutionState ==
                GameRoleIdentityResolutionState.Unresolved))
            {
                string displayName = string.IsNullOrWhiteSpace(account.DisplayName)
                    ? account.Uid
                    : $"{account.DisplayName} ({account.Uid})";
                options[$"{displayName} ({account.Id.ToString("N")[..8]})"] =
                    account.Id;
            }
            const string createNew = "创建新的 Unresolved 账号副本";
            string? selected = await DisplayActionSheetAsync(
                "Unresolved 来源账号必须明确映射",
                CancelOption,
                destruction: null,
                options.Keys.Append(createNew).ToArray());
            if (selected is null or CancelOption)
            {
                return false;
            }
            await ViewModel.ResolveAccountAsync(
                unresolvedAccount.SourceAccountReference,
                options.TryGetValue(selected, out Guid accountId)
                    ? accountId
                    : null);
        }
    }

    private async Task<ArchiveCatalogArchive?> ChooseSourceArchiveAsync(
        IReadOnlyList<ArchiveCatalogArchive> archives)
    {
        var options = archives.ToDictionary(
            item => $"{item.Name} ({item.ArchiveReference.ToString("N")[..8]})",
            item => item,
            StringComparer.Ordinal);
        string? selected = await DisplayActionSheetAsync(
            "选择来源档案",
            CancelOption,
            destruction: null,
            options.Keys.ToArray());
        return selected is not null &&
            options.TryGetValue(selected, out ArchiveCatalogArchive? archive)
                ? archive
                : null;
    }

    private async Task<ArchiveCatalogAccount?> ChooseSourceAccountAsync(
        IReadOnlyList<ArchiveCatalogAccount> accounts)
    {
        var options = accounts.ToDictionary(
            item =>
            {
                string name = string.IsNullOrWhiteSpace(item.DisplayName)
                    ? item.LegacyUid
                    : $"{item.DisplayName} ({item.LegacyUid})";
                return $"{name} ({item.AccountReference.ToString("N")[..8]})";
            },
            item => item,
            StringComparer.Ordinal);
        string? selected = await DisplayActionSheetAsync(
            "选择来源账号",
            CancelOption,
            destruction: null,
            options.Keys.ToArray());
        return selected is not null &&
            options.TryGetValue(selected, out ArchiveCatalogAccount? account)
                ? account
                : null;
    }

    private static FilePickerFileType ArchiveFileTypes() => new(
        new Dictionary<DevicePlatform, IEnumerable<string>>
        {
            [DevicePlatform.WinUI] = [".fca", ".zip"],
            [DevicePlatform.Android] =
                ["application/zip", "application/octet-stream"]
        });
}
