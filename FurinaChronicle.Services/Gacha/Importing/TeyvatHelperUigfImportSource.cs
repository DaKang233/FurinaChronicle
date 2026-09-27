using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Passport;
using FurinaChronicle.Services.Gacha.Refreshing;
using FurinaChronicle.Services.Passport;

namespace FurinaChronicle.Services.Gacha.Importing;

public sealed class TeyvatHelperUigfImportSource(
    IPassportAccountStore accountStore,
    IPassportSelectionStore selectionStore,
    ISTokenGachaUrlProvider sTokenGachaUrlProvider,
    ITeyvatHelperUigfClient client)
{
    public async Task<TeyvatHelperImportAvailability> GetAvailabilityAsync(
        CancellationToken cancellationToken = default)
    {
        SelectedPassportRole? selected = await LoadSelectedRoleAsync(
            required: false,
            cancellationToken);
        if (selected is null)
        {
            return new TeyvatHelperImportAvailability(
                SelectedRoleUid: null,
                CanAutomaticallyImport: false);
        }

        GameServerRegion region = GameServerRegionResolver.Resolve(
            selected.Uid);
        bool canAutomaticallyImport =
            selected.Account.Realm == PassportRealm.MainlandChina &&
            selected.Account.Credentials.SToken is not null &&
            selected.Account.Mid is not null &&
            region is GameServerRegion.ChinaOfficial or
                GameServerRegion.ChinaBilibili;
        return new TeyvatHelperImportAvailability(
            selected.Uid,
            canAutomaticallyImport);
    }

    public async Task<TeyvatHelperUigfDownload> DownloadForSelectedRoleAsync(
        CancellationToken cancellationToken = default)
    {
        SelectedPassportRole selected = await LoadSelectedRoleAsync(
            required: true,
            cancellationToken)
            ?? throw new InvalidOperationException(
                "请先在用户页登录通行证账号并选择原神角色。");

        if (selected.Account.Realm != PassportRealm.MainlandChina)
        {
            throw new NotSupportedException(
                "自动生成抽卡链接目前仅支持国服米哈游通行证；国际服可使用手动方式提供 UID 和抽卡链接。");
        }

        if (selected.Account.Credentials.SToken is null ||
            selected.Account.Mid is null)
        {
            throw new InvalidOperationException(
                "自动生成抽卡链接要求所选通行证账号同时包含 SToken 和 MID；请重新登录或使用手动方式。");
        }

        GameServerRegion region = GameServerRegionResolver.Resolve(
            selected.Uid);
        if (region is not GameServerRegion.ChinaOfficial and
            not GameServerRegion.ChinaBilibili)
        {
            throw new NotSupportedException(
                "自动生成抽卡链接目前仅支持国服原神角色。");
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        var gameAccount = new GameAccount(
            Guid.NewGuid(),
            Guid.NewGuid(),
            selected.Uid,
            region,
            DisplayName: null,
            IsPlaceholder: false,
            now,
            now);

        // Do not cache this URI: its authkey is temporary and must be generated
        // immediately before it is sent to the explicitly selected third party.
        Uri freshGachaUrl = await sTokenGachaUrlProvider.CreateAsync(
            selected.Account,
            gameAccount,
            cancellationToken);
        return await client.DownloadAsync(
            selected.Uid,
            freshGachaUrl,
            cancellationToken);
    }

    public async Task<TeyvatHelperUigfDownload> DownloadManuallyAsync(
        string uid,
        string gachaUrl,
        CancellationToken cancellationToken = default)
    {
        string normalizedUid = uid?.Trim() ?? string.Empty;
        if (!GameUidValidation.IsValidUid(normalizedUid))
        {
            throw new ArgumentException(
                "请输入有效的原神 UID。",
                nameof(uid));
        }

        Uri validatedGachaUrl = GachaRefreshUrl.Parse(gachaUrl);
        return await client.DownloadAsync(
            normalizedUid,
            validatedGachaUrl,
            cancellationToken);
    }

    private async Task<SelectedPassportRole?> LoadSelectedRoleAsync(
        bool required,
        CancellationToken cancellationToken)
    {
        PassportSelection? selection = await selectionStore.LoadAsync(
            cancellationToken);
        if (selection is null ||
            string.IsNullOrWhiteSpace(selection.GameUid))
        {
            if (required)
            {
                throw new InvalidOperationException(
                    "请先在用户页登录通行证账号并选择原神角色。");
            }

            return null;
        }

        PassportAccount? account = await accountStore.GetByIdAsync(
            selection.PassportAccountId,
            cancellationToken);
        if (account is null)
        {
            if (required)
            {
                throw new InvalidOperationException(
                    "所选通行证账号已不存在，请返回用户页重新选择。");
            }

            return null;
        }

        string uid = selection.GameUid.Trim();
        return GameUidValidation.IsValidUid(uid)
            ? new SelectedPassportRole(account, uid)
            : required
                ? throw new InvalidOperationException(
                    "所选原神角色的 UID 无效，请返回用户页重新选择。")
                : null;
    }

    private sealed record SelectedPassportRole(
        PassportAccount Account,
        string Uid);
}
