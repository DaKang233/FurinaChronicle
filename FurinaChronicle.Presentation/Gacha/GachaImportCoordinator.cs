// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Text.Json;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Services.Gacha;
using FurinaChronicle.Services.Gacha.Importing;

namespace FurinaChronicle.Presentation.Gacha;

// Plans a UIGF file import without materializing its records for the UI, then commits through
// the production use case only after re-validating the frozen target. Expected failures never
// reach a write: the flow returns a message and keeps the existing database untouched.
public sealed class GachaImportCoordinator(
    IGachaImportReader reader,
    IGachaUigfImporter importer,
    IGachaImportPlanningSource planningSource)
{
    private static readonly HashSet<string> SupportedVersions =
        new(StringComparer.Ordinal)
        {
            "v2.2", "v2.3", "v2.4", "v3.0", "v4.0", "v4.1", "v4.2"
        };

    public async Task<GachaImportSourceReadResult> ReadSourceAsync(
        GachaImportSource source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();

        string version;
        try
        {
            await using Stream headerStream = await OpenAsync(source, cancellationToken).ConfigureAwait(false);
            version = await DetectVersionAsync(headerStream, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (GachaImportSourceException exception)
        {
            return new(null, exception.Message);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new(null, "无法读取所选文件，未写入任何数据。请确认文件仍可访问后重试。");
        }
        catch (Exception)
        {
            return new(null, "无法读取所选文件，未写入任何数据。");
        }

        GachaReadResult readResult;
        try
        {
            await using Stream stream = await OpenAsync(source, cancellationToken).ConfigureAwait(false);
            readResult = await reader.ReadAsync(stream, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (GachaImportFormatException exception)
        {
            return new(null, GachaImportText.DescribeReaderFailure(exception.Message, version));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new(null, "无法读取所选文件，未写入任何数据。请确认文件仍可访问后重试。");
        }
        catch (Exception)
        {
            return new(null, "UIGF 文件校验失败，未写入任何数据。请确认文件版本与内容后重试。");
        }

        return new(BuildSummary(source, version, readResult), null);
    }

    public async Task<GachaImportPlanResult> PlanAsync(
        GachaImportSourceSummary source,
        GachaImportTargetRequest target,
        GachaImportMode mode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        if (mode is not (GachaImportMode.Incremental or GachaImportMode.Full))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<PlayerArchive> archives;
        try
        {
            archives = await planningSource.GetArchivesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return FailedPlan("无法读取本地档案列表，未写入任何数据。请稍后重试。");
        }

        PlayerArchive? archive = archives.FirstOrDefault(item => item.Id == target.ArchiveId);
        if (archive is null)
        {
            return FailedPlan("目标档案已不存在，未写入任何数据。请重新选择档案。");
        }

        IReadOnlyList<GameAccount> accounts;
        try
        {
            accounts = await planningSource.GetAccountsAsync(archive.Id, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return FailedPlan("无法读取目标档案的账号副本，未写入任何数据。请稍后重试。");
        }

        return target.Scope == GachaImportTargetScope.CurrentAccount
            ? PlanCurrentAccount(source, target, mode, archive, accounts)
            : PlanArchiveAccounts(source, target, mode, archive, accounts);
    }

    public async Task<GachaImportExecution> ExecuteAsync(
        GachaImportPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!plan.CanExecute)
        {
            return new(GachaImportExecutionStatus.Rejected, null,
                plan.BlockMessage ?? "导入预览不可执行，未写入任何数据。");
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Re-plan from the captured target. Any target or account-copy change invalidates the
        // preview and prevents the commit, even if the page's selection changed meanwhile.
        GachaImportPlanResult revalidated = await PlanAsync(
            plan.Source,
            plan.Request,
            plan.Mode,
            cancellationToken).ConfigureAwait(false);
        if (!revalidated.Succeeded || revalidated.Plan is null)
        {
            return new(GachaImportExecutionStatus.Rejected, null,
                "目标校验失败，原预览已失效，未写入任何数据。请重新选择目标并生成预览。" +
                (revalidated.FailureMessage is null ? "" : $"\n{revalidated.FailureMessage}"));
        }

        GachaImportPlan current = revalidated.Plan;
        if (current.Target.Scope != plan.Target.Scope ||
            current.Target.ArchiveId != plan.Target.ArchiveId ||
            current.Target.GameAccountId != plan.Target.GameAccountId ||
            !string.Equals(current.Target.Uid, plan.Target.Uid, StringComparison.Ordinal))
        {
            return new(GachaImportExecutionStatus.Rejected, null,
                "目标范围已变化，原预览已失效，未写入任何数据。请重新选择目标并生成预览。");
        }

        if (!current.CanExecute)
        {
            return new(GachaImportExecutionStatus.Rejected, null,
                current.BlockMessage ?? "目标校验未通过，未写入任何数据。");
        }

        try
        {
            await using Stream stream = await OpenAsync(plan.Source.Source, cancellationToken).ConfigureAwait(false);
            GachaImportResult result = await importer.ExecuteAsync(
                stream,
                plan.Target.ArchiveId,
                plan.Target.Scope == GachaImportTargetScope.CurrentAccount ? plan.Target.GameAccountId : null,
                plan.Mode,
                cancellationToken).ConfigureAwait(false);
            return new(GachaImportExecutionStatus.Succeeded, result, GachaImportText.BuildSavedFacts(plan, result));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (GachaImportFormatException exception)
        {
            return new(GachaImportExecutionStatus.Failed, null,
                GachaImportText.DescribeImportFailure(exception.Message));
        }
        catch (KeyNotFoundException)
        {
            return new(GachaImportExecutionStatus.Rejected, null,
                "目标账号或档案已不存在，未写入任何数据。请重新选择目标并生成预览。");
        }
        catch (InvalidOperationException)
        {
            return new(GachaImportExecutionStatus.Rejected, null,
                "目标账号或档案已变化，未写入任何数据。请重新选择目标并生成预览。");
        }
        catch (IOException exception) when (exception.Message.Contains("history cleanup", StringComparison.OrdinalIgnoreCase))
        {
            return new(GachaImportExecutionStatus.Failed, null,
                "导入需要历史清理确认，当前入口不提供该确认，未写入任何数据。");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new(GachaImportExecutionStatus.Failed, null,
                "无法读取所选文件或目标数据库，未写入任何数据。请重试。");
        }
        catch (Exception)
        {
            return new(GachaImportExecutionStatus.Failed, null,
                "导入未完成，未确认任何记录被修改；请重新载入后核对并重试。");
        }
    }

    private static GachaImportPlanResult PlanCurrentAccount(
        GachaImportSourceSummary source,
        GachaImportTargetRequest target,
        GachaImportMode mode,
        PlayerArchive archive,
        IReadOnlyList<GameAccount> accounts)
    {
        if (target.GameAccountId is not Guid accountId || accountId == Guid.Empty)
        {
            return FailedPlan("未指定导入目标账号，未写入任何数据。请重新选择目标。");
        }

        GameAccount? account = accounts.FirstOrDefault(item => item.Id == accountId);
        if (account is null)
        {
            return FailedPlan("目标账号已不存在，未写入任何数据。请重新选择档案和账号。");
        }

        if (account.PlayerArchiveId != archive.Id)
        {
            return FailedPlan("目标账号不属于当前档案，未写入任何数据。请重新选择档案和账号。");
        }

        GachaImportSourceAccountSummary? match = source.Accounts.FirstOrDefault(
            item => string.Equals(item.Uid, account.Uid, StringComparison.Ordinal));

        string? block = null;
        if (match is null)
        {
            string fileUids = source.Accounts.Count == 0
                ? "文件中没有任何账号记录"
                : $"文件包含 UID {string.Join("、", source.Accounts.Select(item => item.Uid))}";
            block = $"文件中没有目标账号 UID {account.Uid} 的记录（{fileUids}），未写入任何数据。" +
                "请选择包含该账号的 UIGF 文件，或改用“当前档案全部账号”。";
        }
        else if (match.ErrorCount > 0)
        {
            block = $"文件中账号 UID {account.Uid} 的记录存在 {match.ErrorCount} 处解析问题；" +
                "服务会拒绝该账号的导入，未写入任何数据。";
        }
        else if (match.ParsedRecordCount == 0)
        {
            block = $"文件中账号 UID {account.Uid} 没有可导入的记录，未写入任何数据。";
        }
        else if (match.Region == GameServerRegion.Unknown)
        {
            block = $"无法从文件中的 UID {account.Uid} 推断区服，服务会拒绝该账号的导入，未写入任何数据。";
        }
        else if (!MatchesTargetIdentity(account, match))
        {
            block = $"文件中的 UID 与目标账号身份不一致：文件为 UID {match.Uid}" +
                $"（{GachaImportText.RegionLabel(match.Region)}），" +
                $"目标账号为 UID {account.Uid}（{GachaImportText.RegionLabel(account.ServerRegion)}）。" +
                "未写入任何数据。";
        }

        var previews = source.Accounts.Select(item =>
        {
            bool isTarget = string.Equals(item.Uid, account.Uid, StringComparison.Ordinal);
            return new GachaImportSourceAccountPreview(
                item.Uid,
                item.Region,
                item.ParsedRecordCount,
                item.ErrorCount)
            {
                ExistingAccount = isTarget,
                ExistingAccountLabel = isTarget ? AccountIdentity(account) : null,
                MatchesTarget = isTarget,
                Ignored = !isTarget
            };
        }).ToArray();

        var plan = new GachaImportPlan
        {
            Source = source,
            Request = target,
            Target = new(
                GachaImportTargetScope.CurrentAccount,
                archive.Id,
                archive.Name,
                account.Id,
                account.DisplayName,
                account.Uid,
                account.ServerRegion),
            Mode = mode,
            Accounts = previews,
            ImportableRecordCount = match?.ParsedRecordCount ?? 0,
            IgnoredRecordCount = previews.Where(item => item.Ignored).Sum(item => item.ParsedRecordCount),
            BlockMessage = block
        };
        return new(plan, null);
    }

    private static GachaImportPlanResult PlanArchiveAccounts(
        GachaImportSourceSummary source,
        GachaImportTargetRequest target,
        GachaImportMode mode,
        PlayerArchive archive,
        IReadOnlyList<GameAccount> accounts)
    {
        if (source.ParsedRecordCount == 0)
        {
            string message = source.ErrorCount > 0
                ? $"文件中的记录均无法解析（{source.ErrorCount} 处解析问题），未写入任何数据。"
                : "文件没有可导入的记录，未写入任何数据。";
            return FailedPlan(message);
        }

        var previews = new List<GachaImportSourceAccountPreview>(source.Accounts.Count);
        foreach (GachaImportSourceAccountSummary item in source.Accounts)
        {
            bool regionKnown = item.Region != GameServerRegion.Unknown;
            bool skipped = item.ErrorCount > 0 || !regionKnown;
            GameAccount? existing = null;
            if (!skipped && GenshinGameRoleIdentity.TryCreate(item.Uid, item.Region, out GameRoleNaturalIdentity? identity))
            {
                existing = accounts.FirstOrDefault(
                    account => account.RoleIdentity?.NaturalIdentity == identity);
            }
            else if (!skipped)
            {
                // The production service rejects a UID whose region cannot be resolved; never
                // promise a copy that the service cannot create.
                skipped = true;
            }

            previews.Add(new(item.Uid, item.Region, item.ParsedRecordCount, item.ErrorCount)
            {
                ExistingAccount = existing is not null,
                ExistingAccountLabel = existing is null ? null : AccountIdentity(existing),
                WillCreateAccount = !skipped && existing is null,
                Skipped = skipped
            });
        }

        GachaImportSourceAccountPreview[] accountPreviews = previews.ToArray();
        int importable = accountPreviews.Where(item => !item.Skipped).Sum(item => item.ParsedRecordCount);
        int skippedRecords = accountPreviews.Where(item => item.Skipped).Sum(item => item.ParsedRecordCount);
        if (importable == 0)
        {
            return FailedPlan(source.ErrorCount > 0
                ? $"文件中的记录无法导入（{source.ErrorCount} 处解析问题或区服无法判断），未写入任何数据。"
                : "文件没有可导入的记录，未写入任何数据。");
        }

        var plan = new GachaImportPlan
        {
            Source = source,
            Request = target,
            Target = new(GachaImportTargetScope.ArchiveAccounts, archive.Id, archive.Name, null, null, null, null),
            Mode = mode,
            Accounts = accountPreviews,
            ImportableRecordCount = importable,
            IgnoredRecordCount = 0,
            SkippedRecordCount = skippedRecords
        };
        return new(plan, null);
    }

    private static bool MatchesTargetIdentity(GameAccount account, GachaImportSourceAccountSummary sourceAccount)
    {
        GameRoleNaturalIdentity? targetIdentity = account.RoleIdentity?.NaturalIdentity;
        if (targetIdentity is null &&
            !GenshinGameRoleIdentity.TryCreate(account.Uid, account.ServerRegion, out targetIdentity))
        {
            return false;
        }

        if (!GenshinGameRoleIdentity.TryCreate(sourceAccount.Uid, sourceAccount.Region, out GameRoleNaturalIdentity? fileIdentity))
        {
            return false;
        }

        return targetIdentity == fileIdentity;
    }

    private static string AccountIdentity(GameAccount account) =>
        $"{account.DisplayName ?? "游戏账号"} · UID {account.Uid} · " +
        GachaImportText.RegionLabel(account.ServerRegion);

    private static GachaImportPlanResult FailedPlan(string message) => new(null, message);

    private static Task<Stream> OpenAsync(GachaImportSource source, CancellationToken cancellationToken) =>
        source.OpenReadAsync(cancellationToken);

    private static async Task<string> DetectVersionAsync(Stream stream, CancellationToken cancellationToken)
    {
        JsonDocument document;
        try
        {
            document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            throw new GachaImportSourceException(
                "所选文件不是有效的 JSON，未写入任何数据。请选择 UIGF 抽卡 JSON 文件。");
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new GachaImportSourceException(
                    "所选 JSON 的根节点不是对象，无法识别为 UIGF 文件。未写入任何数据。");
            }

            if (!root.TryGetProperty("info", out JsonElement info) ||
                info.ValueKind != JsonValueKind.Object)
            {
                throw new GachaImportSourceException(
                    "所选 JSON 缺少 UIGF 的 info 对象，未写入任何数据。");
            }

            string? version = ReadVersion(info, "uigf_version") ?? ReadVersion(info, "version");
            if (version is null && info.TryGetProperty("srgf_version", out _))
            {
                throw new GachaImportSourceException(
                    "这是 SRGF（星穹铁道）文件，不是原神 UIGF 抽卡文件。未写入任何数据。");
            }

            if (version is null)
            {
                throw new GachaImportSourceException(
                    "无法识别 UIGF 版本。请选择 UIGF v2.2、v2.3、v2.4、v3.0、v4.0、v4.1 或 v4.2 的抽卡 JSON 文件。未写入任何数据。");
            }

            if (!SupportedVersions.Contains(version))
            {
                throw new GachaImportSourceException(
                    $"不支持的 UIGF 版本：{version}。支持 v2.2、v2.3、v2.4、v3.0、v4.0、v4.1、v4.2。未写入任何数据。");
            }

            return version;
        }
    }

    private static string? ReadVersion(JsonElement info, string name)
    {
        if (!info.TryGetProperty(name, out JsonElement value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => string.IsNullOrWhiteSpace(value.GetString())
                ? null
                : value.GetString()!.Trim(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    private static GachaImportSourceSummary BuildSummary(
        GachaImportSource source,
        string version,
        GachaReadResult readResult)
    {
        var parsedByUid = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (GachaSourceAccount account in readResult.Accounts)
        {
            parsedByUid[account.Uid] = parsedByUid.GetValueOrDefault(account.Uid) + account.Records.Count;
        }

        var errorsByUid = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (GachaReadError error in readResult.Errors)
        {
            if (!string.IsNullOrWhiteSpace(error.Uid))
            {
                errorsByUid[error.Uid!] = errorsByUid.GetValueOrDefault(error.Uid!) + 1;
            }
        }

        GachaImportSourceAccountSummary[] accounts = parsedByUid.Keys
            .Union(errorsByUid.Keys, StringComparer.Ordinal)
            .OrderBy(uid => uid, StringComparer.Ordinal)
            .Select(uid => new GachaImportSourceAccountSummary(
                uid,
                parsedByUid.GetValueOrDefault(uid),
                errorsByUid.GetValueOrDefault(uid))
            {
                Region = GameServerRegionResolver.Resolve(uid)
            })
            .ToArray();

        return new GachaImportSourceSummary
        {
            Source = source,
            FormatVersion = version,
            TotalRecordCount = readResult.TotalRecordCount,
            Accounts = accounts,
            ErrorCount = readResult.Errors.Count
        };
    }

    private sealed class GachaImportSourceException(string message) : Exception(message);
}

// Chinese user-facing wording for planning, confirmation and result surfaces. Keeping it here
// means both hosts render identical semantics and tests can assert on it without a UI.
internal static class GachaImportText
{
    public static string RegionLabel(GameServerRegion region) => region switch
    {
        GameServerRegion.ChinaOfficial => "官服",
        GameServerRegion.ChinaBilibili => "B 服",
        GameServerRegion.America => "美服",
        GameServerRegion.Europe => "欧服",
        GameServerRegion.Asia => "亚服",
        GameServerRegion.TaiwanHongKongMacao => "港澳台服",
        _ => "区服未知"
    };

    public static string FormatLabel(string version)
    {
        string normalized = version.Trim();
        return normalized switch
        {
            "v4.2" => "UIGF 4.2",
            "v4.0" or "v4.1" => $"UIGF {normalized.TrimStart('v')}（兼容读取）",
            "v2.2" or "v2.3" or "v2.4" or "v3.0" =>
                $"UIGF {normalized.TrimStart('v')}（兼容读取）",
            _ => $"UIGF {normalized}"
        };
    }

    public static string BuildConfirmationText(GachaImportPlan plan)
    {
        var lines = new List<string>
        {
            $"文件：{plan.Source.FileName}",
            $"识别格式：{plan.Source.FormatLabel}（源数据版本 {plan.Source.FormatVersion}）",
            $"目标：{plan.Target.ScopeLabel} · {plan.Target.ArchiveName}"
        };

        if (plan.Target.AccountIdentity is string identity)
        {
            lines.Add($"账号：{identity}");
        }

        lines.Add($"模式：{plan.ModeOption.Label}");
        lines.Add($"模式说明：{plan.ModeOption.Description}");
        lines.Add($"源记录共 {plan.Source.TotalRecordCount} 条；其中可提交 {plan.ImportableRecordCount} 条。");

        if (plan.IgnoredRecordCount > 0)
        {
            lines.Add($"其他 UID 的 {plan.IgnoredRecordCount} 条记录将被忽略（只导入当前所选账号）。");
        }

        if (plan.SkippedRecordCount > 0)
        {
            lines.Add($"源文件中 {plan.SkippedRecordCount} 条记录因解析问题或区服无法判断，导入时将被跳过。");
        }

        if (plan.Source.ErrorCount > 0)
        {
            lines.Add($"源文件共有 {plan.Source.ErrorCount} 处解析问题；实际无效数量以提交结果为准。");
        }

        lines.Add("来源账号：");
        foreach (GachaImportSourceAccountPreview account in plan.Accounts)
        {
            lines.Add(DescribeSourceAccount(account));
        }

        lines.Add(plan.Mode == GachaImportMode.Full
            ? "覆盖说明：只覆盖来源明确包含的同记录并保留修订；不删除来源缺失的本地记录。"
            : "覆盖说明：本次为仅补充模式，不覆盖已有同记录；不删除来源缺失的本地记录。");
        lines.Add("提交说明：新增、重复与覆盖数量以服务提交结果为准；预览不解析物品元数据。");
        lines.Add("撤销说明：本次导入通过现有原子变更集提交并保留修订；本页不提供撤销入口，撤销沿用现有操作历史规则。");
        return string.Join(Environment.NewLine, lines);
    }

    public static string BuildSavedFacts(GachaImportPlan plan, GachaImportResult result)
    {
        string target = plan.Target.AccountIdentity is string identity
            ? $"{plan.Target.ArchiveName} · {identity}"
            : $"{plan.Target.ArchiveName}（档案内全部账号）";
        return "已保存的事实：" + Environment.NewLine +
            $"文件：{plan.Source.FileName} · {plan.Source.FormatLabel}" + Environment.NewLine +
            $"目标：{plan.Target.ScopeLabel} · {target}" + Environment.NewLine +
            $"模式：{plan.ModeOption.Label}" + Environment.NewLine +
            $"总数 {result.TotalCount} 条 · 新增 {result.ImportedCount} · " +
            $"重复 {result.DuplicateCount} · 无效 {result.InvalidCount} · " +
            $"忽略 {result.IgnoredCount} · 新建账号 {result.CreatedAccountCount} · " +
            $"覆盖修订 {result.UpdatedCount}";
    }

    public static string DescribeReaderFailure(string message, string version)
    {
        if (message.Contains("not valid UIGF JSON", StringComparison.Ordinal))
        {
            return $"文件 {FormatLabel(version)} 的 JSON 结构无效，无法读取，未写入任何数据。";
        }

        if (message.Contains("Unsupported UIGF version", StringComparison.Ordinal) ||
            message.Contains("Only UIGF", StringComparison.Ordinal))
        {
            return $"不支持的 UIGF 版本：{version}。支持 v2.2、v2.3、v2.4、v3.0、v4.0、v4.1、v4.2。未写入任何数据。";
        }

        if (message.Contains("missing info", StringComparison.Ordinal) ||
            message.Contains("export_timestamp", StringComparison.Ordinal) ||
            message.Contains("exporter details", StringComparison.Ordinal))
        {
            return "UIGF 文件缺少必需的 info 字段（例如 export_timestamp、export_app），无法读取，未写入任何数据。";
        }

        if (message.Contains("SRGF", StringComparison.Ordinal))
        {
            return "这是 SRGF（星穹铁道）文件，不是原神 UIGF 抽卡文件。未写入任何数据。";
        }

        if (message.Contains("list", StringComparison.OrdinalIgnoreCase))
        {
            return "UIGF 文件缺少必需的记录列表或账号结构，无法读取，未写入任何数据。";
        }

        return "UIGF 文件校验失败，未写入任何数据。请确认文件版本与内容后重试。";
    }

    public static string DescribeImportFailure(string message)
    {
        if (message.Contains("irreversibly deleted", StringComparison.OrdinalIgnoreCase))
        {
            return "文件包含已在本机不可逆删除的记录；需要显式重新引入确认，当前入口不提供该确认，未写入任何数据。";
        }

        if (message.Contains("conflicts with existing", StringComparison.OrdinalIgnoreCase))
        {
            return "文件记录与本地事实冲突，未写入任何数据。请重新载入后重试。";
        }

        const string marker = "Cannot import UID ";
        int start = message.IndexOf(marker, StringComparison.Ordinal);
        if (start >= 0)
        {
            int uidStart = start + marker.Length;
            int uidEnd = message.IndexOf(':', uidStart);
            string uid = uidEnd > uidStart
                ? message[uidStart..uidEnd].Trim()
                : message[uidStart..].Split('.')[0].Trim();
            string reason = message.Contains("same resolved natural identity", StringComparison.Ordinal)
                ? "文件中的 UID 与目标账号身份不一致"
                : message.Contains("metadata that cannot be completed", StringComparison.Ordinal)
                    ? "记录缺少可补全的物品名称、类型或星级"
                    : message.Contains("server region cannot be inferred", StringComparison.Ordinal)
                        ? "无法从 UID 推断区服"
                        : "记录无效或账号身份不匹配";
            return $"账号 {uid} 的记录无法导入：{reason}。未写入任何数据。";
        }

        return "文件格式或内容校验失败，未写入任何数据。请确认文件版本与内容后重试。";
    }

    private static string DescribeSourceAccount(GachaImportSourceAccountPreview account)
    {
        string region = RegionLabel(account.Region);
        string status;
        if (account.Ignored)
        {
            status = $"将忽略 {account.ParsedRecordCount} 条（不属于当前所选账号）";
        }
        else if (account.Skipped)
        {
            status = account.ErrorCount > 0
                ? $"将跳过 {account.ParsedRecordCount} 条可解析记录（该账号另有 {account.ErrorCount} 处解析问题，服务会整账号跳过）"
                : $"将跳过 {account.ParsedRecordCount} 条记录（无法从 UID 推断区服）";
        }
        else
        {
            status = $"可提交 {account.ParsedRecordCount} 条";
            if (account.MatchesTarget)
            {
                status += "（与目标账号身份一致）";
            }
            else if (account.WillCreateAccount)
            {
                status += "（档案中没有该账号副本，导入时将新建）";
            }
            else if (account.ExistingAccount && account.ExistingAccountLabel is string label)
            {
                status += $"（匹配档案内副本：{label}）";
            }
        }

        if (account.ErrorCount > 0 && !account.Ignored && !account.Skipped)
        {
            status += $"；另有 {account.ErrorCount} 处解析问题";
        }

        return $"· UID {account.Uid}（{region}）：{status}";
    }
}
